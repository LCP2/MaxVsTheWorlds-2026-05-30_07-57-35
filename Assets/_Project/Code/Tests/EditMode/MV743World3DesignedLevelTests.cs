using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-743: <c>world3_config.json</c> was a skeleton that satisfied MV-712's counting acceptance
    /// criteria (36 visits, 30 areas, 4 bridges, band compliance) while authoring zero cover, four
    /// sheds and near-identical rooms. This re-authors the file as a designed level; the ONE new test
    /// this ticket adds (testing policy v2, MV-465) — six RESOLVED-value checks in one method, reading
    /// the shipped file directly (same reason <see cref="World3ConfigTests"/> does) so it cannot drift
    /// out of sync with what actually ships. Fails on 5d57354 (the commit before this ticket): that
    /// world3_config.json authors 0 cover entries, 4 sheds and 24 identical 20x20 rooms.
    /// </summary>
    public sealed class MV743World3DesignedLevelTests
    {
        [Test]
        public void World3Config_IsADesignedLevelNotASkeleton()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "the shipped world3_config.json failed to load — see the error log above");

            var numbered = new List<WorldArea>();
            foreach (WorldArea a in cfg.areas)
                if (a != null && a.id != "stub") numbered.Add(a);
            Assert.AreEqual(30, numbered.Count, "World 3 should still author 30 numbered areas");

            // --- AC1: at least 12 distinct (w,d) footprints, no single pair used by more than 5 areas ---
            var pairCounts = new Dictionary<(float, float), int>();
            foreach (WorldArea a in numbered)
            {
                var key = (a.size.w, a.size.d);
                pairCounts[key] = pairCounts.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            Assert.GreaterOrEqual(pairCounts.Count, 12, "World 3 should author at least 12 distinct area footprints");
            foreach (var kv in pairCounts)
                Assert.LessOrEqual(kv.Value, 5, $"footprint {kv.Key} is used by {kv.Value} areas — over the 5-area cap");

            // --- AC2: every non-entry area authors >=8 cover, world totals >=350 ---
            int totalCover = 0;
            foreach (WorldArea a in numbered)
            {
                int n = a.cover?.Length ?? 0;
                Assert.GreaterOrEqual(n, 8, $"area '{a.id}' authors only {n} cover entries — needs at least 8");
                totalCover += n;
            }
            Assert.GreaterOrEqual(totalCover, 350, $"World 3 authors only {totalCover} cover entries world-wide — needs at least 350");

            // --- AC3: hasShed true for 10-15 areas, cadence (dials.powerupCadence) respected along route[] ---
            var shedByArea = new Dictionary<string, bool>();
            int shedCount = 0;
            foreach (WorldArea a in numbered)
            {
                bool has = a.hasShed;
                shedByArea[a.id] = has;
                if (has) shedCount++;
            }
            Assert.GreaterOrEqual(shedCount, 10, "World 3 should author hasShed on at least 10 areas");
            Assert.LessOrEqual(shedCount, 15, "World 3 should author hasShed on at most 15 areas");

            int run = 0, maxRun = 0;
            foreach (WorldRouteVisit v in cfg.route)
            {
                if (!shedByArea.TryGetValue(v.area, out bool has) || !has) { run++; maxRun = System.Math.Max(maxRun, run); }
                else run = 0;
            }
            Assert.LessOrEqual(maxRun, cfg.dials.powerupCadence,
                $"longest shed-free run along route[] is {maxRun} — over the powerupCadence ({cfg.dials.powerupCadence}) cadence");

            // --- AC4: every non-entry, non-boss area authors >=1 garrison entry, world totals >=120 ---
            int totalGarrison = 0;
            foreach (WorldArea a in numbered)
            {
                int n = a.garrison?.Length ?? 0;
                totalGarrison += n;
                if (!a.IsBossRole)
                    Assert.GreaterOrEqual(n, 1, $"non-boss area '{a.id}' authors no garrison entries");
            }
            Assert.GreaterOrEqual(totalGarrison, 120, $"World 3 authors only {totalGarrison} garrison entries world-wide — needs at least 120");

            // --- AC5: every bridge's from/to area authors a decks[] entry ---
            foreach (WorldBridge b in cfg.bridges)
            {
                WorldArea from = cfg.Area(b.from.area), to = cfg.Area(b.to.area);
                Assert.IsTrue(from != null && from.decks != null && from.decks.Length > 0,
                    $"bridge '{b.id}' leaves area '{b.from.area}', which authors no deck");
                Assert.IsTrue(to != null && to.decks != null && to.decks.Length > 0,
                    $"bridge '{b.id}' enters area '{b.to.area}', which authors no deck");
                Assert.IsTrue(b.piers != null && b.piers.Length > 0, $"bridge '{b.id}' authors no piers");
            }

            // --- AC7: no area name reads as a placeholder ---
            var placeholder = new Regex(@"^(a\d+$|Area \d+)");
            foreach (WorldArea a in cfg.areas)
                Assert.IsFalse(placeholder.IsMatch(a.name), $"area '{a.id}' still carries a placeholder name '{a.name}'");

            // --- AC8: MapValidation passes on the whole config, bridges and piers included ---
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string mapReason), mapReason);
            Assert.IsTrue(MapValidation.Validate(map, out string validateReason), validateReason);
        }
    }
}
