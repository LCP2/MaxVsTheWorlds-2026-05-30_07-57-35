using System;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Max's own seeking projectile (MV-708) — the LPPE's discrete pulse, steered by the same
    /// <see cref="HomingSteering"/> the Launcher's <see cref="HomingMissile"/> already uses. Unlike that
    /// missile it carries no splash and never sputters/bounces on timeout: a pulse that never finds (or
    /// loses) a target just flies straight and quietly expires at its lifetime.
    ///
    /// Target acquisition happens once, at <see cref="Fire"/> time, against whatever <see cref="RobotEnemy"/>
    /// is nearest, awake and inside the lock cone — never re-evaluated mid-flight, so a locked pulse
    /// commits to the shot the player actually saw fire.
    /// </summary>
    public sealed class SeekerPulse : MonoBehaviour
    {
        private const float ContactRadius = 0.5f;

        /// <summary>Bolt length in metres (spec: "each pulse is a 0.35m cyan-white bolt").</summary>
        private const float BoltLength = 0.35f;

        private static readonly Color BoltColor = new Color(0.55f, 0.95f, 1f);

        private RobotEnemy _target;
        private IDamageable _targetDamageable;
        private float _speed;
        private float _turnRateDegPerSec;
        private float _damage;
        private float _lifetime;
        private float _age;
        private Action<RobotEnemy, float> _onHit;
        private bool _spent;

        /// <summary>The robot this pulse locked onto at fire time, or null if none qualified — the
        /// resolved value MV-708 AC1 asserts against.</summary>
        public RobotEnemy Target => _target;

        /// <summary>True once this pulse has hit its target, been blocked, or expired — it takes no
        /// further action after this, so a test can keep ticking it without double-applying damage.</summary>
        public bool IsSpent => _spent;

        /// <summary>
        /// Fire one pulse from <paramref name="origin"/> along <paramref name="aimDir"/>. Locks at fire
        /// time onto the nearest awake <see cref="RobotEnemy"/> within <paramref name="lockRange"/> and
        /// <paramref name="lockHalfAngleDeg"/> of the aim direction; flies straight and dies at its
        /// lifetime if none qualifies. <paramref name="onHit"/> (optional) lets the weapon track its own
        /// Shock combo per target without this projectile knowing anything about that mechanic.
        /// </summary>
        public static SeekerPulse Fire(Vector3 origin, Vector3 aimDir, float speed, float turnRateDegPerSec,
            float lifetime, float damage, float lockRange, float lockHalfAngleDeg,
            Action<RobotEnemy, float> onHit = null)
        {
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 1e-4f) aimDir = Vector3.forward;
            aimDir.Normalize();

            var go = new GameObject("SeekerPulse (stand-in)");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
            BuildVisual(go.transform);

            RobotEnemy target = AcquireTarget(origin, aimDir, lockRange, lockHalfAngleDeg);

            var pulse = go.AddComponent<SeekerPulse>();
            pulse.Init(target, speed, turnRateDegPerSec, lifetime, damage, onHit);
            return pulse;
        }

        /// <summary>Nearest awake, alive robot within range and the lock cone — "awake" excludes a
        /// still-<see cref="RobotEnemy.IsDormant"/> robot by rule (spec: "dormant robots are invisible
        /// targets"). Reads the field-wide registry, so any level/deck qualifies, not just this room.</summary>
        private static RobotEnemy AcquireTarget(Vector3 origin, Vector3 aimDir, float lockRange,
            float lockHalfAngleDeg)
        {
            var active = RobotEnemy.Active;
            RobotEnemy best = null;
            float bestDistSq = float.MaxValue;
            float rangeSq = lockRange * lockRange;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy candidate = active[i];
                if (candidate == null || !candidate.IsAlive || candidate.IsDormant) continue;

                Vector3 to = candidate.transform.position - origin;
                to.y = 0f;
                float distSq = to.sqrMagnitude;
                if (distSq > rangeSq || distSq < 1e-6f) continue;

                float angle = Vector3.Angle(aimDir, to.normalized);
                if (angle > lockHalfAngleDeg) continue;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = candidate;
                }
            }

            return best;
        }

        private void Init(RobotEnemy target, float speed, float turnRateDegPerSec, float lifetime,
            float damage, Action<RobotEnemy, float> onHit)
        {
            _target = target;
            _targetDamageable = target;
            _speed = speed;
            _turnRateDegPerSec = turnRateDegPerSec;
            _lifetime = lifetime;
            _damage = damage;
            _onHit = onHit;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advance one step. Public — rather than the ordinary private-Update-only shape — so
        /// an EditMode test can step the flight deterministically without a live Unity frame loop, the
        /// same reason <see cref="HomingMissile.BlockedByGeometry"/> is its own callable static query.</summary>
        public void Tick(float dt)
        {
            if (_spent) return;
            _age += dt;

            bool targetLive = _target != null && _target.IsAlive;
            if (targetLive)
            {
                transform.rotation = HomingSteering.TurnToward(transform.rotation, transform.position,
                    _target.transform.position, _turnRateDegPerSec, dt);
            }

            Vector3 from = transform.position;
            Vector3 next = from + transform.forward * (_speed * dt);

            if (HomingSteering.BlockedByGeometry(from, next, out RaycastHit hit))
            {
                transform.position = hit.point;
                Retire();
                return;
            }

            transform.position = next;

            if (targetLive)
            {
                Vector3 toTarget = _target.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude <= ContactRadius * ContactRadius)
                {
                    ApplyHit();
                    return;
                }
            }

            if (_age >= _lifetime) Retire();
        }

        private void ApplyHit()
        {
            if (_targetDamageable != null && _targetDamageable.IsAlive)
            {
                _targetDamageable.TakeDamage(new DamageInfo(_damage, transform.position, transform.forward,
                    Team.Player, source: DamageSource.PrimaryWeapon));
                _onHit?.Invoke(_target, _damage);
            }
            Retire();
        }

        private void Retire()
        {
            if (_spent) return;
            _spent = true;
            // Same Application.isPlaying guard as HomingMissile.Strip() — Destroy is illegal outside
            // Play mode, which an EditMode test driving Tick() directly hits every time.
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>A slim cyan-white bolt with a short trail (spec: "0.35m cyan-white bolt with a
        /// short trail") — same build idiom as <see cref="HomingMissile.BuildVisual"/>.</summary>
        private static void BuildVisual(Transform parent)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.1f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = 0.08f;
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, BoltColor);
            trail.Clear();

            var bolt = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bolt.name = "Bolt";
            var col = bolt.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }
            bolt.transform.SetParent(parent, false);
            bolt.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bolt.transform.localScale = new Vector3(0.08f, BoltLength * 0.5f, 0.08f);
            Material mat = MaterialLibrary.Tinted(SurfaceKind.Metal, BoltColor);
            if (mat != null) bolt.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
