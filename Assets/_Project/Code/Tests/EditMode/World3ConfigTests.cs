using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-712: the shipped <c>world3_config.json</c> loads through the real engines and satisfies the
    /// ticket's shape (30 areas, a branching gate graph, the a7-a12 double-pass revisit loop, 4+
    /// bridges). Loads the real resource file directly, same reason <see cref="World1RuntimeTests"/>
    /// does, so this test cannot drift out of sync with what actually ships. The ONE new test this
    /// ticket adds (testing policy v2, MV-465) — five RESOLVED-value checks in one method, the same
    /// "several concerns, one test" shape <see cref="MV711BridgeAndRouteTests"/> used before it.
    /// </summary>
    public sealed class World3ConfigTests
    {
        [Test]
        public void World3Config_LoadsRouteBranchesBridgesAndThreatBudgetWithinBand()
        {
            // --- AC1: the shipped file loads through the full WorldConfigLoader with zero errors ---
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "the shipped world3_config.json failed to load — see the error log above");

            // --- AC2: route[] has 36 visits over 30 distinct areas; a7-a12 appear exactly twice, ---
            // --- once at level 0 and once at level 1. ---
            Assert.AreEqual(36, cfg.route.Length, "route[] should carry 36 visits");

            var distinctAreas = new System.Collections.Generic.HashSet<string>();
            var visitsByArea = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>();
            foreach (WorldRouteVisit v in cfg.route)
            {
                distinctAreas.Add(v.area);
                if (!visitsByArea.TryGetValue(v.area, out var levels))
                    visitsByArea[v.area] = levels = new System.Collections.Generic.List<int>();
                levels.Add(v.level);
            }
            Assert.AreEqual(30, distinctAreas.Count, "route[] should cover 30 distinct areas");

            string[] revisit = { "a7", "a8", "a9", "a10", "a11", "a12" };
            foreach (string id in revisit)
            {
                Assert.IsTrue(visitsByArea.TryGetValue(id, out var levels) && levels.Count == 2,
                    $"{id} should appear exactly twice in route[]");
                CollectionAssert.AreEquivalent(new[] { 0, 1 }, levels, $"{id}'s two visits should be level 0 and level 1");
            }
            foreach (var kv in visitsByArea)
            {
                if (System.Array.IndexOf(revisit, kv.Key) >= 0) continue;
                Assert.AreEqual(1, kv.Value.Count, $"{kv.Key} should appear exactly once in route[]");
            }

            // --- AC3: the gate graph is fully connected with no empty clear-condition set — proven by ---
            // --- AC1's successful load itself (WorldReachability/opensWith parsing are hard load gates, ---
            // --- MapValidation.cs), reasserted directly here so a future regression that weakened that ---
            // --- gate still fails this test even if it stopped failing the load. ---
            Assert.Greater(cfg.gates.Length, 0);
            foreach (WorldGate g in cfg.gates)
                Assert.IsFalse(string.IsNullOrWhiteSpace(g.opensWith), $"gate '{g.id}' has an empty opensWith");

            // --- AC4: every visit's solved threat budget lies within the band 0.85-1.4 against World ---
            // --- 3's own dials — R = SigmaThreatValue(index) / TargetBudget(index), the composition ---
            // --- solver's own target for that area (composition is dial-solved throughout, so this ---
            // --- checks the solver actually landed close to what its own dials asked for). ---
            foreach (WorldRouteVisit v in cfg.route)
            {
                WorldArea area = cfg.Area(v.area);
                Assert.IsNotNull(area, $"route references unknown area '{v.area}'");

                float targetBudget = DifficultyEngine.TargetBudget(area.index, cfg.dials.baseThreat, cfg.dials.threatGrowth, cfg.dials.EnginePacing);
                float sigma = cfg.SigmaThreatValue(area.index);
                float ratio = targetBudget > 0f ? sigma / targetBudget : 0f;

                Assert.IsTrue(PowerScoring.WithinBand(ratio),
                    $"visit '{v.area}' (level {v.level}, index {area.index}): R={ratio:0.###} is outside the 0.85-1.4 band " +
                    $"(sigma={sigma:0.#}, targetBudget={targetBudget:0.#})");
            }

            // --- AC5: MapValidation passes on the whole config, bridges included (belt and suspenders — ---
            // --- ValidateWorldConfig already ran as part of AC1's load; this proves the POST-CONVERSION ---
            // --- MapData also validates, same as World1RuntimeTests does for World 1). ---
            Assert.GreaterOrEqual(cfg.bridges.Length, 4, "World 3 should author at least 4 bridges");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string mapReason), mapReason);
            Assert.IsTrue(MapValidation.Validate(map, out string validateReason), validateReason);
        }
    }
}
