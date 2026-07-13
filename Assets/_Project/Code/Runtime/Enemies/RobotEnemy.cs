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
    /// hit reaction are code-driven. Steering is direct rather than NavMesh: the arena is a
    /// hand-authored greybox with a handful of cover props, so a beeline plus
    /// <see cref="ObstacleSteering"/> (walk along what you bump into) gets around them for a
    /// fraction of a NavMesh's cost. Revisit if the levels ever get maze-like.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class RobotEnemy : MonoBehaviour, IDamageable, IKnockbackable
    {
        public enum State { Chase, Telegraph, Lunge, Recover, Dead }

        [Header("Target")]
        [Tooltip("Max. If null, located by tag 'Player' on enable.")]
        [SerializeField] private Transform target;

        [Header("Movement")]
        // Fallback only — Apply() stamps the real number from EnemyArchetype, which is where you
        // tune it. Kept in step with Rusher (60% of Max's 6 m/s) so a robot built without an
        // archetype isn't a different animal (YT-80).
        [SerializeField] private float moveSpeed = 3.6f;
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
        private Vector3 _knockback;
        [Tooltip("How fast a spray shove bleeds off (m/s²). Higher = a shorter shove (YT-64).")]
        [SerializeField] private float knockbackDecay = 28f;

        [Tooltip("How long a wall stays 'in the way' after touching it — long enough to walk clear " +
                 "of the corner rather than re-hugging it every frame (YT-68).")]
        [SerializeField] private float wallMemory = 0.2f;

        private Vector3 _wallNormal;
        private float _wallTimer;
        private float _preferSign = 1f;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _mpb = new MaterialPropertyBlock();
            _preferSign = ObstacleSteering.PreferSignFor(GetInstanceID());
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
            _wallTimer = 0f;          // a pooled robot doesn't inherit the last one's wall
            _knockback = Vector3.zero;
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
            _cc.Move(_knockback * dt);
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, knockbackDecay * dt);
        }

        private void TickChase(float dt)
        {
            if (target == null) { AcquireTarget(); return; }
            Vector3 to = target.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;

            // The lawn has cover in it (YT-68). Beelining into a prop just presses against it, so
            // while a wall is remembered, walk along it and round the corner instead.
            Vector3 dir = to.normalized;
            if (_wallTimer > 0f)
            {
                _wallTimer -= dt;
                dir = ObstacleSteering.SlideAlongWall(dir, _wallNormal, _preferSign);
            }
            FaceAndMove(dir, moveSpeed, dt);

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
