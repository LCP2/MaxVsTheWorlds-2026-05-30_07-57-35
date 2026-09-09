using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// A small turret mounted on a shed roof corner (MV-547, shed roadmap stage 2) — as the game
    /// progresses, sheds carry weapon fittings that are independently destroyable, on a FIXED authored
    /// curve (per the World &amp; Difficulty Framework, never scaled to Max), the same way every other
    /// area-authored hazard already is.
    ///
    /// Each fitting has its own <see cref="DestructibleHealth"/>, IDamageable like <see cref="MowerHutch"/>,
    /// and drops NO loot and counts in NO economy (composition THV, MV-375 large-robot count) — it is
    /// invisible to both by construction, the same way <see cref="MowerHutch"/> is: it never implements
    /// <see cref="MaxWorlds.Enemies.RobotEnemy"/> and never raises
    /// <see cref="MaxWorlds.Pickups.DropSignals.RobotDied"/>.
    ///
    /// Bound to the shed it rides on via <see cref="Bind"/>: there is no public "I died" event on
    /// <see cref="MowerHutch"/> to subscribe to, so this polls <see cref="MowerHutch.IsAlive"/> every
    /// tick and takes itself out the instant it flips false — the same idiom <c>FactoryHusk</c> already
    /// uses for the same reason.
    /// </summary>
    public sealed class ShedFitting : MonoBehaviour, IDamageable
    {
        /// <summary>The per-type table from the ticket: range/cadence/HP, fixed and never scaled.</summary>
        public readonly struct Stats
        {
            public readonly float Range;
            public readonly float Cadence;
            public readonly float Hp;
            public Stats(float range, float cadence, float hp) { Range = range; Cadence = cadence; Hp = hp; }
        }

        public static Stats StatsFor(ShedFittingKind kind) => kind switch
        {
            ShedFittingKind.Spiker => new Stats(range: 8f, cadence: 2.5f, hp: 40f),
            ShedFittingKind.Laser => new Stats(range: 12f, cadence: 6f, hp: 60f),
            ShedFittingKind.Missile => new Stats(range: 14f, cadence: 8f, hp: 80f),
            _ => new Stats(0f, 0f, 0f),
        };

        // --- Spiker: the Bolter's own bolt-flight numbers (EnemyArchetype.Bolter) reused verbatim so
        // the fitting's shot behaves exactly like the enemy's. ---
        private const float SpikerBoltSpeed = 14f;
        private const float SpikerHitRadius = 0.35f;

        // --- Laser: the Gunner's own telegraph/beam numbers (EnemyArchetype.Gunner) reused verbatim.
        // Cadence (6s) is the FULL cycle; recover is whatever's left after telegraph + beam. ---
        private const float LaserDps = 18f;
        private const float LaserTelegraphTime = 0.5f;
        private const float LaserBeamTime = 1.1f;

        // --- Missile: the Launcher's own splash numbers (EnemyArchetype.Launcher) reused verbatim. ---
        private const float MissileSpeed = 4.5f;
        private const float MissileDamage = 22f;
        private const float MissileSplashRadius = 2f;

        private enum Phase { Idle, Telegraph, Beam }

        private ShedFittingKind _kind;
        private MowerHutch _hutch;
        private DestructibleHealth _health;
        private Transform _target;
        private IDamageable _targetDamageable;
        private Phase _phase;
        private float _phaseTimer;
        private float _cooldownTimer;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // Water Blaster (Team.Player) can damage it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;
        public ShedFittingKind Kind => _kind;
        public float Range => StatsFor(_kind).Range;
        public float Cadence => StatsFor(_kind).Cadence;

        /// <summary>Wire this fitting to the shed it rides on and its authored kind (MV-547). Public and
        /// explicit — like <see cref="MowerHutch.Build"/> — so an EditMode test can drive it directly
        /// outside Play mode, with no reliance on Awake (which AddComponent never fires in Edit mode).</summary>
        public void Bind(MowerHutch hutch, ShedFittingKind kind)
        {
            _hutch = hutch;
            _kind = kind;
            _health = new DestructibleHealth(StatsFor(kind).Hp);
        }

        /// <summary>Point this fitting at what it should shoot — a test's synthetic Max, or (via
        /// <see cref="Update"/>) the real Player tag lookup. Public and explicit for the same reason
        /// <see cref="Bind"/> is.</summary>
        public void SetTarget(Transform target, IDamageable damageable = null)
        {
            _target = target;
            _targetDamageable = damageable ?? (target != null ? target.GetComponent<IDamageable>() : null);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return; // robots can't wreck a fitting either
            HudSignals.EmitDamage(transform.position + Vector3.up * 0.4f, info.Amount);
            _health.TakeDamage(info.Amount);
            if (!IsAlive) OnDestroyed();
        }

        /// <summary>Destroying the shed takes every surviving fitting with it (the ticket's own rule) —
        /// bypasses <see cref="DamageRules"/> entirely since this isn't combat damage, it's the fitting's
        /// mount going away.</summary>
        private void KillWithShed()
        {
            if (!IsAlive) return;
            _health.TakeDamage(_health.Max);
            OnDestroyed();
        }

        private void OnDestroyed()
        {
            HudSignals.EmitFittingDestroyed(transform.position);
            var rend = GetComponent<Renderer>();
            if (rend != null) rend.enabled = false;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        private void Update()
        {
            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) SetTarget(p.transform);
            }

            Tick(Time.deltaTime);
        }

        /// <summary>Advances this fitting one step — dt-parameterized, like
        /// <see cref="MowerHutch.TickMobility"/>, so an EditMode test can drive it directly without a
        /// live scene or <see cref="Time.deltaTime"/>. Checks the shed's own pulse FIRST, every tick:
        /// there is no public "I died" event on <see cref="MowerHutch"/> to subscribe to instead (the
        /// same poll idiom <c>FactoryHusk</c> already uses), so this is also what makes "destroying the
        /// shed destroys surviving fittings" testable with a single explicit call rather than needing a
        /// live <see cref="Update"/> loop.</summary>
        public void Tick(float dt)
        {
            if (_hutch != null && !_hutch.IsAlive) { KillWithShed(); return; }
            if (!IsAlive || _kind == ShedFittingKind.None) return;

            switch (_phase)
            {
                case Phase.Idle:
                    _cooldownTimer -= dt;
                    if (_cooldownTimer > 0f || !InRangeAndSighted()) return;
                    BeginAttack();
                    break;
                case Phase.Telegraph:
                    TickTelegraph(dt);
                    break;
                case Phase.Beam:
                    TickBeam(dt);
                    break;
            }
        }

        private bool InRangeAndSighted()
        {
            if (_target == null) return false;
            Vector3 to = _target.position - transform.position; to.y = 0f;
            if (to.magnitude > StatsFor(_kind).Range) return false;
            return LineOfSight.Between(transform, _target);
        }

        private void BeginAttack()
        {
            if (_kind == ShedFittingKind.Laser)
            {
                _phase = Phase.Telegraph;
                _phaseTimer = 0f;
                return;
            }

            Fire();
            _phase = Phase.Idle;
            _cooldownTimer = StatsFor(_kind).Cadence;
        }

        private void TickTelegraph(float dt)
        {
            _phaseTimer += dt;
            if (_phaseTimer < LaserTelegraphTime) return;
            _phase = Phase.Beam;
            _phaseTimer = 0f;
        }

        private void TickBeam(float dt)
        {
            _phaseTimer += dt;

            if (InRangeAndSighted())
            {
                if (_targetDamageable != null && _targetDamageable.IsAlive)
                {
                    Vector3 dir = _target.position - transform.position; dir.y = 0f;
                    _targetDamageable.TakeDamage(new DamageInfo(
                        LaserDps * dt, transform.position, dir.normalized, Team.Enemy));
                }
            }

            if (_phaseTimer < LaserBeamTime) return;
            _phase = Phase.Idle;
            _cooldownTimer = Mathf.Max(0f, StatsFor(_kind).Cadence - LaserTelegraphTime - LaserBeamTime);
        }

        private void Fire()
        {
            if (_target == null) return;
            switch (_kind)
            {
                case ShedFittingKind.Spiker:
                    BolterBolt.Fire(transform.position, _target, SpikerBoltSpeed, StatsFor(_kind).Range, SpikerHitRadius);
                    break;
                case ShedFittingKind.Missile:
                    HomingMissile.Fire(transform.position, _target, MissileSpeed, MissileDamage, MissileSplashRadius);
                    break;
            }
        }
    }
}
