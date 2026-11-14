using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.CameraRig;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// The Helmsman (MV-1239) — World 5's final boss, on the Bridge (area 30). Seated on its throne in
    /// Phase 1: a lens sweep (<see cref="LensSweepTelegraph"/>) plus a pod volley from the side hatches,
    /// both while stationary. Below half health it tears free and walks (Phase 2): a faster lens sweep,
    /// plus the shared passive contact damage every boss gives (<see cref="BossTuning"/>'s own MV-720
    /// rule). The look (seated-then-torn-free body) is MV-1242's own ticket — this class is behaviour and
    /// placement only, and rides the plain greybox cube <see cref="BossKinds"/> spawns every boss on
    /// until a rig binds over it.
    ///
    /// Modelled on <see cref="SludgequeenBoss"/> (standalone boss, not a <see cref="BigBermudaBoss"/>
    /// companion like <see cref="AnchorheadBoss"/>) because Phase 1 does not move or approach at all —
    /// the companion pattern assumes the base class is already walking in both phases, which the
    /// Helmsman's seated opening breaks. Pod volleys reuse Big Bermuda's own brood-volley shape wholesale
    /// (<see cref="BigBermudaBrain"/> for the shared time-based L1-L4 ramp, <see cref="BroodSpawnLevels"/>
    /// for composition, <see cref="BroodArc"/> for the landing maths) — "as today" (the ticket's own
    /// words), not a reimplementation.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [PerfSection("bosses")]
    public sealed class HelmsmanBoss : MonoBehaviour, IDamageable
    {
        private enum Phase { Dormant, Seated, Transition, Walking, Dying, Dead }

        private const string BossName = "HELMSMAN";
        private const float Gravity = 20f;

        private Phase _phase = Phase.Dormant;
        private DestructibleHealth _health;
        private CharacterController _cc;
        private Transform _target;

        private float _contactCooldownTimer;
        private float _verticalVel;

        private readonly WallLatch _wallLatch = new WallLatch();
        private float _preferSign;
        private readonly ZoneRouteBudget _routeBudget = new ZoneRouteBudget();

        // This boss's own authored area, same convention as every other boss's _wakeArea — unused
        // beyond storage today (the shared wake rule consults range + line of sight only, not the
        // area), kept for parity with SludgequeenBoss/BigBermudaBoss's own SetWakeArea.
        private Rect _wakeArea;
        private int _areaIndex;

        private readonly BigBermudaBrain _brain = new BigBermudaBrain(HelmsmanTuning.PhaseTwoThreshold);
        private float _podTimer;
        private Transform _podRoot;

        // ---------------------------------------------------------------- phase 1 "Wired In" (MV-1370)
        private readonly HelmsmanBeamClock _beams = new HelmsmanBeamClock();
        private float _beamBaseYaw;
        private float _beamDir = 1f;
        private readonly Vector3[] _beamEnd = new Vector3[2];
        private Vector3 _beamOrigin;
        private readonly Dictionary<int, float> _beamNextHit = new Dictionary<int, float>();
        private float _beamNow;
        private float _spiralTimer;
        private int _volleyLeft;
        private int _volleyFired;
        private float _volleyGap;
        private float _volleyBaseYaw;
        private float _transitionLeft;
        private bool _transitionToPhase3;
        private bool _phase3Begun;

        // ---------------------------------------------------------------- phase 2 "Torn Free" leap slam (MV-1371)
        public enum SlamStage { Idle, Tell, Leap, Wave }

        private SlamStage _slamStage = SlamStage.Idle;
        private float _slamCycle;      // seconds since the current cycle's tell started (runs through every stage)
        private float _slamStageT;
        private Vector3 _slamStart;
        private Vector3 _slamCenter;
        private float _waveRadius;
        private float _waveGapYaw;
        private readonly HashSet<int> _waveHit = new HashSet<int>();

        // ---------------------------------------------------------------- phase 3 "Overload" (MV-1372)
        private Vector3 _home;                  // where the throne stands (set on wake)
        private HelmsmanRig _rig;
        private float _ringTimer;
        private int _ringCount;
        private float _ringGapYaw;
        private bool _lastStand;
        private float _deathT;
        private Vector3[] _ventCenters;
        private float _ventClock;
        private float _ventColumnLeft;

        public const float VentGlowSeconds = 1f;

        // ---------------------------------------------------------------- Bridge airlocks (MV-1313)
        private AirlockPanel[] _bridgeAirlocks;
        private bool _bridgeAirlocksResolved;
        private float _airlockElapsed;
        private int _airlockCycleIndex;

        public bool IsAlive => _phase != Phase.Dead && _health != null && _health.IsAlive;
        public Team Team => Team.Enemy;

        /// <summary>True from wake until death — same meaning as <see cref="BigBermudaBoss.Engaged"/>.</summary>
        public bool Engaged => _phase == Phase.Seated || _phase == Phase.Walking;

        public bool IsDead => _phase == Phase.Dead;

        /// <summary>The 3 s death beat is running: collapse, wheel shears off and rolls, then the key drops.</summary>
        public bool IsDying => _phase == Phase.Dying;

        /// <summary>0..1 through the death beat; 0 outside it (1 once dead).</summary>
        public float DeathT01 => _phase == Phase.Dying ? Mathf.Clamp01(_deathT / HelmsmanTuning.DeathSeconds)
            : _phase == Phase.Dead ? 1f : 0f;

        /// <summary>Phase 3 proper is running (the spin-up beat is over): ring bursts, vents, exposed core.</summary>
        public bool InPhase3 => _phase == Phase.Walking && _phase3Begun;

        /// <summary>The red core is open and takes <see cref="HelmsmanTuning.CoreDamageMultiplier"/> hits.</summary>
        public bool CoreExposed => InPhase3;

        /// <summary>At or below 15% in phase 3: lights red, every P3 cooldown x0.75.</summary>
        public bool LastStand => _lastStand;

        private float CooldownScale => _lastStand ? HelmsmanTuning.LastStandCooldownMultiplier : 1f;

        /// <summary>Compass yaw (degrees) of the middle of the current ring's two-missile gap.</summary>
        public float RingGapYaw => _ringGapYaw;
        public int RingCount => _ringCount;

        public int VentCount => _ventCenters != null ? _ventCenters.Length : 0;
        public Vector3 VentCenter(int i) => _ventCenters[i];

        /// <summary>0..1 through the 1 s orange glow before the vents fire; 0 outside it.</summary>
        public float VentGlow01 => _ventCenters == null ? 0f
            : Mathf.Clamp01(1f - (HelmsmanTuning.VentInterval * CooldownScale - _ventClock) / VentGlowSeconds);

        /// <summary>The flame columns are up (0.6 s after each firing).</summary>
        public bool VentColumnOn => _ventColumnLeft > 0f;

        internal void SetRig(HelmsmanRig rig) => _rig = rig;

        /// <summary>True once Phase 2 has begun (torn free of the throne, walking) — what an EditMode
        /// test reads instead of the private phase enum.</summary>
        public bool IsWalking => _phase == Phase.Transition || _phase == Phase.Walking || _phase == Phase.Dying;

        /// <summary>The 2 s invulnerable P1 -> P2 beat is running (MV-1370).</summary>
        public bool IsTransitioning => _phase == Phase.Transition;

        /// <summary>1, 2 or 3 (MV-1370): P1 100-66%, P2 66-33%, P3 33-0%. 0 before it wakes.</summary>
        public int PhaseNumber
        {
            get
            {
                if (_phase == Phase.Dormant || _health == null) return 0;
                if (_phase == Phase.Seated) return 1;
                return _health.Normalized <= HelmsmanTuning.PhaseThreeThreshold ? 3 : 2;
            }
        }

        /// <summary>The lens attack's tell is drawn and visible right now (MV-1242) — <see cref="HelmsmanRig"/>'s
        /// cue to glow the lens brighter. Phase 1: the beam tell; walking: the old sweep's tell.</summary>
        public bool LensTelling => (_phase == Phase.Seated || _phase == Phase.Walking)
            && _beams.Current == HelmsmanBeamClock.Stage.Tell;

        /// <summary>The lens beam(s) are showing (floor line or live) right now.</summary>
        public bool BeamsShowing => (_phase == Phase.Seated || _phase == Phase.Walking)
            && _beams.Current != HelmsmanBeamClock.Stage.Idle;

        /// <summary>The lens beam(s) are live (sweeping, doing damage) right now.</summary>
        public bool BeamsLive => (_phase == Phase.Seated || _phase == Phase.Walking)
            && _beams.Current == HelmsmanBeamClock.Stage.Sweep;

        /// <summary>Phase 1 fires twin beams; phase 2 one (MV-1371).</summary>
        public int BeamCount => _phase == Phase.Seated ? 2 : 1;

        /// <summary>The P2 -> P3 beat (invulnerable, wheel spinning up) is running (MV-1371).</summary>
        public bool TransitionToPhase3 => _phase == Phase.Transition && _transitionToPhase3;

        public SlamStage Slam => _slamStage;
        public Vector3 SlamCenter => _slamCenter;

        /// <summary>0..1 through the 1 s red tell; 0 outside it.</summary>
        public float SlamTell01 => _slamStage == SlamStage.Tell
            ? Mathf.Clamp01(_slamStageT / HelmsmanTuning.SlamTellSeconds) : 0f;

        /// <summary>Current shockwave radius in metres while it expands; 0 otherwise.</summary>
        public float WaveRadius => _slamStage == SlamStage.Wave ? Mathf.Max(0f, _waveRadius) : 0f;

        /// <summary>Compass yaw (degrees) of one of the shockwave's two opposite 40 degree gaps.</summary>
        public float WaveGapYaw => _waveGapYaw;

        public Vector3 BeamOrigin => _beamOrigin;

        /// <summary>Where beam <paramref name="i"/> (0 or 1) ends - its full range, or the cover that blocks it.</summary>
        public Vector3 BeamEnd(int i) => _beamEnd[i];

        /// <summary>Which two opposite <see cref="HelmsmanRig.LensNodes"/> are lit: the returned index and +4.</summary>
        public int BeamNodePair => Mathf.Max(0, _beams.Cycles - 1) % (HelmsmanRig.LensNodeCount / 2);

        /// <summary>Hands this boss its own authoring area's floor, same convention as
        /// <see cref="SludgequeenBoss.SetWakeArea"/>.</summary>
        public void SetWakeArea(Rect area) => _wakeArea = area;

        /// <summary>Same MV-613 reasoning as <see cref="SludgequeenBoss.FitColliderTo"/> — called once
        /// <see cref="HelmsmanRig"/>'s own combined body bounds are known.</summary>
        public void FitColliderTo(Bounds localBounds)
        {
            if (_cc == null) return;
            _cc.center = localBounds.center;
            _cc.height = Mathf.Max(0.01f, localBounds.size.y);
            _cc.radius = Mathf.Max(0.01f, Mathf.Max(localBounds.extents.x, localBounds.extents.z));
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            FitColliderToRenderedBody();
            _preferSign = ObstacleSteering.PreferSignFor(GetInstanceID());
            _health = new DestructibleHealth(DevTuning.Or(DevTuning.BossHealth, HelmsmanTuning.Health));
            _health.Destroyed += BeginDeath;
            _contactCooldownTimer = HelmsmanTuning.ContactCooldown; // no free first hit
            AcquireTarget();
        }

        /// <summary>Same MV-542 collider-fit reasoning as <see cref="SludgequeenBoss.FitColliderToRenderedBody"/>.</summary>
        private void FitColliderToRenderedBody()
        {
            if (_cc == null) return;
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            Bounds local = mf.sharedMesh.bounds;
            _cc.center = local.center;
            _cc.height = local.size.y;
            _cc.radius = Mathf.Max(local.extents.x, local.extents.z);
        }

        private void Start() => HudSignals.EmitBossRegistered();

        private void Wake()
        {
            int areaIndex = ResolveAreaIndex();
            _areaIndex = areaIndex;

            // MV-995: same reasoning as every other boss's Wake -- a cold-boot RESUME rebuilds every
            // authored boss fresh and Dormant, so an area already beaten before the checkpoint was saved
            // must not fight again.
            if (BossCensus.IsAreaDefeated(areaIndex))
            {
                _phase = Phase.Dead;
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            _phase = Phase.Seated;
            _home = transform.position;
            // 3 phases -> HUD bar shows the 66% and 33% segments.
            BossCensus.Register(this, BossName, 3, _health.Current, _health.Max, areaIndex);
        }

        private int ResolveAreaIndex()
        {
            MapData map = EnemyNavigation.Map;
            MapZone zone = map?.ZoneAt(transform.position.x, transform.position.z);
            if (zone == null) return 0;
            return AreaAccumulationDirector.AreaIndexOf(zone.id);
        }

        /// <summary>Same shared wake rule as every other boss: within <see cref="BossTuning.WakeRadius"/>
        /// (16 m) of its own post, with clear line of sight.</summary>
        private void TickDormant()
        {
            if (_target == null) { AcquireTarget(); return; }
            if (IsWithinWakeRange(_target.position)) Wake();
        }

        private bool IsWithinWakeRange(Vector3 targetPosition)
        {
            Vector3 to = targetPosition - transform.position;
            to.y = 0f;
            if (to.magnitude > BossTuning.WakeRadius) return false;
            return LineOfSight.Between(transform, _target);
        }

        /// <summary>The full per-frame fight tick, dt-parameterized so an EditMode test can drive the
        /// whole fight deterministically without a live Update loop — same contract every other boss's
        /// own <c>Tick(dt)</c> gives.</summary>
        public void Tick(float dt)
        {
            switch (_phase)
            {
                case Phase.Dormant: TickDormant(); break;
                case Phase.Seated: TickSeated(dt); break;
                case Phase.Transition: TickTransition(dt); break;
                case Phase.Walking: TickWalking(dt); break;
                case Phase.Dying: TickDying(dt); break;
            }
            if (_slamStage != SlamStage.Leap) ApplyGravity(dt);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Phase 1 "Wired In" (MV-1370): stationary, cabled to the ceiling. Faces Max and runs
        /// three clocks: twin lens beams every 10 s, an 8-missile spiral volley every 7 s, hatch pods
        /// every 12 s.</summary>
        private void TickSeated(float dt)
        {
            if (_target == null) { AcquireTarget(); return; }
            FaceTarget();

            TickBeams(dt);
            TickSpiral(dt);

            _podTimer += dt;
            if (_podTimer >= HelmsmanTuning.PodVolleyInterval)
            {
                _podTimer = 0f;
                SpawnPods();
            }

            _brain.Tick(dt, _health.Normalized);
            BossCensus.ReportSpawnLevel(this, _brain.SpawnLevel, _brain.SpawnLevelProgress01);
        }

        /// <summary>The P1 -> P2 beat: invulnerable and motionless; the rig unfolds its legs and drops
        /// its cables (it reads <see cref="IsWalking"/>); then it walks.</summary>
        private void TickTransition(float dt)
        {
            FaceTarget();
            _transitionLeft -= dt;
            if (_transitionLeft > 0f) return;

            _phase = Phase.Walking;
            // Phase 2's cadence: one beam every 8 s, a slam every 6 s (a fresh clock after the beat).
            _beams.Interval = HelmsmanTuning.SingleBeamInterval;
            _beams.Reset();
            _slamCycle = 0f;
            if (_transitionToPhase3) BeginPhase3();
        }

        /// <summary>Phase 3 "Overload" opens (MV-1372): the Breakers snap their chains, the four vents
        /// start their 5 s clock, the first ring bursts one full interval in.</summary>
        private void BeginPhase3()
        {
            _ringTimer = 0f;
            _ringCount = 0;
            _ringGapYaw = 0f;
            _ventClock = 0f;
            _ventColumnLeft = 0f;
            BuildVents();
            ReleaseBreakers();
        }

        private void BeginTransition(bool toPhase3)
        {
            _transitionToPhase3 = toPhase3;
            if (toPhase3) _phase3Begun = true;
            _slamStage = SlamStage.Idle;
            _verticalVel = 0f;
            _phase = Phase.Transition;
            _transitionLeft = HelmsmanTuning.TransitionSeconds;
            _beams.Reset();
            _volleyLeft = 0;
            var cam = Object.FindFirstObjectByType<CameraTargetRig>();
            if (cam != null) cam.ApplyFocusOverride(transform, HelmsmanTuning.TransitionSeconds);
        }

        private static Vector3 YawDir(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        private float BearingTo(Vector3 worldPoint)
        {
            Vector3 to = worldPoint - transform.position; to.y = 0f;
            return to.sqrMagnitude < 1e-4f ? transform.eulerAngles.y : Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        }

        // ---------------------------------------------------------------- twin lens beams (phase 1)

        private void TickBeams(float dt)
        {
            _beamNow += dt;
            _beams.Tick(dt);
            if (_beams.TellStarted)
            {
                _beamDir = (_beams.Cycles % 2 == 0) ? -1f : 1f;
                // Built so the pair sweeps across Max's bearing at the middle of the sweep.
                _beamBaseYaw = BearingTo(_target.position) - _beamDir * HelmsmanTuning.BeamSweepDegrees * 0.5f;
            }
            if (_beams.Current == HelmsmanBeamClock.Stage.Idle) return;

            float yaw = _beamBaseYaw + _beamDir * HelmsmanTuning.BeamSweepDegrees * _beams.SweepProgress01;
            Vector3 p = transform.position;
            // Height is absolute (floors are at y = 0, like the rig's own Follow), so cover's 1.6 m is always
            // taller than the cast no matter where this boss's collider origin sits.
            _beamOrigin = new Vector3(p.x, HelmsmanTuning.BeamHeight, p.z);
            int beamCount = BeamCount;
            for (int i = 0; i < beamCount; i++)
            {
                Vector3 dir = YawDir(yaw + 180f * i);
                _beamEnd[i] = _beamOrigin + dir * BeamLength(dir);
            }

            if (_beams.Current != HelmsmanBeamClock.Stage.Sweep) return;

            ApplyBeamDamage(_target);
            IReadOnlyList<Sentinel> sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
                ApplyBeamDamage(sentinels[i] != null ? sentinels[i].transform : null);
        }

        /// <summary>A beam runs to its full range unless cover (the 1.6 m bulkheads and every other
        /// cover collider - what <see cref="LineOfSight"/> raycasts) stops it first.</summary>
        private float BeamLength(Vector3 dir)
        {
            if (Physics.Raycast(_beamOrigin, dir, out RaycastHit hit, HelmsmanTuning.BeamRange,
                                CoverLayer.Mask, QueryTriggerInteraction.Ignore))
                return hit.distance;
            return HelmsmanTuning.BeamRange;
        }

        private void ApplyBeamDamage(Transform t)
        {
            if (t == null) return;
            int id = t.GetInstanceID();
            if (_beamNextHit.TryGetValue(id, out float next) && _beamNow < next) return;

            Vector3 v = t.position - _beamOrigin; v.y = 0f;
            float reach = HelmsmanTuning.BeamHalfWidth + TargetRadius(t);
            bool hit = false;
            for (int i = 0; i < BeamCount && !hit; i++)
            {
                Vector3 axis = _beamEnd[i] - _beamOrigin; axis.y = 0f;
                float len = axis.magnitude;
                if (len < 1e-3f) continue;
                axis /= len;
                float along = Vector3.Dot(v, axis);
                if (along < 0f || along > len) continue;   // behind it, or beyond the cover that stops it
                hit = Mathf.Abs(v.x * axis.z - v.z * axis.x) <= reach;
            }
            if (!hit) return;

            if (!t.TryGetComponent<IDamageable>(out var damageable) || !damageable.IsAlive) return;
            _beamNextHit[id] = _beamNow + HelmsmanTuning.BeamRehitSeconds;
            Vector3 dir = v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
            damageable.TakeDamage(new DamageInfo(HelmsmanTuning.BeamDamage, transform.position, dir, Team.Enemy));
        }

        // ---------------------------------------------------------------- spiral volley (phase 1)

        private void TickSpiral(float dt)
        {
            _spiralTimer += dt;
            if (_spiralTimer >= HelmsmanTuning.SpiralInterval)
            {
                _spiralTimer -= HelmsmanTuning.SpiralInterval;
                _volleyLeft = HelmsmanTuning.SpiralMissiles;
                _volleyFired = 0;
                _volleyGap = 0f;
                _volleyBaseYaw = BearingTo(_target.position);
            }

            if (_volleyLeft <= 0) return;
            _volleyGap -= dt;
            while (_volleyLeft > 0 && _volleyGap <= 0f)
            {
                LaunchSpiralMissile();
                _volleyLeft--;
                _volleyFired++;
                _volleyGap += HelmsmanTuning.SpiralLaunchGapSeconds;
            }
        }

        /// <summary>Missile n of the volley leaves n x 45 deg round the wheel from the line to Max, so the
        /// eight together draw one spiral. It is ORBITAL's own missile (<see cref="OrbitalMissile"/>,
        /// MV-1344) - slow steer, long trail - carrying this boss's damage.</summary>
        private void LaunchSpiralMissile()
        {
            float step = 360f / HelmsmanTuning.SpiralMissiles;
            Vector3 heading = YawDir(_volleyBaseYaw + step * _volleyFired);
            Vector3 p = transform.position;
            Vector3 mouth = new Vector3(p.x, OrbitalTuning.FlightHeight, p.z) + heading * 2.6f;
            OrbitalMissile.Spawn(new Vector3(p.x, 0f, p.z), heading, _target, _areaIndex, mouth,
                HelmsmanTuning.SpiralDamage, HelmsmanTuning.SpiralTrailSeconds);
        }

        /// <summary>Phase 2 "Torn Free" (MV-1371): walks at 2.2 m/s, hurts on contact, one lens beam every
        /// 8 s, a leap slam every 6 s, and the Bridge airlocks blowing every 12 s.</summary>
        private void TickWalking(float dt)
        {
            if (_target == null) { AcquireTarget(); return; }
            bool planted = _slamStage == SlamStage.Tell || _slamStage == SlamStage.Leap;
            if (!planted) Approach(dt);
            FaceTarget();
            TickContactDamage(dt);

            TickBeams(dt);
            TickSlam(dt);
            TickAirlockCycle(dt);
            if (_phase3Begun)
            {
                TickRing(dt);
                TickVents(dt);
            }
        }

        // ---------------------------------------------------------------- phase 3: ring burst, vents, Breakers (MV-1372)

        private void TickRing(float dt)
        {
            _ringTimer += dt;
            if (_ringTimer < HelmsmanTuning.RingInterval * CooldownScale) return;
            _ringTimer = 0f;
            FireRing();
        }

        /// <summary>16 light missiles leave the wheel in a ring, one adjacent pair left out; the gap turns
        /// 45 degrees every burst. The first gap faces Max so the first ring is always readable.</summary>
        private void FireRing()
        {
            if (_ringCount == 0) _ringGapYaw = BearingTo(_target.position);
            else _ringGapYaw += HelmsmanTuning.RingGapStepDegrees;
            _ringGapYaw = Mathf.Repeat(_ringGapYaw, 360f);
            _ringCount++;

            int n = HelmsmanTuning.RingMissiles;
            float step = 360f / n;
            // The two missiles straddling the gap's centre sit at gapYaw -/+ step/2 and are skipped.
            float first = _ringGapYaw + step * 0.5f;
            Vector3 p = transform.position;
            for (int i = 0; i < n - HelmsmanTuning.RingGapMissiles; i++)
            {
                Vector3 heading = YawDir(first + step * (i + 1));
                Vector3 mouth = new Vector3(p.x, OrbitalTuning.FlightHeight, p.z) + heading * 2.6f;
                OrbitalMissile.Spawn(new Vector3(p.x, 0f, p.z), heading, _target, _areaIndex, mouth,
                    HelmsmanTuning.RingDamage, HelmsmanTuning.RingTrailSeconds, straight: true);
            }
        }

        private void BuildVents()
        {
            if (_ventCenters != null) return;
            Vector3 c = _wakeArea.width > 1f
                ? new Vector3(_wakeArea.center.x, 0f, _wakeArea.center.y)
                : new Vector3(_home.x, 0f, _home.z - HelmsmanTuning.VentOffsetZ * 1.5f);
            _ventCenters = new[]
            {
                c + new Vector3(-HelmsmanTuning.VentOffsetX, 0f, -HelmsmanTuning.VentOffsetZ),
                c + new Vector3( HelmsmanTuning.VentOffsetX, 0f, -HelmsmanTuning.VentOffsetZ),
                c + new Vector3(-HelmsmanTuning.VentOffsetX, 0f,  HelmsmanTuning.VentOffsetZ),
                c + new Vector3( HelmsmanTuning.VentOffsetX, 0f,  HelmsmanTuning.VentOffsetZ),
            };
        }

        private void TickVents(float dt)
        {
            if (_ventCenters == null) return;
            if (_ventColumnLeft > 0f) _ventColumnLeft -= dt;
            _ventClock += dt;
            if (_ventClock < HelmsmanTuning.VentInterval * CooldownScale) return;
            _ventClock = 0f;
            _ventColumnLeft = MapVentDamage.ColumnDuration;
            FireVents();
        }

        private bool InVent(Vector3 pos)
        {
            float half = HelmsmanTuning.VentSize * 0.5f;
            for (int i = 0; i < _ventCenters.Length; i++)
                if (Mathf.Abs(pos.x - _ventCenters[i].x) <= half && Mathf.Abs(pos.z - _ventCenters[i].z) <= half)
                    return true;
            return false;
        }

        private void BurnIfInVent(Transform t, IDamageable d)
        {
            if (t == null || d == null || !d.IsAlive || !InVent(t.position)) return;
            d.TakeDamage(new DamageInfo(MapVentDamage.DamagePerCycle, t.position, Vector3.up, Team.Neutral,
                source: DamageSource.Environment));
        }

        /// <summary>Once per cycle, <see cref="MapVentDamage.DamagePerCycle"/> to anything standing in a vent:
        /// Max, Sentinels and robots alike (the shared MV-1240 rule), never the Helmsman himself.</summary>
        private void FireVents()
        {
            if (_target != null) BurnIfInVent(_target, _target.GetComponent<IDamageable>());
            IReadOnlyList<Sentinel> sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
                if (sentinels[i] != null) BurnIfInVent(sentinels[i].transform, sentinels[i]);
            IReadOnlyList<RobotEnemy> robots = RobotEnemy.Active;
            for (int i = 0; i < robots.Count; i++)
                if (robots[i] != null) BurnIfInVent(robots[i].transform, robots[i]);
        }

        /// <summary>The two Breakers chained beside the throne snap their chains and charge, once.</summary>
        private void ReleaseBreakers()
        {
            EnemyArchetype archetype = EnemyArchetype.Of(EnemyKind.Breaker);
            for (int i = 0; i < HelmsmanTuning.BreakerCount; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;
                Vector3 pos = _home + new Vector3(side * HelmsmanTuning.BreakerSide, archetype.SpawnHeight, -1.5f);

                RobotEnemy breaker = CreatePod(archetype);
                breaker.gameObject.name = "Helmsman Breaker";
                breaker.transform.position = pos;
                breaker.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                breaker.TagNoReplicatePermanent();
                breaker.SetAreaIndex(_areaIndex);

                var cc = breaker.GetComponent<CharacterController>();
                if (!CharacterControllerSafety.CanCreate(breaker.transform, cc, out string reason))
                {
                    CharacterControllerSafety.LogRefusal("HelmsmanBoss.ReleaseBreakers", breaker.gameObject.name,
                        reason, pos, breaker.transform.lossyScale);
                    Destroy(breaker.gameObject);
                    continue;
                }
                breaker.gameObject.SetActive(true);
                breaker.Activate();   // the chain snaps (ChainSignals) and it charges
            }
        }

        private void TickDying(float dt)
        {
            _deathT += dt;
            if (_deathT >= HelmsmanTuning.DeathSeconds) OnDeath();
        }

        // ---------------------------------------------------------------- leap slam + shockwave (phase 2)

        private void TickSlam(float dt)
        {
            _slamCycle += dt;
            switch (_slamStage)
            {
                case SlamStage.Idle:
                    if (_slamCycle >= HelmsmanTuning.SlamInterval * CooldownScale)
                    {
                        _slamCycle -= HelmsmanTuning.SlamInterval * CooldownScale;
                        _slamStage = SlamStage.Tell;
                        _slamStageT = 0f;
                        // The ring marks Max's spot as it is NOW; moving away is the dodge.
                        _slamCenter = new Vector3(_target.position.x, 0f, _target.position.z);
                    }
                    break;
                case SlamStage.Tell:
                    _slamStageT += dt;
                    if (_slamStageT >= HelmsmanTuning.SlamTellSeconds)
                    {
                        _slamStage = SlamStage.Leap;
                        _slamStageT = 0f;
                        _slamStart = transform.position;
                    }
                    break;
                case SlamStage.Leap:
                    TickLeap(dt);
                    break;
                case SlamStage.Wave:
                    TickShockwave(dt);
                    break;
            }
        }

        private void TickLeap(float dt)
        {
            _slamStageT += dt;
            float f = Mathf.Clamp01(_slamStageT / HelmsmanTuning.SlamLeapSeconds);
            Vector3 p = transform.position;
            Vector3 want = new Vector3(
                Mathf.Lerp(_slamStart.x, _slamCenter.x, f),
                _slamStart.y + 4f * HelmsmanTuning.SlamLeapHeight * f * (1f - f),
                Mathf.Lerp(_slamStart.z, _slamCenter.z, f));
            CharacterControllerMotion.SafeMove(_cc, want - p);
            if (f < 1f) return;

            _verticalVel = 0f;
            Land();
        }

        /// <summary>The landing: 40 damage inside 3 m of the ring's centre, then the shockwave starts with
        /// two opposite 40 degree gaps at a random angle.</summary>
        private void Land()
        {
            ForEachTarget(t =>
            {
                Vector3 to = t.position - _slamCenter; to.y = 0f;
                if (to.magnitude <= HelmsmanTuning.SlamRadius) Hurt(t, HelmsmanTuning.SlamDamage, to);
            });

            _slamStage = SlamStage.Wave;
            _slamStageT = 0f;
            _waveRadius = 0f;
            _waveGapYaw = UnityEngine.Random.Range(0f, 360f);
            _waveHit.Clear();
        }

        private void TickShockwave(float dt)
        {
            _slamStageT += dt;
            float prev = _waveRadius;
            _waveRadius = HelmsmanTuning.ShockwaveRadius
                * Mathf.Clamp01(_slamStageT / HelmsmanTuning.ShockwaveSeconds);
            float cur = _waveRadius;

            ForEachTarget(t =>
            {
                Vector3 to = t.position - _slamCenter; to.y = 0f;
                float d = to.magnitude;
                if (d <= prev || d > cur) return;                 // not crossed this tick
                if (!_waveHit.Add(t.GetInstanceID())) return;     // once per wave

                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float half = HelmsmanTuning.ShockwaveGapDegrees * 0.5f;
                bool inGap = Mathf.Abs(Mathf.DeltaAngle(bearing, _waveGapYaw)) <= half
                          || Mathf.Abs(Mathf.DeltaAngle(bearing, _waveGapYaw + 180f)) <= half;
                if (inGap) return;

                // 1.6 m cover between the landing and the target stops it.
                Vector3 from = new Vector3(_slamCenter.x, HelmsmanTuning.ShockwaveCastHeight, _slamCenter.z);
                if (Physics.Raycast(from, to / d, d, CoverLayer.Mask, QueryTriggerInteraction.Ignore)) return;

                Hurt(t, HelmsmanTuning.ShockwaveDamage, to);
            });

            if (_slamStageT >= HelmsmanTuning.ShockwaveSeconds) _slamStage = SlamStage.Idle;
        }

        private void ForEachTarget(System.Action<Transform> act)
        {
            if (_target != null) act(_target);
            IReadOnlyList<Sentinel> sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
                if (sentinels[i] != null) act(sentinels[i].transform);
        }

        private void Hurt(Transform t, float amount, Vector3 away)
        {
            if (!t.TryGetComponent<IDamageable>(out var damageable) || !damageable.IsAlive) return;
            Vector3 dir = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            damageable.TakeDamage(new DamageInfo(amount, transform.position, dir, Team.Enemy));
        }

        /// <summary>Phase 2's own airlock cycle (MV-1313): the two Bridge airlocks (MV-1310) blow in
        /// turn every <see cref="HelmsmanTuning.AirlockCycleSeconds"/>, alternating sides, each with a
        /// <see cref="HelmsmanTuning.AirlockWarnSeconds"/> white warning flash on its own panel first
        /// (<see cref="AirlockPanel.MarkPendingBlow"/>) before the blow itself
        /// (<see cref="AirlockPanel.Hit"/> — the same call a player hit or a Welder's own cut makes,
        /// MV-1305/MV-1312). The Helmsman never consults <see cref="AirlockPanel.RobotPullAt"/> or
        /// <see cref="AirlockPanel.MaxPullAt"/> for her own movement (those are read only by
        /// <c>RobotEnemy</c>/<c>PlayerController</c>), so she is exempt from the pull by construction —
        /// the ticket's own "the Helmsman herself is exempt from the pull".</summary>
        private void TickAirlockCycle(float dt)
        {
            ResolveBridgeAirlocksOnce();
            if (_bridgeAirlocks == null || _bridgeAirlocks.Length == 0) return;

            _airlockElapsed += dt;
            AirlockPanel panel = _bridgeAirlocks[_airlockCycleIndex % _bridgeAirlocks.Length];

            float blowAt = (_airlockCycleIndex + 1) * HelmsmanTuning.AirlockCycleSeconds;
            float warnAt = blowAt - HelmsmanTuning.AirlockWarnSeconds;

            if (_airlockElapsed >= warnAt && _airlockElapsed < blowAt) panel.MarkPendingBlow();

            if (_airlockElapsed >= blowAt)
            {
                panel.Hit();
                _airlockCycleIndex++;
            }
        }

        /// <summary>Finds this boss's own area's airlocks exactly once, off the static registry
        /// (<see cref="AirlockPanel.PanelsInArea"/>) keyed by this boss's own <see cref="_areaIndex"/> —
        /// resolved lazily on the first Phase 2 tick rather than in <see cref="Wake"/>, since
        /// <see cref="AirlockPanel.AreaIndex"/> is stamped by <c>WorldRunner.Build</c>'s own pass over
        /// the loaded config, which always completes before any boss's Update ever runs. Sorted by
        /// world X so "alternating sides" is deterministic across a run.</summary>
        private void ResolveBridgeAirlocksOnce()
        {
            if (_bridgeAirlocksResolved) return;
            _bridgeAirlocksResolved = true;

            List<AirlockPanel> found = AirlockPanel.PanelsInArea(_areaIndex);
            found.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            _bridgeAirlocks = found.ToArray();
        }

        private void Approach(float dt)
        {
            if (_target == null) return;
            TickApproach(dt, _target.position);
        }

        /// <summary>The actual approach-and-steer step, dt/target-parameterized so a test can drive it
        /// directly — same shape as <see cref="SludgequeenBoss.TickApproach"/>, but stopping at the
        /// ticket's own fixed 3 m stand-off rather than a collider-reach-derived one.</summary>
        public void TickApproach(float dt, Vector3 targetPosition)
        {
            Vector3 to = targetPosition - transform.position;
            to.y = 0f;

            if (to.magnitude <= HelmsmanTuning.WalkStandoff)
            {
                DriftAtStandoff(dt, to, HelmsmanTuning.MoveSpeed);
                return;
            }

            Vector3 waypoint = EnemyNavigation.Waypoint(transform.position, targetPosition,
                useZoneRoute: true, budget: _routeBudget, dt: dt);
            Vector3 routeTo = waypoint - transform.position;
            routeTo.y = 0f;
            Vector3 bearing = routeTo.sqrMagnitude > 0.0001f ? routeTo.normalized : to.normalized;

            Vector3 desired = _wallLatch.Tick(bearing, transform.position, dt, _preferSign);
            CharacterControllerMotion.SafeMove(_cc, desired * HelmsmanTuning.MoveSpeed * dt);
        }

        /// <summary>Same circling drift as <see cref="SludgequeenBoss.DriftAtStandoff"/>, so a walking
        /// Helmsman never stops dead at its stand-off either.</summary>
        private void DriftAtStandoff(float dt, Vector3 to, float speed)
        {
            if (to.sqrMagnitude < 0.0001f) return; // exactly on top of the target -- no ring to walk
            Vector3 inward = to.normalized;
            Vector3 tangent = new Vector3(-inward.z, 0f, inward.x) * _preferSign;

            Vector3 desired = _wallLatch.Tick(tangent, transform.position, dt, _preferSign);
            CharacterControllerMotion.SafeMove(_cc, desired * speed * dt);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit) => HandleWallContact(hit.collider, hit.normal);

        private void HandleWallContact(Collider collider, Vector3 normal)
        {
            if (Mathf.Abs(normal.y) >= 0.5f) return;
            if (collider.TryGetComponent<CharacterController>(out _)) return;
            _wallLatch.NoteHit(normal);
        }

        // ---------------------------------------------------------------- contact damage (phase 2 only)

        /// <summary>Phase 2's passive touch damage — same "Bosses must do damage to Max and Sentinels in
        /// every world" shape as <see cref="SludgequeenBoss.TickContactDamage"/>: rate-limited, distance-
        /// based against the boss's own world-space collider radius.</summary>
        private void TickContactDamage(float dt)
        {
            _contactCooldownTimer -= dt;
            if (_contactCooldownTimer > 0f) return;

            float bossRadius = WorldRadius(_cc, transform);
            bool hitSomething = DamageIfTouching(_target, bossRadius);

            IReadOnlyList<Sentinel> sentinels = Sentinel.Active;
            for (int i = 0; i < sentinels.Count; i++)
            {
                Sentinel s = sentinels[i];
                if (DamageIfTouching(s != null ? s.transform : null, bossRadius)) hitSomething = true;
            }

            if (hitSomething) _contactCooldownTimer = HelmsmanTuning.ContactCooldown;
        }

        private bool DamageIfTouching(Transform t, float bossRadius)
        {
            if (t == null) return false;
            Vector3 to = t.position - transform.position; to.y = 0f;
            float reach = bossRadius + TargetRadius(t) + HelmsmanTuning.ContactSkin;
            if (to.magnitude > reach) return false;

            if (!t.TryGetComponent<IDamageable>(out var damageable) || !damageable.IsAlive) return false;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : Vector3.forward;
            damageable.TakeDamage(new DamageInfo(HelmsmanTuning.ContactDamagePerTick, transform.position, dir, Team.Enemy));
            return true;
        }

        private static float TargetRadius(Transform t) =>
            t.TryGetComponent<CharacterController>(out var cc) ? WorldRadius(cc, t) : EnemyArchetype.PlayerRadius;

        private static float WorldRadius(CharacterController cc, Transform t)
        {
            Vector3 scale = t.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        // ---------------------------------------------------------------- pod volley (phase 1 only)

        /// <summary>Releases a pod volley down the side hatches, never taking the Helmsman's own live pod
        /// count past <see cref="HelmsmanTuning.MaxConcurrentPods"/> — counted fresh every wave, never a
        /// decrement-only field, same rule MV-1085/MV-1127 give Big Bermuda's and Sludgequeen's own
        /// volleys. Composition draws from the shared <see cref="BroodSpawnLevels"/> table at this boss's
        /// current <see cref="BigBermudaBrain.SpawnLevel"/> ("as today" — the ticket's own words).</summary>
        private void SpawnPods()
        {
            int alive = CountLivePodsInArea();
            int volley = _health.Normalized < HelmsmanTuning.PodLowHealthFraction
                ? HelmsmanTuning.PodsPerVolleyLow : HelmsmanTuning.PodsPerVolley;
            int want = Mathf.Min(volley, Mathf.Max(0, HelmsmanTuning.MaxConcurrentPods - alive));
            if (want <= 0) return;

            Vector3 pos = transform.position;
            Quaternion facing = transform.rotation;
            EnemyKind[] kinds = BroodSpawnLevels.KindsFor(_brain.SpawnLevel);

            for (int i = 0; i < want; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;
                EnemyKind kind = kinds[UnityEngine.Random.Range(0, kinds.Length)];
                EnemyArchetype archetype = EnemyArchetype.Of(kind);

                Vector3 landing = BroodArc.Landing(pos, facing, side, HelmsmanTuning.HatchSide,
                    HelmsmanTuning.HatchLandingForward, archetype.SpawnHeight, 0f);

                RobotEnemy add = CreatePod(archetype);
                add.transform.position = landing;
                add.transform.rotation = facing;
                add.TagNoReplicatePermanent(); // a boss-flung robot may never be lured into a Replicator
                add.SetAreaIndex(_areaIndex);

                var cc = add.GetComponent<CharacterController>();
                if (!CharacterControllerSafety.CanCreate(add.transform, cc, out string spawnReason))
                {
                    CharacterControllerSafety.LogRefusal("HelmsmanBoss.SpawnPods", add.gameObject.name,
                        spawnReason, landing, add.transform.lossyScale);
                    Destroy(add.gameObject);
                    continue;
                }

                add.gameObject.SetActive(true);
            }
        }

        /// <summary>This boss's own pods currently alive — counted fresh off <see cref="_podRoot"/> every
        /// wave, never carried in a decrement-only field, so one lost by any route frees its place the
        /// instant it stops being alive.</summary>
        private int CountLivePodsInArea()
        {
            if (_podRoot == null) return 0;

            int count = 0;
            for (int i = 0; i < _podRoot.childCount; i++)
            {
                Transform child = _podRoot.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                if (!child.TryGetComponent<RobotEnemy>(out RobotEnemy robot) || !robot.enabled || !robot.IsAlive) continue;
                count++;
            }
            return count;
        }

        private RobotEnemy CreatePod(in EnemyArchetype a)
        {
            var go = GameObject.CreatePrimitive(a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.name = $"Helmsman Pod {a.Kind}";
            go.transform.SetParent(PodRoot(), false);
            go.transform.localScale = a.BodyScale;

            go.SetActive(false);
            if (!CharacterControllerSafety.CanCreate(go.transform, null, out string createReason))
            {
                CharacterControllerSafety.LogRefusal("HelmsmanBoss.CreatePod", go.name,
                    createReason, go.transform.position, go.transform.lossyScale);
            }

            var cc = go.AddComponent<CharacterController>();
            float lateral = Mathf.Max(a.BodyScale.x, a.BodyScale.z);
            cc.height = a.ColliderHeight / Mathf.Max(a.BodyScale.y, 1e-4f);
            cc.radius = a.ColliderRadius / Mathf.Max(lateral, 1e-4f);
            cc.center = Vector3.zero;

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(a);
            go.AddComponent<MaxWorlds.VFX.RobotRig>();
            e.gameObject.SetActive(false);
            return e;
        }

        /// <summary>Top-level and unit-scaled, same reasoning as <see cref="SludgequeenBoss.BroodRoot"/>
        /// — the boss never moves while pods are active (Phase 1 is seated), but matching the pattern
        /// keeps this consistent with every other boss's own brood root.</summary>
        private Transform PodRoot()
        {
            if (_podRoot == null) _podRoot = new GameObject("Helmsman Pods").transform;
            return _podRoot;
        }

        private void OnDestroy()
        {
            if (_podRoot != null) Destroy(_podRoot.gameObject);
            BossCensus.Forget(this);
        }

        // ---------------------------------------------------------------- IDamageable

        public void TakeDamage(in DamageInfo info)
        {
            // Same "a hit landing is itself a wake trigger" rule as every other boss's TakeDamage —
            // consumed as the wake, not as damage.
            if (_phase == Phase.Dormant)
            {
                if (!DamageRules.Applies(info.Attacker, Team)) return;
                Wake();
                return;
            }

            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            if (_phase == Phase.Transition) return;   // MV-1370: the P1 -> P2 beat is invulnerable
            float amount = info.Amount;
            if (CoreExposed && IsCoreHit(info.Point)) amount *= HelmsmanTuning.CoreDamageMultiplier;
            HudSignals.EmitDamage(transform.position + Vector3.up * 2.5f, amount);
            _health.TakeDamage(amount);
            BossCensus.ReportHealth(this, _health.Current, _health.Max);

            // MV-1370: below 66% it tears free - a 2 s invulnerable beat first, then it walks.
            if (_phase == Phase.Seated && _health.Normalized <= HelmsmanTuning.PhaseTwoThreshold)
                BeginTransition(false);
            // MV-1371: below 33% the wheel spins up for a 2 s invulnerable beat, then phase 3 (MV-1372).
            else if (_phase == Phase.Walking && !_phase3Begun && _health.Normalized <= HelmsmanTuning.PhaseThreeThreshold)
                BeginTransition(true);

            if (InPhase3 && !_lastStand && _health.IsAlive && _health.Normalized <= HelmsmanTuning.LastStandThreshold)
            {
                _lastStand = true;
                _beams.Interval = HelmsmanTuning.SingleBeamInterval * HelmsmanTuning.LastStandCooldownMultiplier;
            }
        }

        /// <summary>A hit lands on the core when its point is within <see cref="HelmsmanTuning.CoreHitRadius"/>
        /// of the core's world position (the rig's chest hex).</summary>
        private bool IsCoreHit(Vector3 point)
        {
            if (_rig == null) return false;
            return (point - _rig.CoreWorldPosition).sqrMagnitude
                <= HelmsmanTuning.CoreHitRadius * HelmsmanTuning.CoreHitRadius;
        }

        /// <summary>The killing blow starts the 3 s death beat (MV-1372): inert while the rig collapses and
        /// the wheel shears off and rolls; <see cref="OnDeath"/> then drops the key.</summary>
        private void BeginDeath()
        {
            if (_phase == Phase.Dying || _phase == Phase.Dead) return;
            _phase = Phase.Dying;
            _deathT = 0f;
            _slamStage = SlamStage.Idle;
            _beams.Reset();
            _volleyLeft = 0;
            _ventColumnLeft = 0f;
        }

        private void OnDeath()
        {
            _phase = Phase.Dead;
            BossCensus.ReportDefeated(this);

            // MV-1313: "on her death both reseal and stop" -- the cycle itself already stops (TickWalking
            // never runs again once _phase is Dead), this forces whichever airlock is mid-cycle back to
            // READY immediately rather than leaving it to finish its own 20 s recharge.
            if (_bridgeAirlocks != null)
                for (int i = 0; i < _bridgeAirlocks.Length; i++)
                    _bridgeAirlocks[i]?.ForceReady();

            // MV-1245 owns what collecting the Helm Key actually DOES (the viewport exit) — this only
            // drops it where the Helmsman dies.
            PickupDirector.EnsureInstalled().PlacePickupAt(PickupKind.HelmKey, transform.position);
            gameObject.SetActive(false);
        }

        private void AcquireTarget()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            _target = p.transform;
        }

        private void FaceTarget()
        {
            if (_target == null) return;
            Vector3 to = _target.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        private void ApplyGravity(float dt)
        {
            if (_cc == null || !_cc.enabled) return;
            if (_cc.isGrounded && _verticalVel < 0f) _verticalVel = -2f;
            _verticalVel -= Gravity * dt;
            _cc.Move(Vector3.up * _verticalVel * dt);
        }
    }
}
