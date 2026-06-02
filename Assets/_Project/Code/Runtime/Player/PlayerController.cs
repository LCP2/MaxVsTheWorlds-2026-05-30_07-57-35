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

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();

            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddBinding("<Gamepad>/leftStick");

            _aim = new InputAction("Aim", InputActionType.Value);
            _aim.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _aim.AddBinding("<Gamepad>/rightStick");

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

            // Facing: aim takes priority, falling back to movement direction.
            Vector3 aimDir = new Vector3(aimInput.x, 0f, aimInput.y);
            IsAiming = aimDir.sqrMagnitude > 0.04f;
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
