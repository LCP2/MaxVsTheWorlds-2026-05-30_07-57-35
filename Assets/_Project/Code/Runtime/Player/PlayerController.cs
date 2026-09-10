using UnityEngine;
using UnityEngine.InputSystem;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Player
{
    /// <summary>
    /// Twin-stick locomotion for Max (YT-34). Greybox capsule stand-in —
    /// no art dependency. Left stick / WASD moves; right stick / arrow keys aims.
    /// Input is defined in code (Input System), so it works in-editor
    /// with keyboard or a gamepad; on-screen touch controls are added in the
    /// device pass.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Move")]
        [SerializeField] private float moveSpeed = 2.42f;   // MV-658: baked from Lee's 2026-09-02 tuning pass (was 3.01, YT-106)

        /// <summary>The authored walk speed, ignoring any dev override. The tuning panel shows the
        /// live value as a percentage of this (YT-105).</summary>
        public float AuthoredMoveSpeed => moveSpeed;

        /// <summary>Max's effective walk speed right now — the authored/dev-tuned base scaled by the
        /// Acceleration engine if it's installed (YT-133/141) and by the Speed ability's level (WV-231,
        /// spec §6). The single number <see cref="Update"/> moves him at; exposed so the effect can be
        /// measured without driving input.</summary>
        public float WalkSpeed => DevTuning.Or(DevTuning.PlayerMoveSpeed, moveSpeed)
            * UpgradeState.MoveSpeedMultiplier * SpeedAbilityMultiplier * SludgeMultiplier;

        private static float SpeedAbilityMultiplier => AbilityTuning.SpeedMultiplier(
            WeaponSystemState.AbilityLevel(AbilityKind.Speed),
            DevTuning.Or(DevTuning.SpeedMultiplierPerLevel, AbilityTuning.DefaultSpeedMultiplierPerLevel));

        /// <summary>MV-692: 0.6 while Max's feet are inside a sludge rect, 1 otherwise.</summary>
        private float SludgeMultiplier => MapSlowZones.Instance.SpeedMultiplierAt(transform.position);
        [SerializeField] private float rotationSpeed = 720f; // deg/s
        [SerializeField] private float gravity = 20f;

        [Header("Aim")]
        [Tooltip("Aim-stick magnitude required to count as 'aiming' (gates gadget fire). " +
                 "High enough that resting-stick drift never trips it.")]
        [Range(0.2f, 0.9f)]
        [SerializeField] private float aimActivateThreshold = 0.5f;

        private CharacterController _cc;
        private InputAction _move;
        private InputAction _aim;

        private Vector3 _facing = Vector3.forward;
        private float _verticalVel;

        // MV-503: "Max rotates but never translates on a fresh run" diagnostic. ELIMINATED input, walk
        // speed, an exception and spawn-in-cover as causes, which leaves the CharacterController itself
        // either disabled or geometrically pinned — this instrument pins which, from a live build,
        // without changing any behaviour (that is the next ticket, once this one's log says what to fix).
        private const float StuckDisplacementEpsilon = 0.001f;   // 1 mm
        private const float StuckLogIntervalSeconds = 1f;
        private float _stuckLogCooldown;

        /// <summary>True while the aim stick/keys are engaged — the gadget (YT-35) auto-fires while this holds.</summary>
        public bool IsAiming { get; private set; }

        /// <summary>Current planar facing (unit vector). The gadget fires along this.</summary>
        public Vector3 Facing => _facing;

        /// <summary>Latest movement input (left stick / WASD), clamped to the unit disc.
        /// The HUD (YT-30) reads this to light the movement joystick + direction arrow.</summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary>MV-752: authored World 2 ramps climb as steep as ~50 degrees (`world2_config.json`
        /// AC), which the scene's `slopeLimit 45` (never touched by code before this) refused to climb
        /// at all. Set here, not in the scene, so the value holds in every capture/test scene as well as
        /// the play scene. `stepOffset` is untouched.</summary>
        private const float SlopeLimitDegrees = 55f;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.slopeLimit = SlopeLimitDegrees;

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

            // Water Balloon + Teleport's live component self-attaches, same code-driven-scenes rule
            // WaterBlaster's own sub-components follow (WV-231) — no scene wiring.
            if (GetComponent<PlayerAbilities>() == null) gameObject.AddComponent<PlayerAbilities>();

            // MV-694: the Shoulder Rack self-attaches the same way — it no-ops entirely while
            // WeaponSystemState.SecondaryKind still reads WaterBalloon, so attaching it unconditionally
            // costs nothing before World 2's morph flips the kind.
            if (GetComponent<ShoulderRack>() == null) gameObject.AddComponent<ShoulderRack>();

            // MV-739: the LPPE self-attaches the same way — World 2's Weapon Core morph flips
            // WeaponSystemState.ActivePrimary to Lppe, but nothing ever actually attached a live LPPE
            // component to fire; WaterBlaster (baked into the scene, never torn down) kept firing the
            // RCDA stream regardless. Attaching this unconditionally costs nothing before the morph —
            // it self-gates on ActivePrimary the same way ShoulderRack gates on SecondaryKind.
            if (GetComponent<PulseLaser>() == null) gameObject.AddComponent<PulseLaser>();
        }

        private void OnEnable()
        {
            _move.Enable();
            _aim.Enable();
        }

        private void OnDisable()
        {
            _move.Disable();
            _aim.Disable();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

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

            // Dev tuning panel may be overriding the walk speed this session (YT-105); off by
            // default and in release. Then the Acceleration engine (YT-133) scales it — read at the
            // point of use so installing the part speeds up the Max you're already controlling, not
            // just the next one.
            Vector3 planarVel = moveDir * WalkSpeed;
            (Vector3 horizontalDisplacement, Vector3 verticalDisplacement) = ComputeSplitMotion(planarVel, dt);

            Vector3 posBeforeMove = transform.position;
            // MV-752: horizontal and vertical motion as two SEPARATE Move calls, matching RobotEnemy's
            // proven FaceAndMove/ApplyGravity split -- summing them into one call let CharacterController
            // project the combined vector onto a ramp's slope, cancelling most of the forward component
            // against the downward one and grinding Max's climb to a tenth speed.
            // MV-386: SafeMove, not cc.Move directly -- a stall-inflated dt can otherwise tunnel
            // Max straight through a gate/fence in one oversized Move() call.
            CharacterControllerMotion.SafeMove(_cc, horizontalDisplacement);
            CharacterControllerMotion.SafeMove(_cc, verticalDisplacement);
            Vector3 actualDelta = transform.position - posBeforeMove;
            Vector3 displacement = horizontalDisplacement + verticalDisplacement;

            EvaluateStuckDiagnostic(moveDir, displacement, actualDelta, dt);

            if (_facing.sqrMagnitude > 0.001f)
            {
                Quaternion target = Quaternion.LookRotation(_facing, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * dt);
            }
        }

        /// <summary>MV-752: this frame's motion as two separate vectors — horizontal (the walk, Y forced
        /// to exactly 0) and vertical (gravity only, X/Z forced to exactly 0) — so <see cref="Update"/>
        /// issues them as two distinct <see cref="CharacterControllerMotion.SafeMove"/> calls instead of
        /// summing them into one and letting the slope projection cancel most of the climb. Split out so
        /// a test can assert the split directly without a live Input System driving <see cref="Update"/>
        /// (same idiom as <see cref="EvaluateStuckDiagnostic"/> below). Also updates <see cref="_verticalVel"/>
        /// exactly as before — the grounded reset, then gravity accumulated over <paramref name="dt"/>.</summary>
        private (Vector3 horizontal, Vector3 vertical) ComputeSplitMotion(Vector3 planarVel, float dt)
        {
            // Keep grounded on the flat arena.
            if (_cc.isGrounded && _verticalVel < 0f)
            {
                _verticalVel = -2f;
            }
            _verticalVel -= gravity * dt;

            Vector3 horizontal = new Vector3(planarVel.x, 0f, planarVel.z) * dt;
            Vector3 vertical = new Vector3(0f, _verticalVel, 0f) * dt;
            return (horizontal, vertical);
        }

        /// <summary>MV-503: fires at most once a second, and only while a move input is held but the
        /// actual displacement came back under 1 mm — silent in the normal case, on the record the
        /// instant the symptom reproduces. <paramref name="dt"/> and the pre-computed vectors are taken
        /// as parameters (rather than read again from <see cref="Time"/>/<c>_cc</c> here) so a test can
        /// drive this directly without a live Input System action or a running player loop.</summary>
        private void EvaluateStuckDiagnostic(Vector3 moveDir, Vector3 displacement, Vector3 actualDelta, float dt)
        {
            _stuckLogCooldown -= dt;

            bool stuck = moveDir.sqrMagnitude > 0f &&
                         actualDelta.sqrMagnitude < StuckDisplacementEpsilon * StuckDisplacementEpsilon;
            if (!stuck) { _stuckLogCooldown = 0f; return; }
            if (_stuckLogCooldown > 0f) return;

            _stuckLogCooldown = StuckLogIntervalSeconds;
            Debug.Log(DiagnosticState("stuck", moveDir, dt, displacement, actualDelta));
        }

        /// <summary>MV-503: logged unconditionally — bug reproducing or not — at the exact instant
        /// <c>HomeScreen.Close()</c> hands control back, next to the existing <c>[Boot] controllable</c>
        /// line, so the CharacterController's state at handoff is always on the record.</summary>
        public void LogHandoffDiagnostic() =>
            Debug.Log(DiagnosticState("handoff", new Vector3(MoveInput.x, 0f, MoveInput.y), Time.deltaTime));

        private string DiagnosticState(string label, Vector3 moveDir, float dt,
            Vector3? displacement = null, Vector3? actualDelta = null) =>
            $"[MV-503] {label}: cc.enabled={_cc.enabled} isGrounded={_cc.isGrounded} radius={_cc.radius} " +
            $"height={_cc.height} center={_cc.center} pos={transform.position} moveDir={moveDir} " +
            $"walkSpeed={WalkSpeed} dt={dt:0.####} " +
            // MV-504: state this directly rather than by inference from dt — dt==0 is consistent with
            // both Time.timeScale==0 (the strongest MV-504 candidate) and a genuine stall, and only
            // timeScale distinguishes them.
            $"timeScale={Time.timeScale:0.####} unscaledDt={Time.unscaledDeltaTime:0.####} " +
            $"displacement={displacement ?? Vector3.zero:F4} actualDelta={actualDelta ?? Vector3.zero:F4}";
    }
}
