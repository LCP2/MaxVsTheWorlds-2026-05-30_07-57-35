using UnityEngine;
using UnityEngine.InputSystem;

namespace MaxWorlds.Player
{
    /// <summary>
    /// Twin-stick locomotion + dash for Max (YT-34). Greybox capsule stand-in —
    /// no art dependency. Left stick / WASD moves; right stick / arrow keys aims;
    /// the dash button bursts in the move direction with brief i-frames and a
    /// cooldown. Input is defined in code (Input System), so it works in-editor
    /// with keyboard or a gamepad; on-screen touch controls are added in the
    /// device pass.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Move")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float rotationSpeed = 720f; // deg/s
        [SerializeField] private float gravity = 20f;

        [Header("Aim")]
        [Tooltip("Aim-stick magnitude required to count as 'aiming' (gates gadget fire). " +
                 "High enough that resting-stick drift never trips it.")]
        [Range(0.2f, 0.9f)]
        [SerializeField] private float aimActivateThreshold = 0.5f;

        [Header("Dash")]
        [SerializeField] private float dashSpeed = 18f;
        [SerializeField] private float dashDuration = 0.18f;
        [SerializeField] private float dashInvulnerable = 0.18f;
        [SerializeField] private float dashCooldown = 0.6f;

        private CharacterController _cc;
        private InputAction _move;
        private InputAction _aim;
        private InputAction _dash;

        private Vector3 _facing = Vector3.forward;
        private float _verticalVel;
        private float _dashTimer;       // > 0 while dashing
        private float _iframeTimer;     // > 0 while invulnerable
        private float _cooldownTimer;   // > 0 while dash is on cooldown
        private Vector3 _dashDir;

        /// <summary>True during the dash burst.</summary>
        public bool IsDashing => _dashTimer > 0f;

        /// <summary>True during the dash i-frame window — combat (YT-35/36) reads this to ignore contact hits.</summary>
        public bool IsInvulnerable => _iframeTimer > 0f;

        /// <summary>True while the aim stick/keys are engaged — the gadget (YT-35) auto-fires while this holds.</summary>
        public bool IsAiming { get; private set; }

        /// <summary>Current planar facing (unit vector). The gadget fires along this.</summary>
        public Vector3 Facing => _facing;

        /// <summary>Latest movement input (left stick / WASD), clamped to the unit disc.
        /// The HUD (YT-30) reads this to light the movement joystick + direction arrow.</summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary>Dash cooldown as a 0..1 wipe (1 = just dashed, 0 = ready) for the HUD dash slot.</summary>
        public float DashCooldownNormalized
        {
            get
            {
                float total = dashCooldown + dashDuration;
                return total > 0f ? Mathf.Clamp01(_cooldownTimer / total) : 0f;
            }
        }

        /// <summary>True when the dash is off cooldown (HUD dash slot "ready" glow).</summary>
        public bool DashReady => _cooldownTimer <= 0f;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();

            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            // stickDeadzone rejects resting-stick drift so an untouched gamepad reads (0,0).
            _move.AddBinding("<Gamepad>/leftStick", processors: "stickDeadzone(min=0.2)");

            _aim = new InputAction("Aim", InputActionType.Value);
            _aim.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            // Without a deadzone, right-stick drift reads non-zero with no input pressed,
            // which made the Water Blaster (driven by IsAiming) auto-discharge. (YT-36 regression fix.)
            _aim.AddBinding("<Gamepad>/rightStick", processors: "stickDeadzone(min=0.2)");

            _dash = new InputAction("Dash", InputActionType.Button);
            _dash.AddBinding("<Keyboard>/space");
            _dash.AddBinding("<Gamepad>/buttonSouth");
        }

        private void OnEnable()
        {
            _move.Enable();
            _aim.Enable();
            _dash.Enable();
        }

        private void OnDisable()
        {
            _move.Disable();
            _aim.Disable();
            _dash.Disable();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _dashTimer = Mathf.Max(0f, _dashTimer - dt);
            _iframeTimer = Mathf.Max(0f, _iframeTimer - dt);
            _cooldownTimer = Mathf.Max(0f, _cooldownTimer - dt);

            Vector2 moveInput = _move.ReadValue<Vector2>();
            Vector2 aimInput = _aim.ReadValue<Vector2>();

            Vector3 moveDir = new Vector3(moveInput.x, 0f, moveInput.y);
            if (moveDir.sqrMagnitude > 1f)
            {
                moveDir.Normalize();
            }
            MoveInput = new Vector2(moveDir.x, moveDir.z);

            // Facing: aim takes priority, falling back to movement direction.
            // Require a deliberate push (magnitude > aimActivate) so resting-stick
            // drift never counts as aiming — this is what gates the gadget's fire.
            Vector3 aimDir = new Vector3(aimInput.x, 0f, aimInput.y);
            IsAiming = aimDir.sqrMagnitude > aimActivateThreshold * aimActivateThreshold;
            if (IsAiming)
            {
                _facing = aimDir.normalized;
            }
            else if (moveDir.sqrMagnitude > 0.04f)
            {
                _facing = moveDir.normalized;
            }

            // Dash trigger (ignored mid-dash or on cooldown).
            if (_dash.WasPressedThisFrame() && _dashTimer <= 0f && _cooldownTimer <= 0f)
            {
                _dashDir = moveDir.sqrMagnitude > 0.04f ? moveDir.normalized : _facing;
                _dashTimer = dashDuration;
                _iframeTimer = dashInvulnerable;
                _cooldownTimer = dashCooldown + dashDuration;
            }

            Vector3 planarVel = _dashTimer > 0f ? _dashDir * dashSpeed : moveDir * moveSpeed;

            // Keep grounded on the flat arena.
            if (_cc.isGrounded && _verticalVel < 0f)
            {
                _verticalVel = -2f;
            }
            _verticalVel -= gravity * dt;

            Vector3 velocity = planarVel + Vector3.up * _verticalVel;
            _cc.Move(velocity * dt);

            if (_facing.sqrMagnitude > 0.001f)
            {
                Quaternion target = Quaternion.LookRotation(_facing, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * dt);
            }
        }
    }
}
