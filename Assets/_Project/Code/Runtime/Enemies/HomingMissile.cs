using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The Launcher's slow homing missile (MV-293). Free-flying — not pooled, not parented to the
    /// spawner — because it's short-lived and self-destroys on impact or timeout, the same "one and
    /// done" lifetime as the death VFX it plays a similar role to.
    ///
    /// Homing is deliberately GENTLE (<see cref="TurnRateDegPerSec"/>): a player who is actually
    /// moving can juke it, which is the whole point of a "slow homing" threat over a hitscan one — it
    /// pressures you into not standing still, it doesn't guarantee a hit the way a perfectly-tracking
    /// projectile would.
    ///
    /// MV-349: a missile that outlasts its fuel budget without reaching the target no longer just
    /// vanishes. It sputters, drops, bounces along the ground with decaying energy, then explodes —
    /// see <see cref="FlightState"/> and <see cref="TickSputtering"/>/<see cref="TickBouncing"/>.
    /// </summary>
    public sealed class HomingMissile : MonoBehaviour
    {
        private const float TurnRateDegPerSec = 90f;
        private const float ContactRadius = 0.5f;

        /// <summary>Seconds of fuel before a missile that hasn't reached its target gives up. Same
        /// total budget the missile always had as its hard timeout (MV-293) — only what happens AT
        /// the timeout changed, from an instant silent Destroy to the sputter/bounce/boom below.</summary>
        private const float FuelBudget = 6f;

        /// <summary>How high the missile flies. Without an explicit altitude it travels at whatever Y
        /// its launcher's root sits at — effectively the ground — which would leave the fuel-out
        /// bounce nothing to fall FROM. A nominal flight height gives "sputters, drops, bounces"
        /// something to actually read as a drop.</summary>
        private const float FlightHeight = 1.0f;

        /// <summary>Thrust cutting out (MV-349): a brief coast-and-decelerate beat between giving up
        /// the chase and starting to fall, so the drop reads as a failure rather than a mode switch.</summary>
        private const float SputterDuration = 0.35f;

        private const float Gravity = 20f;

        /// <summary>Energy kept per bounce — comedic and decaying, not a rubber ball.</summary>
        private const float BounceRestitution = 0.55f;

        /// <summary>Below this vertical speed on landing, the missile has nothing left to hop with —
        /// that landing is the last one.</summary>
        private const float MinBounceSpeed = 1.5f;

        private const int MaxBounces = 3;
        private const float GroundY = 0f;

        /// <summary>MV-508: short motion smear, not a ribbon — see <see cref="BuildTrail"/>.</summary>
        private const float TrailTime = 0.12f;
        private const float TrailWidth = 0.16f;

        private enum FlightState { Flying, Sputtering, Bouncing, Detonated }

        private Transform _target;
        private IDamageable _targetDamageable;
        private float _speed;
        private float _damage;
        private float _splashRadius;
        private float _age;
        private float _stateTimer;
        private FlightState _state;
        private Vector3 _bounceVelocity;
        private int _bounceCount;
        private TrailRenderer _trail;

        /// <summary>Launch one missile from <paramref name="origin"/> toward <paramref name="target"/>.
        /// <paramref name="damage"/>/<paramref name="splashRadius"/> come straight off the Launcher's
        /// <see cref="EnemyArchetype.ContactDamage"/>/<see cref="EnemyArchetype.ContactRadius"/> — same
        /// numbers, ranged-attack meaning.</summary>
        public static HomingMissile Fire(Vector3 origin, Transform target, float speed, float damage,
            float splashRadius)
        {
            var go = new GameObject("HomingMissile (stand-in)");
            go.transform.position = origin + Vector3.up * FlightHeight;

            Vector3 aim = target != null ? target.position - origin : Vector3.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-4f) aim = Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);

            BuildVisual(go.transform);

            var missile = go.AddComponent<HomingMissile>();
            missile.Init(target, speed, damage, splashRadius);
            return missile;
        }

        /// <summary>MV-405: Lee's explicit colour call — the missile body matches the Launcher's own
        /// body colour exactly. Read live off <see cref="CharacterSkin.BaseColorFor"/> rather than a
        /// copied literal, which is what the ticket's "don't introduce a second, slightly-different
        /// blue" actually requires: a literal drifts the moment the archetype's colour is retuned, a
        /// live read cannot. (Supersedes the MV-377 dark rust-copper this used to be — that colour
        /// existed only because the missile had no archetype identity of its own yet.)</summary>
        private static Color ShaftColor => CharacterSkin.BaseColorFor(CharacterSkin.RoleFor(EnemyKind.Launcher));

        /// <summary>Exposes <see cref="ShaftColor"/> for <c>HomingMissileTests</c> (MV-405), same shape
        /// as <see cref="TipColorForTests"/>.</summary>
        public static Color ShaftColorForTests => ShaftColor;

        /// <summary>MV-405: the warhead band at the nose, recoloured from the general warm "incoming"
        /// tell (rust/orange, shared with every other telegraph in the game) to a distinct, saturated
        /// red — Lee's explicit ask, and the one accent colour that still reads as ordnance rather than
        /// as "another instance of the game's universal wind-up glow". Peak channel held at the same
        /// <see cref="SunlitAlbedo.Ceiling"/> (0.6) headroom every other pulled palette entry in this
        /// project uses, so it doesn't self-bloom and wash out under the yard's key light.</summary>
        private static readonly Color TipColor = new Color(0.58f, 0.05f, 0.05f);

        /// <summary>Exposes <see cref="TipColor"/> for <c>HomingMissileTests</c> (MV-405 AC3) — the
        /// same "public static accessor onto a private palette constant" shape as
        /// <see cref="MaxWorlds.VFX.CharacterSkin.BaseColorFor"/>.</summary>
        public static Color TipColorForTests => TipColor;

        /// <summary>
        /// A slim missile — shaft, tail fins, a warhead band — replacing the plain sphere "ball" this
        /// used to fire (MV-329's AC2). The shaft is a Capsule rotated onto the object's own forward
        /// axis, so it always points the way it's flying without any per-frame work: <see cref="Update"/>
        /// already keeps <c>transform.rotation</c> aimed along the flight path, and everything built here
        /// is a child in that same local space.
        /// </summary>
        private static void BuildVisual(Transform parent)
        {
            // MV-350: none of Shaft/WarheadBand/Fin below is IDamageable, so with nothing else marking
            // them, RuntimeSurfaceDirector's sweep claimed them one frame after spawn and overwrote this
            // deliberate gunmetal/warn paint with the generic white world-prop material — the same tan
            // that hit the robots, but on every missile fired rather than only recycled ones. This marks
            // the whole missile as arriving with its materials attached, exactly like imported art.
            parent.gameObject.AddComponent<KeepsOwnMaterial>();
            BuildTrail(parent);

            Material shaftMat = MaterialLibrary.Tinted(SurfaceKind.Metal, ShaftColor);
            Material tipMat = MaterialLibrary.Tinted(SurfaceKind.Metal, TipColor);

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shaft.name = "Shaft";
            Strip(shaft);
            shaft.transform.SetParent(parent, false);
            // The capsule's long axis is local Y; rotating 90° about X lays it onto local Z, which is
            // this object's forward — the same trick every beam/tube part in the game uses to point a
            // cylinder primitive down its own travel direction.
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // MV-432: doubled from (0.11, 0.30, 0.11) — at the fixed 72° camera the missile is seen
            // nearly end-on, so cross-section (not length) is most of what reaches the screen, and the
            // old thickness read as a needle. Length (Y) is untouched.
            Vector3 shaftScale = new Vector3(0.22f, 0.30f, 0.22f);
            shaft.transform.localScale = shaftScale;
            if (shaftMat != null) shaft.GetComponent<MeshRenderer>().sharedMaterial = shaftMat;

            // A red tip band at the nose (MV-405) — the AC's "reads as ordnance", not just "reads as a
            // stick", and now also the ticket's explicit red-tip call: a distinct coloured section at
            // the front end only, not a full recolour of the airframe.
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = "WarheadBand";
            Strip(band);
            band.transform.SetParent(parent, false);
            band.transform.localPosition = new Vector3(0f, 0f, 0.34f);
            band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // MV-432: doubled alongside the shaft so the band stays proportional to the fatter body.
            band.transform.localScale = new Vector3(0.26f, 0.05f, 0.26f);
            if (tipMat != null) band.GetComponent<MeshRenderer>().sharedMaterial = tipMat;

            // Tail fins — two flat vanes at the back, the part of the silhouette that says "missile"
            // rather than "dropped tool" even at gameplay zoom.
            // MV-432: fin scale doubled on X/Y alongside the shaft so the vanes stay proportional to
            // the fatter body.
            Vector3 finScale = new Vector3(0.04f, 0.32f, 0.12f);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fin.name = "Fin";
                Strip(fin);
                fin.transform.SetParent(parent, false);
                // MV-432: derived, not a literal — this is what puts the fin's inner face exactly on
                // the shaft surface (previously a 0.035 gap left two loose slivers, the same
                // detached-geometry class as the MV-430 gear teeth).
                float finOffsetX = 0.5f * (shaftScale.x + finScale.x);
                fin.transform.localPosition = new Vector3(side * finOffsetX, 0f, -0.26f);
                fin.transform.localScale = finScale;
                if (shaftMat != null) fin.GetComponent<MeshRenderer>().sharedMaterial = shaftMat;
            }
        }

        /// <summary>MV-508: a fast object with a hard aliased silhouette and nothing connecting one
        /// frame's position to the next reads as strobing rather than motion — this is the standard
        /// fix. Short on purpose (<see cref="TrailTime"/> 0.10-0.15s): a motion smear, not a ribbon.
        /// Sourced from <see cref="MaterialLibrary"/> like the rest of the missile's own visual, so it
        /// participates in the same tint/caching rules rather than being a one-off VFX material; the
        /// root already carries <see cref="KeepsOwnMaterial"/>, which covers this too.</summary>
        private static void BuildTrail(Transform parent)
        {
            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = TrailTime;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = TrailWidth;
            trail.minVertexDistance = 0.03f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, ShaftColor);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(ShaftColor, 0f), new GradientColorKey(ShaftColor, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            trail.Clear();
        }

        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            // manual proximity check in Update, not physics — same Application.isPlaying idiom as
            // MaterialLibrary.Clear (MV-304): Destroy is illegal outside play mode, which an EditMode
            // test calling HomingMissile.Fire() hits directly.
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }

        private void Init(Transform target, float speed, float damage, float splashRadius)
        {
            _target = target;
            _targetDamageable = target != null ? target.GetComponent<IDamageable>() : null;
            _speed = speed;
            _damage = damage;
            _splashRadius = splashRadius;

            _trail = GetComponent<TrailRenderer>();
            ClearTrailForRespawn();
        }

        /// <summary>MV-508 AC2 — defensive against a future pool reusing this instance instead of
        /// always Instantiate-ing fresh through <see cref="Fire"/>: clears whatever trail history the
        /// <see cref="TrailRenderer"/> still holds from wherever it was flying before, so a respawn at
        /// a new position never drags a streak across the whole arena. Called from <see cref="Init"/>
        /// so an ordinary fresh spawn always starts clean too.</summary>
        public void ClearTrailForRespawn()
        {
            if (_trail != null) _trail.Clear();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            switch (_state)
            {
                case FlightState.Flying: TickFlying(dt); break;
                case FlightState.Sputtering: TickSputtering(dt); break;
                case FlightState.Bouncing: TickBouncing(dt); break;
            }
        }

        private void TickFlying(float dt)
        {
            _age += dt;

            if (_target != null)
            {
                Vector3 to = _target.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-4f)
                {
                    Quaternion wanted = Quaternion.LookRotation(to.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted,
                        TurnRateDegPerSec * dt);
                }
            }

            Vector3 from = transform.position;
            Vector3 next = from + transform.forward * (_speed * dt);

            // A fence or a shut gate stops ordnance too (MV-364) — the same Cover layer a sight-line
            // stops at, because a fence is cover for both sides, not just Max's. Detonate harmlessly
            // at the wall rather than carrying the splash through to whatever it was chasing beyond it.
            if (BlockedByGeometry(from, next, out RaycastHit hit))
            {
                transform.position = hit.point;
                DetonateAgainstGeometry();
                return;
            }

            transform.position = next;

            // Horizontal only: FlightHeight (MV-349) puts the missile above the target's root, and a
            // hit was never meant to depend on the two sharing an exact Y — it only ever did because
            // both used to fly at the same implicit height of 0.
            bool closeEnough = _target != null && HorizontalDistanceSq(transform.position, _target.position)
                <= ContactRadius * ContactRadius;

            if (closeEnough) { Detonate(hitTarget: true); return; }
            if (HasRunDry(_age, FuelBudget)) BeginSputter();
        }

        /// <summary>Whether solid geometry stands between two points on the missile's flight path this
        /// frame — the same Cover layer <see cref="LineOfSight"/> stops at (MV-364). Extracted as its
        /// own static query, rather than inlined in <see cref="Update"/>, so a test can prove a fence
        /// stops a missile without having to drive a live MonoBehaviour's per-frame Update.</summary>
        public static bool BlockedByGeometry(Vector3 from, Vector3 to, out RaycastHit hit)
        {
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 1e-4f)
            {
                hit = default;
                return false;
            }

            return Physics.Raycast(from, delta / dist, out hit, dist, CoverLayer.Mask,
                                   QueryTriggerInteraction.Ignore);
        }

        /// <summary>Pure so the fuel-exhausted transition can be tested without a scene or a clock
        /// (MV-349 AC6).</summary>
        public static bool HasRunDry(float age, float fuelBudget) => age >= fuelBudget;

        private void BeginSputter()
        {
            _state = FlightState.Sputtering;
            _stateTimer = 0f;
            HudSignals.EmitMissileSputtering(transform.position);
        }

        /// <summary>Thrust cutting out (MV-349): still coasting forward, but decelerating and no
        /// longer homing — the player has to be able to tell it's failing before it falls.</summary>
        private void TickSputtering(float dt)
        {
            _stateTimer += dt;
            float coast = Mathf.Clamp01(1f - _stateTimer / SputterDuration);
            transform.position += transform.forward * (_speed * coast * dt);

            // MV-508: the trail dying alongside the coast is a free extension of MV-349's "the player
            // has to be able to tell it's failing" — the smear shrinks as the thrust does.
            if (_trail != null) _trail.widthMultiplier = TrailWidth * coast;

            if (_stateTimer >= SputterDuration) BeginBounce();
        }

        private void BeginBounce()
        {
            _state = FlightState.Bouncing;
            _bounceCount = 0;
            // Over-eager, not dead on its feet (AC3's "slightly rubbery, over-eager motion"): it
            // still has some of its forward zip when it starts to fall, which is what turns a
            // straight drop into a first, longest hop.
            _bounceVelocity = transform.forward * (_speed * 0.35f);
            _bounceVelocity.y = 0f;

            // MV-508: no more thrust means no more smear once it starts to fall. Stop emitting new
            // points rather than Clear()-ing outright — the existing segment still fades out over
            // TrailTime instead of vanishing mid-frame.
            if (_trail != null) _trail.emitting = false;
        }

        /// <summary>Gravity pulls it down; touching the ground reverses the vertical speed at
        /// <see cref="BounceRestitution"/> of what it landed with and bleeds the horizontal speed the
        /// same way — a rubbery, decaying hop rather than a bounce that never settles.</summary>
        private void TickBouncing(float dt)
        {
            _bounceVelocity.y -= Gravity * dt;
            Vector3 p = transform.position + _bounceVelocity * dt;
            transform.Rotate(Vector3.right * (420f * dt), Space.Self); // the "trying its best" tumble

            if (p.y > GroundY) { transform.position = p; return; }

            p.y = GroundY;
            transform.position = p;
            _bounceCount++;
            HudSignals.EmitMissileBounced(p);

            bool spent = _bounceCount >= MaxBounces || Mathf.Abs(_bounceVelocity.y) < MinBounceSpeed;
            if (spent) { Detonate(hitTarget: false); return; }

            _bounceVelocity.y = -_bounceVelocity.y * BounceRestitution;
            _bounceVelocity.x *= BounceRestitution;
            _bounceVelocity.z *= BounceRestitution;
        }

        private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary><paramref name="hitTarget"/> selects the AC2 "struck Max" reading vs. the AC3
        /// "ran dry and hit the ground" reading. Both still splash-check against the target and deal
        /// the same damage if it's in range — AC3 is explicit that "the bouncing missile should still
        /// be dangerous when it finally goes off" — and either way
        /// <see cref="HudSignals.MissileImpact"/> fires so the impact VFX/screen feedback plays on
        /// every detonation, hit or miss.</summary>
        private void Detonate(bool hitTarget)
        {
            _state = FlightState.Detonated;

            bool dealtDamage = _targetDamageable != null && _targetDamageable.IsAlive && _target != null &&
                (hitTarget || (transform.position - _target.position).sqrMagnitude <= _splashRadius * _splashRadius);

            if (dealtDamage)
            {
                _targetDamageable.TakeDamage(
                    new DamageInfo(_damage, transform.position, transform.forward, Team.Enemy));
            }

            HudSignals.EmitMissileImpact(transform.position, dealtDamage ? _damage : 0f);
            Destroy(gameObject);
        }

        /// <summary>Detonation against solid geometry (MV-364) — never damages the target. A missile
        /// that slammed into a fence stopped BECAUSE the fence is between it and whatever it was
        /// chasing, so applying splash from here would let the blast leak through the wall it just
        /// proved it can't cross. Still fires the impact signal (MV-349's "every detonation, hit or
        /// miss" — see <see cref="Detonate"/>) so the VFX layer plays the same beat a ground impact
        /// would, with zero damage since nothing was hit.</summary>
        private void DetonateAgainstGeometry()
        {
            _state = FlightState.Detonated;
            HudSignals.EmitMissileImpact(transform.position, 0f);
            Destroy(gameObject);
        }
    }
}
