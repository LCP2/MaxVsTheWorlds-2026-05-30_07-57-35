using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-907: a13 ("Trolley Yard (floor)") and its overlay a15 ("Trolley Yard (deck)") both authored
    /// a deck at local x 0, z 23, w 30, d 3 in world2_config.json — byte-identical to each other since
    /// a15 shares a13's origin/size by definition (MV-697). WorldMapLoader.TryLoad built BOTH as
    /// separate "deck" entities, so MapGeometry.Decks(map) resolved two DeckSlab boxes occupying the
    /// exact same world-space rect at the same height: double the renderers (slab, edge beams,
    /// parapets, posts) for one footprint.
    ///
    /// This is a Tier 2 (resolved value) assertion: it reads the real World 2 config through the real
    /// loader and checks the RESOLVED <see cref="DeckSlab"/> boxes MapGeometry hands the renderer/
    /// collider builders, not an authored constant.
    ///
    /// Fails on 1ffc64b (base commit for this ticket): "MV-907: deck slabs 'a13_deck1' and 'a15_deck1'
    /// resolve to the same world-space rect (centre (287.00, 2.43, 118.50), size (30.00, 0.15, 3.00))
    /// at the same height 2.5 — the deck is being built twice".
    /// </summary>
    public sealed class MV907DeckDuplicateBuildTests
    {
        [Test]
        public void World2Config_BuildsNoTwoDeckSlabsAtTheSameResolvedRectAndHeight()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "world2_config.json failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var decks = MapGeometry.Decks(map);
            Assert.Greater(decks.Count, 0, "setup failure: World 2 must author at least one deck");

            for (int i = 0; i < decks.Count; i++)
            {
                for (int j = i + 1; j < decks.Count; j++)
                {
                    DeckSlab a = decks[i];
                    DeckSlab b = decks[j];

                    bool sameHeight = Mathf.Abs(a.TopY - b.TopY) < 0.01f;
                    bool sameXExtent = Mathf.Abs(a.Center.x - b.Center.x) < 0.01f && Mathf.Abs(a.Size.x - b.Size.x) < 0.01f;
                    bool sameZExtent = Mathf.Abs(a.Center.z - b.Center.z) < 0.01f && Mathf.Abs(a.Size.z - b.Size.z) < 0.01f;

                    Assert.IsFalse(sameHeight && sameXExtent && sameZExtent,
                        $"MV-907: deck slabs '{a.Id}' and '{b.Id}' resolve to the same world-space rect " +
                        $"(centre {a.Center}, size {a.Size}) at the same height {a.TopY} — the deck is being built twice");
                }
            }
        }
    }
}
