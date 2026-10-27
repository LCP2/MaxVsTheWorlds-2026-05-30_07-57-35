using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1307 AC1, both sub-cases in one test (MV-465 Rule 1): "a lane with reverseEvery 10 pushes a
    /// robot north at t = 5 s and south at t = 15 s; a lane with 0 never flips."
    ///
    /// Fails to compile on the commit before this ticket: neither <c>MapCurrentReversalRunner</c> nor
    /// <c>MapEntity.CurrentReverseEverySec</c> exist there.
    ///
    /// Drives the shared reversal clock directly (<see cref="MapCurrentReversalRunner.TickCurrentReversal"/>,
    /// same "a caller decides the cadence" idiom <c>MV1240PlasmaVentDamageTests</c> already uses for
    /// <c>MapVentDamageRunner</c>) to jump straight to the two ages the AC names, then drives the real
    /// per-tick push (<c>RobotEnemy.ApplyCurrentDrift</c>, reflection-invoked, same idiom
    /// <c>MV1300CurrentLaneTests</c> established) over a 1 s window at each age. Both robots share the
    /// SAME clock throughout, same as the real game. Every assertion reads a RESOLVED displacement
    /// (MV-465 Tier 2), never an authored constant.
    /// </summary>
    public sealed class MV1307CurrentReversalTests
    {
        private const float Dt = 0.02f;
        private const int OneSecondTicks = 50; // 50 * 0.02s = 1s -- same idiom as MV1300CurrentLaneTests,
        // avoids CharacterControllerMotion.SafeMove's "oversized single-frame Move" warning a single big
        // dt step would trip (and that Unity's strict per-test log check would then fail the test on).

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo RobotCcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo ApplyCurrentDriftMethod =
            typeof(RobotEnemy).GetMethod("ApplyCurrentDrift", NonPublicInstance);

        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", NonPublicInstance);

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            UpgradeState.Reset();
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            DevTuning.Reset();
            UpgradeState.Reset();
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
        }

        private static RobotEnemy NewRusher(Vector3 position)
        {
            var go = new GameObject("Rusher");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // EditMode never runs Awake, so _cc (normally seeded there) has to be stamped by hand --
            // same idiom MV1300CurrentLaneTests.NewRusher already established.
            RobotCcField.SetValue(e, cc);
            Physics.SyncTransforms();
            return e;
        }

        private static float ZDriftOverOneSecondAt(RobotEnemy robot)
        {
            Vector3 start = robot.transform.position;
            for (int i = 0; i < OneSecondTicks; i++)
            {
                ApplyCurrentDriftMethod.Invoke(robot, new object[] { Dt });
                Physics.SyncTransforms();
            }
            return robot.transform.position.z - start.z;
        }

        [Test]
        public void ReverseEvery10Lane_PushesNorthAtT5_SouthAtT15_WhileAZeroLaneNeverFlips()
        {
            GameObject pathGo = null, runnerGo = null, reversingGo = null, steadyGo = null;
            try
            {
                var map = new MapData
                {
                    deckHeight = 2.5f,
                    entities = new[]
                    {
                        new MapEntity
                        {
                            id = "lane_reversing", kind = "current",
                            x = 0f, z = 0f, width = 5f, depth = 60f,
                            facing = "N", height = 2f, // CurrentSpeedMps reuses height, 2 m/s
                            crossingAt = 10f, // CurrentReverseEverySec reuses crossingAt, flips every 10s
                        },
                        new MapEntity
                        {
                            id = "lane_steady", kind = "current",
                            x = 20f, z = 0f, width = 5f, depth = 60f,
                            facing = "N", height = 2f, // crossingAt left at 0 -- never reverses
                        },
                    },
                };

                // MapCurrents resolves the map through a live BackyardPath (via EnemyNavigation.Map),
                // not through the local `map` above -- same stub MV1300CurrentLaneTests installs for the
                // same reason.
                pathGo = new GameObject("MV1307-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                runnerGo = new GameObject("MV1307-current-reversal-runner");
                var runner = runnerGo.AddComponent<MapCurrentReversalRunner>();

                RobotEnemy reversing = NewRusher(new Vector3(0f, 0f, 0f));
                reversingGo = reversing.gameObject;
                RobotEnemy steady = NewRusher(new Vector3(20f, 0f, 0f));
                steadyGo = steady.gameObject;

                // --- t = 5 s: both lanes still in their first (as-authored) interval, [0, 10) ---
                runner.TickCurrentReversal(5f);
                float reversingAtT5 = ZDriftOverOneSecondAt(reversing);
                float steadyAtT5 = ZDriftOverOneSecondAt(steady);
                Assert.AreEqual(2f, reversingAtT5, 0.2f,
                    $"MV-1307: a reverseEvery=10 lane at t=5s (before its first flip at t=10s) must still " +
                    $"push 2 +/- 0.2 m north, got {reversingAtT5:0.00} m");
                Assert.AreEqual(2f, steadyAtT5, 0.2f,
                    $"MV-1307: a reverseEvery=0 lane at t=5s must push 2 +/- 0.2 m north, got {steadyAtT5:0.00} m");

                // --- t = 15 s: the reversing lane is now in its second interval, [10, 20), flipped ---
                runner.TickCurrentReversal(10f); // age 5 -> 15
                float reversingAtT15 = ZDriftOverOneSecondAt(reversing);
                float steadyAtT15 = ZDriftOverOneSecondAt(steady);
                Assert.AreEqual(-2f, reversingAtT15, 0.2f,
                    $"MV-1307: a reverseEvery=10 lane at t=15s (past its first flip at t=10s) must push " +
                    $"2 +/- 0.2 m SOUTH, got {reversingAtT15:0.00} m");
                Assert.AreEqual(2f, steadyAtT15, 0.2f,
                    $"MV-1307: a reverseEvery=0 lane must never flip -- still 2 +/- 0.2 m north at t=15s, " +
                    $"got {steadyAtT15:0.00} m");
            }
            finally
            {
                if (steadyGo != null) Object.DestroyImmediate(steadyGo);
                if (reversingGo != null) Object.DestroyImmediate(reversingGo);
                if (runnerGo != null) Object.DestroyImmediate(runnerGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
                RobotEnemy.ResetRegistry();
            }
        }
    }
}
