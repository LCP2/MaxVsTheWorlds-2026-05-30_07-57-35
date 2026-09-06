using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-678 — Lee, on the build: "max walking is not working well... I think there are situations
    /// where he's moving backwards and is walking forwards too and this looks wrong."
    ///
    /// Root cause, confirmed in <c>MaxRig.TickRun</c>: the stride phase used to advance off
    /// <c>MoveInput.magnitude</c>, which is unsigned, so it always ran the same direction no matter
    /// which way Max was actually travelling. The fix signs it with <c>moveLocal.z</c> (already
    /// computed for the lean) through a deadband, so a pure strafe — where that value hovers near
    /// zero — doesn't flicker the direction every frame.
    ///
    /// EditMode, same reflection idiom <see cref="MaxRigTests"/>/<see cref="MV474MaxWalkTests"/> and
    /// <see cref="OnScreenStickDeflectionTests"/> already use for this rig and for
    /// <c>PlayerController</c>: Awake is not called automatically outside Play Mode, so both
    /// components' Awake methods are invoked directly, and <c>TickRun</c> — private, and driven every
    /// frame from <c>LateUpdate</c> — is invoked the same way. Reads the exposed <see
    /// cref="MaxRig.Stride"/> property throughout, never an internal field.
    ///
    /// Must fail on the pre-fix commit: that <c>TickRun</c> only ever adds a positive amount to
    /// <c>_stride</c>, so the backward phase below (which asserts a DECREASE) instead sees an
    /// increase.
    /// </summary>
    public sealed class MV678StrideDirectionTests
    {
        private GameObject _playerGo;
        private GameObject _rigGo;
        private PlayerController _player;
        private MaxRig _rig;

        private const float Dt = 0.02f;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player-MV678-Test", typeof(CharacterController));
            _player = _playerGo.AddComponent<PlayerController>();
            Invoke(_player, "Awake");

            _rigGo = new GameObject("MaxRig-MV678-Test");
            _rig = _rigGo.AddComponent<MaxRig>();
            Invoke(_rig, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        private static void Invoke(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private void SetMoveInput(float x, float y) =>
            typeof(PlayerController).GetProperty("MoveInput")
                .GetSetMethod(nonPublic: true)
                .Invoke(_player, new object[] { new Vector2(x, y) });

        /// <summary>One frame: set the stick, tick the rig's run cycle, hand back how much
        /// <see cref="MaxRig.Stride"/> moved.</summary>
        private float Step(float x, float y)
        {
            SetMoveInput(x, y);
            float before = _rig.Stride;
            Invoke(_rig, "TickRun", Dt);
            return _rig.Stride - before;
        }

        private static void Invoke(object target, string methodName, float dt) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, new object[] { dt });

        /// <summary>
        /// AC1 + AC2 + AC3, driven as one continuous walk so each phase's starting condition is the
        /// previous phase's real, settled outcome rather than a fresh instance:
        ///
        /// standing still -> forwards -> a noisy strafe (deadband must hold +1) -> backwards (the
        /// direction must actually flip) -> a noisy strafe again (deadband must hold -1) -> forwards
        /// again -> standing still.
        /// </summary>
        [Test]
        public void StrideSignsWithTravelDirection_AndHoldsThroughADeadbandDuringAStrafe()
        {
            // AC3: no input, no stride movement.
            Assert.That(Step(0f, 0f), Is.EqualTo(0f), "the stride moved with zero MoveInput.");
            Assert.That(Step(0f, 0f), Is.EqualTo(0f), "the stride moved with zero MoveInput.");

            // AC1 (forwards): moveLocal.z is clearly positive here (identity rotation, so world Z is
            // local Z) — the stride phase must increase.
            for (int i = 0; i < 2; i++)
            {
                Assert.That(Step(0f, 1f), Is.GreaterThan(0f),
                    "forward MoveInput must increase the stride phase.");
            }

            // AC2 (the deadband guard): a pure strafe, with small noise straddling zero on the axis
            // that drives direction. |y| stays under the 0.15 deadband the fix uses, so a correct
            // implementation holds the +1 direction it just established above; a naive raw-sign flip
            // would go negative the moment y dips below zero (e.g. -0.05).
            float[,] strafeNoise = { { 0.9f, 0.05f }, { -0.9f, -0.05f }, { 0.95f, 0.10f }, { -0.95f, -0.12f } };
            for (int i = 0; i < strafeNoise.GetLength(0); i++)
            {
                Assert.That(Step(strafeNoise[i, 0], strafeNoise[i, 1]), Is.GreaterThan(0f),
                    "the stride direction flickered during a pure strafe instead of holding the " +
                    "deadband's last direction (forwards).");
            }

            // AC1 (backwards): moveLocal.z is clearly negative now — the stride phase must actually
            // decrease. This is the assertion the pre-fix code cannot pass: MoveInput.magnitude is
            // unsigned, so the old TickRun kept adding a positive amount here too.
            for (int i = 0; i < 3; i++)
            {
                Assert.That(Step(0f, -1f), Is.LessThan(0f),
                    "backward MoveInput must decrease the stride phase — the pre-MV-678 stride only " +
                    "ever advanced forwards regardless of travel direction.");
            }

            // AC2 again, now holding the flipped (-1) direction through the same noisy strafe.
            for (int i = 0; i < strafeNoise.GetLength(0); i++)
            {
                Assert.That(Step(strafeNoise[i, 0], strafeNoise[i, 1]), Is.LessThan(0f),
                    "the stride direction flickered during a pure strafe instead of holding the " +
                    "deadband's last direction (backwards).");
            }

            // Direction flips cleanly back to forwards once travel is clearly forwards again.
            for (int i = 0; i < 2; i++)
            {
                Assert.That(Step(0f, 1f), Is.GreaterThan(0f),
                    "forward MoveInput must increase the stride phase after a backward stretch.");
            }

            // AC3 again: coming to a stop must not keep advancing the stride.
            Assert.That(Step(0f, 0f), Is.EqualTo(0f), "the stride moved with zero MoveInput.");
        }
    }
}
