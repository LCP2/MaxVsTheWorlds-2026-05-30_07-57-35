using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-837: <see cref="MapGeometry.SpeedMultiplierAt"/> tested only X/Z and ignored Y, so anyone
    /// standing on a deck built ABOVE a sludge rect read the sludge slow anyway — a19's decks cross a3's
    /// sludge channel (a19 overlays a3's footprint). Fails on 98f55d0 (the commit before this ticket):
    /// <c>MapGeometry.SpeedMultiplierAt</c> there takes exactly two float args (x, z), so this file does
    /// not compile (CS1501 "no overload for method 'SpeedMultiplierAt' takes 3 arguments") before a
    /// single assertion runs.
    ///
    /// Loads the real, shipped World 2 config through <see cref="WorldLibrary"/>/<see cref="WorldMapLoader"/>
    /// (no hand-authored fixture) and reads RESOLVED <see cref="MapSlowZones.Instance"/> values at each
    /// deck's own rect centre, never an authored constant.
    ///
    /// Retargeted by MV-852 (World 2 re-layout): Gantry Run's deck (local z9-12) no longer sits above
    /// its own sludge channel (local z2-6 — the ticket's own numbers put them on separate bands of the
    /// same floor, not stacked) — so Gantry Run no longer has a scenario to prove "deck above sludge
    /// reads full speed, floor below still slows" against. It's kept only for "the sludge channel itself
    /// still slows on the floor" and a3's deck overlay (untouched by that ticket) keeps carrying the
    /// actual deck-shields-sludge proof.
    ///
    /// Retargeted again by MV-865 (World 2 re-author, renumbered areas in play order): Gantry Run is now
    /// area a16 (was a14), and a3's deck overlay is now a17 (was a19) — a3 itself is unchanged.
    /// </summary>
    public sealed class MV837DeckSludgeTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            EnemyNavigation.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void DeckAboveSludge_ReadsFullSpeed_FloorBelowStillSlows()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            GameObject pathGo = null;
            try
            {
                // MapSlowZones resolves the map through a live BackyardPath (via EnemyNavigation.Map),
                // not through the local `map` this test already has — same stub MV836FloodOffTests
                // installs for the same reason.
                pathGo = new GameObject("MV837-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                // === a16 Gantry Run (MV-852 re-layout, renumbered a14->a16 by MV-865): the deck and the
                // sludge channel now sit on separate bands of the same floor, not stacked — the deck
                // itself reads full speed, and the sludge channel (elsewhere on that same floor) still
                // slows on its own. ===
                WorldArea a16 = cfg.Area("a16");
                Assert.IsNotNull(a16, "setup failure: World 2 must author area 'a16'");
                WorldDeck a16Deck1 = Array.Find(a16.decks, d => d.id == "a16_deck1");
                WorldSludge a16Sludge1 = Array.Find(a16.sludge, s => s.id == "a16_sludge1");
                Assert.IsNotNull(a16Deck1, "setup failure: a16 must author deck 'a16_deck1'");
                Assert.IsNotNull(a16Sludge1, "setup failure: a16 must author sludge 'a16_sludge1'");
                Rect a16DeckRect = a16.WorldRectOf(a16Deck1.x, a16Deck1.z, a16Deck1.w, a16Deck1.d);
                Rect a16SludgeRect = a16.WorldRectOf(a16Sludge1.x, a16Sludge1.z, a16Sludge1.w, a16Sludge1.d);
                Assert.IsFalse(a16DeckRect.Overlaps(a16SludgeRect),
                    "setup failure: a16's deck and sludge must NOT overlap post-MV-852, or this proves nothing new");

                Assert.AreEqual(1f,
                    MapSlowZones.Instance.SpeedMultiplierAt(new Vector3(a16DeckRect.center.x, map.deckHeight, a16DeckRect.center.y)),
                    0.001f, "MV-837: standing on a16_deck1 must read full speed");
                Assert.AreEqual(0.6f,
                    MapSlowZones.Instance.SpeedMultiplierAt(new Vector3(a16SludgeRect.center.x, 0f, a16SludgeRect.center.y)),
                    0.001f, "MV-837: a16's sludge channel must still slow on the floor");

                // === a17's decks cross a3's sludge channel (a17 is a3's deck-level overlay, renumbered
                // from a19 by MV-865). ===
                WorldArea a3 = cfg.Area("a3");
                WorldArea a17 = cfg.Area("a17");
                Assert.IsNotNull(a3, "setup failure: World 2 must author area 'a3'");
                Assert.IsNotNull(a17, "setup failure: World 2 must author area 'a17'");
                WorldSludge a3Sludge1 = Array.Find(a3.sludge, s => s.id == "a3_sludge1");
                WorldDeck a17Deck1 = Array.Find(a17.decks, d => d.id == "a17_deck1");
                Assert.IsNotNull(a3Sludge1, "setup failure: a3 must author sludge 'a3_sludge1'");
                Assert.IsNotNull(a17Deck1, "setup failure: a17 must author deck 'a17_deck1'");

                Rect a3SludgeRect = a3.WorldRectOf(a3Sludge1.x, a3Sludge1.z, a3Sludge1.w, a3Sludge1.d);
                Rect a17DeckRect = a17.WorldRectOf(a17Deck1.x, a17Deck1.z, a17Deck1.w, a17Deck1.d);

                // A point inside BOTH rects: the deck's own centre X (narrow enough to lie wholly inside
                // the sludge's X span) crossed with the sludge's own centre Z (narrow enough to lie
                // wholly inside the deck's Z span).
                var overlap = new Vector2(a17DeckRect.center.x, a3SludgeRect.center.y);
                Assert.IsTrue(a3SludgeRect.Contains(overlap), "setup failure: the probe point must fall inside a3's sludge rect");
                Assert.IsTrue(a17DeckRect.Contains(overlap),
                    "setup failure: the probe point must fall inside a17's deck rect, or this proves nothing about a deck crossing sludge");

                Assert.AreEqual(1f,
                    MapSlowZones.Instance.SpeedMultiplierAt(new Vector3(overlap.x, map.deckHeight, overlap.y)),
                    0.001f, "MV-837: standing on a17_deck1 above a3's sludge channel must read full speed");
                Assert.AreEqual(0.6f,
                    MapSlowZones.Instance.SpeedMultiplierAt(new Vector3(overlap.x, 0f, overlap.y)),
                    0.001f, "MV-837: the same XZ at floor level must still slow");
            }
            finally
            {
                if (pathGo != null) UnityEngine.Object.DestroyImmediate(pathGo);
            }
        }
    }
}
