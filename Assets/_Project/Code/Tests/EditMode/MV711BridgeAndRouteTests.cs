using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-711: World 3's bridges — a world-level <c>WorldBridge</c> spanning the gap between two areas
    /// (built through the same deck/DeckVisibility path MV-692 gave an area's own deck), a world-level
    /// <c>route[]</c> of VISITS rather than areas (an area id may appear twice, at a different level
    /// each time), and the map validation that refuses a bridge that doesn't land on floor, is too
    /// narrow, or lets a pier pinch the under-route shut — reporting every violation in one pass rather
    /// than stopping at the first (the shape <c>MapValidation.Cover</c> deliberately does NOT follow).
    /// The ONE new test this ticket adds (testing policy v2, MV-465) — five RESOLVED-value checks in one
    /// method, the same "several concerns, one test" shape MV-692/MV-697 used before it. Fails to compile
    /// on 61b2d1f (the commit before this ticket) — <c>WorldBridge</c>, <c>WorldRouteVisit</c>,
    /// <c>WorldConfig.bridges</c>/<c>route</c>, <c>WorldArea.GarrisonForVisit</c> and
    /// <c>DamageRules.Applies(Team, Team, int, int)</c> don't exist there.
    /// </summary>
    public sealed class MV711BridgeAndRouteTests
    {
        /// <summary>Entry stub → a1 (a fight room) → boss, connected by ordinary floor gates exactly like
        /// every other fixture in this suite, PLUS two world-level bridges spanning the gap between a1's
        /// E wall and boss's W wall — a gap because, unlike a gate, a bridge's two walls are never
        /// required to coincide. A full dials/enemyTypes block so this fixture also validates through the
        /// FULL <see cref="WorldConfigLoader"/>, not just <see cref="WorldMapLoader"/>.</summary>
        private static WorldConfig BridgeWorld()
        {
            // a1/boss are 20x20 — under MapValidation.Structure's 18 m fight-room minimum they'd refuse
            // to convert to a real MapData at all, which the DeckVisibility half of this test needs to.
            var stub = new WorldArea
            {
                id = "stub", role = "entry",
                origin = new WorldAreaOrigin { x = 0f, z = -6f }, size = new WorldAreaSize { w = 20f, d = 6f },
            };
            var a1 = new WorldArea
            {
                id = "a1", role = "normal", index = 1,
                origin = new WorldAreaOrigin { x = 0f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
            };
            var boss = new WorldArea
            {
                id = "boss", role = "boss+exit",
                origin = new WorldAreaOrigin { x = 0f, z = 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
            };

            var route = new List<WorldRouteVisit>();
            for (int i = 0; i < 30; i++) route.Add(new WorldRouteVisit { area = $"filler{i}", level = 0 });
            route.Add(new WorldRouteVisit { area = "a7", level = 0 }); // under the bridge
            route.Add(new WorldRouteVisit { area = "a7", level = 1 }); // over it

            return new WorldConfig
            {
                world = "Test World",
                dials = new WorldDials
                {
                    areaCount = 1, baseThreat = 10f, threatGrowth = 1f,
                    band = new WorldBand { up = 0.2f, down = 0.2f },
                    pacingRhythm = new[] { 1f },
                    toughnessCurve = new WorldToughnessCurve(),
                    powerupCadence = 3,
                    deckHeight = 2.5f,
                },
                enemyTypes = new WorldEnemyTypes
                {
                    small = new WorldEnemyTypeEntry { thv = 1f },
                    large = new WorldEnemyTypeEntry { thv = 2f },
                    heavy = new WorldEnemyTypeEntry { thv = 3f },
                    brute = new WorldEnemyTypeEntry { thv = 4f },
                },
                areas = new[] { stub, a1, boss },
                gates = new[]
                {
                    new WorldGate
                    {
                        id = "g0", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                    },
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
                bridges = new[]
                {
                    new WorldBridge
                    {
                        id = "b1", width = 4f, height = 4f,
                        from = new WorldGateEndpoint { area = "a1", wall = "E", pos = 0.2f },
                        to = new WorldGateEndpoint { area = "boss", wall = "W", pos = 0.2f },
                    },
                    new WorldBridge
                    {
                        id = "b2", width = 4f, height = 3.5f,
                        from = new WorldGateEndpoint { area = "a1", wall = "E", pos = 0.7f },
                        to = new WorldGateEndpoint { area = "boss", wall = "W", pos = 0.7f },
                    },
                },
                route = route.ToArray(),
            };
        }

        /// <summary>A valid entry→a1→boss world with THREE deliberately broken bridges, each violating a
        /// different AC5 rule — one bridge per rule, so the aggregated reason must name all three at
        /// once. a1 is only 6 m wide, so a single 1 m pier dropped in its middle leaves at most 2.5 m
        /// clear either side of it — under the 6 m under-route minimum.</summary>
        private static WorldConfig BrokenBridgesWorld()
        {
            var stub = new WorldArea
            {
                id = "stub", role = "entry",
                origin = new WorldAreaOrigin { x = 0f, z = -6f }, size = new WorldAreaSize { w = 6f, d = 6f },
            };
            var a1 = new WorldArea
            {
                id = "a1", role = "normal",
                origin = new WorldAreaOrigin { x = 0f, z = 0f }, size = new WorldAreaSize { w = 6f, d = 20f },
            };
            var boss = new WorldArea
            {
                id = "boss", role = "boss+exit",
                origin = new WorldAreaOrigin { x = 0f, z = 20f }, size = new WorldAreaSize { w = 6f, d = 20f },
            };

            return new WorldConfig
            {
                world = "Broken Bridges",
                areas = new[] { stub, a1, boss },
                gates = new[]
                {
                    new WorldGate
                    {
                        id = "g0", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                    },
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
                bridges = new[]
                {
                    // (a) does not land on floor — from-pos is outside [0, 1].
                    new WorldBridge
                    {
                        id = "bad_end", width = 6f, height = 3f,
                        from = new WorldGateEndpoint { area = "a1", wall = "E", pos = 1.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "W", pos = 0.5f },
                    },
                    // (b) 2 m wide — under MinDoorway.
                    new WorldBridge
                    {
                        id = "narrow", width = 2f, height = 3f,
                        from = new WorldGateEndpoint { area = "a1", wall = "E", pos = 0.3f },
                        to = new WorldGateEndpoint { area = "boss", wall = "W", pos = 0.3f },
                    },
                    // (c) a pier that closes the 6 m under-route channel in a1 (only 6 m wide itself).
                    new WorldBridge
                    {
                        id = "pinched", width = 4f, height = 3f,
                        from = new WorldGateEndpoint { area = "a1", wall = "E", pos = 0.7f },
                        to = new WorldGateEndpoint { area = "boss", wall = "W", pos = 0.7f },
                        piers = new[] { new WorldBridgePier { x = 3f, z = 10f } },
                    },
                },
            };
        }

        [Test]
        public void BridgesAndRoute_RoundTripDeckVisibilityLevelSeparationAndValidation()
        {
            // --- AC1: bridges/route round-trip through the full WorldConfigLoader ---
            WorldConfig cfg = BridgeWorld();
            string json = JsonUtility.ToJson(cfg);
            Assert.IsTrue(WorldConfigLoader.TryLoad(json, out WorldConfig loaded, out string loadReason), loadReason);
            Assert.AreEqual(2, loaded.bridges.Length, "both authored bridges should read back");
            Assert.AreEqual(32, loaded.route.Length, "all 32 authored route visits should read back");

            int a7Visits = 0, a7Level0 = 0, a7Level1 = 0;
            foreach (WorldRouteVisit v in loaded.route)
            {
                if (v.area != "a7") continue;
                a7Visits++;
                if (v.level == 0) a7Level0++;
                if (v.level == 1) a7Level1++;
            }
            Assert.AreEqual(2, a7Visits, "a7 should appear exactly twice in the route");
            Assert.AreEqual(1, a7Level0, "a7's first visit should be level 0 (under the bridge)");
            Assert.AreEqual(1, a7Level1, "a7's second visit should be level 1 (over it)");

            // --- AC2: a bridge deck registers DeckVisibility exactly like an area deck does ---
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string mapReason), mapReason);

            var root = new GameObject("MV711 Bridge Probe Root");
            try
            {
                MapRuntime.Build(map, root.transform);

                DeckVisibility deckVis = null;
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "b1") continue;
                    deckVis = t.GetComponent<DeckVisibility>();
                    break;
                }
                Assert.IsNotNull(deckVis, "bridge 'b1' should have built a deck actor carrying DeckVisibility");

                // b1's resolved footprint is X[0,20] Z[12,16] at 4 m top height (see BridgeWorld()).
                float underAlpha = deckVis.TargetAlphaFor(new Vector3(10f, 1f, 14f));
                float outsideAlpha = deckVis.TargetAlphaFor(new Vector3(10f, 1f, 5f));
                Assert.AreEqual(0.35f, underAlpha, 0.001f, "standing under the bridge should fade it to the underneath alpha");
                Assert.AreEqual(1f, outsideAlpha, 0.001f, "standing clear of the bridge's footprint should read fully opaque");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            // --- AC3: level-separated damage is a pure, testable rule (see IDamageable's own doc comment
            // for why it is not yet wired into every receiver — Max's weapons don't carry his live level). ---
            Assert.IsFalse(DamageRules.Applies(Team.Enemy, Team.Player, 1, 0),
                "a level-1 attacker must not damage a level-0 target sharing the same XZ footprint");
            Assert.IsFalse(DamageRules.Applies(Team.Player, Team.Enemy, 0, 1),
                "and the reverse: a level-0 attacker must not damage a level-1 target");
            Assert.IsTrue(DamageRules.Applies(Team.Enemy, Team.Player, 1, 1),
                "the same two levels must still damage across teams");
            Assert.IsFalse(DamageRules.Applies(Team.Enemy, Team.Enemy, 0, 0),
                "same-team rejection still applies regardless of level");

            // --- AC4: a second visit seeds only its own level's garrison entries ---
            var visitArea = new WorldArea
            {
                id = "a7", origin = new WorldAreaOrigin { x = 0f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                garrison = new[]
                {
                    new WorldGarrisonEntry { kind = "rusher", level = 0, x = 2f, z = 2f },
                    new WorldGarrisonEntry { kind = "rusher", level = 0, x = 4f, z = 4f },
                    new WorldGarrisonEntry { kind = "bolter", level = 1, x = 6f, z = 6f },
                    new WorldGarrisonEntry { kind = "bolter", level = 1, x = 8f, z = 8f },
                    new WorldGarrisonEntry { kind = "bolter", level = 1, x = 10f, z = 10f },
                },
            };
            Garrison.Seed[] firstVisit = Garrison.SeedSlots(visitArea, 0, 0);
            Garrison.Seed[] secondVisit = Garrison.SeedSlots(visitArea, 0, 1);
            Assert.AreEqual(2, firstVisit.Length, "the first visit should seed only the level-0 entries");
            Assert.AreEqual(3, secondVisit.Length, "the second visit should seed only the level-1 entries");

            // --- AC5: MapValidation reports every bridge violation in one pass, each named ---
            Assert.IsFalse(WorldMapLoader.TryLoad(BrokenBridgesWorld(), out _, out string violationReason));
            StringAssert.Contains("does not land on floor", violationReason);
            StringAssert.Contains("wide", violationReason);
            StringAssert.Contains("pinches shut", violationReason);
        }
    }
}
