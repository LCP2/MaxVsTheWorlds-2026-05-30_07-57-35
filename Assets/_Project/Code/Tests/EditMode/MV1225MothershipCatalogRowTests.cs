using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1225 (the one new test, per CC_AUTONOMY's testing policy): World 5, the Mothership, joins
    /// <see cref="WorldCatalog"/> as a fifth row keyed by <see cref="WorldIds.Mothership"/>, not by a
    /// literal index — the ticket's own prevention rule ("keyed by id, not index", World Design
    /// Reference prevention row 4). Asserts only RESOLVED values (Testing Policy v2, Tier 2): the
    /// catalog's own id lookup, THE RIG's real loaded board (<see cref="RigBoard"/>), and the real
    /// world-config -&gt; <see cref="MapData"/> conversion pipeline — never an authored JSON/catalog
    /// constant re-read against itself.
    ///
    /// Fails on base commit 41cac5b (before this ticket): <c>WorldIds</c> has no <c>Mothership</c>
    /// member, so this fails to COMPILE (CS0117) — confirmed via <c>cc-test.bat</c> on
    /// base-plus-test-only:
    /// "error CS0117: 'WorldIds' does not contain a definition for 'Mothership'"
    /// (and the same error again for <c>RigBoardLibrary.ShipResourcePath</c> and
    /// <c>WorldLibrary.Mothership</c>, neither of which exist on that commit either).
    /// </summary>
    public sealed class MV1225MothershipCatalogRowTests
    {
        [TearDown]
        public void TearDown() => RigBoard.ResetForTests();

        [Test]
        public void MothershipRowResolvesByIdToIndex4_WithLightMissileAndAOneAreaPlaceholderConfig()
        {
            int index = WorldCatalog.IndexOf(WorldIds.Mothership);
            Assert.AreEqual(4, index, "the Mothership must resolve to catalog index 4, by its own stable id");

            WorldDefinition row = WorldCatalog.Get(index);
            Assert.AreEqual(WeaponCatalog.PrimaryKind.LightMissile, row.PrimaryWeapon,
                "the Mothership's own PRIMARY must be LIGHT MISSILE");

            // THE RIG's real loaded board (not the raw JSON text) -- the PRIMARY family must carry
            // LIGHT MISSILE's own p_cnt, the same resolved-category read MV1017World3RigBoardTests
            // already uses for World 3's board.
            RigBoard.UseWorld(index);
            string[] primaryIds = RigBoard.AllIds.Where(id => RigBoard.Category(id) == "PRIMARY").ToArray();
            Assert.That(primaryIds, Does.Contain("p_cnt"),
                "the Mothership's own board must carry LIGHT MISSILE's p_cnt in its PRIMARY family");

            // The real config -> MapData conversion pipeline (WorldMapLoader.TryLoad), not the raw
            // cfg.wallHeight field read back against itself -- a resolved value, same idiom
            // MV1013LastWorldFinaleTests/WorldTransitionCoverageTests already use for this world's own
            // door geometry.
            WorldConfig cfg = WorldLibrary.Load(row.ConfigKey);
            Assert.IsNotNull(cfg, "the Mothership's own ConfigKey must load");

            // Exactly one placeholder content area (dials.areaCount == 1) -- the standard entry stub
            // every other world also carries is scaffolding, not the ticket's "one placeholder area".
            int contentAreas = cfg.areas.Count(a => a != null && a.index >= 1);
            Assert.AreEqual(1, contentAreas,
                "the Mothership's placeholder config must carry exactly one content area besides the entry stub");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.AreEqual(3f, map.wallHeight,
                "the Mothership's placeholder area's own resolved wall height must be 3 m");
        }
    }
}
