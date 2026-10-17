using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1213: the Reef's walls read as a corridor you see clean over (1.5 m), not the hull interior Lee's
    /// concept frames show — World 2 (MV-771) already made the same 1.5 -> 3.0 m move. Loads the Reef through
    /// the production path, found by its stable <see cref="WorldIds.Reef"/> row rather than a literal catalog
    /// index, and asserts the RESOLVED <see cref="MapData.wallHeight"/> the engine actually loaded, not the
    /// authored JSON constant (testing policy v2, Tier 2).
    /// </summary>
    public sealed class MV1213ReefWallHeightTests
    {
        [Test]
        public void ReefWallHeight_ResolvesTo3m()
        {
            int reefIndex = WorldCatalog.IndexOf(WorldIds.Reef);
            Assert.AreNotEqual(-1, reefIndex, "WorldCatalog carries no row for WorldIds.Reef");

            WorldDefinition reefRow = WorldCatalog.Get(reefIndex);
            WorldConfig cfg = WorldLibrary.Load(reefRow.ConfigKey);
            Assert.IsNotNull(cfg, $"the Reef's own shipped config ('{reefRow.ConfigKey}') failed to load — see the error log above");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            Assert.AreEqual(3f, map.wallHeight, 0.001f,
                "the Reef's resolved wallHeight must be 3 m (MV-1213: 1.5 -> 3.0)");
        }
    }
}
