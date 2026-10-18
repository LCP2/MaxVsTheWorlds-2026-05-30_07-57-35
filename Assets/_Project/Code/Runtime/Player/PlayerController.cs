using UnityEngine;
using UnityEngine.InputSystem;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Player
{
    /// <summary>MV-1222: who's pushing/pulling Max right now — the Breaker's knockback and the
    /// Tractor's pull are the two cases <see cref="PlayerController.ApplyForcedMove"/> exists for.</summary>
    public enum ForcedMoveSource { Knockback, Pull }

    /// <summary>
    /// Twin-stick locomotion for Max (YT-34). Greybox capsule stand-in —
    /// no art dependency. Left stick / WASD moves; right stick / arrow keys aims.
    /// Input is defined in code (Input System), so it works in-editor
    /// with keyboard or a gamepad; on-screen touch controls are added in the
    /// device pass.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [MaxWorlds.Core.PerfSection("player")]
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

        /// <summary>MV-946: the fall/out-of-bounds safety net — ticked every <see cref="Update"/>,
        /// seeded with Max's spawn position so it always has somewhere real to recover to.</summary>
        private FallRecoveryState _fallRecovery;

        // MV-503: "Max rotates but never translates on a fresh run" diagnostic. ELIMINATED input, walk
        // speed, an exception and spawn-in-cover as causes, which leaves the CharacterController itself
        // either disabled or geometrically pinned — this instrument pins which, from a live build,
        // without changing any behaviour (that is the next ticket, once this one's log says what to fix).
        private const float StuckDisplacementEpsilon = 0.001f;   // 1 mm
        private const float StuckLogIntervalSeconds = 1f;
        private float _stuckLogCooldown;

        /// <summary>MV-1222: one shared push/pull rule — the Breaker's knockback and the Tractor's
        /// pull both drive Max through <see cref="ApplyForcedMove"/> instead of each inventing its own
        /// way to move him without his input. <see cref="_forcedMoveRemaining"/> counting down to 0
        /// is "no forced move active"; a fresh <see cref="ApplyForcedMove"/> call simply overwrites
        /// these, which is what makes a newer one replace an older one rather than stack with it.</summary>
        private Vector3 _forcedMoveVelocity;
        private float _forcedMoveRemaining;
        private ForcedMoveSource _forcedMoveSource;

        /// <summary>Consecutive <see cref="TickForcedMove"/> ticks in a row the active forced move has
        /// made ~no progress (a wall/gate holding it) — <see cref="ForcedMoveBlockedTicksLimit"/> of
        /// these ends it early rather than spending the rest of its duration pushing at nothing.</summary>
        private int _forcedMoveBlockedTicks;

        private const float ForcedMoveBlockedEpsilon = 0.001f;   // 1 mm, same idiom as StuckDisplacementEpsilon
        private const int ForcedMoveBlockedTicksLimit = 3;

        /// <summary>Lazily resolved once <see cref="PlayerHealth"/> exists on this object (it self-adds
        /// after <see cref="PlayerController"/>, so Awake order can't be relied on) so a death clears
        /// whatever forced move was mid-flight — see <see cref="ClearForcedMove"/>.</summary>
        private bool _forcedMoveDiedSubscribed;

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
            _fallRecovery = new FallRecoveryState(transform.position);

            // MV-895: Unity's CharacterController.minMoveDistance defaults to 0.001 m and silently
            // discards any Move() below it. A residual, non-normalised analogue moveDir at a lane
            // corner -- shrunk further by MV-883's low-timeScale clamp on dt -- resolves to a commanded
            // displacement under that threshold, which reads in-game as an invisible, immovable wall.
            _cc.minMoveDistance = 0f;

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

            // MV-1012: Undertow self-attaches the same way — World 3's Weapon Core morph flips
            // WeaponSystemState.ActivePrimary to Undertow, but nothing ever attached a live Undertow
            // component to fire; WaterBlaster kept firing the RCDA stream regardless. Attaching this
            // unconditionally costs nothing before the morph — it self-gates on ActivePrimary the same
            // way PulseLaser gates on ActivePrimary == Lppe.
            if (GetComponent<Undertow>() == null) gameObject.AddComponent<Undertow>();

            // MV-1195: Ricochet self-attaches the same way — the City's Weapon Core morph flips
            // WeaponSystemState.ActivePrimary to Ricochet, but nothing else ever attaches a live
            // Ricochet component to fire. Attaching this unconditionally costs nothing before the
            // morph — it self-gates on ActivePrimary the same way Undertow/PulseLaser do.
            if (GetComponent<Ricochet>() == null) gameObject.AddComponent<Ricochet>();

            // MV-1210: LIGHT MISSILE self-attaches the same way — World 5's Weapon Core morph (a later
            // ticket, MV-1225's WorldCatalog row) will flip WeaponSystemState.ActivePrimary to
            // LightMissile, but nothing else ever attaches a live LightMissile component to fire.
            // Attaching this unconditionally costs nothing before that morph — it self-gates on
            // ActivePrimary the same way Ricochet/Undertow/PulseLaser do.
            if (GetComponent<LightMissile>() == null) gameObject.AddComponent<LightMissile>();

            // MV-1236: Orbitals self-attaches the same way — World 5's Weapon Core morph flips
            // WeaponSystemState.SecondaryKind to Orbitals, but nothing else ever attaches a live
            // Orbitals component to orbit/hit. Attaching this unconditionally costs nothing before the
            // morph — it self-gates on SecondaryKind the same way ShoulderRack does.
            if (GetComponent<Orbitals>() == null) gameObject.AddComponent<Orbitals>();
        }

        private void OnEnable()
        {
            _move.Enable();
            _aim.Enable();
            // MV-1222: a freeze/cutscene/world-join resume (this is exactly what re-enabling
            // PlayerController means — see ApplyForcedMove's own doc comment) must never hand a stale
            // shove back to whatever this run becomes next.
            ClearForcedMove();
        }

        private void OnDisable()
        {
            _move.Disable();
            _aim.Disable();
            // MV-1222: a freeze, a cutscene and a world-join all suspend Max by disabling this
            // component (WorldFinaleGate.SuspendGameplayForBeat, WorldJoinSequence.SuspendGameplay,
            // IntroCinematic) — clearing here is what makes all three "clear the forced move" for free.
            ClearForcedMove();
        }

        /// <summary>MV-1222: one shared push/pull rule for every feature that moves Max without his
        /// input — built for the Breaker's knockback (MV-1228) and the Tractor's pull (MV-1238), its
        /// first two production callers. While active, <paramref name="velocity"/> displaces Max every tick through the
        /// same <see cref="CharacterControllerMotion.SafeMove"/> path his walk uses (<see cref="TickForcedMove"/>),
        /// so a wall, solid cover or a gate frame stops it exactly as it stops ordinary walking — never
        /// a teleport-style position set. Only one forced move is ever active: calling this again
        /// simply overwrites the fields below, so a newer call replaces an older one rather than
        /// stacking with it.</summary>
        public void ApplyForcedMove(Vector3 velocity, float duration, ForcedMoveSource source)
        {
            _forcedMoveVelocity = velocity;
            _forcedMoveRemaining = Mathf.Max(0f, duration);
            _forcedMoveSource = source;
            _forcedMoveBlockedTicks = 0;
        }

        /// <summary>True while a forced move (knockback/pull) is still displacing Max this frame.</summary>
        public bool IsForcedMoveActive => _forcedMoveRemaining > 0f;

        /// <summary>Ends whatever forced move is active with no further displacement. Called from
        /// <see cref="OnEnable"/>/<see cref="OnDisable"/> (freeze/cutscene/world-join/resume), from
        /// <see cref="MaxWorlds.Weapons.PlayerAbilities.TryTeleport"/> (a teleport) and from
        /// <see cref="PlayerHealth.Died"/> (death) — every case MV-1222's AC says must never carry a
        /// shove across it.</summary>
        public void ClearForcedMove()
        {
            _forcedMoveVelocity = Vector3.zero;
            _forcedMoveRemaining = 0f;
            _forcedMoveBlockedTicks = 0;
        }

        /// <summary>This frame's slice of the active forced move, if any — its own
        /// <see cref="CharacterControllerMotion.SafeMove"/> call, same idiom as
        /// <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s own knockback (applied separately from its
        /// state-driven movement, not folded into one combined vector). Ends early once
        /// <see cref="ForcedMoveBlockedTicksLimit"/> consecutive ticks have made ~no progress — a wall
        /// or gate holding it — rather than spending the rest of <paramref name="dt"/>'s duration
        /// pushing at something that isn't moving.</summary>
        private void TickForcedMove(float dt)
        {
            if (_forcedMoveRemaining <= 0f) return;

            float step = Mathf.Min(dt, _forcedMoveRemaining);
            Vector3 before = transform.position;
            CharacterControllerMotion.SafeMove(_cc, _forcedMoveVelocity * step);
            Vector3 actualDelta = transform.position - before;

            _forcedMoveRemaining -= dt;
            _forcedMoveBlockedTicks = actualDelta.sqrMagnitude < ForcedMoveBlockedEpsilon * ForcedMoveBlockedEpsilon
                ? _forcedMoveBlockedTicks + 1
                : 0;

            if (_forcedMoveRemaining <= 0f || _forcedMoveBlockedTicks >= ForcedMoveBlockedTicksLimit)
            {
                ClearForcedMove();
            }
        }

        /// <summary>MV-1222: <see cref="PlayerHealth"/> self-adds after <see cref="PlayerController"/>
        /// (see every scaffold/test site that builds Max), so it may not exist yet the first time this
        /// runs — resolved lazily, same "cache once found" idiom <see cref="PlayerHealth.Abilities"/>
        /// itself uses for its own lazy lookups. A no-op forever on an object with no PlayerHealth
        /// (never true for the real Max, but true for plenty of movement-only test rigs).</summary>
        private void EnsureForcedMoveClearsOnDeath()
        {
            if (_forcedMoveDiedSubscribed) return;
            PlayerHealth health = GetComponent<PlayerHealth>();
            if (health == null) return;
            health.Died += ClearForcedMove;
            _forcedMoveDiedSubscribed = true;
        }

        private void Update()
        {
            EnsureForcedMoveClearsOnDeath();
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

            // MV-1222: its own SafeMove call, after the diagnostic above so a knockback/pull never
            // pollutes the MV-503 stuck read, which is about INPUT-driven movement specifically.
            TickForcedMove(dt);

            if (_facing.sqrMagnitude > 0.001f)
            {
                Quaternion target = Quaternion.LookRotation(_facing, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * dt);
            }

            // MV-946: below the floor or outside the world bounds for more than the grace window ->
            // back to solid ground. No damage, no death -- this is recovery, not a hazard.
            Vector3? recoverTo = _fallRecovery.Tick(transform.position, EnemyNavigation.Map, _cc.isGrounded, dt);
            if (recoverTo.HasValue)
            {
                // MV-955: evidence for a live fall that could never be reproduced in EditMode.
                FallEventLog.Record("max", EnemyNavigation.Map, _fallRecovery.FirstOutOfPlayPosition, recoverTo.Value, dt);
                Recover(recoverTo.Value);
            }
        }

        /// <summary>MV-946: teleports Max back to solid ground once <see cref="_fallRecovery"/> trips —
        /// same disable/set/enable shape every other direct position write in this project uses so the
        /// CharacterController's cached internal state doesn't fight the jump. Clears the accumulated
        /// fall speed too, so the very next frame doesn't read as still-falling and immediately start
        /// re-accumulating gravity from a large negative <see cref="_verticalVel"/>.</summary>
        private void Recover(Vector3 position)
        {
            // MV-1021: routed through the guard — Max is the one case the ticket calls out by name
            // ("Max is restored to his FallRecoveryState last safe position"), which is exactly what a
            // refused SafeReposition already does (leaves him at his current transform, controller
            // re-enabled) since that current position is itself the last spot recovery landed him at.
            CharacterControllerSafety.SafeReposition(_cc, position, "PlayerController.Recover");
            _verticalVel = 0f;
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
