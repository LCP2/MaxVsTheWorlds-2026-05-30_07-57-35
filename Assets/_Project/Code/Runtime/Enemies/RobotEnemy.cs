using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Domestic-robot enemy v0 (YT-36), "pure machine" tier. State machine:
    /// Chase (steering toward where it last SAW Max — YT-83) → Search (it has lost him and casts
    /// about; regains Chase the moment the sight-line comes back) → Telegraph (wind-up tell) → Lunge
    /// (committed burst that deals contact damage) → Recover → back to Chase.
    /// Implements <see cref="IDamageable"/> (dies to the Water Blaster). Death pop +
    /// hit reaction are code-driven.
    ///
    /// It steers on two scales, and the split is the point (YT-93). WALLS are routed around: the level
    /// is a graph of rooms and doorways and <see cref="EnemyNavigation"/> reads the way through it, so
    /// a robot leaving the shed walks out of the shed door rather than into the side of the shed —
    /// which is what a beeline did, and which had them piling up against fences while the player walked
    /// away. COVER is not routed around: it is sparse, it sits inside a room, and
    /// <see cref="ObstacleSteering"/> already rounds it by walking along whatever it bumps into. Still
    /// no NavMesh, and now for a better reason than cost: the map already knows the way, and a baked
    /// mesh would be a second, drifting copy of an answer we author.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class RobotEnemy : MonoBehaviour, IDamageable, IKnockbackable, IHaltable, IHealthReadout
    {
        // Emerging is appended, not inserted: these are serialized as ints, and renumbering the
        // existing members would silently re-label every one of them.
        // Teleport (MV-293) follows the same rule — it's the Blinker's flank-blink, appended after
        // Emerging rather than inserted anywhere earlier.
        // Dormant/Alert (MV-363) follow it too — a concealed robot that hasn't yet seen Max, and
        // the short "waking up" beat between spotting him and actually joining the chase.
        public enum State { Chase, Telegraph, Lunge, Recover, Dead, Search, Emerging, Teleport, Dormant, Alert }

        [Header("Target")]
        [Tooltip("Max. If null, located by tag 'Player' on enable.")]
        [SerializeField] private Transform target;

        [Header("Movement")]
        // Fallback only — Apply() stamps the real number from EnemyArchetype, which is where you
        // tune it. Kept in step with Rusher (60% of Max's 6 m/s) so a robot built without an
        // archetype isn't a different animal (YT-80).
        [SerializeField] private float moveSpeed = 3.6f;

        /// <summary>
        /// Chase speed after any dev override (YT-105). Read at the point of movement rather than
        /// stamped in <see cref="Apply"/>, so dragging the slider retimes the robots already on the
        /// field — the ones you're watching — instead of only the next wave.
        /// </summary>
        private float EffectiveMoveSpeed => DevTuning.Or(DevTuning.RobotMoveSpeed, moveSpeed);
        [SerializeField] private float gravity = 20f;

        /// <summary>Minimum spacing this robot keeps from other active robots while chasing (MV-321),
        /// after any dev override — same live-read idiom as <see cref="EffectiveMoveSpeed"/>.</summary>
        private float EffectiveMinSeparation =>
            DevTuning.Or(DevTuning.RobotMinSeparation, EnemySeparation.DefaultMinDistance);

        [Header("Lunge")]
        [SerializeField] private float lungeRange = 2.2f;     // start telegraph within this
        [SerializeField] private float telegraphTime = 0.55f; // wind-up (dodge window)
        [SerializeField] private float lungeSpeed = 11f;
        [SerializeField] private float lungeTime = 0.22f;
        [SerializeField] private float recoverTime = 0.7f;
        [SerializeField] private float contactDamage = 12f;
        [SerializeField] private float contactRadius = 1.0f;

        [Header("Ranged / teleport (MV-293) — Gunner/Bomber/Blinker only, 0 for every melee kind")]
        [Tooltip("A ranged kind backs off inside this instead of closing — see EnemyArchetype.StandoffRange.")]
        [SerializeField] private float standoffRange = 0f;
        [Tooltip("How often a Blinker may flank-teleport while still out of melee range.")]
        [SerializeField] private float teleportCooldown = 0f;

        [Header("Health")]
        [SerializeField] private float maxHealth = 24f;

        [Header("Sight (YT-83)")]
        [Tooltip("Seconds of GETTING NOWHERE before it accepts it has lost Max. This is the price of " +
                 "hiding: too short and stepping behind cover and straight back out is a free reset, " +
                 "too long and cover isn't an escape at all. Measured from the last time it got closer " +
                 "to the spot it is hunting — not from the last time it saw him (YT-93).")]
        [SerializeField] private float searchTime = 2.5f;

        [Tooltip("How close it has to get to the last place it saw Max before it starts casting about.")]
        [SerializeField] private float arriveRadius = 1.2f;
        [Tooltip("Speed while hunting a spot it can't see Max at. Slower than a chase — it has lost " +
                 "him, and a robot that searches at full sprint reads as one that hasn't.")]
        [SerializeField] private float searchSpeedScale = 0.55f;

        /// <summary>Floor on how long a lost-sight hunt must run before it is allowed to end (MV-387).
        /// Without this, a robot that is already within <see cref="arriveRadius"/> of Max's last-seen
        /// spot the instant sight breaks — the common case, since that spot is exactly where the
        /// chase was standing a frame ago — reads "arrived" immediately and spins into Search with no
        /// visible pursuit at all. See <see cref="PursuitStall"/>.</summary>
        [Tooltip("Minimum time a lost-sight hunt must run before it's allowed to end, even if it's " +
                 "already within arriveRadius of the last-seen spot. Keeps close-range cover breaks " +
                 "(MV-387) from reading as an instant give-up.")]
        [SerializeField] private float minHuntTime = 0.6f;

        [Header("Tells (gold-ring / lens)")]
        [SerializeField] private Renderer tellRenderer; // optional; the gold-ring/eye
        [SerializeField] private Color idleTell = new Color(0.85f, 0.7f, 0.2f);
        [SerializeField] private Color windupTell = new Color(1f, 0.2f, 0.1f);

        // --- Field-wide registry (YT-186) ---------------------------------------------------------
        // Tracked directly off OnEnable/OnDisable rather than tallied by hand, so it can never drift
        // from what is actually switched on: a normal death, being pooled back out, and a test's
        // blunt Object.Destroy all fire OnDisable the same way. This is what EnemySpawner's global
        // spawn budget and TelegraphVfx's windup scan both read instead of a FindObjectsByType<>()
        // sweep — a full-scene scan+allocation that used to run every single frame regardless of
        // enemy count, and got more expensive exactly as YT-185 grew the population it was scanning.
        private static readonly List<RobotEnemy> _active = new List<RobotEnemy>(32);

        /// <summary>Every robot switched on right now, across every factory.</summary>
        public static IReadOnlyList<RobotEnemy> Active => _active;

        /// <summary>How many robots are switched on right now, field-wide (not per-factory).</summary>
        public static int ActiveCount => _active.Count;

        /// <summary>Empties the registry. Called when a level starts building, alongside
        /// <see cref="MaxWorlds.Factories.FactoryCensus.Reset"/> — belt-and-braces against a robot
        /// whose OnDisable hasn't run yet when the next level (or test) starts counting.</summary>
        public static void ResetRegistry() => _active.Clear();

        /// <summary>Scratch buffer for <see cref="EnemySeparation"/>'s neighbour lookup (MV-321) — one
        /// shared list, cleared and refilled per robot per tick, instead of a fresh allocation every
        /// frame for every chaser in a ~20-30 robot swarm.</summary>
        private static readonly List<Vector3> _separationScratch = new List<Vector3>(32);

        public State Current { get; private set; } = State.Chase;
        public bool IsAlive => Current != State.Dead && _health > 0f;

        /// <summary>Concealed and unaware (MV-363) — placed behind cover, world-present from area
        /// load, but not yet chasing, firing or telegraphing. Ends the moment it (or a groupmate,
        /// via <see cref="DormantGroup"/>) sees Max.</summary>
        public bool IsDormant => Current == State.Dormant;

        /// <summary>Fired the instant a dormant robot wakes (MV-363) — what <see cref="DormantGroup"/>
        /// listens to so the rest of a concealed knot wakes with it, reading as an ambush rather than
        /// a trickle. Cleared on every <see cref="ResetState"/> so a pooled robot never carries a
        /// stale group's subscription into its next life.</summary>
        public event Action<RobotEnemy> WokeFromDormant;

        /// <summary>Which robot this is (YT-66). Set by <see cref="Apply"/>; the spawner pools by it,
        /// so a dead bruiser is never recycled as a rusher wearing the wrong body.</summary>
        public EnemyKind Kind { get; private set; } = EnemyKind.Rusher;

        /// <summary>Stamp this robot with an archetype's stats and reset it to fresh. Must be called
        /// after the component exists (Awake has already run and seeded the old defaults), so it
        /// re-runs <see cref="ResetState"/> to pick the new health up.</summary>
        public void Apply(in EnemyArchetype a)
        {
            Kind = a.Kind;
            moveSpeed = a.MoveSpeed;
            maxHealth = a.MaxHealth;
            contactDamage = a.ContactDamage;
            contactRadius = a.ContactRadius;
            lungeRange = a.LungeRange;
            telegraphTime = a.TelegraphTime;
            lungeSpeed = a.LungeSpeed;
            lungeTime = a.LungeTime;
            recoverTime = a.RecoverTime;
            knockbackDecay = a.KnockbackDecay;
            standoffRange = a.StandoffRange;
            teleportCooldown = a.TeleportCooldown;
            ResetState();
        }

        /// <summary>How far through the wind-up this enemy is, 0..1 (0 when not telegraphing).
        /// A read-only window into existing state so the readability VFX (YT-53) can draw a
        /// dodge-window indicator on the ground — a colour tell on a small robot doesn't read at
        /// the fixed ~72° camera with 20–30 enemies on screen. No behaviour change.</summary>
        public float TelegraphProgress =>
            Current == State.Telegraph && telegraphTime > 0f
                ? Mathf.Clamp01(_stateTimer / telegraphTime)
                : 0f;

        /// <summary>The Gunner's committed beam length and half-width (MV-312) — the same
        /// <see cref="lungeRange"/>/<see cref="contactRadius"/> fields <see cref="TickBeam"/> already
        /// hit-tests against (see <see cref="EnemyArchetype.Gunner"/>'s own doc comment for why they
        /// double as beam geometry), exposed read-only so <see cref="MaxWorlds.VFX.RobotRig"/> can draw
        /// the beam VFX from the same numbers gameplay uses, instead of a second copy that could drift.</summary>
        public float BeamRange => lungeRange;
        public float BeamHalfWidth => contactRadius;

        /// <summary>Melee/attack range this robot commits from — what a Blinker squad jump (MV-366)
        /// checks against to decide whether it's still worth blinking in, and how close a landing
        /// point needs to be to read as "arrives and attacks" rather than "arrives and walks the rest
        /// of the way".</summary>
        public float LungeRange => lungeRange;

        public Team Team => Team.Enemy;

        /// <summary>Fired on death (spawner decrements its live count). Arg = this enemy.</summary>
        public event Action<RobotEnemy> Died;

        private CharacterController _cc;
        private IDamageable _targetDamageable;

        /// <summary>Max's own transform — fixed the moment it's acquired, and what every distance
        /// comparison in <see cref="RetargetIfNeeded"/> measures against even while <see cref="target"/>
        /// is pointed at a Sentinel (MV-362).</summary>
        private Transform _playerTarget;

        /// <summary>The Sentinel currently being engaged, or null while targeting Max (MV-362).</summary>
        private Sentinel _engagedSentinel;
        private float _health;
        private float _stateTimer;
        private float _verticalVel;
        private Vector3 _lungeDir;
        private bool _dealtThisLunge;
        private MaterialPropertyBlock _mpb;
        private Vector3 _knockback;
        [Tooltip("How fast a spray shove bleeds off (m/s²). Higher = a shorter shove (YT-64).")]
        [SerializeField] private float knockbackDecay = 28f;

        [Tooltip("How long a wall stays 'in the way' after touching it — long enough to walk clear " +
                 "of the corner rather than re-hugging it every frame (YT-68).")]
        [SerializeField] private float wallMemory = 0.2f;

        private Vector3 _wallNormal;
        private float _wallTimer;

        [Tooltip("Speed while walking out of the factory door, as a fraction of chase speed (YT-100). " +
                 "Dropped further at YT-169 so the birth beat reads as a distinctly slower, more " +
                 "deliberate step than the chase that follows it, not almost the same pace.")]
        [SerializeField] private float emergeSpeedScale = 0.65f;

        /// <summary>How close to the spot outside the door counts as "out". Loose — the point is to
        /// be clear of the building, not to hit a coordinate.</summary>
        private const float EmergeArriveRadius = 0.35f;

        [Header("Dormant / concealed (MV-363)")]
        [Tooltip("How long the 'waking up' beat lasts between spotting Max and actually joining " +
                 "the chase — Lee, 12 Aug: give the player a beat to react, so being spotted reads " +
                 "as legible rather than instant.")]
        [SerializeField] private float alertTime = 0.45f;

        /// <summary>Longest a robot may spend getting out of the door before it gives up and fights
        /// anyway. The walk is well under a second; this only ever catches a blocked doorway.</summary>
        private const float EmergeTimeout = 1.5f;

        private Vector3 _emergeTarget;

        /// <summary>Tracks whether the current lost-sight hunt has run its course — reached the spot
        /// it is hunting, or stopped getting any closer to it. This is what "it has lost him" means
        /// now (YT-93) — not "it hasn't seen him lately", which is true of every robot the moment it
        /// is born.</summary>
        private readonly PursuitStall _pursuitStall = new PursuitStall();

        /// <summary>Seconds until a Blinker may next flank-teleport (MV-293); irrelevant, and never
        /// counted down, for every other kind.</summary>
        private float _teleportTimer;

        /// <summary>Where a Blinker is warping to — computed the instant the teleport triggers, in
        /// <see cref="TickChase"/>, then executed after the charge-up in <see cref="TickTeleport"/>.</summary>
        private Vector3 _teleportTarget;

        /// <summary>What this robot knows about where Max is — which, since YT-83, is no longer the
        /// same thing as where he is. Read-only outside; the state machine drives it.</summary>
        public Perception Sight => _sight;
        private readonly Perception _sight = new Perception();
        private float _preferSign = 1f;

        // --- IHealthReadout (YT-111): what the floating bar over this robot reads. ---
        public float HealthNormalized => maxHealth > 0f ? Mathf.Clamp01(_health / maxHealth) : 0f;
        public float HealthCurrent => _health;
        public string ReadoutName => Kind switch
        {
            EnemyKind.Bruiser => "BRUISER",
            EnemyKind.Heavy => "HEAVY",
            EnemyKind.Brute => "BRUTE",
            EnemyKind.Gunner => "LASER",   // MV-404: display-only rename, EnemyKind.Gunner unchanged
            EnemyKind.Bomber => "BOMBER",
            EnemyKind.Blinker => "BLINKER",
            _ => "RUSHER",
        };

        /// <summary>This robot's full HP, unscaled by current damage — what Water Balloon's
        /// percentage splash (WV-231, spec §9 <c>waterBalloonDamagePct</c>) is a fraction of.
        /// Deliberately not part of <see cref="IHealthReadout"/>: that interface is a UI readout and
        /// says nothing about max on purpose (see its own doc comment); this is a combat number a
        /// weapon actually needs.</summary>
        public float MaxHealth => maxHealth;

        /// <summary>Seconds left on a Water Balloon halt (WV-231) — 0 when not halted.</summary>
        private float _haltTimer;

        /// <summary>True while frozen by a halt — the state machine and its timer are paused, but
        /// knockback and gravity still apply (a halted robot can still be shoved or fall).</summary>
        public bool IsHalted => _haltTimer > 0f;

        // --- IHaltable (WV-231) ---
        public void ApplyHalt(float seconds)
        {
            if (Current == State.Dead) return;
            _haltTimer = Mathf.Max(_haltTimer, seconds);
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _mpb = new MaterialPropertyBlock();
            _preferSign = ObstacleSteering.PreferSignFor(GetInstanceID());
            ResetState();

            // A child of the body, so it deactivates and comes back with a POOLED robot instead of
            // needing to be reattached on reuse. The bar re-derives its own metre space every frame,
            // so it does not care that Apply() stamps this body's scale on after Awake has run.
            //
            // alwaysShow (YT-122): every robot carries its bar from the moment it spawns, not only
            // once it has been hit. YT-111 hid a full-health robot's bar to cut clutter, but the
            // result on device read as "the robots have no life bars" — and a green bar you can see
            // approaching is exactly the read the ticket wants. The shared ramp keeps a healthy
            // robot's bar green and quiet, so a wall of full robots stays calm rather than loud.
            WorldHealthBar.Attach(gameObject, this, BarHeight, BarWidth, alwaysShow: true);
        }

        /// <summary>Metres above a robot's origin its bar floats. The origin is the body's centre
        /// and the tallest archetype is 1.4 m, so this clears every head with room to spare.</summary>
        private const float BarHeight = 1.15f;
        private const float BarWidth = 1.5f;   // YT-136: wider so a flat, short bar still reads at 23 m zoom

        private void OnEnable()
        {
            _active.Add(this);
            ResetState(); // reset for pooling reuse
        }

        private void OnDisable() => _active.Remove(this);

        /// <summary>Reset to a fresh, alive Chase state. Called from Awake/OnEnable and
        /// directly by tests (which don't get Unity lifecycle callbacks).</summary>
        public void ResetState()
        {
            _health = maxHealth;
            Current = State.Chase;
            _stateTimer = 0f;
            _wallTimer = 0f;          // a pooled robot doesn't inherit the last one's wall
            _pursuitStall.NoteSightHeld(); // ...nor its idea of how well the last one was doing
            _knockback = Vector3.zero;
            _haltTimer = 0f;
            // A pooled robot must never wake a group it no longer belongs to (MV-363) — the
            // DormantGroup that subscribed here is for the LAST life this body had.
            WokeFromDormant = null;
            // Full cooldown, not zero: a freshly spawned Blinker gets the same beat as everything
            // else before its first attack, rather than an instant blink the moment it's born.
            _teleportTimer = teleportCooldown;
            AcquireTarget();
            SetTell(idleTell);
        }

        private void AcquireTarget()
        {
            if (target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) target = p.transform;
            }
            _playerTarget = target;
            _engagedSentinel = null;
            _targetDamageable = target != null ? target.GetComponent<IDamageable>() : null;

            // A robot is dispatched toward the fight, not born knowing where it is. Without a seed
            // it has never seen anything, has nowhere to go, and stands in the factory mouth — which
            // is precisely what happens now that the hutch it just walked out of blocks its view.
            if (target != null) _sight.Spawn(target.position);
        }

        /// <summary>Re-decide whether to chase Max or the nearest Sentinel (MV-362) — proximity-based
        /// only ("must NOT always prefer sentinels over Max"), checked once per Chase tick, never
        /// mid-Telegraph/Lunge (same "no info through the wind-up" rule the state machine already
        /// locks everything else against). Blinker is excluded outright: its answer to an obstacle is
        /// to blink past it, not to fight it (spec: "Blinkers can teleport past a wall entirely") —
        /// every other kind treats a close, blocking sentinel as a real target, ranged kinds shooting
        /// it from their existing standoff band exactly as they would Max.</summary>
        private void RetargetIfNeeded()
        {
            if (Kind == EnemyKind.Blinker || _playerTarget == null) return;

            // The sentinel we were fighting died since the last tick — fall back to Max before
            // re-evaluating, so a dead Sentinel's Transform is never read below.
            if (_engagedSentinel != null && !_engagedSentinel.IsAlive)
            {
                _engagedSentinel = null;
                RetargetTo(_playerTarget, _playerTarget.GetComponent<IDamageable>());
            }

            float distToPlayer = Vector3.Distance(transform.position, _playerTarget.position);
            Sentinel nearest = SentinelTargeting.Nearest(transform.position);
            float distToSentinel = nearest != null
                ? Vector3.Distance(transform.position, nearest.transform.position)
                : float.MaxValue;

            bool engageSentinel = nearest != null &&
                SentinelTargeting.ShouldEngageSentinel(distToPlayer, distToSentinel, SentinelTargeting.AggroRadius);

            if (engageSentinel && nearest != _engagedSentinel)
            {
                _engagedSentinel = nearest;
                RetargetTo(nearest.transform, nearest);
            }
            else if (!engageSentinel && _engagedSentinel != null)
            {
                _engagedSentinel = null;
                RetargetTo(_playerTarget, _playerTarget.GetComponent<IDamageable>());
            }
        }

        /// <summary>Point <see cref="target"/>/<see cref="_targetDamageable"/> at a new goal and give
        /// it fresh sight memory (MV-362) — every state below reads only these two fields, so
        /// switching them is the whole retarget.</summary>
        private void RetargetTo(Transform newTarget, IDamageable newDamageable)
        {
            target = newTarget;
            _targetDamageable = newDamageable;
            if (target != null) _sight.Spawn(target.position);
        }

        private void Update()
        {
            if (Current == State.Dead) return;
            float dt = Time.deltaTime;

            // Look, once, before deciding anything. Everything below reads the memory, never the
            // transform — the robot no longer knows where Max is, only where it last saw him.
            if (target != null)
                _sight.Tick(LineOfSight.Between(transform, target), target.position, dt);

            // Water Balloon's halt (WV-231): a true freeze, not just a movement stop — the state
            // timer doesn't advance either, so a robot caught mid-telegraph resumes exactly where it
            // left off once the halt ends, rather than the wind-up quietly expiring while frozen.
            if (_haltTimer > 0f)
            {
                _haltTimer -= dt;
            }
            else
            {
                _stateTimer += dt;
                switch (Current)
                {
                    case State.Emerging: TickEmerge(dt);   break;
                    case State.Chase:    TickChase(dt);    break;
                    case State.Search:   TickSearch(dt);   break;
                    case State.Telegraph: TickTelegraph(dt); break;
                    case State.Lunge:    TickLunge(dt);    break;
                    case State.Recover:  TickRecover(dt);  break;
                    case State.Teleport: TickTeleport(dt); break;
                    case State.Dormant:  TickDormant();    break;
                    case State.Alert:    TickAlert(dt);    break;
                }
            }

            ApplyKnockback(dt);
            ApplyGravity(dt);
        }

        /// <summary>Spray knockback (YT-64): a shove that decays over ~0.2s. Applied on top of the
        /// state machine so being pushed doesn't cancel the chase/lunge, it just displaces.</summary>
        public void ApplyKnockback(Vector3 impulse)
        {
            if (Current == State.Dead) return;
            _knockback += impulse;
        }

        private void ApplyKnockback(float dt)
        {
            if (_knockback.sqrMagnitude < 0.0004f) { _knockback = Vector3.zero; return; }
            CharacterControllerMotion.SafeMove(_cc, _knockback * dt); // MV-386: no oversized single Move()
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, knockbackDecay * dt);
        }

        /// <summary>
        /// Walk out of the factory door before doing anything else (YT-100). The spawner puts the
        /// robot in the doorway and hands it the spot just outside; this is the walk between them.
        ///
        /// It is a state rather than a flag on Chase because chasing is exactly what it must not do
        /// yet: Chase steers at Max, and from inside a doorway that means turning immediately and
        /// scraping along the shed wall. The robot has one job for half a second — get clear of the
        /// building it came out of — and having it hold that beat is what makes the factory read as
        /// producing robots rather than as a place they appear.
        /// </summary>
        public void BeginEmergence(Vector3 exitPoint)
        {
            if (Current == State.Dead) return;
            _emergeTarget = exitPoint;
            Current = State.Emerging;
            _stateTimer = 0f;
            SetTell(idleTell);
        }

        private void TickEmerge(float dt)
        {
            Vector3 to = _emergeTarget - transform.position;
            to.y = 0f;

            // Out of the doorway, or it has taken long enough that something is in the way — a piece
            // of cover, another robot, a corner of the shed. Either way it stops pushing and gets on
            // with the fight, because a robot stuck emerging is a robot that never attacks.
            if (to.sqrMagnitude <= EmergeArriveRadius * EmergeArriveRadius || _stateTimer >= EmergeTimeout)
            {
                Current = State.Chase;
                _stateTimer = 0f;
                return;
            }

            // Deliberately slower than a chase. It is heaving itself out of a shed, not sprinting;
            // the step up to full speed as it clears the door is what sells the hand-off.
            FaceAndMove(to.normalized, EffectiveMoveSpeed * emergeSpeedScale, dt);
        }

        /// <summary>Puts this robot to sleep behind cover (MV-363): world-present and rendered from
        /// the moment it's placed — never spawned later at the moment a gate opens — but not yet
        /// chasing, firing or telegraphing. Called by the spawner right after placement, in place of
        /// the ordinary fresh-Chase state <see cref="ResetState"/> leaves it in.</summary>
        public void BeginDormant()
        {
            if (Current == State.Dead) return;
            Current = State.Dormant;
            _stateTimer = 0f;
            SetTell(idleTell);
        }

        /// <summary>Nothing: the whole point (AC2) is that a dormant robot does not path toward Max,
        /// does not fire, and does not telegraph its position. The only way out is <see cref="Activate"/>
        /// — called here the instant this robot's own sight-line opens (ticked unconditionally above,
        /// same as every other state), or by a groupmate's via <see cref="DormantGroup"/>.</summary>
        private void TickDormant()
        {
            if (_sight.HasSight) Activate();
        }

        /// <summary>Wakes a dormant robot into the short "waking up" beat (<see cref="TickAlert"/>)
        /// before it joins the chase for real. Idempotent — a robot no longer Dormant ignores a
        /// second call, which is what lets <see cref="DormantGroup"/> call this on every member of a
        /// concealed knot without first checking which one actually saw Max.</summary>
        public void Activate()
        {
            if (Current != State.Dormant) return;
            Current = State.Alert;
            _stateTimer = 0f;
            SetTell(windupTell);
            WokeFromDormant?.Invoke(this);
        }

        /// <summary>The beat itself: a pulsing tell (same idiom as <see cref="TickTelegraph"/>'s
        /// wind-up) so being spotted reads as legible rather than an instant switch, then straight
        /// into Chase — which reads <see cref="_sight"/> fresh on its very next tick, exactly as if
        /// it had walked into view instead of waking up already looking at him.</summary>
        private void TickAlert(float dt)
        {
            float t = Mathf.PingPong(_stateTimer * 6f, 1f);
            SetTell(Color.Lerp(idleTell, windupTell, t));

            if (_stateTimer >= alertTime)
            {
                Current = State.Chase;
                _stateTimer = 0f;
            }
        }

        private void TickChase(float dt)
        {
            if (target == null) { AcquireTarget(); return; }

            RetargetIfNeeded();

            // The destination is MEMORY, not Max. While it can see him the two are the same thing;
            // the moment it can't, this is where cover starts paying — it commits to a stale spot.
            Vector3 goal = _sight.Destination(target.position);
            Vector3 to = goal - transform.position;
            to.y = 0f;
            float dist = to.magnitude;   // to the GOAL — what "arrived" and "close enough to lunge" mean

            // Blinker (MV-293): the one kind that cheats the distance instead of closing it. Checked
            // here, ahead of the ordinary chase, so a Blinker still out of melee range blinks rather
            // than plodding the rest of the way like a rusher.
            if (Kind == EnemyKind.Blinker)
            {
                _teleportTimer -= dt;
                if (_teleportTimer <= 0f && _sight.HasSight && dist > lungeRange)
                {
                    float sign = UnityEngine.Random.value < 0.5f ? 1f : -1f;
                    _teleportTarget = BlinkerTeleport.FlankPoint(
                        target.position, transform.position, lungeRange * 0.85f, sign);
                    Current = State.Teleport;
                    _stateTimer = 0f;
                    SetTell(windupTell);
                    return;
                }
            }

            // Ask the level the way (YT-93). In the same room this is the goal itself and the chase is
            // the beeline it always was; from another room it is the next doorway, so a robot leaving
            // the shed walks out of the shed instead of into the side of it. The route is computed to
            // the goal it BELIEVES in, never to Max — a robot that could be routed to a player it
            // cannot see would be omniscient again, and cover would stop working (YT-83).
            Vector3 waypoint = EnemyNavigation.Waypoint(transform.position, goal);

            // Its own lane, so a pack arrives as a fan rather than a queue. Only on the last leg: a
            // doorway is a metre wide and taking it at a personal angle just walks into the frame.
            bool lastLeg = (waypoint - goal).sqrMagnitude < 0.01f;
            if (lastLeg) waypoint = EnemyFormation.ApproachPoint(goal, transform.position, GetInstanceID());

            Vector3 step = waypoint - transform.position;
            step.y = 0f;

            Vector3 dir = step.normalized;

            // MV-321: lean away from anything crowding this robot right now. EnemyFormation's fan
            // above only biases the GOAL each robot walks toward and collapses to zero on arrival —
            // it never looks at where the pack actually is, so robots sharing a lane bias still ended
            // up pressed shoulder-to-shoulder. This reacts to real neighbours instead.
            //
            // Computed and blended BEFORE the wall-slide below (MV-402), not after: a neighbour push
            // knows nothing about the wall, so blending it in last could hand back a direction with a
            // component into the wall the robot is currently rounding — and at a barrier several
            // robots converge on together, that is exactly what queued neighbours pushing back along
            // the wall face produced: every robot's along-wall progress cancelled by the one behind
            // it, the whole line reading as stuck against the barrier instead of routing around it.
            _separationScratch.Clear();
            for (int i = 0; i < _active.Count; i++)
            {
                RobotEnemy other = _active[i];
                if (other != this) _separationScratch.Add(other.transform.position);
            }
            Vector3 separation = EnemySeparation.Push(transform.position, _separationScratch, EffectiveMinSeparation);
            dir = EnemySeparation.Steer(dir, separation);

            // The lawn has cover in it (YT-68). Beelining (or being shoved by a crowded neighbour)
            // into a prop just presses against it, so while a wall is remembered, walk along it and
            // round the corner instead — the last word on direction, so nothing steered in above it
            // can ever send this robot back into the wall it's already rounding.
            if (_wallTimer > 0f)
            {
                _wallTimer -= dt;
                dir = ObstacleSteering.SlideAlongWall(dir, _wallNormal, _preferSign);
            }

            // Gunner/Bomber (MV-293): the answer to a ranged kind must never be "walk at it" — inside
            // its standoff band it backs off along the same line it was closing on, rather than
            // committing to melee range like everything else in the swarm.
            bool ranged = Kind == EnemyKind.Gunner || Kind == EnemyKind.Bomber;
            bool tooClose = ranged && standoffRange > 0f && _sight.HasSight && dist < standoffRange;
            if (tooClose) dir = -dir;

            bool hunting = !_sight.HasSight;
            float speed = EffectiveMoveSpeed;
            FaceAndMove(dir, hunting ? speed * searchSpeedScale : speed, dt);

            // Is it getting anywhere? Seeing Max is a new spot to walk to, so the clock starts again.
            //
            // It reached the spot, or it has stopped getting closer to it. Either way it is now
            // standing somewhere Max isn't, and it has to admit that.
            //
            // "Stopped getting closer" — not "hasn't seen him for a while", which is what this used to
            // ask (YT-93). Every robot is now born out of sight of Max, in a shed on the other side of
            // the yard, so it had never seen him and the clock was already running: it gave up two and
            // a half seconds into a thirty-second walk, every time, and stood there spinning in the
            // shed. That is the pile-up the playtest found. A robot that is still closing has not lost
            // anything and does not stop; one grinding on a fence gets nowhere and does.
            if (_sight.HasSight)
            {
                _pursuitStall.NoteSightHeld();
            }
            else if (_pursuitStall.TickHunting(dist, dt, arriveRadius, searchTime, minHuntTime))
            {
                Current = State.Search;
                _stateTimer = 0f;
                SetTell(idleTell);
                return;
            }

            // Only wind up at something you can actually SEE. Without this a robot lunges at the
            // tree Max is standing behind, which looks broken and is free damage for the player.
            //
            // A ranged kind also withholds the wind-up until it has actually opened its standoff gap
            // (MV-293) — without this check it "retreats" for exactly one frame and then fires from
            // point-blank anyway, since Telegraph holds position and dist <= lungeRange was already
            // true before it took that one step back.
            if (_sight.HasSight && dist <= lungeRange && !tooClose)
            {
                Current = State.Telegraph;
                _stateTimer = 0f;
                SetTell(windupTell);   // visual tell: dodge window opens
            }
        }

        /// <summary>
        /// It has lost him. It stands where it last had him and casts about, and it does NOT get to
        /// walk to wherever he really is — that would be the omniscience this ticket removed, wearing
        /// a different name. Contact is broken until Max shows himself again.
        ///
        /// This is the beat the loop was missing. Duck behind the tree, the pack commits to an empty
        /// patch of lawn, your health starts trickling back (YT-80's out-of-combat regen), and you
        /// choose when to re-engage. Pressure, relief, pressure.
        /// </summary>
        private void TickSearch(float dt)
        {
            if (target == null) { AcquireTarget(); return; }

            if (_sight.HasSight)
            {
                Current = State.Chase;     // there he is
                _stateTimer = 0f;
                return;
            }

            // Scan: turn on the spot so it reads as looking rather than as a statue someone left.
            // Gravity is applied for every state at the end of Update — don't do it twice here.
            transform.Rotate(Vector3.up, 70f * dt, Space.World);
        }

        private void TickTelegraph(float dt)
        {
            // Wind-up: hold, face the target, do not move — this is the dodge window.
            //
            // Only re-aim while it can still SEE him. Duck behind the tree mid-wind-up and the robot
            // keeps the angle it committed to, rather than tracking you through solid timber — which
            // is what it did before, and which quietly made cover useless in the one moment it
            // mattered most.
            if (target != null && _sight.HasSight)
            {
                Vector3 to = target.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            }
            // Pulse the tell so the wind-up reads.
            float t = Mathf.PingPong(_stateTimer * 6f, 1f);
            SetTell(Color.Lerp(idleTell, windupTell, t));

            if (_stateTimer >= telegraphTime)
            {
                _lungeDir = transform.forward;
                _dealtThisLunge = false;
                Current = State.Lunge;
                _stateTimer = 0f;
                SetTell(windupTell);
            }
        }

        /// <summary>What "commit to the attack" means for this kind (MV-293) — a melee dash for
        /// everything that closes to contact range, or the Gunner/Bomber's ranged payoff for the two
        /// kinds that don't. Same state, same Telegraph→Lunge→Recover shape; only the middle beat
        /// differs, which is what keeps a second and third ranged kind cheap to add later.</summary>
        private void TickLunge(float dt)
        {
            switch (Kind)
            {
                case EnemyKind.Gunner: TickBeam(dt); break;
                case EnemyKind.Bomber: TickMissileFire(dt); break;
                default:               TickMeleeLunge(dt); break;
            }
        }

        private void TickMeleeLunge(float dt)
        {
            CharacterControllerMotion.SafeMove(_cc, _lungeDir * lungeSpeed * dt); // MV-386
            if (!_dealtThisLunge) TryContactDamage();
            if (_stateTimer >= lungeTime)
            {
                Current = State.Recover;
                _stateTimer = 0f;
                SetTell(idleTell);
            }
        }

        /// <summary>Gunner's laser (MV-293): <see cref="_lungeDir"/> was locked the instant the
        /// telegraph ended (<see cref="TickTelegraph"/> re-aims live only up to that point) — this
        /// state never re-aims, it only checks whether the target is still standing in the beam it
        /// already committed to, and whether anything now blocks it. <see cref="contactDamage"/> is
        /// applied as damage PER SECOND while both hold, not as a single hit.</summary>
        private void TickBeam(float dt)
        {
            if (target != null && _sight.HasSight &&
                BeamGeometry.Hits(transform.position, _lungeDir, lungeRange, contactRadius, target.position))
            {
                _targetDamageable ??= target.GetComponent<IDamageable>();
                if (_targetDamageable != null && _targetDamageable.IsAlive)
                {
                    _targetDamageable.TakeDamage(new DamageInfo(
                        contactDamage * dt, transform.position, _lungeDir, Team.Enemy));
                }
            }

            if (_stateTimer >= lungeTime)
            {
                Current = State.Recover;
                _stateTimer = 0f;
                SetTell(idleTell);
            }
        }

        /// <summary>Bomber's homing missile (MV-293): fired once, on the first tick of the state —
        /// <see cref="_dealtThisLunge"/> is the same "already acted this cycle" flag the melee lunge
        /// uses to gate its contact damage to a single hit, reused here to gate the launch to one shot.
        /// The rest of the state is just the release beat before Recover.</summary>
        private void TickMissileFire(float dt)
        {
            if (!_dealtThisLunge)
            {
                _dealtThisLunge = true;
                if (target != null)
                    HomingMissile.Fire(transform.position, target, lungeSpeed, contactDamage, contactRadius);
            }

            if (_stateTimer >= lungeTime)
            {
                Current = State.Recover;
                _stateTimer = 0f;
                SetTell(idleTell);
            }
        }

        private void TickRecover(float dt)
        {
            if (_stateTimer >= recoverTime)
            {
                Current = State.Chase;
                _stateTimer = 0f;
            }
        }

        /// <summary>Whether <see cref="BlinkerSquadDirector"/> may draft this robot into a coordinated
        /// group jump (MV-366) right now: a Blinker, currently chasing (not mid-attack, mid-teleport,
        /// dead, or still walking out of its factory), that can actually see Max and is still outside
        /// melee range — the same fairness/range gate the solo blink in <see cref="TickChase"/> uses,
        /// so a squad jump never warps onto a player it has no business knowing the location of.</summary>
        public bool IsEligibleForGroupTeleport(Vector3 targetPos)
        {
            if (Kind != EnemyKind.Blinker || Current != State.Chase || !_sight.HasSight) return false;
            Vector3 to = targetPos - transform.position; to.y = 0f;
            return to.magnitude > lungeRange;
        }

        /// <summary>Drafts this robot into a squad jump (MV-366): the same charge-up/teleport/land
        /// beat as a solo blink (<see cref="TickTeleport"/> doesn't know or care which triggered it),
        /// just aimed at a shared destination the squad's coordinator already computed instead of this
        /// robot's own flank point. Returns false without side effects if it's no longer eligible —
        /// the coordinator checks first, but state can change between that check and this call.</summary>
        public bool TryBeginGroupTeleport(Vector3 destination)
        {
            if (Kind != EnemyKind.Blinker || Current != State.Chase) return false;

            _teleportTarget = destination;
            Current = State.Teleport;
            _stateTimer = 0f;
            SetTell(windupTell);
            return true;
        }

        /// <summary>The Blinker's blink (MV-293): a held charge-up (<see cref="telegraphTime"/> doing
        /// double duty as the teleport's own tell, since this kind is never mid-lunge and mid-teleport
        /// at once) then an instant reposition to the flank point <see cref="TickChase"/> already
        /// computed (or the shared point a squad jump drafted this robot toward — <see cref="TryBeginGroupTeleport"/>).
        /// Lands back in Chase, not straight into a lunge — it's now close enough that the very next
        /// tick reads the range and telegraphs normally, same as if it had walked there.</summary>
        private void TickTeleport(float dt)
        {
            if (_stateTimer < telegraphTime) return;

            Vector3 from = transform.position;

            // CharacterController owns its own internal position state; setting the transform directly
            // while it's enabled fights that on the next Move(). Disable around the jump so the
            // controller re-reads the new spot instead of resisting it.
            _cc.enabled = false;
            transform.position = _teleportTarget;
            _cc.enabled = true;

            // The reposition above is instant with nothing to see (MV-330) — HudSignals carries both
            // points so the VFX layer (CombatVfx) can play the surge/vanish/reappear beat without this
            // state machine knowing or caring what that beat looks like.
            HudSignals.EmitBlinkerTeleported(from, _teleportTarget);

            _teleportTimer = teleportCooldown;
            Current = State.Chase;
            _stateTimer = 0f;
            SetTell(idleTell);
        }

        private void TryContactDamage()
        {
            if (target == null) return;
            Vector3 to = target.position - transform.position; to.y = 0f;
            if (to.magnitude <= contactRadius)
            {
                _dealtThisLunge = true;
                _targetDamageable ??= target.GetComponent<IDamageable>();
                if (_targetDamageable != null && _targetDamageable.IsAlive)
                {
                    _targetDamageable.TakeDamage(
                        new DamageInfo(contactDamage, transform.position, _lungeDir, Team.Enemy));
                }
            }
        }

        private void FaceAndMove(Vector3 dir, float speed, float dt)
        {
            if (dir.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                CharacterControllerMotion.SafeMove(_cc, dir * speed * dt); // MV-386
            }
        }

        /// <summary>Remember the last piece of world geometry we walked into, so the chase can steer
        /// along it (YT-68). Ground contacts are ignored (they're not in the way), and so is anything
        /// with a CharacterController — Max, the boss and other robots are things to walk INTO, not
        /// around.</summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (Mathf.Abs(hit.normal.y) >= 0.5f) return;                       // floor/ramp, not a wall
            if (hit.collider.TryGetComponent<CharacterController>(out _)) return; // a character
            _wallNormal = hit.normal;
            _wallTimer = wallMemory;
        }

        private void ApplyGravity(float dt)
        {
            if (_cc.isGrounded && _verticalVel < 0f) _verticalVel = -2f;
            _verticalVel -= gravity * dt;
            CharacterControllerMotion.SafeMove(_cc, Vector3.up * _verticalVel * dt); // MV-386
        }

        // --- IDamageable ---
        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            // Friendly-fire rejection: an enemy never damages another enemy, whatever
            // path delivered the hit. Logged so any same-team source is visible.
            if (!DamageRules.Applies(info.Attacker, Team))
            {
                Debug.Log($"[RobotEnemy] rejected same-team damage from {info.Attacker} at {info.Point}");
                return;
            }
            _health -= info.Amount;
            // Floating damage number (YT-30 HUD). No-op if nothing is listening (tests).
            HudSignals.EmitDamage(transform.position, info.Amount);
            if (_health <= 0f) Die(info.Direction);
            else SetTell(Color.white); // brief hit flash; next state tick restores
        }

        private void Die(Vector3 fromDir)
        {
            Current = State.Dead;
            // Kill → HUD converts to XP + a SPARKS pickup and advances arena/boss (YT-30).
            // The death VFX also hangs off this signal (CombatVfx, YT-48).
            HudSignals.EmitEnemyKilled(transform.position);
            // Announce the death to the drop system (YT-131); it decides whether loot falls out of
            // this kind. The enemy stays ignorant of pickups — the policy lives in PickupDirector.
            MaxWorlds.Pickups.DropSignals.EmitRobotDied(transform.position, Kind);
            Died?.Invoke(this);
            gameObject.SetActive(false);
        }

        private void SetTell(Color c)
        {
            if (tellRenderer == null) return;
            tellRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_EmissionColor", c);
            tellRenderer.SetPropertyBlock(_mpb);
        }

    }
}
