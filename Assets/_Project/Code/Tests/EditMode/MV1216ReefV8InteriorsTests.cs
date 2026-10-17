using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1216: the Reef's own <c>world3_config.json</c> is replaced byte-for-byte with Lee's approved
    /// V8 layout (17 Oct 2026) — the Reef kit (MV-1215) dressing, the raised 3 m walls (MV-1213) and the
    /// new per-area <c>decor</c>/<c>windows</c>/<c>wallDressing</c> fields, with every footprint, route,
    /// gate, garrison, shed and hull-fill piece left untouched. Fails on the base commit (3aa277f): the
    /// pre-V8 config has no <c>a1_k1</c> cover piece and no <c>wallDressing</c> authored on a1 at all.
    /// Loads the Reef through the production path (<see cref="WorldCatalog.IndexOf(string)"/>,
    /// <see cref="WorldLibrary.Load"/>, <see cref="WorldMapLoader.TryLoad"/>) and asserts RESOLVED values
    /// only (testing policy v2, Tier 2): the built <see cref="MapEntity"/> for a1_k1 and the loaded
    /// <see cref="WorldArea"/> for a1 — never the raw JSON string against itself.
    /// </summary>
    public sealed class MV1216ReefV8InteriorsTests
    {
        [Test]
        public void ReefV8Config_ResolvesA1PillarAndWallDressing()
        {
            int reefIndex = WorldCatalog.IndexOf(WorldIds.Reef);
            Assert.AreNotEqual(-1, reefIndex, "WorldCatalog carries no row for WorldIds.Reef");

            WorldDefinition reefRow = WorldCatalog.Get(reefIndex);
            WorldConfig cfg = WorldLibrary.Load(reefRow.ConfigKey);
            Assert.IsNotNull(cfg, $"the Reef's own shipped config ('{reefRow.ConfigKey}') failed to load — see the error log above");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapEntity pillar = map.Entity("a1_k1");
            Assert.IsNotNull(pillar, "the V8 config's a1_k1 cover piece did not resolve through the production loader");

            Assert.AreEqual(CoverDressing.ReefPillar, pillar.Dressing, "a1_k1 must resolve to the Reef pillar dressing");
            Assert.AreEqual(CoverKind.Solid, pillar.CoverKind, "a1_k1 must resolve to solid cover");
            Assert.AreEqual(3f, pillar.height, 0.001f, "a1_k1 must resolve to height 3 (MV-1213's wall height)");
            Assert.AreEqual(10f, pillar.x, 0.001f, "a1_k1 must be centred at world x=10");
            Assert.AreEqual(60f, pillar.z, 0.001f, "a1_k1 must be centred at world z=60");

            WorldArea a1 = cfg.areas.FirstOrDefault(a => a.id == "a1");
            Assert.IsNotNull(a1, "the Reef's loaded config carries no area a1");
            Assert.AreEqual("racks", a1.wallDressing, "a1 must resolve its V8-authored wallDressing to 'racks'");
        }
    }
}
