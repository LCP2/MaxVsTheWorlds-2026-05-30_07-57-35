using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-865 — <see cref="AreaAccumulationDirector.EnterArea"/> only ever moves <c>CurrentArea</c>
    /// forward by index (AreaAccumulationDirector.cs:225-230: "if (areaIndex &lt;= CurrentArea) return;"),
    /// and a garrison is pre-placed a step ahead by <c>FillArea</c>'s own
    /// <c>PlacePendingGarrison(areaIndex + 1)</c>. Before this ticket's renumbering, World 2's physical
    /// gate route did not visit areas in increasing index order (it ran old a11 -&gt; a18 -&gt; a17 -&gt;
    /// a16 -&gt; a6), so calling <c>EnterArea</c> in that real walked order fired out of index order and
    /// several areas (old a14/a15/a16/a17) never got their pre-placed garrison at all — Lee reported "A16
    /// - there are no robots". MV-865 renumbers every area to its play-order index specifically to fix
    /// this. Fails on base commit dd85276 (before this ticket) — see the fix comment for the captured
    /// failure output.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) asserting RESOLVED values (Rule 2, Tier 2):
    /// (1) walking the real shipped gate graph from the entry stub, always advancing to whichever
    /// not-yet-reached area has the SMALLEST index among every area gate-adjacent to an already-reached
    /// one, visits every index from 1 to areaCount in strictly increasing order — the structural property
    /// that makes <c>EnterArea</c>'s forward-only advance safe; (2) driving the real
    /// <see cref="AreaAccumulationDirector"/> through that exact same order and reading back the real
    /// placed instances shows every area whose garrison is fully authored (no difficulty-budget ring-fill
    /// on top of it) has exactly its authored count of level-0 and level-1 robots placed once the route
    /// reaches it.
    ///
    /// Reads placed robots via <see cref="Object.FindObjectsByType{T}(FindObjectsSortMode)"/>, not
    /// <see cref="RobotEnemy.Active"/> — <c>AreaAccumulationDirector</c>'s garrison placement builds a
    /// robot via a plain <c>GameObject.CreatePrimitive</c> + <c>AddComponent&lt;RobotEnemy&gt;</c>, and
    /// Unity never fires <c>OnEnable</c> for either step outside Play mode, so <c>RobotEnemy.Active</c>
    /// (which only <c>OnEnable</c> populates) silently stays empty for every real garrison spawn in
    /// EditMode — the same documented quirk <c>MV828ReplicatorAreaIndexTests.RegisterAllUnregisteredRobots</c>
    /// works around. <c>AreaIndex</c>/<c>Level</c> are plain fields stamped directly by
    /// <c>SeedGarrison</c>/<c>PlacePendingGarrison</c>, not dependent on any Unity lifecycle callback, so
    /// a scene scan reads them correctly without needing that reflection workaround here.
    /// </summary>
    public sealed class MV865World2RouteOrderTests
    {
        [Test]
        public void GateRouteVisitsEveryAreaInIncreasingIndexOrder_AndEveryAuthoredGarrisonIsPlaced()
        {
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");

            // === Structural: the gate graph itself visits every index in strictly increasing order ===
            int areaCount = cfg.dials.areaCount;
            var adjacency = new Dictionary<string, List<string>>();
            foreach (WorldGate g in cfg.gates)
            {
                if (g?.from == null || g.to == null) continue;
                if (!adjacency.TryGetValue(g.from.area, out List<string> a)) adjacency[g.from.area] = a = new List<string>();
                a.Add(g.to.area);
                if (!adjacency.TryGetValue(g.to.area, out List<string> b)) adjacency[g.to.area] = b = new List<string>();
                b.Add(g.from.area);
            }

            WorldArea entry = cfg.areas.First(ar => ar.IsEntryRole);
            var reached = new HashSet<string> { entry.id };
            var order = new List<int>();

            for (int step = 0; step < areaCount; step++)
            {
                string next = null;
                int nextIndex = int.MaxValue;
                foreach (string reachedId in reached)
                {
                    if (!adjacency.TryGetValue(reachedId, out List<string> neighbours)) continue;
                    foreach (string candidate in neighbours)
                    {
                        if (reached.Contains(candidate)) continue;
                        WorldArea candidateArea = cfg.Area(candidate);
                        if (candidateArea == null || candidateArea.index <= 0) continue; // skip the stub itself
                        if (candidateArea.index < nextIndex) { nextIndex = candidateArea.index; next = candidate; }
                    }
                }

                Assert.IsNotNull(next,
                    $"MV-865: no not-yet-reached area is gate-adjacent to anything reached so far, after visiting {string.Join(",", order)}");
                reached.Add(next);
                order.Add(nextIndex);
            }

            CollectionAssert.AreEqual(Enumerable.Range(1, areaCount).ToArray(), order,
                $"MV-865: walking the gate graph, always taking the lowest-indexed not-yet-reached neighbour, " +
                $"must visit every area index from 1 to {areaCount} in strictly increasing order — got {string.Join(",", order)}");

            // === Driving the real director through that exact order actually places every authored garrison ===
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            var root = new GameObject("MV865 Root");
            GameObject areaGo = null;
            try
            {
                MapBuild build = MapRuntime.Build(map, root.transform);

                areaGo = new GameObject("Area Accumulation");
                var director = areaGo.AddComponent<AreaAccumulationDirector>();
                director.ConfigureWorld(cfg);
                director.Configure(map, build.Cover); // seeds area 1

                foreach (int index in order.Where(i => i > 1)) director.EnterArea(index);

                // MV-966: Include, not the default Exclude — EnterArea alone never moves the physical
                // tracker ParkByReach keys off (only SetCurrentArea/a live position crossing does), so
                // every garrison this walk pre-places for an area still ahead of area1 starts PARKED
                // (inactive). This test counts placement by AreaIndex/Level, not aliveness or art
                // coverage, so Include alone is enough — nothing here ever kills a robot.
                RobotEnemy[] placedRobots = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);

                foreach (WorldArea area in cfg.areas)
                {
                    if (area.garrison == null || area.garrison.Length == 0) continue;

                    // Ring-fill only ever adds MORE than the authored entries when the difficulty-solved
                    // seed count exceeds them (Garrison.cs SeedSlotsFor) — guard out the (rare, legitimate)
                    // areas where that happens, since this test proves every AUTHORED entry got placed,
                    // not the ring-fill formula itself.
                    int seedCount = Garrison.SeedCount(area.index, cfg);
                    if (seedCount > area.garrison.Length) continue;

                    foreach (int level in new[] { 0, 1 })
                    {
                        int expected = area.garrison.Count(e => e.level == level);
                        if (expected == 0) continue;

                        int placed = placedRobots.Count(r => r.AreaIndex == area.index && r.Level == level);
                        Assert.AreEqual(expected, placed,
                            $"MV-865: area '{area.id}' (index {area.index}) authors {expected} level-{level} garrison " +
                            $"robot(s) but {placed} are actually placed after walking the route in order");
                    }
                }
            }
            finally
            {
                GameObject bodies = GameObject.Find("Area Robots");
                if (bodies != null) Object.DestroyImmediate(bodies);
                RobotEnemy.ResetRegistry();
                if (areaGo != null) Object.DestroyImmediate(areaGo);
                Object.DestroyImmediate(root);
                DevTuning.Reset();
            }
        }
    }
}
