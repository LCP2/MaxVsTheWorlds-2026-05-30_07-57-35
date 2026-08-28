using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-611 — <c>ZoneRouteGrid.FindPath</c> allocated a fresh <c>float[]</c>/<c>int[]</c>/<c>bool[]</c>
    /// scratch trio EVERY call ("~15 KB of garbage per robot per frame" at <c>CellSize = 0.5f</c> over a
    /// 20x24 m room), and nothing gated how often a chasing robot asked for a fresh solve at all — a
    /// robot navigating around cover called it every single Chase tick. AC2 pins the allocation fix
    /// (same buffer-identity idiom <c>MV527AllocationGuardTests</c> used for <c>IsOnScreen</c>'s
    /// <c>Plane[6]</c>); AC3 pins the solve-frequency fix (<see cref="ZoneRouteBudget"/>).
    ///
    /// Must fail to COMPILE on the pre-fix commit: <c>ZoneGrid._gScoreScratch</c>, <c>ZoneRouteGrid.PathSolves</c>
    /// and <c>ZoneRouteBudget</c> did not exist before this ticket — the same "fails on the base commit"
    /// the project's testing policy accepts, per <c>ZoneRouteGridTests</c>' own doc comment precedent.
    /// </summary>
    public sealed class MV611PathfindingBudgetTests
    {
        private GameObject _pathGo;

        [SetUp]
        public void SetUp() => EnemyNavigation.Reset();

        [TearDown]
        public void TearDown()
        {
            EnemyNavigation.Reset();
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
        }

        /// <summary>Same fixture shape as <c>ZoneRouteGridTests.RoomWithAHedgeGap</c> — a single 20x10 m
        /// room with a hedge row splitting it at z=0, open only through a gap around x≈5 — so a straight
        /// walk from one side to the other stays blocked (and forces a real <c>FindPath</c> solve) for
        /// most of the crossing.</summary>
        private static MapData RoomWithAHedgeGap(out MapZone room)
        {
            room = new MapZone { id = "room", x = 0f, z = 0f, width = 20f, depth = 10f };
            return new MapData
            {
                zones = new[] { room },
                entities = new[]
                {
                    new MapEntity
                    {
                        id = "hedgeA", kind = "cover", dressing = "hedge",
                        x = -3.25f, z = 0f, width = 13.5f, depth = 1f, height = 1.8f,
                    },
                    new MapEntity
                    {
                        id = "hedgeB", kind = "cover", dressing = "hedge",
                        x = 8.25f, z = 0f, width = 3.5f, depth = 1f, height = 1.8f,
                    },
                },
            };
        }

        /// <summary>Points <see cref="EnemyNavigation.Map"/> at <paramref name="map"/> without running
        /// <see cref="BackyardPath.Awake"/> — the same AddComponent-without-Awake seam
        /// <c>MV493DoorwayWaypointTests.InstallMap</c> already established.</summary>
        private void InstallMap(MapData map)
        {
            _pathGo = new GameObject("MV611-test-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        private static object GetZoneGrid(string zoneId)
        {
            FieldInfo gridsField = typeof(ZoneRouteGrid).GetField("_grids",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(gridsField, "ZoneRouteGrid._grids went missing");
            var grids = (IDictionary)gridsField.GetValue(null);
            return grids.Contains(zoneId) ? grids[zoneId] : null;
        }

        // ---------------------------------------------------------------------------- AC2

        [Test]
        public void FindPath_ReusesItsScratchArrays_NeverAllocatingFreshOnesOnARepeatSolve()
        {
            MapData map = RoomWithAHedgeGap(out MapZone room);

            var from = new Vector2(0f, -4f);
            var target = new Vector2(0f, 4f);   // blocked by the hedge — forces a real FindPath solve

            Vector2? step1 = ZoneRouteGrid.NextStep(map, room, from, target);
            Assert.IsTrue(step1.HasValue, "fixture didn't force a real solve — nothing to prove buffer reuse against");

            object grid = GetZoneGrid(room.id);
            Assert.IsNotNull(grid, "ZoneRouteGrid._grids has no entry for this zone after a solve");

            FieldInfo gScoreField = grid.GetType().GetField("_gScoreScratch",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(gScoreField,
                "ZoneGrid._gScoreScratch went missing — the reusable buffer this test guards (MV-611)");

            var first = (float[])gScoreField.GetValue(grid);
            Assert.IsNotNull(first, "the gScore scratch buffer was never populated by the first solve");

            Vector2? step2 = ZoneRouteGrid.NextStep(map, room, from, target);
            Assert.IsTrue(step2.HasValue);

            var second = (float[])gScoreField.GetValue(grid);
            Assert.AreSame(first, second,
                "FindPath is handing back a NEW float[] gScore array on a repeat solve — that's the " +
                "per-call allocation MV-611 removed (same for cameFrom/visited), and it ran every " +
                "Chase tick for every robot navigating around cover.");
        }

        // ---------------------------------------------------------------------------- AC3

        [Test]
        public void ChasingRobotAroundAHedge_DoesNotResolveAFreshPathEveryFrame()
        {
            MapData map = RoomWithAHedgeGap(out MapZone room);
            InstallMap(map);

            var from = new Vector3(0f, 0f, -4f);
            var goal = new Vector3(0f, 0f, 4f);

            const float dt = 1f / 60f;
            const float speed = 3.6f;   // a Rusher's own pace
            const int ticks = 120;      // 2 seconds of chase

            var budget = new ZoneRouteBudget();
            Vector3 at = from;
            int solvesBefore = ZoneRouteGrid.PathSolves;

            for (int i = 0; i < ticks; i++)
            {
                Vector3 way = EnemyNavigation.Waypoint(at, goal, useZoneRoute: true, budget: budget, dt: dt);
                Vector3 dir = way - at;
                dir.y = 0f;
                if (dir.magnitude < 1e-4f) break;
                at += dir.normalized * speed * dt;
            }

            int solves = ZoneRouteGrid.PathSolves - solvesBefore;

            Assert.Less(solves, ticks / 4,
                $"a chasing robot re-solved ZoneRouteGrid.FindPath {solves} times across {ticks} ticks " +
                $"(budgeted at one resolve per {ZoneRouteBudget.ResolveInterval:0.00}s) — a budgeted " +
                "re-solve must land well below one solve per tick");
        }
    }
}
