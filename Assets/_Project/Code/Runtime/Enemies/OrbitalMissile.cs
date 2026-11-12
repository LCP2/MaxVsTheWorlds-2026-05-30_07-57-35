using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// ORBITAL's light missile (MV-1344) — a pooled, slow-steering crimson ball. Not a sniper: it flies
    /// OVER cover at 1.7 m, steers toward Max with a turn rate that grows the longer it is airborne
    /// (45 deg/s + 10 deg/s per second, so it arcs wide and comes back), and is an "encumbrance he has to
    /// navigate around". Every number is read live off <see cref="OrbitalTuning"/>, so a moved Settings
    /// slider affects missiles already in flight.
    ///
    /// Counterplay: it is an <see cref="IDamageable"/> on <see cref="Team.Enemy"/>, so any of Max's
    /// damage pops it in one hit; it deals damage to nothing but Max (contact 0.45 m XZ, no splash) and
    /// never hurts robots. Max's own Light Missile targeting walks <c>RobotEnemy.Active</c>, so these are
    /// never TARGETED. Ends (a pop, no damage) on life out, on leaving its ORBITAL's area, or when the
    /// ORBITAL dies (<see cref="SchedulePop"/>). A popped missile's trail keeps fading for its own length.
    /// </summary>
    [MaxWorlds.Core.PerfSection("weapons")]
    public sealed class OrbitalMissile : MonoBehaviour, IDamageable
    {
        /// <summary>#FF3D6E — crimson, never violet (Max's Light Missile).</summary>
        public static readonly Color BallColor = new Color(1f, 0x3D / 255f, 0x6E / 255f);
        private static readonly Color CoreColor = new Color(1f, 0.96f, 0.92f);

        private const float ColliderRadius = 0.3f;
        private const float ShoveSpeed = 5f;
        private const float ShoveSeconds = 0.1f;

        private static readonly List<OrbitalMissile> s_pool = new List<OrbitalMissile>();

        private Transform _target;
        private IDamageable _targetDamageable;
        private int _areaIndex;
        private Vector3 _dir;
        private float _age;
        private float _travelled;
        private bool _active;
        private bool _popping;
        private float _popTimer;
        private float _scheduledPopIn = -1f;
        private TrailRenderer _trail;
        private GameObject _ballRoot;
        private SphereCollider _collider;

        /// <summary>True while it is flying (not yet popped) — what counts toward the cap and the budget.</summary>
        public bool IsFlying => _active && !_popping;
        public float Age => _age;
        public Vector3 Heading => _dir;
        /// <summary>The heading it was launched on, for tests (set once in <see cref="Spawn"/>).</summary>
        public Vector3 LaunchHeading { get; private set; }

        // ---- IDamageable --------------------------------------------------------------------------
        bool IDamageable.IsAlive => IsFlying;
        Team IDamageable.Team => Team.Enemy;

        void IDamageable.TakeDamage(in DamageInfo info)
        {
            if (!IsFlying) return;
            if (!DamageRules.Applies(info.Attacker, Team.Enemy)) return;
            Pop(0f);
        }

        /// <summary>Flying missiles world-wide (rule 11's budget counts these, not fading trails).</summary>
        public static int FlyingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < s_pool.Count; i++)
                    if (s_pool[i] != null && s_pool[i].IsFlying) n++;
                return n;
            }
        }

        /// <summary>Launch one missile. <paramref name="feet"/> is the launcher's ground position; the
        /// missile flies at <see cref="OrbitalTuning.FlightHeight"/> above it. Null when the world budget
        /// (<see cref="OrbitalTuning.WorldBudget"/>) is spent. <paramref name="areaIndex"/> &lt;= 0
        /// disables the "leaves its own area" rule.</summary>
        public static OrbitalMissile Spawn(Vector3 feet, Vector3 heading, Transform target, int areaIndex)
        {
            PrunePool();
            if (FlyingCount >= OrbitalTuning.WorldBudget) return null;

            OrbitalMissile m = null;
            for (int i = 0; i < s_pool.Count; i++)
                if (!s_pool[i]._active) { m = s_pool[i]; break; }

            if (m == null)
            {
                var go = new GameObject("OrbitalMissile (stand-in)");
                m = go.AddComponent<OrbitalMissile>();
                m.BuildVisual();
                s_pool.Add(m);
            }

            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;
            heading.Normalize();

            m.gameObject.SetActive(true);
            m.transform.position = new Vector3(feet.x, feet.y + OrbitalTuning.FlightHeight, feet.z);
            m.transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
            m._dir = heading;
            m.LaunchHeading = heading;
            m._target = target;
            m._targetDamageable = target != null ? target.GetComponent<IDamageable>() : null;
            m._areaIndex = areaIndex;
            m._age = 0f;
            m._travelled = 0f;
            m._popping = false;
            m._popTimer = 0f;
            m._scheduledPopIn = -1f;
            m._active = true;
            m._ballRoot.SetActive(true);
            m._collider.enabled = true;
            m._trail.Clear();
            m._trail.emitting = true;
            return m;
        }

        /// <summary>Retires every pooled missile — scene reset / test teardown.</summary>
        public static void ClearAll()
        {
            PrunePool();
            for (int i = 0; i < s_pool.Count; i++)
                if (s_pool[i]._active) s_pool[i].Retire();
        }

        /// <summary>Test-only teardown — destroys every pooled instance.</summary>
        public static void DestroyPoolForTests()
        {
            for (int i = 0; i < s_pool.Count; i++)
                if (s_pool[i] != null) DestroyImmediate(s_pool[i].gameObject);
            s_pool.Clear();
        }

        private static void PrunePool()
        {
            for (int i = s_pool.Count - 1; i >= 0; i--)
                if (s_pool[i] == null) s_pool.RemoveAt(i);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterSceneUnloadHook()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private static void OnSceneUnloaded(Scene scene) => s_pool.Clear();

        /// <summary>Pop after <paramref name="delay"/> seconds (the ORBITAL died: its missiles go within 0.3 s).</summary>
        public void SchedulePop(float delay)
        {
            if (!IsFlying) return;
            if (delay <= 0f) { Pop(0f); return; }
            if (_scheduledPopIn < 0f || delay < _scheduledPopIn) _scheduledPopIn = delay;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The per-frame step — public so an EditMode test can drive it deterministically.</summary>
        public void Tick(float dt)
        {
            if (!_active) return;

            if (_popping)
            {
                _popTimer -= dt;
                if (_popTimer <= 0f) Retire();
                return;
            }

            if (_scheduledPopIn >= 0f)
            {
                _scheduledPopIn -= dt;
                if (_scheduledPopIn <= 0f) { Pop(0f); return; }
            }

            _age += dt;
            if (_age >= OrbitalTuning.Life) { Pop(0f); return; }

            // Live trail length (the Settings knob) — Pop reuses it for the fade-out.
            _trail.time = Mathf.Max(0.01f, OrbitalTuning.TrailSeconds);

            // Steer toward Max's CURRENT position, but only after the first straight metre (rule 4).
            if (_target != null && _travelled >= OrbitalTuning.StraightMetres)
            {
                Vector3 want = _target.position - transform.position;
                want.y = 0f;
                if (want.sqrMagnitude > 1e-6f)
                {
                    float rate = OrbitalTuning.TurnAtLaunch + OrbitalTuning.TurnGrowth * _age;   // deg/s
                    float angle = Vector3.SignedAngle(_dir, want, Vector3.up);
                    float turn = Mathf.Clamp(angle, -rate * dt, rate * dt);
                    _dir = Quaternion.AngleAxis(turn, Vector3.up) * _dir;
                }
            }

            float step = OrbitalTuning.MissileSpeed * dt;
            transform.position += _dir * step;
            transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
            _travelled += step;

            if (_areaIndex > 0 && LightMissileBall.AreaIndexAt(transform.position) != _areaIndex)
            {
                Pop(0f);
                return;
            }

            if (_target != null)
            {
                Vector3 d = transform.position - _target.position;
                d.y = 0f;
                if (d.sqrMagnitude <= OrbitalTuning.ContactRadius * OrbitalTuning.ContactRadius)
                    HitTarget();
            }
        }

        private void HitTarget()
        {
            float dealt = 0f;
            if (_targetDamageable != null && _targetDamageable.IsAlive && _targetDamageable.Team != Team.Enemy)
            {
                dealt = OrbitalTuning.Damage;
                _targetDamageable.TakeDamage(new DamageInfo(dealt, transform.position, _dir, Team.Enemy));
                if (_target != null && _target.TryGetComponent(out PlayerController player))
                    player.ApplyForcedMove(_dir * ShoveSpeed, ShoveSeconds, ForcedMoveSource.Knockback);
            }
            Pop(dealt);
        }

        /// <summary>The pop: a small flash, no damage of its own; the ball vanishes at once and the trail
        /// fades out over its own length (rule 12).</summary>
        private void Pop(float damageDealt)
        {
            if (!IsFlying) return;
            _popping = true;
            _collider.enabled = false;
            _ballRoot.SetActive(false);
            _trail.emitting = false;
            _popTimer = Mathf.Max(0.01f, _trail.time);
            HudSignals.EmitMissileImpact(transform.position, damageDealt);
        }

        private void Retire()
        {
            _active = false;
            _popping = false;
            gameObject.SetActive(false);
        }

        private void BuildVisual()
        {
            gameObject.AddComponent<KeepsOwnMaterial>();

            _ballRoot = new GameObject("Ball");
            _ballRoot.transform.SetParent(transform, false);

            BuildSphere(_ballRoot.transform, "Body", OrbitalTuning.BallDiameter,
                VfxMaterials.AdditiveTinted(BallColor));
            BuildSphere(_ballRoot.transform, "Core", OrbitalTuning.BallDiameter * 0.45f,
                VfxMaterials.AdditiveTinted(CoreColor));
            Color halo = BallColor * 0.4f;
            BuildSphere(_ballRoot.transform, "Halo", OrbitalTuning.BallDiameter * 1.9f,
                VfxMaterials.AdditiveTinted(halo));

            // Physics collider so Max's beam/pulses (which skip triggers) can hit and pop it.
            _collider = gameObject.AddComponent<SphereCollider>();
            _collider.radius = ColliderRadius;

            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = OrbitalTuning.DefaultTrailSeconds;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));   // tapers to zero
            _trail.widthMultiplier = OrbitalTuning.BallDiameter;                                  // starts at the ball's width
            _trail.minVertexDistance = 0.05f;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(BallColor, 0f), new GradientColorKey(BallColor, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });   // 80% -> 0
            _trail.colorGradient = gradient;
        }

        private static void BuildSphere(Transform parent, string name, float diameter, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * diameter;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    /// <summary>
    /// One ORBITAL's firing control (MV-1344) — spin-up, cadence, the semi-spiral launch sequence and the
    /// in-flight cap, with no dependence on <see cref="RobotEnemy"/> so an EditMode test can tick it with
    /// a stationary Max and a clock (<see cref="Tick"/> is the public seam). <see cref="RobotEnemy"/> owns
    /// one and ticks it every frame while the robot is awake.
    /// </summary>
    public sealed class OrbitalBattery
    {
        private readonly List<OrbitalMissile> _live = new List<OrbitalMissile>();
        private bool _engaged;
        private float _spinUp;
        private float _cycle;
        private int _step;

        /// <summary>Missiles in flight right now (popped ones, still fading their trails, don't count).</summary>
        public int InFlight { get { Prune(); return _live.Count; } }

        /// <summary>The crown's spin-up, 0..1 (1 once firing). Drives the body's tell.</summary>
        public float SpinUpProgress => !_engaged ? 0f : Mathf.Clamp01(_spinUp / OrbitalTuning.SpinUpSeconds);

        /// <summary>The 0.25 s firing-tube glow before each launch, 0..1.</summary>
        public float GlowProgress
        {
            get
            {
                if (!_engaged || _spinUp < OrbitalTuning.SpinUpSeconds) return 0f;
                return Mathf.Clamp01((_cycle - (OrbitalTuning.FireInterval - OrbitalTuning.GlowSeconds)) / OrbitalTuning.GlowSeconds);
            }
        }

        public IReadOnlyList<OrbitalMissile> Live { get { Prune(); return _live; } }

        /// <summary>Total missiles this battery has launched (the launch-sequence step).</summary>
        public int Launched => _step;

        /// <summary>Advance by <paramref name="dt"/>. <paramref name="feet"/> is the launcher's ground
        /// position. Returns the number of missiles launched this tick (0 or 1).</summary>
        public int Tick(float dt, Vector3 feet, Transform target, int areaIndex)
        {
            Prune();
            if (target == null) return 0;

            Vector3 to = target.position - feet;
            to.y = 0f;
            float dist = to.magnitude;

            if (!_engaged)
            {
                if (dist > OrbitalTuning.SpinUpRange) return 0;
                _engaged = true;
                _spinUp = 0f;
            }

            if (_spinUp < OrbitalTuning.SpinUpSeconds)
            {
                _spinUp += dt;
                if (_spinUp >= OrbitalTuning.SpinUpSeconds)
                    _cycle = Mathf.Max(0f, OrbitalTuning.FireInterval - OrbitalTuning.GlowSeconds);
                return 0;
            }

            // Past ~1.5x the spin-up range it holds fire (Max is not in play), without losing its spin.
            if (dist > OrbitalTuning.SpinUpRange * 1.5f) return 0;

            float interval = OrbitalTuning.FireInterval;
            _cycle = Mathf.Min(_cycle + dt, interval);
            if (_cycle < interval) return 0;
            if (_live.Count >= OrbitalTuning.MaxInFlight) return 0;   // at the cap it waits

            Vector3 line = dist > 1e-3f ? to / dist : Vector3.forward;
            Vector3 heading = Quaternion.AngleAxis(OrbitalTuning.LaunchAngle(_step), Vector3.up) * line;
            OrbitalMissile m = OrbitalMissile.Spawn(feet, heading, target, areaIndex);
            if (m == null) return 0;   // world budget spent — try again next tick

            _live.Add(m);
            _step++;
            _cycle -= interval;
            return 1;
        }

        /// <summary>The ORBITAL died or was captured: every missile pops within
        /// <see cref="OrbitalTuning.PopAllWithinSeconds"/>, staggered across that window.</summary>
        public void PopAll()
        {
            Prune();
            int n = _live.Count;
            for (int i = 0; i < n; i++)
                _live[i].SchedulePop(OrbitalTuning.PopAllWithinSeconds * (i + 1) / (n + 1) * 0.9f);
            _engaged = false;
            _spinUp = 0f;
            _cycle = 0f;
        }

        /// <summary>A pooled robot's next life starts fresh.</summary>
        public void Reset()
        {
            PopAll();
            _live.Clear();
            _step = 0;
        }

        private void Prune()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i] == null || !_live[i].IsFlying) _live.RemoveAt(i);
        }
    }
}
