using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

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

        /// <summary>Contact-damage cooldown for a non-lunging kind (MV-428), after any dev override —
        /// same live-read idiom as <see cref="EffectiveMoveSpeed"/>.</summary>
        private float EffectiveContactCooldown =>
            DevTuning.Or(DevTuning.ContactDamageCooldown, RobotCompositionTuning.DefaultContactCooldown);

        /// <summary>How many Rusher/Blinker-kind robots may hold an attack token at once (MV-428),
        /// after any dev override. Rounded — the panel's slider is continuous but a token count is
        /// not.</summary>
        private static int EffectiveLungeTokenCap => Mathf.Max(0, Mathf.RoundToInt(
            DevTuning.Or(DevTuning.LungeTokenCap, RobotCompositionTuning.DefaultLungeTokenCap)));

        /// <summary>How close this robot's centre may sit to Max's, XZ-plane only (MV-434) — the
        /// body-separation floor <see cref="EnemyBodySeparation"/> enforces every Chase/Lunge tick.
        /// Archetype-specific: a Heavy needs more clearance than a Rusher.</summary>
        private float MinBodyDistance => EnemyBodySeparation.MinDistance(
            EnemyArchetype.Of(Kind).ColliderRadius, EnemyArchetype.PlayerRadius);

        [Header("Lunge")]
        [SerializeField] private float lungeRange = 2.2f;     // start telegraph within this
        [SerializeField] private float telegraphTime = 0.55f; // wind-up (dodge window)
        [SerializeField] private float lungeSpeed = 11f;
        [SerializeField] private float lungeTime = 0.22f;
        [SerializeField] private float recoverTime = 0.7f;
        [SerializeField] private float contactDamage = 12f;
        [SerializeField] private float contactRadius = 1.0f;

        /// <summary>MV-428: Bruiser/Heavy/Brute's replacement for the lunge — damage per
        /// contact-cooldown tick while standing in <see cref="contactRadius"/> of Max. 0 for every
        /// kind that still lunges, which never reads it (see <see cref="TickContactTouch"/>).</summary>
        [SerializeField] private float touchDamage = 0f;

        [Header("Ranged / teleport (MV-293) — Gunner/Launcher/Blinker only, 0 for every melee kind")]
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
        public static void ResetRegistry()
        {
            _active.Clear();
            _separationGrid.Clear();   // MV-611: test/level-reset hygiene — see SeparationGrid's own doc comment
        }

        /// <summary>Scratch buffer for <see cref="EnemySeparation"/>'s neighbour lookup (MV-321) — one
        /// shared list, cleared and refilled per robot per tick, instead of a fresh allocation every
        /// frame for every chaser in a ~20-30 robot swarm. MV-611: now filled by
        /// <see cref="_separationGrid"/>'s <see cref="SeparationGrid.CollectNearby"/>, so it only ever
        /// holds this robot's own local neighbours, not the whole field-wide roster.</summary>
        private static readonly List<Vector3> _separationScratch = new List<Vector3>(32);

        /// <summary>Buckets the field-wide roster by position so a chasing robot's own neighbour lookup
        /// costs only as much as its own neighbourhood, not the whole accumulated population (MV-611) —
        /// see <see cref="SeparationGrid"/>'s own doc comment for why this is maintained incrementally
        /// (every active robot's own <see cref="Update"/> keeps its entry current) rather than rebuilt
        /// from scratch once a frame. Cell size is <see cref="EnemySeparation.DefaultMinDistance"/>, not
        /// the live dev-tuned <see cref="EffectiveMinSeparation"/>: a debug slider pushed well past the
        /// default could in principle widen a robot's search past one cell, but that's a dev-only
        /// steering nicety, not a shipped-gameplay correctness concern.</summary>
        private static readonly SeparationGrid _separationGrid = new SeparationGrid(EnemySeparation.DefaultMinDistance);

        public State Current { get; private set; } = State.Chase;
        public bool IsAlive => Current != State.Dead && _health > 0f;

        /// <summary>Concealed and unaware (MV-363) — placed behind cover, world-present from area
        /// load, but not yet chasing, firing or telegraphing. Ends the moment IT sees Max (MV-603:
        /// individually, never off another robot waking).</summary>
        public bool IsDormant => Current == State.Dormant;

        /// <summary>Which robot this is (YT-66). Set by <see cref="Apply"/>; the spawner pools by it,
        /// so a dead bruiser is never recycled as a rusher wearing the wrong body.</summary>
        public EnemyKind Kind { get; private set; } = EnemyKind.Rusher;

        /// <summary>Stamp this robot with an archetype's stats and reset it to fresh. Must be called
        /// after the component exists (Awake has already run and seeded the old defaults), so it
        /// re-runs <see cref="ResetState"/> to pick the new health up.</summary>
        public void Apply(in EnemyArchetype a)
        {
            Kind = a.Kind;
            // MV-473: Awake() attached the bar against the Rusher default (Apply hasn't run yet — see
            // that method's own doc comment), so a Heavy/Brute spawned with a Rusher-height anchor
            // would clip its own head for one frame and then jump. Re-anchor now that the real
            // archetype (and its real ColliderHeight) is known.
            _bar?.SetHeightAboveCentre(BarHeightFor(a));
            moveSpeed = a.MoveSpeed;
            maxHealth = a.MaxHealth;
            contactDamage = a.ContactDamage;
            contactRadius = a.ContactRadius;
            touchDamage = a.TouchDamage;
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

        /// <summary>Counts down to the next contact-damage tick for a non-lunging kind (MV-428).
        /// Seeded to a full <see cref="EffectiveContactCooldown"/> on reset — same "no free first hit"
        /// convention as <see cref="_teleportTimer"/> — and read/reset only by
        /// <see cref="TickContactTouch"/>.</summary>
        private float _contactCooldownTimer;

        /// <summary>True while this robot holds a slot in <see cref="LungeTokenPool"/> (MV-428) —
        /// only ever set for a Rusher/Blinker that is mid-Telegraph or mid-Lunge. Tracked per-instance
        /// so <see cref="Die"/>/<see cref="Despawn"/> can hand the token back even if death interrupts
        /// the attack before it reaches Recover.</summary>
        private bool _holdsAttackToken;

        /// <summary>Counts down to the next Force Field ram-drain (MV-586) — a robot body blocked by
        /// the bubble never reaches <see cref="TryContactDamage"/>/<see cref="TickContactTouch"/>, so
        /// without this the shield cost nothing to grind on forever. Rate-limited to this robot's own
        /// <see cref="recoverTime"/> (floored at <see cref="MinForceFieldRamInterval"/>), ticked down
        /// unconditionally in <see cref="Update"/> — same "no free first hit" seeding as
        /// <see cref="_contactCooldownTimer"/> would give, but zero here is correct: the very first
        /// contact with a freshly-raised bubble should cost it immediately, the same as ramming Max
        /// himself would.</summary>
        private float _forceFieldRamCooldownTimer;

        /// <summary>Floor on the ram rate limit (MV-586 spec) — a robot pushing against the bubble
        /// ticks the shield down at its own attack cadence, never faster than this even if its
        /// archetype's <see cref="recoverTime"/> is shorter.</summary>
        private const float MinForceFieldRamInterval = 0.5f;

        private MaterialPropertyBlock _mpb;
        private Vector3 _knockback;
        [Tooltip("How fast a spray shove bleeds off (m/s²). Higher = a shorter shove (YT-64).")]
        [SerializeField] private float knockbackDecay = 28f;

        /// <summary>Rounds cover and walls it presses into (YT-68), latched rather than a bare timer
        /// since MV-447 — see <see cref="WallLatch"/>'s own doc comment for the two bugs that lived in
        /// the timer this replaced.</summary>
        private readonly WallLatch _wallLatch = new WallLatch();

        /// <summary>Which room this robot counts itself as routing from right now (MV-447 cause 3) —
        /// see <see cref="ZoneHysteresis"/>'s own doc comment for the boundary-flip bug this fixes.</summary>
        private readonly ZoneHysteresis _zoneHysteresis = new ZoneHysteresis();

        /// <summary>Holds this robot to its current route decision for a minimum dwell (MV-477) —
        /// see <see cref="RouteDwell"/>'s own doc comment for the hedge-specific flip this fixes.
        /// </summary>
        private readonly RouteDwell _routeDwell = new RouteDwell();

        /// <summary>The last <see cref="EnemyNavigation.RouteEpoch"/> this robot has seen. A change
        /// means a gate opened or shut, or the level reset, since the last Chase tick — a genuinely
        /// new route decision that must not wait out <see cref="_routeDwell"/>'s own clock (MV-477).
        /// </summary>
        private int _routeEpoch;

        /// <summary>Throttles how often this robot's in-room <see cref="ZoneRouteGrid"/> step is
        /// re-solved (MV-611) — see <see cref="ZoneRouteBudget"/>'s own doc comment. Irrelevant for a
        /// kind that never sets <c>useZoneRoute</c> (<see cref="UsesGridRoute"/> false), which never
        /// touches it.</summary>
        private readonly ZoneRouteBudget _zoneRouteBudget = new ZoneRouteBudget();

        /// <summary>Below this fraction of <see cref="standoffRange"/>, a ranged kind backs off
        /// (MV-447 cause 4). Tuned against <see cref="StandoffCloseInFraction"/> to leave a band wide
        /// enough that ordinary chase jitter can't cross it twice in one tick.</summary>
        private const float StandoffBackOffFraction = 0.85f;

        /// <summary>Above this fraction of <see cref="standoffRange"/>, a ranged kind closes in
        /// (MV-447 cause 4). Between this and <see cref="StandoffBackOffFraction"/> it holds.</summary>
        private const float StandoffCloseInFraction = 1.15f;

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
            EnemyKind.Launcher => "LAUNCHER",   // MV-405 renamed the display only; MV-451 renamed the enum to match
            EnemyKind.Blinker => "BLINKER",
            EnemyKind.Bolter => "BOLTER",
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
            //
            // MV-473's de-clutter pass (WorldHealthBarDeclutter) is what now keeps a pile of these
            // always-on bars from stacking illegibly — see that class's own doc comment.
            _bar = WorldHealthBar.Attach(gameObject, this, BarHeightFor(EnemyArchetype.Rusher), BarWidth,
                                         alwaysShow: true);
        }

        private WorldHealthBar _bar;

        /// <summary>Metres above a robot's origin its bar floats — per archetype (MV-473), since a flat
        /// 1.15 m cleared a Rusher's 1.4 m collider with room to spare but barely cleared a Brute's
        /// 1.9 m one. <see cref="HeadClearance"/> is the same world-space margin for every kind; the
        /// actual on-screen daylight above that also depends on <see cref="WorldHealthBar"/>'s
        /// camera-space <c>ScreenClearance</c>, which is pitch-tuned once, shared by every bar in the
        /// game rather than duplicated per call site.</summary>
        private static float BarHeightFor(in EnemyArchetype a) => a.ColliderHeight * 0.5f + HeadClearance;
        private const float HeadClearance = 0.35f;
        private const float BarWidth = 1.5f;   // YT-136: wider so a flat, short bar still reads at 23 m zoom

        private void OnEnable()
        {
            _active.Add(this);
            ResetState(); // reset for pooling reuse
        }

        private void OnDisable()
        {
            _active.Remove(this);
            _separationGrid.Remove(GetInstanceID());   // MV-611: else a dead/pooled robot stays a phantom neighbour forever
        }

        /// <summary>Reset to a fresh, alive Chase state. Called from Awake/OnEnable and
        /// directly by tests (which don't get Unity lifecycle callbacks).</summary>
        public void ResetState()
        {
            _health = maxHealth;
            Current = State.Chase;
            _stateTimer = 0f;
            _wallLatch.Reset();       // a pooled robot doesn't inherit the last one's wall
            _zoneHysteresis.Reset();  // ...nor its idea of which room it was routing from
            _pursuitStall.NoteSightHeld(); // ...nor its idea of how well the last one was doing
            _routeDwell.Reset();       // ...nor its committed route decision (MV-477)
            _zoneRouteBudget.Reset();  // ...nor its cached in-room ZoneRouteGrid step (MV-611)
            _routeEpoch = EnemyNavigation.RouteEpoch;
            _knockback = Vector3.zero;
            _haltTimer = 0f;
            // Full cooldown, not zero: a freshly spawned Blinker gets the same beat as everything
            // else before its first attack, rather than an instant blink the moment it's born.
            _teleportTimer = teleportCooldown;
            // Same convention (MV-428): a fresh Bruiser/Heavy/Brute doesn't get a free hit the
            // instant it touches Max.
            _contactCooldownTimer = EffectiveContactCooldown;
            // A pooled robot must never hand back a token it doesn't hold — Die()/Despawn() already
            // released whatever this body was carrying in its last life before it got here.
            _holdsAttackToken = false;
            // A pooled robot must never inherit the last life's ram cooldown (MV-586) — its first
            // contact with a fresh bubble this life should cost it immediately.
            _forceFieldRamCooldownTimer = 0f;
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

            _forceFieldRamCooldownTimer = Mathf.Max(0f, _forceFieldRamCooldownTimer - dt);

            // MV-657: target can go null in ANY state, not only at spawn — Unity's fake-null makes a
            // destroyed target (a dead Sentinel this robot was retargeted to, MV-362) read as null here.
            // Re-acquire immediately, before the sight tick below, or the tick below stays permanently
            // skipped: _sight.HasSight freezes at whatever it last was, and a Dormant robot gated on it
            // (TickDormant/AmbushWake) can never wake again for the rest of its life.
            if (target == null) AcquireTarget();

            // Look, once, before deciding anything. Everything below reads the memory, never the
            // transform — the robot no longer knows where Max is, only where it last saw him.
            //
            // MV-611: a DORMANT robot whose own area is well behind the player can never see him or be
            // seen — the raycast is dead weight for exactly the residue population (concealed knots,
            // garrison never looked at, stragglers run past) that accumulates as the player advances.
            // Every other state still ticks sight live every frame; cover/chase correctness depends on
            // it being fresh, and only a robot standing still, unaware, and behind is ever this stale.
            bool skipSightForFarDormant = Current == State.Dormant && IsWellBehindPlayer();
            if (target != null && !skipSightForFarDormant)
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

            // MV-611: keeps this robot's own entry in the shared neighbour grid current every tick,
            // REGARDLESS of state — a Dormant/Telegraphing/Lunging robot must still be found by another
            // robot's separation query exactly as it was when _active was scanned directly; only
            // TickChase's own QUERY is state-gated (nothing but a chaser needs to ask). O(1) amortized —
            // see SeparationGrid.UpdatePosition's own doc comment.
            _separationGrid.UpdatePosition(GetInstanceID(), transform.position);
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
            // of cover, another robot, a corner of the shed. Either way it stops pushing and hands off
            // to Dormant (MV-603), not straight into Chase: a shed spawn is still unseen the instant
            // it clears the door, and it must wait for its own AmbushWake tick exactly like a placed
            // garrison/concealed member does, rather than start hunting Max unseen.
            if (to.sqrMagnitude <= EmergeArriveRadius * EmergeArriveRadius || _stateTimer >= EmergeTimeout)
            {
                BeginDormant();
                return;
            }

            // Deliberately slower than a chase. It is heaving itself out of a shed, not sprinting;
            // the step up to full speed as it clears the door is what sells the hand-off.
            FaceAndMove(to.normalized, EffectiveMoveSpeed * emergeSpeedScale, dt);
        }

        /// <summary>Rebases this robot's health/damage to a fresh archetype (MV-514) WITHOUT the reset
        /// <see cref="Apply"/> always does — no state change, no position change, nothing else touched.
        /// For a pre-placed garrison member sitting <see cref="State.Dormant"/> since before its area's
        /// gate broke, whatever <see cref="DifficultyDirector.ToughnessMultiplier"/> was live back at
        /// placement time is stale by the time it wakes; this lets the caller re-stamp it with the
        /// multiplier live right now, immediately before <see cref="Activate"/>, without reverting it to
        /// a fresh Chase state the way <see cref="Apply"/>'s own <see cref="ResetState"/> call would.
        /// Full health, not a fraction carried over — a dormant robot has never been in a fight yet, so
        /// there is no damage to preserve.</summary>
        public void Retoughen(in EnemyArchetype a)
        {
            maxHealth = a.MaxHealth;
            contactDamage = a.ContactDamage;
            touchDamage = a.TouchDamage;
            _health = maxHealth;
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
        /// — called here the instant <see cref="AmbushWake"/> says both the camera and the sight-line
        /// agree (ticked unconditionally above, same as every other state). MV-603: this is the ONLY
        /// way a robot wakes now — the MV-363 group chain-wake that used to also call this on a
        /// groupmate's behalf is retired; each robot answers for its own sighting alone.
        ///
        /// MV-478: sight alone used to be the whole test, but <see cref="MaxWorlds.Arena.LineOfSight"/>
        /// is symmetric geometry — "this robot can see Max" and "Max can see this robot" are the same
        /// fact, so a sight-only gate woke every dormant robot the instant the player walked within
        /// raycast range, whether or not the player had ever looked at it. Requiring the camera
        /// frustum too is what makes waking mean "the player's own view fell on it".</summary>
        private void TickDormant()
        {
            // MV-611: a robot two or more areas behind the player's own can never be the one the
            // camera falls on (the fixed ~72 degree top-down rig never reaches back that far) — skip
            // the frustum test entirely rather than run it every frame only to read false forever.
            if (IsWellBehindPlayer()) return;
            if (AmbushWake.ShouldWake(IsOnScreen(), _sight.HasSight)) Activate();
        }

        /// <summary>How many areas behind the player's own a robot's area must be before it counts as
        /// "well behind" (MV-611) — one area of slack for a robot standing near a just-crossed doorway,
        /// which the player could plausibly still glance back through.</summary>
        private const int WellBehindAreaSlack = 2;

        /// <summary>True when this robot's own area is <see cref="WellBehindAreaSlack"/> or more areas
        /// earlier than wherever the player is standing RIGHT NOW (MV-611) — read from live position on
        /// both sides, the same "not AreaAccumulationDirector.CurrentArea" idiom
        /// <c>WorldRunner.ResolveDeathArea</c> uses, since that tracker advances ahead of the player for
        /// population purposes and would gate this off a room the player hasn't actually reached yet.
        /// Fails CLOSED (false) whenever the area can't be resolved — no map, no player, an unrecognised
        /// zone — so a missing signal never freezes a robot that might otherwise need to wake.</summary>
        private bool IsWellBehindPlayer()
        {
            MapData map = EnemyNavigation.Map;
            if (map == null || _playerTarget == null) return false;

            MapZone robotZone = map.ZoneAt(transform.position.x, transform.position.z);
            MapZone playerZone = map.ZoneAt(_playerTarget.position.x, _playerTarget.position.z);
            if (robotZone == null || playerZone == null) return false;

            int robotArea = AreaAccumulationDirector.AreaIndexOf(robotZone.id);
            int playerArea = AreaAccumulationDirector.AreaIndexOf(playerZone.id);
            if (robotArea <= 0 || playerArea <= 0) return false;

            return robotArea <= playerArea - WellBehindAreaSlack;
        }

        /// <summary>Reused every call (MV-527) — <see cref="GeometryUtility.CalculateFrustumPlanes(Camera)"/>
        /// allocates a fresh <c>Plane[6]</c> every time it's called, and this runs once per DORMANT
        /// robot per frame (<see cref="TickDormant"/>); MV-514 doubled the dormant population, which is
        /// what turned this from a rounding error into GC hitching. The non-allocating overload writes
        /// into this buffer instead.</summary>
        private static readonly Plane[] s_frustumPlanes = new Plane[6];

        /// <summary>Whether this robot's body centre sits inside the gameplay camera's frustum right
        /// now (MV-478). Fails OPEN (true) when no camera can be resolved — headless build, EditMode
        /// fixture — so a missing camera can never leave a level full of frozen robots (AC8). Same
        /// frustum-test idiom as <see cref="AreaAccumulationDirector"/>'s own on-screen check.</summary>
        private bool IsOnScreen()
        {
            _frustumTestCount++;   // MV-611 test instrumentation — see IsWellBehindPlayer's own doc comment
            Camera cam = Camera.main;
            if (cam == null) return true;
            GeometryUtility.CalculateFrustumPlanes(cam, s_frustumPlanes);
            return GeometryUtility.TestPlanesAABB(s_frustumPlanes, new Bounds(transform.position, Vector3.one));
        }

        /// <summary>How many times THIS robot's own <see cref="IsOnScreen"/> has run — test-only
        /// instrumentation (MV-611) proving <see cref="TickDormant"/> skips the frustum test entirely
        /// for a robot well behind the player, rather than merely reading it as false.</summary>
        private int _frustumTestCount;

        /// <summary>Wakes a dormant robot into the short "waking up" beat (<see cref="TickAlert"/>)
        /// before it joins the chase for real. Idempotent — a robot no longer Dormant ignores a
        /// second call, which is what lets <see cref="AreaAccumulationDirector.ActivateGarrisonFor"/>
        /// call this unconditionally on every pre-placed garrison member when the area's own gate
        /// breaks, without first checking which ones are still asleep.</summary>
        public void Activate()
        {
            if (Current != State.Dormant) return;
            Current = State.Alert;
            _stateTimer = 0f;
            SetTell(windupTell);
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
            // MV-447 cause 3: ask the map which room this robot's raw position is in, but route from
            // the hysteresis-settled answer, not the raw one — a robot straddling a zone boundary
            // flips the raw answer frame to frame, and the two rooms it flips between can route to
            // materially different waypoints.
            MapZone rawZone = EnemyNavigation.Map?.ZoneAt(transform.position.x, transform.position.z);
            string routedZoneId = _zoneHysteresis.Resolve(rawZone?.id, dt);
            Vector3 waypoint = EnemyNavigation.Waypoint(transform.position, goal, routedZoneId,
                UsesGridRoute(Kind), _zoneRouteBudget, dt);

            // MV-477 AC3: the un-fanned route point, so "reached the waypoint" means reached the
            // actual doorway/goal the level routed at, not wherever EnemyFormation happened to fan this
            // one robot toward.
            Vector3 routeWaypoint = waypoint;

            // Its own lane, so a pack arrives as a fan rather than a queue. The last leg is the
            // wide, flanker-aware fan onto the real goal; an earlier leg is a doorway, which gets
            // its own narrower fan (MV-449) — wide enough to break up the queue on approach,
            // narrow enough (and everyone, flankers included) to still funnel through the gap.
            // MV-493: compare rooms, not the waypoint position — with useZoneRoute on, ZoneRouteGrid
            // may substitute a cell-centre step even on the final leg (cover between here and the
            // goal, in the goal's own room), and a position comparison against `waypoint` reads that
            // as "not the last leg" and hands back the narrow doorway fan instead of the wide one.
            MapZone routedZone = EnemyNavigation.Map?.Zone(routedZoneId);
            MapZone goalZone = EnemyNavigation.Map?.ZoneAt(goal.x, goal.z);
            bool lastLeg = routedZone == null || goalZone == null || routedZone.id == goalZone.id;
            waypoint = lastLeg
                ? EnemyFormation.ApproachPoint(goal, transform.position, GetInstanceID())
                : EnemyFormation.ApproachPoint(waypoint, transform.position, GetInstanceID(),
                    EnemyFormation.DoorwaySpread, EnemyFormation.DoorwayFullSpreadAt);

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
            // MV-611: the old copy here was unconditional — every chaser copied the WHOLE field-wide
            // roster (residue included: concealed knots, garrison never looked at, stragglers run
            // past), then Push() ran its own magnitude pass a second time over that same full copy to
            // find the few that were actually close — O(n) work per chaser, O(n^2) total across the
            // field. _separationGrid is maintained incrementally (every active robot's own Update, not
            // just chasers) so this query only ever visits the 3x3 cell block around this robot — both
            // the scan AND Push()'s more expensive per-neighbour work below are bounded by local
            // density, not by how large the level's accumulated population has grown.
            _separationGrid.CollectNearby(GetInstanceID(), transform.position, EffectiveMinSeparation, _separationScratch);
            Vector3 separation = EnemySeparation.Push(transform.position, _separationScratch, EffectiveMinSeparation);
            dir = EnemySeparation.Steer(dir, separation);

            // The lawn has cover in it (YT-68). Beelining (or being shoved by a crowded neighbour)
            // into a prop just presses against it, so while a wall is latched, walk along it and
            // round the corner instead — the last word on direction, so nothing steered in above it
            // can ever send this robot back into the wall it's already rounding. MV-447 causes 1/2:
            // see WallLatch's own doc comment for the limit cycle and same-frame race this replaced.
            dir = _wallLatch.Tick(dir, transform.position, dt, _preferSign);

            // MV-477: bound how often the route decision itself may flip. None of WallLatch,
            // ObstacleSteering or ZoneHysteresis above puts a commit window on their combined RESULT,
            // only on their own inputs — a hedge (collider, but deliberately off the Cover layer,
            // MV-400) slips through every one of those guards and flips `dir` outright as sight
            // flickers through the gap. Bypassed the instant this frame is a genuinely new decision
            // rather than a re-litigation of the current one: the route waypoint was just reached, or
            // a gate change/level reset invalidated the route since the last tick.
            int routeEpoch = EnemyNavigation.RouteEpoch;
            bool routeInvalidated = routeEpoch != _routeEpoch;
            _routeEpoch = routeEpoch;
            bool waypointReached = (routeWaypoint - transform.position).sqrMagnitude <= arriveRadius * arriveRadius;
            dir = _routeDwell.Resolve(dir, dt, forceImmediate: waypointReached || routeInvalidated);

            // Gunner/Launcher (MV-293): the answer to a ranged kind must never be "walk at it" — inside
            // its standoff band it backs off along the same line it was closing on, rather than
            // committing to melee range like everything else in the swarm.
            //
            // MV-447 cause 4: a bare `dist < standoffRange` threshold flipped `dir` by a full 180
            // degrees the instant dist crossed it, so a robot sitting exactly at standoffRange
            // alternated advance/retreat every frame by construction. Replaced with a band: back off
            // below the inner edge, close in above the outer edge, and inside the band hold position
            // (speed 0, still facing Max via `to`) — no distance now produces a frame-to-frame flip.
            bool ranged = Kind == EnemyKind.Gunner || Kind == EnemyKind.Launcher || Kind == EnemyKind.Bolter;
            bool inStandoffBand = false;
            bool retreating = false;
            if (ranged && standoffRange > 0f && _sight.HasSight)
            {
                if (dist < standoffRange * StandoffBackOffFraction) { dir = -dir; retreating = true; }
                else if (dist <= standoffRange * StandoffCloseInFraction) { dir = to.normalized; inStandoffBand = true; }
            }

            bool hunting = !_sight.HasSight;
            float speed = EffectiveMoveSpeed;

            // MV-434: a non-lunging kind (Bruiser/Heavy/Brute) presses against Max rather than
            // pushing through him — stop closing once already at the body-separation distance,
            // but keep facing him (FaceAndMove below still runs) and keep TickContactTouch running
            // so its cooldown keeps ticking while it stands in contact.
            if (!LungesAsKind(Kind) && dist <= MinBodyDistance) speed = 0f;

            // MV-447 cause 4: holding in the standoff band means standing still, not creeping — `dir`
            // is already the face-Max direction set above, so this only zeroes the move.
            if (inStandoffBand) speed = 0f;

            FaceAndMove(dir, hunting ? speed * searchSpeedScale : speed, dt);

            // MV-434: whatever FaceAndMove just did, this robot's centre may never end the tick
            // closer to Max's CURRENT position than MinBodyDistance — Physics.IgnoreCollision (see
            // EnemySpawner.LetThePlayerThrough) leaves nothing else to stop it. Measuring against
            // Max's live position, not the stale goal above, is what lets a robot standing its
            // ground get shoved aside as Max walks into it, rather than ever pinning him.
            ClampBodySeparation();

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

            // MV-428 Change 1: Bruiser/Heavy/Brute never wind up at all any more — "a wardrobe should
            // not leap". They just keep walking and hit on a cooldown the instant they're in touch
            // range, checked every Chase tick rather than through a Telegraph/Lunge/Recover cycle
            // that no longer has a tell worth keeping for this kind.
            if (!LungesAsKind(Kind))
            {
                TickContactTouch(dt);
                return;
            }

            // Only wind up at something you can actually SEE. Without this a robot lunges at the
            // tree Max is standing behind, which looks broken and is free damage for the player.
            //
            // A ranged kind also withholds the wind-up until it has actually opened its standoff gap
            // (MV-293) — without this check it "retreats" for exactly one frame and then fires from
            // point-blank anyway, since Telegraph holds position and dist <= lungeRange was already
            // true before it took that one step back.
            if (_sight.HasSight && dist <= lungeRange && !retreating)
            {
                // MV-428 Change 2: Rusher/Blinker must hold an attack token to commit. Without one, a
                // robot just keeps closing and pressuring at normal move speed (exactly what the
                // FaceAndMove call above this frame already did) and tries again next tick — it takes
                // a token the instant one frees rather than queueing for a specific turn.
                bool needsToken = NeedsAttackToken(Kind);
                if (needsToken && !LungeTokenPool.TryAcquire(EffectiveLungeTokenCap)) return;

                _holdsAttackToken = needsToken;
                Current = State.Telegraph;
                _stateTimer = 0f;
                SetTell(windupTell);   // visual tell: dodge window opens
            }
        }

        /// <summary>Whether <paramref name="kind"/> still telegraphs and commits to a Lunge (MV-428).
        /// False for Bruiser/Heavy/Brute, which lose the state entirely — see <see cref="TickChase"/>
        /// and <see cref="TickContactTouch"/>.</summary>
        private static bool LungesAsKind(EnemyKind kind) =>
            kind != EnemyKind.Bruiser && kind != EnemyKind.Heavy && kind != EnemyKind.Brute;

        /// <summary>Whether <paramref name="kind"/> routes around this zone's own cover instead of
        /// beelining across it (MV-476) — the touch-damage archetypes only: Rusher, Bruiser, Heavy,
        /// Brute. Gunner and Launcher keep their standoff steering and Blinker keeps its teleport-flank
        /// steering unchanged — the ticket's scope names all three "ranged" and explicitly excludes
        /// them, even though Blinker eventually melees once it lands.</summary>
        private static bool UsesGridRoute(EnemyKind kind) =>
            kind == EnemyKind.Rusher || kind == EnemyKind.Bruiser
                                      || kind == EnemyKind.Heavy || kind == EnemyKind.Brute;

        /// <summary>Whether <paramref name="kind"/> is gated by <see cref="LungeTokenPool"/> (MV-428
        /// Change 2) — only the two kinds that keep the raw dash: Rusher and Blinker. Gunner/Launcher
        /// also reach <see cref="State.Telegraph"/>/<see cref="State.Lunge"/> but are explicitly out
        /// of scope ("they never lunged" — the ticket means never committed a melee dash) and stay
        /// uncapped.</summary>
        private static bool NeedsAttackToken(EnemyKind kind) =>
            kind == EnemyKind.Rusher || kind == EnemyKind.Blinker;

        /// <summary>Bruiser/Heavy/Brute's whole attack (MV-428 Change 1): no wind-up, no commit — just
        /// <see cref="touchDamage"/> on <see cref="EffectiveContactCooldown"/> while standing within
        /// <see cref="contactRadius"/> of Max, ticked every Chase frame regardless of range so the
        /// cooldown keeps counting down while it's still closing.</summary>
        private void TickContactTouch(float dt)
        {
            _contactCooldownTimer -= dt;
            if (target == null || _contactCooldownTimer > 0f) return;

            Vector3 to = target.position - transform.position; to.y = 0f;
            if (to.magnitude > contactRadius) return;

            _contactCooldownTimer = EffectiveContactCooldown;
            _targetDamageable ??= target.GetComponent<IDamageable>();
            if (_targetDamageable != null && _targetDamageable.IsAlive)
            {
                _targetDamageable.TakeDamage(
                    new DamageInfo(touchDamage, transform.position, to.normalized, Team.Enemy));
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
                    RotateToward(to.normalized, dt);
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
        /// everything that closes to contact range, or the Gunner/Launcher's ranged payoff for the two
        /// kinds that don't. Same state, same Telegraph→Lunge→Recover shape; only the middle beat
        /// differs, which is what keeps a second and third ranged kind cheap to add later.</summary>
        private void TickLunge(float dt)
        {
            switch (Kind)
            {
                case EnemyKind.Gunner: TickBeam(dt); break;
                case EnemyKind.Launcher: TickMissileFire(dt); break;
                case EnemyKind.Bolter: TickBolt(dt); break;
                default:               TickMeleeLunge(dt); break;
            }
        }

        private void TickMeleeLunge(float dt)
        {
            CharacterControllerMotion.SafeMove(_cc, _lungeDir * lungeSpeed * dt); // MV-386
            if (!_dealtThisLunge) TryContactDamage();
            // MV-434: contact damage above reads the real, possibly-overlapping position the dash
            // just reached — the clamp only settles where the robot ends the TICK, so a lunge's
            // hit is never lost to it. Same clamp as TickChase, so a lunge ends against Max's body
            // rather than inside it.
            ClampBodySeparation();
            if (_stateTimer >= lungeTime) EnterRecover();
        }

        /// <summary>Leave Telegraph/Lunge for Recover, handing back this robot's attack token
        /// (MV-428) if it's holding one — the release side of <see cref="LungeTokenPool"/>'s
        /// acquire in <see cref="TickChase"/>. A no-op for every kind that never needed a token
        /// (<see cref="_holdsAttackToken"/> is false for them), so this is safe to call from every
        /// Lunge variant unconditionally.</summary>
        private void EnterRecover()
        {
            if (_holdsAttackToken)
            {
                LungeTokenPool.Release();
                _holdsAttackToken = false;
            }
            Current = State.Recover;
            _stateTimer = 0f;
            SetTell(idleTell);
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

            if (_stateTimer >= lungeTime) EnterRecover();
        }

        /// <summary>Launcher's homing missile (MV-293): fired once, on the first tick of the state —
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

            if (_stateTimer >= lungeTime) EnterRecover();
        }

        /// <summary>Bolter's straight-line bolt (MV-539, retargeting fixed MV-622): fired once, on the
        /// first tick of the state — the same "already acted this cycle" gate <see cref="TickMissileFire"/>
        /// uses to gate its own launch to one shot. Aimed at <see cref="target"/>, the MV-362 retargeting
        /// rule's own current answer — the engaged Sentinel while <see cref="_engagedSentinel"/> is set,
        /// Max otherwise — exactly like every other kind's ranged fire already follows it.</summary>
        private void TickBolt(float dt)
        {
            if (!_dealtThisLunge)
            {
                _dealtThisLunge = true;
                if (target != null)
                    BolterBolt.Fire(transform.position, target, lungeSpeed, lungeRange, contactRadius);
            }

            if (_stateTimer >= lungeTime) EnterRecover();
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
                RotateToward(dir, dt);
                CharacterControllerMotion.SafeMove(_cc, dir * speed * dt); // MV-386
            }
        }

        /// <summary>Turns toward <paramref name="dir"/> at a capped rate (MV-434) instead of
        /// snapping instantly via <c>Quaternion.LookRotation</c> — the snap is what read as a rapid
        /// spin once a robot pinned against Max made <paramref name="dir"/> numerically unstable
        /// frame to frame. Capping the RATE means an unreliable direction still reads as a fast
        /// turn, however often it flips, rather than a strobe.</summary>
        private void RotateToward(Vector3 dir, float dt)
        {
            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, EnemyBodySeparation.DefaultMaxTurnDegreesPerSecond * dt);
        }

        /// <summary>Enforces <see cref="MinBodyDistance"/> against Max's CURRENT position (MV-434)
        /// — never against wherever this robot was chasing toward, which may already be stale.
        /// Disables the <see cref="CharacterController"/> around the correction, same idiom as
        /// <see cref="TickTeleport"/>'s reposition, so it doesn't fight the controller's own
        /// internal position state on the next <c>Move()</c>.</summary>
        private void ClampBodySeparation()
        {
            if (_playerTarget == null) return;
            Vector3 corrected = EnemyBodySeparation.Clamp(transform.position, _playerTarget.position, MinBodyDistance);
            if (corrected == transform.position) return;
            _cc.enabled = false;
            transform.position = corrected;
            _cc.enabled = true;
        }

        /// <summary>Remember the last piece of world geometry we walked into, so the chase can steer
        /// along it (YT-68). Ground contacts are ignored (they're not in the way), and so is anything
        /// with a CharacterController — Max, the boss and other robots are things to walk INTO, not
        /// around.</summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (Mathf.Abs(hit.normal.y) >= 0.5f) return;                       // floor/ramp, not a wall
            if (hit.collider.TryGetComponent<CharacterController>(out _)) return; // a character
            HandleWallContact(hit.collider, hit.normal);
        }

        /// <summary>The actual wall-contact reaction, split out from <see cref="OnControllerColliderHit"/>
        /// so an EditMode test can drive it directly with a bare <see cref="Collider"/> — Unity's
        /// <see cref="ControllerColliderHit"/> has no public constructor. MV-586: a robot's body blocked
        /// by the active Force Field bubble never reaches Max's contact radius, so it never drains the
        /// shield through the ordinary damage path — the bubble is the wall, so this is the only seam
        /// that ever sees the ram. Rate-limited to this robot's own attack cadence
        /// (<see cref="_forceFieldRamCooldownTimer"/>) so a robot pressed against the bubble ticks it
        /// down at that pace, not per frame. Steering (<see cref="_wallLatch"/>) is unaffected either
        /// way — the bubble keeps physically blocking exactly as before.</summary>
        private void HandleWallContact(Collider collider, Vector3 normal)
        {
            if (_forceFieldRamCooldownTimer <= 0f &&
                collider.TryGetComponent<ForceFieldBubble>(out var bubble))
            {
                bubble.ReportRam(contactDamage);
                _forceFieldRamCooldownTimer = Mathf.Max(recoverTime, MinForceFieldRamInterval);
            }
            _wallLatch.NoteHit(normal);
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
            // MV-428: death mid-Telegraph/Lunge must not leak the attack token — nothing else on
            // this path ever visits Recover to release it.
            if (_holdsAttackToken) { LungeTokenPool.Release(); _holdsAttackToken = false; }
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

        /// <summary>Remove this robot WITHOUT counting it as a kill (MV-427): no kill signal, no loot,
        /// no death VFX — just the same <see cref="Died"/>/pooling handshake <see cref="Die"/> ends
        /// on, so <c>AreaAccumulationDirector</c> still frees its spawn-queue slot and reclaims the
        /// instance. What wipes an arena's robots when Max dies in it and the room resets to its
        /// authored composition; a robot vanishing this way was never "defeated", so it must not
        /// grant a kill count, cells, or a part.</summary>
        public void Despawn()
        {
            if (!IsAlive) return;
            if (_holdsAttackToken) { LungeTokenPool.Release(); _holdsAttackToken = false; } // MV-428
            Current = State.Dead;
            _health = 0f;
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
