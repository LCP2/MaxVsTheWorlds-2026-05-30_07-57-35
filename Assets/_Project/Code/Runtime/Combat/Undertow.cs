using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// World 3's primary (MV-714, re-shaped by MV-1034) — UNDERTOW: same shell as
    /// <see cref="WaterBlaster"/>/<see cref="PulseLaser"/> (an <see cref="EnergyPool"/> tank built from
    /// <see cref="BlasterTuning"/>, an optional <see cref="PlayerController"/> aim source driving
    /// <see cref="IsFiring"/>/facing).
    ///
    /// Holding fire streams the <b>pressure lance</b> continuously for as long as the trigger is held —
    /// a narrow, long-ranged tick that pierces up to <see cref="MaxPierceCount"/> robots in a line — at
    /// the same per-tick cadence and (base) damage as the RCDA, so its DPS tracks the RCDA's within the
    /// ticket's 10% band by construction rather than by a coincidentally-matched authored number.
    ///
    /// MV-1034 removed the charge-and-release cavitation shot entirely (Lee: "the blue ball does not
    /// work as a design — the Shoulder Rack rockets already kill robots before it can convert them").
    /// There is no charge phase any more: releasing fire simply stops the stream, the same "trigger
    /// held = stream on" shape <see cref="WaterBlaster"/> already uses. Robot conversion
    /// (<see cref="MaxWorlds.Enemies.RobotEnemy.TryConvert"/>) is unchanged and now belongs to a
    /// separate trap ability (MV-1035).
    ///
    /// MV-1064: a tick that damages a robot/boss latches the stream onto it (<see cref="IsLatched"/>) —
    /// the beam's end point then tracks the latched target's own centre every frame, bent away from
    /// Max's aim axis, for as long as <see cref="LatchHolds"/>'s own range/sight/angle/level checks
    /// keep passing. See <see cref="FireLatchedTick"/>/<see cref="FireAimedTick"/> for the two hit
    /// tests this now dispatches between.
    /// </summary>
    [MaxWorlds.Core.PerfSection("combat")]
    public sealed class Undertow : MonoBehaviour
    {
        public const float DefaultDamagePerTick = WaterBlaster.DefaultDamagePerTick;

        public const float DefaultFireInterval = 0.1f;

        /// <summary>Longer-ranged than the RCDA's 5m base (spec).</summary>
        public const float DefaultRange = 9f;

        /// <summary>Narrower than the RCDA's 8° base (spec) — approximates a line rather than a fan,
        /// the same "narrow cone as a line" idiom the codebase already uses for hit-testing instead of a
        /// raycast (see <see cref="SprayHit"/>).</summary>
        public const float DefaultConeHalfAngle = 3f;

        /// <summary>"Pierces up to two robots" (spec) — a hard count cap on the closest in-cone,
        /// in-range, in-sight targets, not a distance/falloff cutoff.</summary>
        public const int MaxPierceCount = 2;

        /// <summary>MV-1064: once latched, the target stays latched out to slightly beyond the
        /// lance's own <see cref="Range"/> — a plain `&lt;= Range` would drop the latch the instant a
        /// damaged robot staggers back half a step, which reads as flickery rather than a solid
        /// grip.</summary>
        public const float LatchRangeMultiplier = 1.15f;

        /// <summary>MV-1064 spec: the latch holds while Max's aim is within 30 degrees of the
        /// latched robot, far wider than the lance's own 3-degree acquire cone — once latched, the
        /// beam is allowed to bend.</summary>
        public const float LatchMaxAngleDegrees = 30f;

        /// <summary>MV-1064 spec: the latched pierce target is the nearest OTHER robot within 1m of
        /// the line from Max through the latched robot, not a cone — the latch has already bent the
        /// beam away from Max's own aim axis, so re-using the cone here would pierce along the wrong
        /// line.</summary>
        public const float LatchPierceLineRadius = 1.0f;

        [Header("Lance")]
        [SerializeField] private float range = DefaultRange;
        [SerializeField] private float coneHalfAngle = DefaultConeHalfAngle;
        [SerializeField] private float damagePerTick = DefaultDamagePerTick;
        [SerializeField] private float fireInterval = DefaultFireInterval;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Aim source")]
        [Tooltip("Optional. If set, fires while the player aims and orients to their facing. " +
                 "If null, IsFiring drives it directly (useful for isolated testing).")]
        [SerializeField] private PlayerController aimSource;

        /// <summary>Whether the trigger is currently held — same contract as <see cref="WaterBlaster.IsFiring"/>.</summary>
        public bool IsFiring { get; private set; }

        /// <summary>Pure fire-gate decision (unit-testable), same shape as <see cref="WaterBlaster.ShouldEmit"/>.</summary>
        public static bool ShouldEmit(bool firingHeld, bool hasEnergy) => firingHeld && hasEnergy;

        public void SetFiring(bool firing) => IsFiring = firing;
        public bool IsEmitting => _lastEmitting;

        /// <summary>The energy tank, 0..1 — same tank shape the RCDA/LPPE drain from.</summary>
        public float EnergyNormalized => _tank != null ? _tank.Normalized : 1f;

        /// <summary>How far the lance reaches — the RCDA Range track's own bonus layered on top (spec:
        /// the weapon is picked up by Max's existing RCDA tracks, not a new set).</summary>
        public float Range => WeaponCatalog.EffectiveRange(
            range, WeaponSystemState.TrackLevel(WeaponTrackKind.Range), WeaponCatalog.DefaultRcdaRangePerLevel);

        public float ConeHalfAngle => WeaponCatalog.EffectiveConeHalfAngle(
            coneHalfAngle, WeaponSystemState.TrackLevel(WeaponTrackKind.Spread), WeaponCatalog.DefaultRcdaSpreadPerLevel);

        /// <summary>Damage one lance tick deals right now — the RCDA Damage track's own bonus layered on
        /// top, the same formula <see cref="WaterBlaster.EffectiveDamagePerTick"/> uses.</summary>
        public float EffectiveDamagePerTick => WeaponCatalog.EffectiveDamagePerTick(
            damagePerTick, WeaponSystemState.TrackLevel(WeaponTrackKind.Damage), WeaponCatalog.DefaultRcdaDamagePerLevel);

        public float FireInterval => fireInterval;

        /// <summary>What the lance actually outputs per second, per pierced target — AC5's comparison
        /// point against the RCDA's own <see cref="WaterBlaster.DamagePerSecond"/>.</summary>
        public float DamagePerSecond => fireInterval > 0f ? EffectiveDamagePerTick / fireInterval : 0f;

        /// <summary>Flat per-tick energy cost — the RCDA's own base drain rate, not track-scaled the way
        /// the RCDA's is (same "flat authored number" shape <see cref="PulseLaser.EnergyPerPulse"/> uses).</summary>
        public float EnergyPerTick => DevTuning.Or(DevTuning.PrimaryDepletionRate, BlasterTuning.EnergyPerSecond) * fireInterval;

        /// <summary>Where the visible stream currently ends, world space — the farthest robot it
        /// actually damaged this tick (it pierces up to two), the first wall/cover in its path, or its
        /// own <see cref="Range"/>, whichever is shortest (MV-1034 spec). Set every time
        /// <see cref="FireLanceTick"/> runs; holds its last value between ticks.</summary>
        public Vector3 StreamEndPoint { get; private set; }

        /// <summary>Whether the stream's VFX is actually showing right now — what an EditMode test
        /// reads instead of inspecting the VFX component's own renderers directly.</summary>
        public bool IsStreamVisible => _vfx != null && _vfx.IsStreaming;

        /// <summary>MV-1064: whether the lance currently has a robot/boss latched — the beam's end
        /// point tracks <see cref="LatchedTransform"/> every frame instead of Max's own aim.</summary>
        public bool IsLatched => _latchedTarget != null;

        /// <summary>The latched target's transform, or null when nothing is latched — what the VFX
        /// (and an EditMode test) reads to find the robot the coils must wrap.</summary>
        public Transform LatchedTransform => _latchedTransform;

        private float _tickTimer;
        private bool _lastEmitting;
        private bool _depleted;
        private EnergyPool _tank;
        private UndertowVfx _vfx;

        // --- MV-1064 latch state. _latchedTransform/_latchedCc are resolved once on acquire (not
        // re-fetched every frame) so LatchedCentre() and the VFX stay cheap on the per-frame path.
        private IDamageable _latchedTarget;
        private Transform _latchedTransform;
        private CharacterController _latchedCc;

        private const int InitialHitBufferSize = 16;
        private Collider[] _hits = new Collider[InitialHitBufferSize];
        private static readonly List<IDamageable> s_buffer = new List<IDamageable>(4);
        private static readonly List<float> s_dist = new List<float>(4);

        private void Awake()
        {
            _tank = new EnergyPool(BlasterTuning.MaxEnergy, BlasterTuning.RegenPerSec, BlasterTuning.RegenDelay);

            // VFX attaches itself — no scene wiring, no prefab (code-driven scenes rule), same idiom
            // WaterVfx/LppeVfx use for their own weapons.
            _vfx = GetComponent<UndertowVfx>();
            if (_vfx == null) _vfx = gameObject.AddComponent<UndertowVfx>();
            _vfx.Init();

            // A safe resting endpoint before the first tick ever lands, so the very first emitting
            // frame doesn't draw a stream collapsed onto the origin.
            StreamEndPoint = transform.position + transform.forward * range;

            // MV-1012: self-attached from PlayerController.Awake (code-driven scenes, no scene wiring),
            // same "resolve-or-fall-back" shape PulseLaser.Awake uses for its own aimSource.
            if (aimSource == null) aimSource = GetComponent<PlayerController>();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The real per-frame update, pulled out to its own explicit-<paramref name="dt"/>
        /// method (the same shape <see cref="MaxWorlds.Enemies.RobotEnemy.TickConversion"/> already
        /// uses) so an EditMode test can drive exactly 0.1s ticks through the SAME logic
        /// <see cref="Update"/> calls, instead of reading Unity's own near-zero-in-EditMode
        /// <see cref="Time.deltaTime"/> (MV-1034's stream test).</summary>
        private void Tick(float dt)
        {
            _tank.Tick(dt);

            if (aimSource != null)
            {
                IsFiring = aimSource.IsAiming;
                Vector3 f = aimSource.Facing;
                if (f.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(f, Vector3.up);
            }

            if (DevMode.IsAutoFiring) IsFiring = true;
            if (DevMode.IsInfiniteEnergy) _tank.Refill();

            float cost = EnergyPerTick;
            if (_depleted && _tank.Normalized >= BlasterTuning.RechargeFraction) _depleted = false;
            else if (!_depleted && !_tank.CanSpend(cost)) _depleted = true;

            // MV-1012: this component is self-attached unconditionally from PlayerController.Awake (see
            // PulseLaser's own ActivePrimary gate for the same self-attach-then-no-op shape), so it must
            // gate itself rather than relying on being added/removed.
            bool emitting = WeaponSystemState.ActivePrimary == WeaponCatalog.PrimaryKind.Undertow
                && ShouldEmit(IsFiring, !_depleted && _tank.CanSpend(cost));
            _lastEmitting = emitting;

            if (_vfx != null) _vfx.SetStreaming(emitting);
            if (!emitting)
            {
                _tickTimer = 0f;
                DropLatch();
                if (_vfx != null) _vfx.SetLatch(false, null, null, dt);
                return;
            }

            _tickTimer -= dt;
            if (_tickTimer <= 0f)
            {
                _tickTimer = fireInterval;
                if (_tank.TrySpend(cost)) FireLanceTick();
            }

            // MV-1064: while latched, pin the endpoint to the target's LIVE position every frame (not
            // just on the fire-tick cadence above) so the beam visibly bends as it or Max moves,
            // rather than snapping only once per 0.1s tick.
            if (_latchedTarget != null) StreamEndPoint = LatchedCentre();

            if (_vfx != null)
            {
                // SetLatch before UpdateStream: the beam's own last-1m curl (UpdateStream) reads the
                // coil geometry this call just resolved, so the two must never visibly lag a frame
                // apart.
                _vfx.SetLatch(_latchedTarget != null, _latchedTransform, _latchedCc, dt);
                // Animate the crackle strands every frame the stream is up, not just on the tick
                // cadence — FireLanceTick above (if it ran this frame) already refreshed StreamEndPoint.
                _vfx.UpdateStream(transform.position, StreamEndPoint, dt);
            }
        }

        /// <summary>Same growing-buffer idiom <see cref="WaterBlaster.OverlapSphereGrowing"/> uses
        /// (MV-666): a fixed-size buffer risks silently dropping robots in a crowded room the moment a
        /// query saturates it, and the lance's 9m base range only makes that more likely than the RCDA's
        /// own 5m. Grows only on saturation, so the steady-state per-tick path stays allocation-free.</summary>
        private int OverlapSphereGrowing(Vector3 origin, float radius)
        {
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(
                       origin, radius, _hits, hitMask, QueryTriggerInteraction.Ignore)) == _hits.Length)
            {
                _hits = new Collider[_hits.Length * 2];
            }
            return count;
        }

        /// <summary>Dispatches to the latched or the plain aimed hit test (MV-1064). Re-validates the
        /// latch every tick — <see cref="LatchHolds"/> — before trusting it, so a robot that walked
        /// out of range/sight/angle this very tick falls straight back to aimed targeting instead of
        /// damaging a target it should have lost.</summary>
        private void FireLanceTick()
        {
            Vector3 origin = transform.position;
            Vector3 dir = transform.forward;
            float reach = Range;

            if (_latchedTarget != null && !LatchHolds(origin, dir, reach))
                DropLatch();

            if (_latchedTarget != null)
            {
                FireLatchedTick(origin, dir, reach);
                return;
            }

            FireAimedTick(origin, dir, reach);
        }

        /// <summary>
        /// Gathers everything in range, keeps only what's inside the narrow lance cone (the codebase's
        /// established line-hit idiom — no raycast, see <see cref="SprayHit"/>) and in sight, sorts by
        /// distance along the aim axis, then damages only the closest <see cref="MaxPierceCount"/> — a
        /// third robot standing further back in the same line is never hit (AC1). Also resolves
        /// <see cref="StreamEndPoint"/>, drives the muzzle/splash flares (MV-1034), and — MV-1064 — tries
        /// to latch onto the nearest robot/boss it just damaged.
        /// </summary>
        private void FireAimedTick(Vector3 origin, Vector3 dir, float reach)
        {
            float cone = ConeHalfAngle;
            float tickDamage = EffectiveDamagePerTick;

            int count = OverlapSphereGrowing(origin, reach);

            s_buffer.Clear();
            s_dist.Clear();
            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;
                if (!_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive || d.Team == Team.Player) continue;
                if (s_buffer.Contains(d)) continue;

                // MV-1044: an AreaGate needs the same gate-aware hit test WaterBlaster.FireTick has
                // carried since MV-302/MV-386 — its own leaf collider's testPoint/sight target reject a
                // shot that isn't dead-centre, and the leaf sits off the Cover layer entirely, so a
                // Cover-masked line of sight always found the gate's threshold first and read every shot
                // as blocked. Shared via GateHitResolver rather than copied a second time.
                GateHitResolver.Resolve(d, origin, dir, _hits[i], out Vector3 pos, out Transform sightTarget);
                if (!GateHitResolver.Passes(origin, dir, pos, sightTarget, reach, cone)) continue;

                Vector3 to = pos - origin; to.y = 0f;
                s_buffer.Add(d);
                s_dist.Add(to.magnitude);
            }

            // Small N (a handful of overlapping colliders at most) — a plain insertion sort by distance
            // is allocation-free and simpler than pulling in a general sort for two list slots.
            for (int i = 1; i < s_buffer.Count; i++)
            {
                float key = s_dist[i];
                IDamageable keyD = s_buffer[i];
                int j = i - 1;
                while (j >= 0 && s_dist[j] > key)
                {
                    s_dist[j + 1] = s_dist[j];
                    s_buffer[j + 1] = s_buffer[j];
                    j--;
                }
                s_dist[j + 1] = key;
                s_buffer[j + 1] = keyD;
            }

            int pierced = Mathf.Min(MaxPierceCount, s_buffer.Count);
            for (int i = 0; i < pierced; i++)
            {
                s_buffer[i].TakeDamage(new DamageInfo(tickDamage, origin, dir, Team.Player, soak: true,
                    source: DamageSource.PrimaryWeapon));
            }

            // MV-1064: "when a stream tick damages a robot, the stream latches to the nearest robot it
            // damaged" — the closest eligible (non-gate, non-trapped) entry among the ones just
            // damaged, since s_buffer is already sorted by distance.
            TryAcquireLatch(pierced);

            // MV-1034: the visible stream reaches whichever is closest — the farthest robot it actually
            // damaged this tick, the first wall/cover in its path, or the weapon's own range.
            float endDistance = reach;
            if (pierced > 0) endDistance = Mathf.Min(endDistance, s_dist[pierced - 1]);
            if (Physics.Raycast(origin, dir, out RaycastHit coverHit, reach, CoverLayer.Mask, QueryTriggerInteraction.Ignore))
                endDistance = Mathf.Min(endDistance, coverHit.distance);

            StreamEndPoint = origin + dir * endDistance;
            if (_vfx != null) _vfx.OnTick(origin, StreamEndPoint, dir);
        }

        /// <summary>MV-1064: picks the nearest target this tick actually damaged (<paramref name="pierced"/>
        /// entries of <see cref="s_buffer"/>, already sorted by distance) that is eligible to be latched
        /// — never a gate (MV-1044's handling is unchanged) and never a trapped robot (its <c>TakeDamage</c>
        /// was already a no-op via <see cref="RobotEnemy.IsDamageable"/>, so it wasn't really "damaged").
        /// A no-op if nothing eligible was hit.</summary>
        private void TryAcquireLatch(int pierced)
        {
            for (int i = 0; i < pierced; i++)
            {
                IDamageable d = s_buffer[i];
                if (d is AreaGate) continue;
                if (d is RobotEnemy robot && robot.IsTrapHeld) continue;
                AcquireLatch(d);
                return;
            }
        }

        /// <summary>MV-1064: the latched target's own continuous tick — damages it every <see cref="FireInterval"/>
        /// regardless of the 3-degree acquire cone (the latch has already widened the hold to
        /// <see cref="LatchMaxAngleDegrees"/>), then looks for a second ("pierce") target along the
        /// line from Max through the latched robot rather than along Max's own aim axis, since the
        /// beam itself has bent off that axis.</summary>
        private void FireLatchedTick(Vector3 origin, Vector3 dir, float reach)
        {
            float tickDamage = EffectiveDamagePerTick;
            Vector3 latchedCentre = LatchedCentre();

            _latchedTarget.TakeDamage(new DamageInfo(tickDamage, origin, dir, Team.Player, soak: true,
                source: DamageSource.PrimaryWeapon));

            Vector3 toLatched = latchedCentre - origin;
            float latchedDist = toLatched.magnitude;
            Vector3 lineDir = latchedDist > 1e-4f ? toLatched / latchedDist : dir;

            int count = OverlapSphereGrowing(origin, reach);
            IDamageable second = null;
            float secondDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;
                if (!_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive || d.Team == Team.Player) continue;
                if (ReferenceEquals(d, _latchedTarget) || d is AreaGate) continue;

                Vector3 testPoint = _hits[i].transform.position;
                Vector3 to = testPoint - origin; to.y = 0f;
                float dist = to.magnitude;
                if (dist > reach) continue;
                if (PerpendicularDistanceToLine(testPoint, origin, lineDir) > LatchPierceLineRadius) continue;
                if (!LineOfSight.Clear(origin, testPoint, _hits[i].transform)) continue;
                if (!CombatLevel.SameLevel(EnemyNavigation.Map, origin, testPoint)) continue;

                if (dist < secondDist) { secondDist = dist; second = d; }
            }
            if (second != null)
            {
                second.TakeDamage(new DamageInfo(tickDamage, origin, dir, Team.Player, soak: true,
                    source: DamageSource.PrimaryWeapon));
            }

            StreamEndPoint = latchedCentre;
            if (_vfx != null) _vfx.OnTick(origin, StreamEndPoint, dir);
        }

        /// <summary>MV-1064 spec #3: the latch holds while ALL of — target alive, still an enemy (a
        /// converted robot flips to <see cref="Team.Player"/>), not trapped, within <see cref="Range"/>
        /// &#215; <see cref="LatchRangeMultiplier"/>, in sight, within <see cref="LatchMaxAngleDegrees"/>
        /// of Max's aim, and on the same combat level.</summary>
        private bool LatchHolds(Vector3 origin, Vector3 aimDir, float reach)
        {
            if (_latchedTarget == null) return false;
            if (!_latchedTarget.IsAlive) return false;
            if (_latchedTarget.Team != Team.Enemy) return false;
            if (_latchedTarget is RobotEnemy robot && robot.IsTrapHeld) return false;

            Vector3 centre = LatchedCentre();
            Vector3 to = centre - origin;
            float dist = to.magnitude;
            if (dist > reach * LatchRangeMultiplier) return false;

            if (!LineOfSight.Clear(origin, centre, _latchedTransform)) return false;

            Vector3 toDir = dist > 1e-4f ? to / dist : aimDir;
            if (Vector3.Angle(aimDir, toDir) > LatchMaxAngleDegrees) return false;

            if (!CombatLevel.SameLevel(EnemyNavigation.Map, origin, centre)) return false;

            return true;
        }

        /// <summary>MV-1064: "the robot's centre (chest height)" — the same <c>CharacterController.bounds.center</c>
        /// idiom <see cref="WaterBlaster"/>'s own gate-damage diagnostic already uses for a robot's
        /// world centre, resolved once on acquire (<see cref="_latchedCc"/>) rather than re-fetched by
        /// component lookup every frame.</summary>
        private Vector3 LatchedCentre()
        {
            if (_latchedCc != null) return _latchedCc.bounds.center;
            return _latchedTransform != null ? _latchedTransform.position : StreamEndPoint;
        }

        private void AcquireLatch(IDamageable target)
        {
            _latchedTarget = target;
            _latchedTransform = (target as Component)?.transform;
            _latchedCc = _latchedTransform != null ? _latchedTransform.GetComponent<CharacterController>() : null;
        }

        private void DropLatch()
        {
            _latchedTarget = null;
            _latchedTransform = null;
            _latchedCc = null;
        }

        /// <summary>Shortest distance from <paramref name="point"/> to the infinite line through
        /// <paramref name="lineOrigin"/> along <paramref name="lineDir"/> (assumed normalised) — what
        /// "within 1m of the line from Max through the latched robot" (spec #4) means for a 3D point.</summary>
        private static float PerpendicularDistanceToLine(Vector3 point, Vector3 lineOrigin, Vector3 lineDir)
        {
            Vector3 toPoint = point - lineOrigin;
            float along = Vector3.Dot(toPoint, lineDir);
            Vector3 closest = lineOrigin + lineDir * along;
            return Vector3.Distance(point, closest);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.95f, 1f, 1f);
            Gizmos.DrawWireSphere(transform.position + transform.forward * range, 0.3f);
        }
#endif
    }
}
