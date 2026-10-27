using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1300 AC1, every sub-case in one test (MV-465 Rule 1 — one scenario, repeated at three
    /// points): a current lane drifts anything standing on the ground inside it, additively, except
    /// that Max's own walk directly against it is floored at 30% of his WalkSpeed so he "can always
    /// make headway", and a mover 1 m outside the lane's own rect is untouched.
    ///
    /// Fails to compile on the commit before this ticket: neither <c>EntityKind.Current</c>,
    /// <see cref="MapGeometry.CurrentDriftAt"/>, <see cref="MapCurrents"/> nor
    /// <c>MapEntity.CurrentSpeedMps</c> exist there, and neither <c>RobotEnemy.ApplyCurrentDrift</c> nor
    /// <c>PlayerController.TickCurrentDrift</c> exist to reflect onto.
    ///
    /// Both movers go through the real per-tick helpers (<c>ApplyCurrentDrift</c>/
    /// <c>TickCurrentDrift</c>), reflection-invoked directly in a fixed-dt loop — the same idiom
    /// <c>MV1238TractorPullTests</c> already established — rather than the whole AI state machine or a
    /// live Input System, which this project's EditMode tests cannot run. Every assertion reads a
    /// RESOLVED position (MV-465 Tier 2), never an authored constant.
    /// </summary>
    public sealed class MV1300CurrentLaneTests
    {
        private const float Dt = 0.02f;
        private const int OneSecondTicks = 50; // 50 * 0.02s = 1s

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo RobotCcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo ApplyCurrentDriftMethod =
            typeof(RobotEnemy).GetMethod("ApplyCurrentDrift", NonPublicInstance);

        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", NonPublicInstance);
        private static readonly MethodInfo PlayerAwakeMethod =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo TickCurrentDriftMethod =
            typeof(PlayerController).GetMethod("TickCurrentDrift", NonPublicInstance);

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
            // same idiom MV1238TractorPullTests.NewTractor already established. Kind defaults to
            // EnemyKind.Rusher (its own field initializer), so no archetype needs applying.
            RobotCcField.SetValue(e, cc);
            Physics.SyncTransforms();
            return e;
        }

        private static GameObject NewMax(out PlayerController player)
        {
            var go = new GameObject("Max", typeof(CharacterController), typeof(PlayerController)) { tag = "Player" };
            player = go.GetComponent<PlayerController>();
            PlayerAwakeMethod.Invoke(player, null); // Awake is not a reliable AddComponent side effect outside Play mode
            Physics.SyncTransforms();
            return go;
        }

        [Test]
        public void CurrentLane_DriftsAnythingOnTheGroundInsideIt_FloorsMaxsWalkAgainstIt_AndStopsAtTheEdge()
        {
            GameObject pathGo = null, rusherInGo = null, rusherOutGo = null, maxGo = null;
            try
            {
                var map = new MapData
                {
                    deckHeight = 2.5f,
                    entities = new[]
                    {
                        new MapEntity
                        {
                            id = "lane1", kind = "current",
                            x = 0f, z = 0f, width = 5f, depth = 10f,
                            facing = "N", height = 2f, // CurrentSpeedMps reuses height, 2 m/s
                        },
                    },
                };

                // MapCurrents resolves the map through a live BackyardPath (via EnemyNavigation.Map),
                // not through the local `map` above -- same stub MV837DeckSludgeTests installs for the
                // same reason.
                pathGo = new GameObject("MV1300-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                // --- a Rusher standing still inside the lane moves 2 +/- 0.2 m north in 1 s ---
                RobotEnemy rusherIn = NewRusher(Vector3.zero);
                rusherInGo = rusherIn.gameObject;
                Vector3 rusherInStart = rusherInGo.transform.position;
                for (int i = 0; i < OneSecondTicks; i++)
                {
                    ApplyCurrentDriftMethod.Invoke(rusherIn, new object[] { Dt });
                    Physics.SyncTransforms();
                }
                float rusherNorth = rusherInGo.transform.position.z - rusherInStart.z;
                Assert.AreEqual(2f, rusherNorth, 0.2f,
                    $"a Rusher standing still in a 2 m/s N lane must drift 2 +/- 0.2 m north in 1 s, got {rusherNorth:0.00} m");

                // --- a Rusher 1 m outside the lane does not move (lane half-width 2.5 m) ---
                RobotEnemy rusherOut = NewRusher(new Vector3(3.5f, 0f, 0f));
                rusherOutGo = rusherOut.gameObject;
                Vector3 rusherOutStart = rusherOutGo.transform.position;
                for (int i = 0; i < OneSecondTicks; i++)
                {
                    ApplyCurrentDriftMethod.Invoke(rusherOut, new object[] { Dt });
                    Physics.SyncTransforms();
                }
                float rusherOutMoved = Vector3.Distance(rusherOutStart, rusherOutGo.transform.position);
                Assert.Less(rusherOutMoved, 0.01f,
                    $"a Rusher 1 m outside the lane must not move, but moved {rusherOutMoved:0.000} m");

                // --- Max holding input directly against the lane still makes southward progress of at
                // least 30% of his own WalkSpeed ("never below 30% ... so he can always make headway") ---
                maxGo = NewMax(out PlayerController player);
                var maxCc = maxGo.GetComponent<CharacterController>();
                float walkSpeed = player.WalkSpeed;
                var southInput = new Vector3(0f, 0f, -1f);
                Vector3 maxStart = maxGo.transform.position;
                for (int i = 0; i < OneSecondTicks; i++)
                {
                    // The real per-tick walk SafeMove Update() issues (ComputeSplitMotion's horizontal
                    // half), plus the drift tick under test -- the same two calls Update() makes, driven
                    // directly since EditMode cannot run the live Input System.
                    CharacterControllerMotion.SafeMove(maxCc, southInput * walkSpeed * Dt);
                    TickCurrentDriftMethod.Invoke(player, new object[] { southInput, Dt });
                    Physics.SyncTransforms();
                }
                float southProgress = maxStart.z - maxGo.transform.position.z;
                Assert.GreaterOrEqual(southProgress, walkSpeed * 0.3f - 0.05f,
                    $"Max walking south against a 2 m/s N lane must still progress at least 30% of his " +
                    $"{walkSpeed:0.00} m/s WalkSpeed in 1 s, got {southProgress:0.00} m");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (rusherOutGo != null) Object.DestroyImmediate(rusherOutGo);
                if (rusherInGo != null) Object.DestroyImmediate(rusherInGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
                RobotEnemy.ResetRegistry();
            }
        }
    }
}
