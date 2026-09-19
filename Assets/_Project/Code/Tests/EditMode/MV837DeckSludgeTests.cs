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
    /// Retargeted by MV-852 (World 2 re-layout): a14 Gantry Run's deck (<c>a14_deck1</c>, local z9-12)
    /// no longer sits above its own sludge channel (<c>a14_sludge1</c>, local z2-6 — the ticket's own
    /// numbers put them on separate bands of the same floor, not stacked) — so a14 no longer has a
    /// scenario to prove "deck above sludge reads full speed, floor below still slows" against. It's
    /// kept only for "the sludge channel itself still slows on the floor" and a19/a3 (untouched by this
    /// ticket) keeps carrying the actual deck-shields-sludge proof.
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

                // === a14 Gantry Run (MV-852 re-layout): the deck and the sludge channel now sit on
                // separate bands of the same floor, not stacked — the deck itself reads full speed, and
                // the sludge channel (elsewhere on that same floor) still slows on its own. ===
                WorldArea a14 = cfg.Area("a14");
                Assert.IsNotNull(a14, "setup failure: World 2 must author area 'a14'");
                WorldDeck a14Deck1 = Array.Find(a14.decks, d => d.id == "a14_deck1");
                WorldSludge a14Sludge1 = Array.Find(a14.sludge, s => s.id == "a14_sludge1");
                Assert.IsNotNull(a14Deck1, "setup failure: a14 must author deck 'a14_deck1'");
                Assert.IsNotNull(a14Sludge1, "setup failure: a14 must author sludge 'a14_sludge1'");
                Rect a14DeckRect = a14.WorldRectOf(a14Deck1.x, a14Deck1.z, a14Deck1.w, a14Deck1.d);
                Rect a14SludgeRect = a14.WorldRectOf(a14Sludge1.x, a14Sludge1.z, a14Sludge1.w, a14Sludge1.d);
                Assert.IsFalse(a14DeckRect.Overlaps(a14SludgeRect),
                    "setup failure: a14's deck and sludge must NOT overlap post-MV-852, or this proves nothing new");

                Assert.AreEqual(1f,
                    MapSlowZones.Instance.SpeedMultiplierAt(new Vector3(a14DeckRect.center.x, map.deckHeight, a14DeckRect.center.y)),
                    0.001f, "MV-837: standing on a14_deck1 must read full speed");
                Assert.AreEqual(0.6f,
                    MapSlowZones.Instance.SpeedMultiplierAt(new Vector3(a14SludgeRect.center.x, 0f, a14SludgeRect.center.y)),
                    0.001f, "MV-837: a14's sludge channel must still slow on the floor");

                // === a19's decks cross a3's sludge channel (a19 is a3's deck-level overlay). ===
                WorldArea a3 = cfg.Area("a3");
                WorldArea a19 = cfg.Area("a19");
                Assert.IsNotNull(a3, "setup failure: World 2 must author area 'a3'");
                Assert.IsNotNull(a19, "setup failure: World 2 must author area 'a19'");
                WorldSludge a3Sludge1 = Array.Find(a3.sludge, s => s.id == "a3_sludge1");
                WorldDeck a19Deck1 = Array.Find(a19.decks, d => d.id == "a19_deck1");
                Assert.IsNotNull(a3Sludge1, "setup failure: a3 must author sludge 'a3_sludge1'");
                Assert.IsNotNull(a19Deck1, "setup failure: a19 must author deck 'a19_deck1'");

                Rect a3SludgeRect = a3.WorldRectOf(a3Sludge1.x, a3Sludge1.z, a3Sludge1.w, a3Sludge1.d);
                Rect a19DeckRect = a19.WorldRectOf(a19Deck1.x, a19Deck1.z, a19Deck1.w, a19Deck1.d);

                // A point inside BOTH rects: the deck's own centre X (narrow enough to lie wholly inside
                // the sludge's X span) crossed with the sludge's own centre Z (narrow enough to lie
                // wholly inside the deck's Z span).
                var overlap = new Vector2(a19DeckRect.center.x, a3SludgeRect.center.y);
                Assert.IsTrue(a3SludgeRect.Contains(overlap), "setup failure: the probe point must fall inside a3's sludge rect");
                Assert.IsTrue(a19DeckRect.Contains(overlap),
                    "setup failure: the probe point must fall inside a19's deck rect, or this proves nothing about a deck crossing sludge");

                Assert.AreEqual(1f,
                    MapSlowZones.Instance.SpeedMultiplierAt(new Vector3(overlap.x, map.deckHeight, overlap.y)),
                    0.001f, "MV-837: standing on a19_deck1 above a3's sludge channel must read full speed");
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
