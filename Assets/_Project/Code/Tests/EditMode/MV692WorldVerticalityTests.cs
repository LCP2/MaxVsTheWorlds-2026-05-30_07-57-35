using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-692: World 2's Stormdrain floor geometry — <c>sludge[]</c> slow zones, <c>decks[]</c> at
    /// +2.5 m and <c>ramps[]</c> joining them to the floor. The ONE new test this ticket adds (testing
    /// policy v2, MV-465): a synthetic area authoring one of each builds via <see cref="WorldMapLoader"/>
    /// and the RESOLVED geometry — never an authored constant — has the deck's top collider at the
    /// authored height, the ramp's slope spanning floor to deck, and a sampled point inside the sludge
    /// rect reporting the authored speed multiplier from the movement hook
    /// (<see cref="MapGeometry.SpeedMultiplierAt"/>). Fails on 81cc1a9, the commit before this ticket —
    /// <c>WorldSludge</c>/<c>WorldDeck</c>/<c>WorldRamp</c> don't exist there, so this test fails to
    /// compile (CS0246) rather than merely failing an assertion.
    /// </summary>
    public sealed class MV692WorldVerticalityTests
    {
        /// <summary>Entry stub → a normal fight room carrying one sludge rect, one deck and one ramp →
        /// a boss room. Every rect the fight room carries sits area-local, inside its own floor, and the
        /// ramp touches exactly the deck's south edge — the same shape a real Stormdrain sub-zone would
        /// author, just small enough to hand-check.</summary>
        private static WorldConfig VerticalityWorld()
        {
            return new WorldConfig
            {
                world = "Test World",
                dials = new WorldDials { sludgeSpeedMultiplier = 0.6f, deckHeight = 2.5f },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", role = "entry",
                        origin = new WorldAreaOrigin { x = 0f, z = -6f },
                        size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "a1", role = "normal",
                        origin = new WorldAreaOrigin { x = 0f, z = 0f },
                        size = new WorldAreaSize { w = 25f, d = 20f },
                        sludge = new[]
                        {
                            new WorldSludge { id = "sludge1", x = 12f, z = 0f, w = 10f, d = 4f },
                        },
                        decks = new[]
                        {
                            new WorldDeck { id = "deck1", x = 8f, z = 5f, w = 3f, d = 12f, height = 2.5f },
                        },
                        ramps = new[]
                        {
                            new WorldRamp { id = "ramp1", x = 8f, z = 2f, w = 3f, d = 3f },
                        },
                    },
                    new WorldArea
                    {
                        id = "boss", role = "boss+exit",
                        origin = new WorldAreaOrigin { x = 0f, z = 20f },
                        size = new WorldAreaSize { w = 20f, d = 20f },
                    },
                },
                gates = new[]
                {
                    new WorldGate
                    {
                        id = "g0", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.08f },
                    },
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
            };
        }

        [Test]
        public void SludgeDeckAndRamp_ResolveToCorrectGeometry()
        {
            WorldConfig cfg = VerticalityWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            List<DeckSlab> decks = MapGeometry.Decks(map);
            Assert.AreEqual(1, decks.Count, "expected exactly one deck to reach the built map");
            DeckSlab deck = decks[0];
            Assert.AreEqual("deck1", deck.Id);
            Assert.AreEqual(2.5f, deck.TopY, 0.01f, "the deck's top collider should sit at its authored 2.5 m height");

            List<RampSlab> ramps = MapGeometry.Ramps(map);
            Assert.AreEqual(1, ramps.Count, "expected exactly one ramp to reach the built map");
            RampSlab ramp = ramps[0];
            Assert.AreEqual("ramp1", ramp.Id);
            Assert.AreEqual(0f, ramp.BottomCenter.y, 0.01f, "a ramp's bottom must sit on the floor");
            Assert.AreEqual(2.5f, ramp.TopCenter.y, 0.01f, "a ramp's top must reach the deck it climbs to");

            // A point inside the authored sludge rect (world x:[12,22], z:[0,4]).
            float insideSludge = MapGeometry.SpeedMultiplierAt(map, 17f, 2f);
            Assert.AreEqual(0.6f, insideSludge, 0.001f,
                "a point sampled inside the sludge rect should report the world's sludge speed multiplier");

            // A point outside every sludge rect must report no slow at all.
            float outsideSludge = MapGeometry.SpeedMultiplierAt(map, 1f, 1f);
            Assert.AreEqual(1f, outsideSludge, 0.001f, "a point outside every sludge rect should report no slow");
        }
    }
}
