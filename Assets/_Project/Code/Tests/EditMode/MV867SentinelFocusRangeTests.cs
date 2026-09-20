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
    /// MV-867: FOCUS ON must not let a sentinel's own base <see cref="AbilityTuning.DefaultSentinelRange"/>
    /// (7m) reject Max's own locked target - MV-862 shipped the wiring but still ran the shared
    /// <c>Sentinel.IsEligibleTarget</c> range check at the sentinel's own reach, so a target beyond it
    /// (as most of Max's own longer-ranged locks are) fell straight through to the MV-832 sticky-nearest
    /// rule instead, which is exactly the reported "sentinels agree with each other but not with Max"
    /// symptom. One test per MV-465 Testing Policy Rule 1: both branches (Max emitting / not emitting)
    /// read the SAME decision surface - the private <c>Sentinel.NearestRobotInRange</c> - not independent
    /// regressions.
    ///
    /// Fails on 68d962b: the sentinel's own 7m <c>_range</c> rejects the 12m focus target regardless of
    /// <see cref="Sentinel.FocusEnabled"/>, and nothing on that commit gates fire on whether Max's own
    /// primary is emitting at all - see the fix comment for the exact failure output.
    /// </summary>
    public sealed class MV867SentinelFocusRangeTests
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
        private static readonly FieldInfo LastEmittingField =
            typeof(PulseLaser).GetField("_lastEmitting", NonPublicInstance);

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
            var go = new GameObject("MV867-Robot");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            CcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            RobotOnEnableMethod.Invoke(robot, null); // seeds Active/ResetState -- Awake/OnEnable don't run outside Play mode
            go.transform.position = position;
            return robot;
        }

        [Test]
        public void FocusOnFiresAtMaxsFarTarget_PastTheSentinelsOwnRange_OnlyWhileMaxIsEmitting()
        {
            var maxGo = new GameObject("MV867-Max");
            _spawned.Add(maxGo);
            maxGo.transform.position = Vector3.zero;
            maxGo.transform.rotation = Quaternion.identity; // forward = +Z

            PulseLaser laser = maxGo.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(laser, null); // Awake doesn't run for AddComponent outside Play mode
            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Lppe;

            // Near robot: 4m from the sentinel, 90 degrees off Max's forward -- inside the sentinel's
            // own 7m base range, but never a candidate for Max's own lock (outside the LPPE's 35 degree
            // cone), so it can only ever be picked by the MV-832 sticky rule.
            RobotEnemy nearRobot = NewRobot(new Vector3(4f, 0f, 0f));
            // Max's own locked target: 12m from the sentinel, dead ahead of Max and within his 14m
            // LockRange -- past the sentinel's own 7m base range, so only reachable via FOCUS's own
            // MaxLockRangeSq substitution.
            RobotEnemy farRobot = NewRobot(new Vector3(0f, 0f, 12f));
            Physics.SyncTransforms();

            var sentinelGo = new GameObject("MV867-Sentinel");
            _spawned.Add(sentinelGo);
            Sentinel sentinel = sentinelGo.AddComponent<Sentinel>();
            sentinel.Init(Vector3.zero, maxHp: 100f, range: AbilityTuning.DefaultSentinelRange,
                fireInterval: 0.6f, moveSpeed: 0f, standoffDistance: 2.5f, followTarget: maxGo.transform);

            InvokeFireTick(laser);
            Assert.AreSame(farRobot, laser.CurrentTarget,
                "test precondition: PulseLaser.CurrentTarget must resolve to the far robot, the only one inside Max's own lock cone");
            LastEmittingField.SetValue(laser, true); // FireTick is invoked directly above; Update never runs outside Play mode

            Sentinel.FocusEnabled = true;

            RobotEnemy resolvedWhileEmitting = InvokeNearestRobotInRange(sentinel);
            Assert.AreSame(farRobot, resolvedWhileEmitting,
                "FOCUS ON + Max emitting must resolve to Max's own 12m locked target, even though it is " +
                "past the sentinel's own 7m base range");

            LastEmittingField.SetValue(laser, false);
            RobotEnemy resolvedNotEmitting = InvokeNearestRobotInRange(sentinel);
            Assert.AreSame(nearRobot, resolvedNotEmitting,
                "FOCUS ON but Max NOT emitting must fall back to the ordinary MV-832 sticky-nearest rule " +
                "at the sentinel's own range, picking the 4m near robot rather than holding Max's stale lock");
        }
    }
}
