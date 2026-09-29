using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-712: the shipped <c>world3_config.json</c> loads through the real engines and satisfies the
    /// ticket's shape (30 areas, a branching gate graph, 4+ bridges). Loads the real resource file
    /// directly, same reason <see cref="World1RuntimeTests"/> does, so this test cannot drift out of
    /// sync with what actually ships.
    ///
    /// MV-1014: the a7-a12 "double-pass" revisit loop this test originally asserted was never actually
    /// built as a real second level — nothing at runtime read <c>route[]</c>'s per-visit level, and the
    /// 12 level-1 garrison entries resolved to y=2.5 in open air with no ramp/hatch to reach them (Lee,
    /// 2026-09-29: nobody built a second level; World 3 is traversed on the ground throughout). AC2 below
    /// is rewritten to the new truth: 30 visits, one per area, all at level 0, and zero garrison entries
    /// anywhere in the config carry a non-zero level.
    /// </summary>
    public sealed class World3ConfigTests
    {
        [Test]
        public void World3Config_LoadsRouteBranchesBridgesAndThreatBudgetWithinBand()
        {
            // --- AC1: the shipped file loads through the full WorldConfigLoader with zero errors ---
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "the shipped world3_config.json failed to load — see the error log above");

            // --- AC2 (MV-1014): route[] has 30 visits, each of the 30 areas exactly once, all level 0; ---
            // --- and no garrison entry anywhere in the config carries a non-zero level (the mid-air ---
            // --- MV-1014 defect). ---
            Assert.AreEqual(30, cfg.route.Length, "route[] should carry 30 visits");

            var distinctAreas = new System.Collections.Generic.HashSet<string>();
            foreach (WorldRouteVisit v in cfg.route)
            {
                Assert.IsTrue(distinctAreas.Add(v.area), $"{v.area} appears more than once in route[]");
                Assert.AreEqual(0, v.level, $"{v.area}'s route[] visit must be level 0 — World 3 has no second level");
            }
            Assert.AreEqual(30, distinctAreas.Count, "route[] should cover 30 distinct areas");

            foreach (WorldArea area in cfg.areas)
            {
                if (area.garrison == null) continue;
                foreach (WorldGarrisonEntry entry in area.garrison)
                    Assert.AreEqual(0, entry.level, $"{area.id} authors a garrison entry at level {entry.level} — World 3 has no second level");
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
            Assert.AreEqual(0, cfg.bridges.Length, "World 3 has no upper levels - no bridges (Lee, 2026-09-30)");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string mapReason), mapReason);
            Assert.IsTrue(MapValidation.Validate(map, out string validateReason), validateReason);
        }

        /// <summary>
        /// MV-1014's own ONE new test (testing policy v2, MV-465): loads World 3 through the real load
        /// path and enters a7 via <see cref="AreaAccumulationDirector.EnterArea"/> — the same call a gate
        /// break fires at runtime — then asserts every robot garrisoned into a7 stands on the floor
        /// (RESOLVED position.y, not the authored JSON) and outside every one of a7's authored cover
        /// boxes. Must fail against the pre-fix config: two of a7's four garrison entries authored
        /// "level": 1 with no deck under them, so <see cref="Garrison.SeedSlots(WorldArea, int, WorldConfig)"/>
        /// resolved their y to <c>dials.deckHeight</c>'s 2.5 m fallback — a robot floating in open air,
        /// nowhere near <c>floorY + archetype.SpawnHeight</c>.
        /// </summary>
        [Test]
        public void World3Area7_EnteredViaTheNormalAreaEntryPath_EveryGarrisonRobotStandsOnTheFloorOutsideCover()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "the shipped world3_config.json failed to load — see the error log above");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string mapReason), mapReason);

            WorldArea a7 = cfg.Area("a7");
            Assert.IsNotNull(a7, "world3_config.json has no area 'a7'");

            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            Camera[] suppressed = CameraTestUtil.SuppressAmbientMainCameras();

            var directorGo = new GameObject("Area Accumulation");
            try
            {
                var director = directorGo.AddComponent<AreaAccumulationDirector>();
                director.ConfigureWorld(cfg, worldIndex: 2); // World3 is index 2 in WorldLibrary.Keys
                director.Configure(map, System.Array.Empty<CoverPiece>()); // fills area 1

                // The normal area-entry path: EnterArea is what a gate break calls at runtime, in order,
                // for every area up to and including a7.
                for (int i = 2; i <= 7; i++) director.EnterArea(i);

                RobotEnemy[] a7Robots = System.Array.FindAll(
                    Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                    r => r != null && r.AreaIndex == 7);

                Assert.Greater(a7Robots.Length, 0, "setup failure: a7 garrisoned no robots — this test proves nothing without a live garrison");

                foreach (RobotEnemy r in a7Robots)
                {
                    Vector3 pos = r.transform.position;

                    // --- resolved floor check: a7 authors no decks under its garrison, so the floor is 0 ---
                    float expectedY = EnemyArchetype.For(r.Kind, cfg).SpawnHeight;
                    Assert.That(pos.y, Is.EqualTo(expectedY).Within(0.2f),
                        $"a7 robot ({r.Kind}) at {pos} is not within 0.2 m of the floor (expected y~{expectedY:0.##}) " +
                        "— MV-1014: a level-1 garrison entry with no deck resolves to the 2.5 m deckHeight fallback");

                    // --- outside every authored cover box ---
                    foreach (WorldCover c in a7.cover)
                    {
                        float minX = c.x - c.width * 0.5f, maxX = c.x + c.width * 0.5f;
                        float minZ = c.z - c.depth * 0.5f, maxZ = c.z + c.depth * 0.5f;
                        bool inside = pos.x >= minX && pos.x <= maxX && pos.z >= minZ && pos.z <= maxZ;
                        Assert.IsFalse(inside, $"a7 robot ({r.Kind}) at {pos} sits inside cover '{c.id}'");
                    }
                }
            }
            finally
            {
                GameObject bodies = GameObject.Find("Area Robots");
                if (bodies != null) Object.DestroyImmediate(bodies);
                Object.DestroyImmediate(directorGo);
                CameraTestUtil.RestoreAmbientMainCameras(suppressed);
                RobotEnemy.ResetRegistry();
                DevTuning.Reset();
            }
        }
    }
}
