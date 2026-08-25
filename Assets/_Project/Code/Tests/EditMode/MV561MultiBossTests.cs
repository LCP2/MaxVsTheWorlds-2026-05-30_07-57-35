using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// WorldArea goes from "one boss, maybe" to "however many are authored" (MV-561): World 1 v4 needs
    /// two bosses in a20 and three in a30, and <see cref="WorldArea.boss"/> can only ever hold one. This
    /// pins the three things that had to change together — the loader builds one entity per authored
    /// boss (not one area-wide fallback), <see cref="MaxWorlds.Arena.MapRuntime"/> actually BUILDS that
    /// many living <see cref="BigBermudaBoss"/> instances rather than moving the scene's single one
    /// three times, and the legacy single-boss shape keeps meaning exactly what it always has — plus the
    /// new separation/wall-margin validation that only a 2+ boss area is subject to.
    /// </summary>
    public sealed class MV561MultiBossTests
    {
        /// <summary>Entry stub → fight room → boss room, gated behind "all-sheds-destroyed" — same
        /// three-area shape <see cref="MV541MultiShedTests"/> and <see cref="WorldMapLoaderTests"/> use.
        /// <paramref name="bossArea"/> supplies whichever boss shape the caller wants to test.</summary>
        private static WorldConfig WorldWithBossArea(WorldArea bossArea)
        {
            bossArea.id = "boss";
            bossArea.role = "boss+exit";
            bossArea.origin = new WorldAreaOrigin { x = -20f, z = 20f };
            bossArea.size = new WorldAreaSize { w = 40f, d = 40f };

            return new WorldConfig
            {
                world = "Test World",
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", role = "entry",
                        origin = new WorldAreaOrigin { x = -2f, z = -6f },
                        size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "a1", role = "normal",
                        origin = new WorldAreaOrigin { x = -20f, z = 0f },
                        size = new WorldAreaSize { w = 40f, d = 20f },
                    },
                    bossArea,
                },
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
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
            };
        }

        /// <summary>Three bosses, each comfortably clear of <see cref="MapValidation.MinBossWallMargin"/>
        /// and <see cref="MapValidation.MinBossSeparation"/> — a real, validating multi-boss world, not
        /// just an isolated area object.</summary>
        private static WorldConfig ThreeBossWorld() => WorldWithBossArea(new WorldArea
        {
            hasShed = false, garrisonDensity = "none",
            bosses = new[]
            {
                new WorldBoss { id = "boss1", x = -10f, z = 30f },
                new WorldBoss { id = "boss2", x = 10f, z = 30f },
                new WorldBoss { id = "boss3", x = 0f, z = 50f },
            },
        });

        private static WorldConfig LegacySingleBossWorld() => WorldWithBossArea(new WorldArea
        {
            hasShed = false, garrisonDensity = "none",
            boss = new WorldBoss { id = "big_bermuda", x = 0f, z = 40f, size = new WorldAreaSize { w = 3.5f, d = 3.5f } },
        });

        [Test]
        public void ThreeAuthoredBosses_BuildThreeLivingInstances_KeepingTheLegacySingleIdUnchanged()
        {
            // --- AC2: an area authoring three bosses builds three real entities, each its own instance
            // — not the scene's single Big Bermuda adopted and moved three times.
            Assert.IsTrue(WorldMapLoader.TryLoad(ThreeBossWorld(), out MapData map, out string reason), reason);

            List<MapEntity> bossEntities = MapValidation.Kind(map, EntityKind.Boss);
            Assert.AreEqual(3, bossEntities.Count, "all three authored bosses should register as boss entities");
            CollectionAssert.AreEquivalent(new[] { "boss1", "boss2", "boss3" },
                bossEntities.ConvertAll(e => e.id), "each boss should keep its own authored id");

            var root = new GameObject("MV-561 Multi-Boss Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.AreEqual(3, built.Bosses.Count, "MapRuntime should have built three bosses");

                var distinctInstances = new HashSet<BigBermudaBoss>();
                foreach (MapEntity e in bossEntities)
                {
                    Assert.IsTrue(built.Actors.TryGetValue(e.id, out GameObject go) && go != null,
                        $"boss '{e.id}' is authored in the map but was never built");

                    var boss = go.GetComponent<BigBermudaBoss>();
                    Assert.IsNotNull(boss, $"'{e.id}' was built without a BigBermudaBoss component");
                    Assert.IsTrue(distinctInstances.Add(boss),
                        $"'{e.id}' shares its BigBermudaBoss instance with another boss entity — three " +
                        "authored bosses must build three LIVING instances, not the same one moved thrice");
                }
                Assert.AreEqual(3, distinctInstances.Count,
                    "three authored bosses must build three distinct BigBermudaBoss instances");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            // --- AC3: the legacy single `boss` object keeps its own authored id exactly as today.
            Assert.IsTrue(WorldMapLoader.TryLoad(LegacySingleBossWorld(), out MapData legacyMap, out string legacyReason), legacyReason);
            MapEntity legacyBoss = legacyMap.First(EntityKind.Boss);
            Assert.IsNotNull(legacyBoss, "the legacy single-boss area built no boss entity");
            Assert.AreEqual("big_bermuda", legacyBoss.id,
                "a legacy single authored boss must keep its own authored id, unchanged by MV-561");

            // --- AC3 (id-less fallback): a boss-role area with no boss id authored at all falls back
            // to "{areaId}_boss" — the new, sane per-area default replacing the old universal
            // "big_bermuda" literal, which would have wrongly named every un-authored boss the same.
            WorldConfig noIdWorld = WorldWithBossArea(new WorldArea { hasShed = false, garrisonDensity = "none" });
            Assert.IsTrue(WorldMapLoader.TryLoad(noIdWorld, out MapData noIdMap, out string noIdReason), noIdReason);
            MapEntity noIdBoss = noIdMap.First(EntityKind.Boss);
            Assert.IsNotNull(noIdBoss, "the id-less boss area built no boss entity");
            Assert.AreEqual("boss_boss", noIdBoss.id,
                "an area with no boss id authored must fall back to '{areaId}_boss', not the old " +
                "hardcoded 'big_bermuda' literal");

            // --- AC4: validation rejects two bosses under the minimum separation, and a boss under the
            // minimum wall margin — naming the area either way.
            WorldConfig tooClose = ThreeBossWorld();
            WorldArea closeArea = tooClose.Area("boss");
            closeArea.bosses[1].x = closeArea.bosses[0].x + 2f; // 2 m apart — well under MinBossSeparation
            closeArea.bosses[1].z = closeArea.bosses[0].z;
            Assert.IsFalse(MapValidation.ValidateWorldConfig(tooClose, out string closeReason),
                "two bosses 2 m apart in the same area must fail validation");
            StringAssert.Contains("boss", closeReason);
            StringAssert.Contains("separation", closeReason);

            WorldConfig againstWall = ThreeBossWorld();
            WorldArea wallArea = againstWall.Area("boss");
            wallArea.bosses[0].x = wallArea.XMin + 1f; // 1 m from the west wall — under MinBossWallMargin
            Assert.IsFalse(MapValidation.ValidateWorldConfig(againstWall, out string wallReason),
                "a boss 1 m from its area's wall must fail validation");
            StringAssert.Contains("boss", wallReason);
            StringAssert.Contains("wall", wallReason);
        }
    }
}
