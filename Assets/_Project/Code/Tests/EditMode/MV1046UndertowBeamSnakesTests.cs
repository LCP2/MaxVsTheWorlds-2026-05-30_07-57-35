using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1046 — World 3's UNDERTOW stream must snake and wind along its whole length, not just its
    /// thin crackle strands: the core/sheath centreline used to be a dead-straight 2-point
    /// <c>LineRenderer</c> from muzzle to end point (MV-1034), which at the play camera's ~48 px/m reads
    /// as a straight laser with fuzz rather than a writhing stream.
    ///
    /// Built off a player through the real entry point (<see cref="PlayerController.Awake"/>) with
    /// World 3's UNDERTOW active, ticked through <c>Undertow.Tick</c>'s own explicit-<c>dt</c> method —
    /// the same reflection-driven, real-entry-point idiom <c>MV1034UndertowStreamTests</c> and
    /// <c>MV1044UndertowOpensGateTests</c> already use — for a full 1s hold (10 × 0.1s ticks) at a robot
    /// 8m ahead, then reads the CORE <c>LineRenderer</c>'s own resolved positions (never a hand-set
    /// stand-in for the geometry).
    ///
    /// Fails on de3c3a3 (the commit before this ticket): the core <c>LineRenderer</c> is a straight
    /// 2-point line (<c>positionCount == 2</c>), so it does not compile against this test's ≥25-point
    /// assertion — reading <c>core.GetPosition</c> past index 1 throws
    /// <c>IndexOutOfRangeException</c>, and the maximum perpendicular offset from the muzzle→end line is
    /// 0m rather than the 0.45-0.9m band the snake must reach.
    /// </summary>
    public sealed class MV1046UndertowBeamSnakesTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo PlayerControllerAwake =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowAwake =
            typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick =
            typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly FieldInfo RobotEnemyCcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo RobotEnemyHealthField =
            typeof(RobotEnemy).GetField("_health", NonPublicInstance);

        private GameObject _playerGo;
        private GameObject _robotGo;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_robotGo != null) Object.DestroyImmediate(_robotGo);
        }

        private static void InvokeTick(Undertow undertow, float dt) =>
            UndertowTick.Invoke(undertow, new object[] { dt });

        /// <summary>Shortest distance from <paramref name="point"/> to the infinite line through
        /// <paramref name="a"/> and <paramref name="b"/> — what "max perpendicular distance from the
        /// straight muzzle→end line" (AC1) actually means for a 3D point.</summary>
        private static float PerpendicularDistance(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            Vector3 ap = point - a;
            float abLenSq = ab.sqrMagnitude;
            if (abLenSq < 1e-8f) return ap.magnitude;
            Vector3 projection = ab * (Vector3.Dot(ap, ab) / abLenSq);
            return (ap - projection).magnitude;
        }

        [Test]
        public void CoreLineSnakesBetweenMuzzleAndHitPoint_MoreThan24Segments_TaperedAndMoving_MV1046()
        {
            // --- Build Max through the real entry point with World 3's UNDERTOW active.
            _playerGo = new GameObject("MV1046 Player", typeof(CharacterController), typeof(PlayerController));
            var player = _playerGo.GetComponent<PlayerController>();
            PlayerControllerAwake.Invoke(player, null);

            var undertow = _playerGo.GetComponent<Undertow>();
            Assert.IsNotNull(undertow, "PlayerController.Awake must self-attach a live Undertow");
            UndertowAwake.Invoke(undertow, null); // Awake doesn't run for AddComponent outside Play mode

            WeaponSystemState.ApplyWeaponCoreMorph(2); // World 3 -> UNDERTOW is the active primary
            DevMode.Enabled = true;
            DevMode.AutoFire = true; // "request fire" without the real Input System (PlayMode is banned)

            // --- A stationary robot 8m ahead, well inside UNDERTOW's 9m base range.
            _robotGo = new GameObject("MV1046 Target");
            var cc = _robotGo.AddComponent<CharacterController>();
            var robot = _robotGo.AddComponent<RobotEnemy>();
            RobotEnemyCcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(robot, null); // seeds Active/ResetState -- Awake/OnEnable don't run outside Play mode
            _robotGo.transform.position = _playerGo.transform.position + _playerGo.transform.forward * 8f;
            RobotEnemyHealthField.SetValue(robot, 10000f); // survives every tick in this test
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

            var vfx = _playerGo.GetComponent<MaxWorlds.VFX.UndertowVfx>();
            Assert.IsNotNull(vfx, "Undertow.Awake must self-attach a live UndertowVfx");
            Transform coreTransform = _playerGo.transform.Find("UndertowCore");
            Assert.IsNotNull(coreTransform, "UndertowVfx.Init must build a child named UndertowCore");
            var core = coreTransform.GetComponent<LineRenderer>();
            Assert.IsNotNull(core, "UndertowCore must carry a LineRenderer");

            // --- Fire for a full 1s hold (10 x 0.1s ticks, matching Undertow's own fireInterval).
            Vector3 midBeforeLastTick = Vector3.zero;
            int midIndex = 0;
            for (int i = 0; i < 10; i++)
            {
                if (i == 9)
                {
                    midIndex = core.positionCount / 2;
                    midBeforeLastTick = core.GetPosition(midIndex);
                }
                InvokeTick(undertow, 0.1f);
            }

            Assert.IsTrue(vfx.IsStreaming, "the stream must still be visible after a continuous 1s hold");
            Assert.GreaterOrEqual(core.positionCount, 25,
                $"the core LineRenderer must snake through >=25 points (Segments>=24), had {core.positionCount}");

            Vector3 muzzle = _playerGo.transform.position;
            Vector3 endPoint = undertow.StreamEndPoint;

            Vector3 firstPos = core.GetPosition(0);
            Vector3 lastPos = core.GetPosition(core.positionCount - 1);
            Assert.Less(Vector3.Distance(firstPos, muzzle), 0.05f,
                $"the core's first point must stay pinned within 0.05m of the muzzle, was {Vector3.Distance(firstPos, muzzle):0.000}m off");
            Assert.Less(Vector3.Distance(lastPos, endPoint), 0.05f,
                $"the core's last point must stay pinned within 0.05m of the hit point, was {Vector3.Distance(lastPos, endPoint):0.000}m off");

            float maxOffset = 0f;
            for (int i = 0; i < core.positionCount; i++)
                maxOffset = Mathf.Max(maxOffset, PerpendicularDistance(core.GetPosition(i), muzzle, endPoint));
            Assert.GreaterOrEqual(maxOffset, 0.45f,
                $"the beam must snake at least 0.45m off its own straight muzzle->end line, max was {maxOffset:0.000}m");
            Assert.LessOrEqual(maxOffset, 0.9f,
                $"the beam must not snake more than 0.9m off its own straight muzzle->end line, max was {maxOffset:0.000}m");

            // --- The pattern travels: the mid-beam point must have visibly moved between the last two
            // 0.1s ticks, not sat still.
            Vector3 midAfterLastTick = core.GetPosition(midIndex);
            float midDelta = Vector3.Distance(midBeforeLastTick, midAfterLastTick);
            Assert.GreaterOrEqual(midDelta, 0.1f,
                $"the mid-beam point must move >=0.1m between ticks 0.1s apart, moved only {midDelta:0.000}m");
        }
    }
}
