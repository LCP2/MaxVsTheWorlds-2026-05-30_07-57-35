using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-862: FOCUS ON makes a deployed Sentinel share Max's own current target — the robot his
    /// equipped primary just locked (<see cref="PulseLaser.CurrentTarget"/>) — overriding the
    /// sentinel's own sticky MV-832 pick; FOCUS OFF leaves that old sticky-nearest behaviour untouched.
    /// One test per MV-465 Testing Policy Rule 1: both branches read the SAME decision surface
    /// (<see cref="Sentinel.FocusEnabled"/> feeding the private <c>Sentinel.NearestRobotInRange</c>),
    /// not independent regressions.
    ///
    /// Fails on dd85276: neither <see cref="Sentinel.FocusEnabled"/> nor
    /// <see cref="PulseLaser.CurrentTarget"/> exist on that commit (the flag was still
    /// <c>AttackModeEnabled</c>, with no per-weapon "current target" property at all), so this test does
    /// not compile there — see the fix comment for the exact error.
    /// </summary>
    public sealed class MV862SentinelFocusTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly MethodInfo NearestRobotInRangeMethod =
            typeof(Sentinel).GetMethod("NearestRobotInRange", NonPublicInstance);
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo PulseLaserFireTick =
            typeof(PulseLaser).GetMethod("FireTick", NonPublicInstance);

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            EnemyNavigation.Reset();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            Sentinel.FocusEnabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();

            EnemyNavigation.Reset();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            Sentinel.FocusEnabled = false;
        }

        private static RobotEnemy InvokeNearestRobotInRange(Sentinel sentinel) =>
            (RobotEnemy)NearestRobotInRangeMethod.Invoke(sentinel, null);

        private static void InvokeFireTick(PulseLaser laser) => PulseLaserFireTick.Invoke(laser, null);

        private RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV862-Robot");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            CcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            RobotOnEnableMethod.Invoke(robot, null); // seeds Active/ResetState -- Awake/OnEnable don't run outside Play mode
            go.transform.position = position;
            return robot;
        }

        private Sentinel NewSentinelAt(Vector3 position, Transform followTarget)
        {
            var go = new GameObject("MV862-Sentinel");
            _spawned.Add(go);
            var sentinel = go.AddComponent<Sentinel>();
            sentinel.Init(position, maxHp: 100f, range: 20f, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: followTarget);
            return sentinel;
        }

        [Test]
        public void FocusOnFollowsMaxsCurrentTarget_FocusOffKeepsTheStickyPick()
        {
            var maxGo = new GameObject("MV862-Max");
            _spawned.Add(maxGo);
            maxGo.transform.position = Vector3.zero;
            maxGo.transform.rotation = Quaternion.identity; // forward = +Z

            PulseLaser laser = maxGo.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(laser, null); // Awake doesn't run for AddComponent outside Play mode
            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Lppe;

            // A sits nearest the sentinel (2m) but 90 degrees off Max's own forward -- outside the
            // LPPE's 35 degree lock cone, so Max's own pulse can never lock it. B sits farther from the
            // sentinel (6m) but dead ahead of Max -- the only candidate inside his lock cone.
            RobotEnemy robotA = NewRobot(new Vector3(2f, 0f, 0f));
            RobotEnemy robotB = NewRobot(new Vector3(0f, 0f, 6f));
            Physics.SyncTransforms();

            // Two identical sentinels at the same spot, sticky target established on each while FOCUS
            // is off -- so the two branches below diverge only on FocusEnabled, never on setup order.
            Sentinel sentinelForFocusOn = NewSentinelAt(Vector3.zero, maxGo.transform);
            Sentinel sentinelForFocusOff = NewSentinelAt(Vector3.zero, maxGo.transform);

            RobotEnemy stickyOn = InvokeNearestRobotInRange(sentinelForFocusOn);
            RobotEnemy stickyOff = InvokeNearestRobotInRange(sentinelForFocusOff);
            Assert.AreSame(robotA, stickyOn, "test precondition: the sentinel's own sticky pick must start on A (nearest)");
            Assert.AreSame(robotA, stickyOff, "test precondition: the sentinel's own sticky pick must start on A (nearest)");

            InvokeFireTick(laser);
            Assert.AreSame(robotB, laser.CurrentTarget,
                "test precondition: PulseLaser.CurrentTarget must resolve to B, the only robot inside Max's own lock cone");

            // FOCUS ON: Max's own current target (B) overrides the sticky pick (A).
            Sentinel.FocusEnabled = true;
            RobotEnemy focused = InvokeNearestRobotInRange(sentinelForFocusOn);
            Assert.AreSame(robotB, focused,
                "FOCUS ON must make the sentinel's next resolved target Max's own current target (B), " +
                "overriding its own sticky pick (A)");

            // FOCUS OFF: exactly today's behaviour -- the sticky pick (A) is untouched by Max's target.
            Sentinel.FocusEnabled = false;
            RobotEnemy unfocused = InvokeNearestRobotInRange(sentinelForFocusOff);
            Assert.AreSame(robotA, unfocused,
                "FOCUS OFF must leave the sentinel's own sticky target (A) alone, ignoring Max's own target entirely");
        }
    }
}
