using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-730 — three faults Lee found on the build after MV-717 shipped:
    ///
    ///   1. The upper arms were welded to the torso: <c>PoseArms</c> only ever moved the swing target
    ///      (the hand end) across a stride, so the shoulder end of <c>PoseArm</c>'s sleeve sat at a
    ///      completely static point and read as glued to the body.
    ///   2. The presented gadget rendered at/near waist height with the wrong hand appearing to drive
    ///      it: the built rig's actual grip points (<c>MaxBody.HandL</c>/<c>HandR</c>) and the muzzle
    ///      glow tip resolve well below where <see cref="MaxRig.GadgetPose"/>'s own formulas claim they
    ///      sit — an artefact of how far each authored point sits from the shoulder-roll pivot that
    ///      MV-717 never measured against the actual built transforms, only the abstract numbers.
    ///
    /// EditMode, same reflection idiom <see cref="MV717MaxArmsAndGunTests"/> already uses for this rig:
    /// Awake is not called automatically outside Play Mode, so <c>PlayerController</c>'s and
    /// <c>MaxRig</c>'s Awake methods are invoked directly, and the private tick methods the same way.
    /// All four assertions below were measured directly off the built rig (not derived by hand) before
    /// being written, and were confirmed to fail on the pre-fix commit.
    /// </summary>
    public sealed class MV730MaxArmsGunWaddleTests
    {
        private const float Dt = 0.05f;
        private const float HipY = 0.74f;   // MaxRig.HipY (private) — the waist height the rig builds at.

        private GameObject _playerGo;
        private GameObject _rigGo;
        private PlayerController _player;
        private MaxRig _rig;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player-MV730-Test", typeof(CharacterController));
            _player = _playerGo.AddComponent<PlayerController>();
            Invoke(_player, "Awake");

            _rigGo = new GameObject("MaxRig-MV730-Test");
            _rig = _rigGo.AddComponent<MaxRig>();
            Invoke(_rig, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        private static object Invoke(object target, string methodName, params object[] args) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);

        private static object GetField(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private void SetMoveInput(float x, float y) =>
            typeof(PlayerController).GetProperty("MoveInput")
                .GetSetMethod(nonPublic: true)
                .Invoke(_player, new object[] { new Vector2(x, y) });

        private void SetIsAiming(bool aiming) =>
            typeof(PlayerController).GetProperty("IsAiming")
                .GetSetMethod(nonPublic: true)
                .Invoke(_player, new object[] { aiming });

        /// <summary>Ticks the gadget to full presentation (_aim -> 1).</summary>
        private void TickToFullAim()
        {
            SetIsAiming(true);
            for (int i = 0; i < 60; i++) Invoke(_rig, "TickGadget", Dt);
            Invoke(_rig, "PoseArms");
        }

        /// <summary>AC1: the shoulder point PoseArm stretches the sleeve from must itself move across a
        /// stride while not aiming — not just the hand end. Fails on the pre-fix commit: the old
        /// PoseArms only ever fed ShoulderAimOffset (aim-only, zero while running) into the shoulder
        /// position, so ShoulderL never changed while he walked.</summary>
        [Test]
        public void ShoulderPointMovesAcrossAStrideWhileNotAiming()
        {
            SetMoveInput(0f, 1f);   // running forward, not aiming
            SetIsAiming(false);

            Invoke(_rig, "TickRun", Dt);
            Invoke(_rig, "PoseArms");
            Vector3 shoulderEarly = _rig.ShoulderL;

            for (int i = 0; i < 10; i++) Invoke(_rig, "TickRun", Dt);
            Invoke(_rig, "PoseArms");
            Vector3 shoulderLater = _rig.ShoulderL;

            Assert.That(Vector3.Distance(shoulderEarly, shoulderLater), Is.GreaterThan(0.01f),
                "the left shoulder's local position barely changes across a stride while not aiming — " +
                "PoseArm's sleeve is stretching from a point that reads as welded to the torso.");
        }

        /// <summary>AC2: the presented gadget's own muzzle tip — the built RCDA gadget's forward-most
        /// glow renderer (index 1; index 0 is the tank lens near the grip, see MaxBody.Build's own
        /// gadgetGlow.Add order) — must clear the waist by a visible margin at full aim, not just
        /// technically exceed it. Fails on the pre-fix commit: the muzzle tip resolved to world y=0.90,
        /// only 0.16 above HipY — before this fix it read as sitting at/just above the waist, not
        /// clearly presented above it.</summary>
        [Test]
        public void PresentedGunClearsTheWaistByAVisibleMarginAtFullAim()
        {
            TickToFullAim();

            var glow = (MeshRenderer[])GetField(_rig, "_gadgetGlow");
            float muzzleY = glow[1].transform.position.y;

            Assert.That(muzzleY, Is.GreaterThan(HipY + 0.20f),
                $"the presented gadget's muzzle tip sits at world y={muzzleY:F3}, not clearly above " +
                $"HipY ({HipY}) + a visible margin — Max still looks like he's presenting the gadget " +
                "down around his waist.");
        }

        /// <summary>AC3: at full aim, the right hand (now the driving grip — see MaxBody's MV-730 doc)
        /// must sit closer to the built gun than the left (the supporting grip). Fails on the pre-fix
        /// commit: HandR resolved FARTHER from the gun than HandL (0.41 vs 0.28), the opposite of what
        /// "right hand drives it" requires.</summary>
        [Test]
        public void RightHandIsCloserToTheGunThanLeftAtFullAim()
        {
            TickToFullAim();

            var gun = (Transform)GetField(_rig, "_gun");
            var handL = (Transform)GetField(_rig, "_handL");
            var handR = (Transform)GetField(_rig, "_handR");

            float distR = Vector3.Distance(handR.position, gun.position);
            float distL = Vector3.Distance(handL.position, gun.position);

            Assert.That(distR, Is.LessThan(distL),
                $"the right hand (dist={distR:F3}) is not closer to the gun than the left (dist={distL:F3}) " +
                "at full aim — the left hand still reads as the one driving the gadget.");
        }

        /// <summary>AC4: BarrelHeight (the abstract formula WaterVfx's jet height is judged against) must
        /// still land close to where the gadget's own built muzzle tip actually renders, so raising
        /// GunAimPos couldn't have silently detached the shot from the barrel. Fails on the pre-fix
        /// commit: BarrelHeight(1) computed 1.025 while the real muzzle tip rendered at 0.90 — a 12.5 cm
        /// gap between the number the water jet is placed at and where the gun is actually drawn.</summary>
        [Test]
        public void BarrelHeightMatchesTheBuiltMuzzlePositionAfterGunAimPosMoves()
        {
            TickToFullAim();

            var glow = (MeshRenderer[])GetField(_rig, "_gadgetGlow");
            float muzzleY = glow[1].transform.position.y;
            float barrelHeight = MaxRig.BarrelHeight(1f);

            Assert.That(Mathf.Abs(barrelHeight - muzzleY), Is.LessThan(0.05f),
                $"BarrelHeight ({barrelHeight:F3}) has drifted away from the built muzzle tip's real " +
                $"world position ({muzzleY:F3}) — the water jet would leave from a height the visible " +
                "gadget isn't actually at.");
        }
    }
}
