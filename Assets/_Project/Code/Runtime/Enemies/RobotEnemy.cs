using System;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Domestic-robot enemy v0 (YT-36), "pure machine" tier. State machine:
    /// Chase (direct steering toward Max) → Telegraph (wind-up tell) → Lunge
    /// (committed burst that deals contact damage) → Recover → back to Chase.
    /// Implements <see cref="IDamageable"/> (dies to the Water Blaster). Death pop +
    /// hit reaction are code-driven. Direct steering is used rather than NavMesh
    /// because the slice arena is an obstacle-free plane until the greybox (YT-38);
    /// NavMesh is the YT-38 upgrade.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class RobotEnemy : MonoBehaviour, IDamageable
    {
        public enum State { Chase, Telegraph, Lunge, Recover, Dead }

        [Header("Target")]
        [Tooltip("Max. If null, located by tag 'Player' on enable.")]
        [SerializeField] private Transform target;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3.2f;
        [SerializeField] private float gravity = 20f;

        [Header("Lunge")]
        [SerializeField] private float lungeRange = 2.2f;     // start telegraph within this
        [SerializeField] private float telegraphTime = 0.55f; // wind-up (dodge window)
        [SerializeField] private float lungeSpeed = 11f;
        [SerializeField] private float lungeTime = 0.22f;
        [SerializeField] private float recoverTime = 0.7f;
        [SerializeField] private float contactDamage = 12f;
        [SerializeField] private float contactRadius = 1.0f;

        [Header("Health")]
        [SerializeField] private float maxHealth = 24f;

        [Header("Tells (gold-ring / lens)")]
        [SerializeField] private Renderer tellRenderer; // optional; the gold-ring/eye
        [SerializeField] private Color idleTell = new Color(0.85f, 0.7f, 0.2f);
        [SerializeField] private Color windupTell = new Color(1f, 0.2f, 0.1f);

        public State Current { get; private set; } = State.Chase;
        public bool IsAlive => Current != State.Dead && _health > 0f;
        public Team Team => Team.Enemy;

        /// <summary>Fired on death (spawner decrements its live count). Arg = this enemy.</summary>
        public event Action<RobotEnemy> Died;

        private CharacterController _cc;
        private IDamageable _targetDamageable;
        private float _health;
        private float _stateTimer;
        private float _verticalVel;
        private Vector3 _lungeDir;
        private bool _dealtThisLunge;
        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _mpb = new MaterialPropertyBlock();
            ResetState();
        }

        private void OnEnable() => ResetState(); // reset for pooling reuse

        /// <summary>Reset to a fresh, alive Chase state. Called from Awake/OnEnable and
        /// directly by tests (which don't get Unity lifecycle callbacks).</summary>
        public void ResetState()
        {
            _health = maxHealth;
            Current = State.Chase;
            _stateTimer = 0f;
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
            _targetDamageable = target != null ? target.GetComponent<IDamageable>() : null;
        }

        private void Update()
        {
            if (Current == State.Dead) return;
            float dt = Time.deltaTime;
            _stateTimer += dt;

            switch (Current)
            {
                case State.Chase:    TickChase(dt);    break;
                case State.Telegraph: TickTelegraph(dt); break;
                case State.Lunge:    TickLunge(dt);    break;
                case State.Recover:  TickRecover(dt);  break;
            }

            ApplyGravity(dt);
        }

        private void TickChase(float dt)
        {
            if (target == null) { AcquireTarget(); return; }
            Vector3 to = target.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            FaceAndMove(to.normalized, moveSpeed, dt);

            if (dist <= lungeRange)
            {
                Current = State.Telegraph;
                _stateTimer = 0f;
                SetTell(windupTell);   // visual tell: dodge window opens
            }
        }

        private void TickTelegraph(float dt)
        {
            // Wind-up: hold, face the target, do not move — this is the dodge window.
            if (target != null)
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

        private void TickLunge(float dt)
        {
            _cc.Move(_lungeDir * lungeSpeed * dt);
            if (!_dealtThisLunge) TryContactDamage();
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
                _cc.Move(dir * speed * dt);
            }
        }

        private void ApplyGravity(float dt)
        {
            if (_cc.isGrounded && _verticalVel < 0f) _verticalVel = -2f;
            _verticalVel -= gravity * dt;
            _cc.Move(Vector3.up * _verticalVel * dt);
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
