using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-701: a world's <see cref="WorldConfig.enemyOverrides"/> restats and renames a kind — World 2's
    /// Rusher wears this as "SCRAP RAT" — without touching the base table every other world still reads.
    /// The one new test this ticket adds — fails on base commit 0a80ab1 (MV-687), whose
    /// <c>world2_config.json</c> ships with no <c>enemyOverrides</c> block at all, so
    /// <c>EnemyArchetype.For(Rusher, world2Config)</c> did not exist (no overload took a
    /// <see cref="WorldConfig"/>) and could not have resolved anything but the base table's
    /// MoveSpeed 0.93 / MaxHealth 32 / DisplayName "RUSHER".
    /// </summary>
    public sealed class MV701EnemyOverrideTests
    {
        [Test]
        public void World2RusherOverride_ResolvesScrapRatStats_ButWorld1StaysBase()
        {
            WorldConfig world1 = WorldLibrary.Load(WorldLibrary.World1);
            WorldConfig world2 = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(world1, "world1_config.json failed to load");
            Assert.IsNotNull(world2, "world2_config.json failed to load");

            EnemyArchetype scrapRat = EnemyArchetype.For(EnemyKind.Rusher, world2);
            Assert.AreEqual(2.4f, scrapRat.MoveSpeed, 1e-4f,
                "World 2's rusher override must resolve MoveSpeed 2.4");
            Assert.AreEqual(28f, scrapRat.MaxHealth, 1e-4f,
                "World 2's rusher override must resolve MaxHealth 28");
            Assert.AreEqual("SCRAP RAT", scrapRat.DisplayName,
                "World 2's rusher override must resolve DisplayName SCRAP RAT");

            EnemyArchetype baseRusher = EnemyArchetype.Of(EnemyKind.Rusher);
            EnemyArchetype resolvedForWorld1 = EnemyArchetype.For(EnemyKind.Rusher, world1);
            Assert.AreEqual(baseRusher.MoveSpeed, resolvedForWorld1.MoveSpeed, 1e-4f,
                "World 1 authors no rusher override — must resolve the base table's MoveSpeed");
            Assert.AreEqual(baseRusher.MaxHealth, resolvedForWorld1.MaxHealth, 1e-4f,
                "World 1 authors no rusher override — must resolve the base table's MaxHealth");
            Assert.AreEqual(baseRusher.DisplayName, resolvedForWorld1.DisplayName,
                "World 1 authors no rusher override — must resolve the base table's DisplayName");
        }
    }
}
