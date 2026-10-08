using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-717 — Lee, on the build: "He's just walking stiff legged. There is absolutely no upper body
    /// movement. Also his weapon is down by his side always." Root cause per the ticket: MV-451 fused
    /// the gun, both arms and both hand grips into <see cref="MaxBody"/>'s static mesh, so <see
    /// cref="MaxBody.Build"/> (AC1) and <c>MaxRig.TickGadget</c>/<c>PoseArm</c> (AC2-4) all had a
    /// null-guarded no-op where a real transform used to be — the exact same failure MV-474 already
    /// found and fixed for the hip pivots.
    ///
    /// EditMode, same reflection idiom <see cref="MV678StrideDirectionTests"/>/<see cref="MaxRigTests"/>
    /// already use for this rig: Awake is not called automatically outside Play Mode, so
    /// <c>PlayerController</c>'s and <c>MaxRig</c>'s Awake methods are invoked directly, and the
    /// private tick methods the same way.
    /// </summary>
    public sealed class MV717MaxArmsAndGunTests
    {
        private const float Dt = 0.05f;

        private GameObject _playerGo;
        private GameObject _rigGo;
        private PlayerController _player;
        private MaxRig _rig;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player-MV717-Test", typeof(CharacterController));
            _player = _playerGo.AddComponent<PlayerController>();
            Invoke(_player, "Awake");

            _rigGo = new GameObject("MaxRig-MV717-Test");
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

        private static Vector3 GetStaticVector3(string name) =>
            (Vector3)typeof(MaxRig).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        private void SetMoveInput(float x, float y) =>
            typeof(PlayerController).GetProperty("MoveInput")
                .GetSetMethod(nonPublic: true)
                .Invoke(_player, new object[] { new Vector2(x, y) });

        private void SetIsAiming(bool aiming) =>
            typeof(PlayerController).GetProperty("IsAiming")
                .GetSetMethod(nonPublic: true)
                .Invoke(_player, new object[] { aiming });

        /// <summary>AC1: must fail on the current commit — <c>MaxBody.Build</c> returns only
        /// <c>GadgetGlow</c>/<c>Hips</c> and the LPPE/Rack integration points; there is no <c>Gun</c>,
        /// no <c>ArmL</c>/<c>ArmR</c>, no <c>HandL</c>/<c>HandR</c> and no <c>Head</c>.</summary>
        [Test]
        public void MaxBodyBuild_ReturnsAllSixMovingParts()
        {
            var root = new GameObject("Root").transform;
            try
            {
                var palette = new MaxPalette(null, null, null, null, null, null, null, null, null, null,
                                             null, null, null);
                var body = MaxBody.Build(root, palette, 0.74f);

                Assert.That(body.Gun, Is.Not.Null,
                    "MaxBody.Build returned no Gun — TickGadget has nothing to raise.");
                Assert.That(body.ArmL, Is.Not.Null,
                    "MaxBody.Build returned no ArmL — PoseArm has nothing to stretch.");
                Assert.That(body.ArmR, Is.Not.Null,
                    "MaxBody.Build returned no ArmR — PoseArm has nothing to stretch.");
                Assert.That(body.HandL, Is.Not.Null,
                    "MaxBody.Build returned no HandL — the left sleeve has nothing to reach for.");
                Assert.That(body.HandR, Is.Not.Null,
                    "MaxBody.Build returned no HandR — the right sleeve has nothing to reach for.");
                Assert.That(body.Head, Is.Not.Null,
                    "MaxBody.Build returned no Head — the head-lag cue has nothing to yaw.");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        /// <summary>AC2: driving <c>IsAiming</c> false-to-true and ticking to convergence must move the
        /// BUILT gun transform from <c>GunHipPos</c> toward <c>GunAimPos</c> — not just the private
        /// <c>_aim</c> float <see cref="MaxRigTests"/> already covers.</summary>
        [Test]
        public void TickGadget_MovesTheBuiltGunFromHipToAim()
        {
            var gun = (Transform)GetField(_rig, "_gun");
            Assert.That(gun, Is.Not.Null, "MaxRig never wired up a _gun transform for TickGadget to move.");

            Vector3 hipPos = GetStaticVector3("GunHipPos");
            Vector3 aimPos = GetStaticVector3("GunAimPos");

            Assert.That(Vector3.Distance(gun.localPosition, hipPos), Is.LessThan(0.005f),
                $"the built gun starts at {gun.localPosition}, not GunHipPos ({hipPos}) — Build's rest " +
                "pose doesn't match the pose TickGadget presents at _aim = 0.");

            SetIsAiming(true);
            for (int i = 0; i < 60; i++) Invoke(_rig, "TickGadget", Dt);

            Assert.That(Vector3.Distance(gun.localPosition, aimPos), Is.LessThan(0.01f),
                $"the gun's built transform did not converge to GunAimPos ({aimPos}) after ticking to " +
                $"convergence; it sits at {gun.localPosition}. TickGadget is computing the pose but not " +
                "reaching the real transform.");
        }

        /// <summary>AC3: the arms' local rotation must change across a stride while not aiming (the arm
        /// swing), and the swing amplitude must fall to near zero once <c>_aim</c> is fully up (both
        /// hands settle on the gun).</summary>
        [Test]
        public void ArmsSwingAcrossTheStrideWhileNotAiming_AndSettleAtFullAim()
        {
            SetMoveInput(0f, 1f);   // running forward, not aiming
            SetIsAiming(false);

            Invoke(_rig, "TickRun", Dt);
            Invoke(_rig, "TickGadget", Dt);
            Invoke(_rig, "PoseArms");
            var armEarly = ((Transform)GetField(_rig, "_armL")).localRotation;

            for (int i = 0; i < 10; i++)
            {
                Invoke(_rig, "TickRun", Dt);
                Invoke(_rig, "TickGadget", Dt);
            }
            Invoke(_rig, "PoseArms");
            var armLater = ((Transform)GetField(_rig, "_armL")).localRotation;

            Assert.That(Quaternion.Angle(armEarly, armLater), Is.GreaterThan(1f),
                "the left arm's local rotation barely changes across a stride while not aiming — the " +
                "arm swing (MV-717 Part 2.2) isn't reaching the built transform.");

            // Now bring him to full aim and hold the stick still moving — the arm's rotation should
            // stop tracking the stride once both hands have settled on the gun's fixed grip.
            SetIsAiming(true);
            for (int i = 0; i < 60; i++) Invoke(_rig, "TickGadget", Dt);

            Invoke(_rig, "TickRun", Dt);
            Invoke(_rig, "PoseArms");
            var armAimedA = ((Transform)GetField(_rig, "_armL")).localRotation;

            for (int i = 0; i < 8; i++) Invoke(_rig, "TickRun", Dt);   // a big stride-phase swing
            Invoke(_rig, "PoseArms");
            var armAimedB = ((Transform)GetField(_rig, "_armL")).localRotation;

            Assert.That(Quaternion.Angle(armAimedA, armAimedB), Is.LessThan(1f),
                "the arm keeps swinging with the stride even at full aim — PoseArms should blend the " +
                "swing out to zero as _aim rises so both hands settle on the gun.");
        }

        /// <summary>AC4: with zero <c>MoveInput</c> the stride phase must not advance (he is standing
        /// still, not marching on the spot) — but the torso must still move: the breathing idle (MV-717
        /// Part 2.5) means he is never perfectly frozen.</summary>
        [Test]
        public void IdleBobRunsWithZeroMoveInput_WhileStrideStaysFrozen()
        {
            SetMoveInput(0f, 0f);
            SetIsAiming(false);

            float strideBefore = _rig.Stride;
            var torso = (Transform)GetField(_rig, "_torso");
            float yBefore = torso.localPosition.y;

            bool torsoHeightChanged = false;
            for (int i = 0; i < 40; i++)
            {
                Invoke(_rig, "TickRun", Dt);
                if (!Mathf.Approximately(torso.localPosition.y, yBefore)) torsoHeightChanged = true;
            }

            Assert.That(_rig.Stride, Is.EqualTo(strideBefore),
                "the stride phase advanced with zero MoveInput — he should be standing still, not " +
                "marching on the spot.");
            Assert.That(torsoHeightChanged, Is.True,
                "the torso's height never changed across 2 seconds standing still — the breathing " +
                "idle must never leave him perfectly frozen.");
        }
    }
}
