using System.Collections.Generic;
using UnityEngine;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Origination — garrison seeding (Confluence MVW 34439170 §6, MV-269), pinned against this
    /// ticket's AC3: garrison seeds authored positions from the area's budget, count scaled by its
    /// garrisonDensity dial.
    /// </summary>
    public sealed class GarrisonTests
    {
        private static WorldConfig FixtureWorld() => new WorldConfig
        {
            world = "Test World",
            dials = new WorldDials
            {
                areaCount = 4,
                baseThreat = 14f,
                threatGrowth = 0.10f,
                band = new WorldBand { up = 0.4f, down = -0.15f },
                pacingRhythm = new[] { 1.0f, 1.05f, 0.9f, 1.1f },
                toughnessCurve = new WorldToughnessCurve
                {
                    heavyFromArea = 10, bruteFromArea = 12, toughSubstitutionPct = 0.25f, tankShareEnd = 0.7f,
                },
                powerupCadence = 2,
            },
            enemyTypes = new WorldEnemyTypes
            {
                small = new WorldEnemyTypeEntry { thv = 1.0f },
                large = new WorldEnemyTypeEntry { thv = 2.5f },
                heavy = new WorldEnemyTypeEntry { thv = 4.5f },
                brute = new WorldEnemyTypeEntry { thv = 7.0f },
            },
            areas = new[]
            {
                new WorldArea
                {
                    id = "a1", index = 1, role = "normal", garrisonDensity = "light",
                    origin = new WorldAreaOrigin { x = 0f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                },
                new WorldArea
                {
                    id = "a3", index = 3, role = "normal", garrisonDensity = "heavy", hasShed = true,
                    origin = new WorldAreaOrigin { x = 0f, z = 40f }, size = new WorldAreaSize { w = 20f, d = 20f },
                },
                new WorldArea
                {
                    id = "a4", index = 4, role = "normal", garrisonDensity = "none",
                    origin = new WorldAreaOrigin { x = 0f, z = 60f }, size = new WorldAreaSize { w = 20f, d = 20f },
                },
            },
            gates = System.Array.Empty<WorldGate>(),
        };

        [Test]
        public void DensityShare_OrdersNoneLightNormalHeavy()
        {
            Assert.Less(Garrison.DensityShare("none"), Garrison.DensityShare("light"));
            Assert.Less(Garrison.DensityShare("light"), Garrison.DensityShare("normal"));
            Assert.Less(Garrison.DensityShare("normal"), Garrison.DensityShare("heavy"));
        }

        [Test]
        public void DensityShare_UnknownOrNullDefaultsToNone()
        {
            Assert.AreEqual(Garrison.NoneShare, Garrison.DensityShare(null));
            Assert.AreEqual(Garrison.NoneShare, Garrison.DensityShare("bogus"));
        }

        [Test]
        public void SeedCount_NoneDensityAreaIsEmptyOnFirstEntry()
        {
            WorldConfig cfg = FixtureWorld();

            Assert.AreEqual(0, Garrison.SeedCount(4, cfg)); // a4, garrisonDensity "none"
        }

        [Test]
        public void SeedCount_HeavyDensityOutseedsLightDensity_ForComparableBudgets()
        {
            WorldConfig cfg = FixtureWorld();

            int light = Garrison.SeedCount(1, cfg);  // a1
            int heavy = Garrison.SeedCount(3, cfg);  // a3, similar target budget to a1

            Assert.Greater(light, 0);
            Assert.Greater(heavy, light);
        }

        [Test]
        public void SeedCount_UnknownAreaIndexIsZero()
        {
            WorldConfig cfg = FixtureWorld();

            Assert.AreEqual(0, Garrison.SeedCount(99, cfg));
        }

        [Test]
        public void SeedPositions_AllPositionsLieWithinTheAreaFootprint()
        {
            WorldConfig cfg = FixtureWorld();
            WorldArea area = cfg.AreaByIndex(3);
            int count = Garrison.SeedCount(3, cfg);

            Vector3[] positions = Garrison.SeedPositions(area, count);

            Assert.AreEqual(count, positions.Length);
            foreach (Vector3 p in positions)
                Assert.IsTrue(area.Footprint.Contains(new Vector2(p.x, p.z)),
                    $"({p.x}, {p.z}) is outside area '{area.id}''s footprint {area.Footprint}");
        }

        [Test]
        public void SeedPositions_IsDeterministic_SameAreaAndCountAlwaysMatch()
        {
            WorldConfig cfg = FixtureWorld();
            WorldArea area = cfg.AreaByIndex(1);

            Vector3[] first = Garrison.SeedPositions(area, 5);
            Vector3[] second = Garrison.SeedPositions(area, 5);

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void SeedPositions_ZeroCount_IsEmpty()
        {
            WorldConfig cfg = FixtureWorld();
            WorldArea area = cfg.AreaByIndex(4);

            Vector3[] positions = Garrison.SeedPositions(area, 0);

            Assert.IsEmpty(positions);
        }

        /// <summary>
        /// MV-459 (redirect, 2026-08-20). Nothing before this checked the garrison's deterministic
        /// ring against the area's own authored <see cref="WorldArea.cover"/> — so a Bruiser could be
        /// (and, on the shipped <c>world1_config.json</c>, WAS) seeded dead inside a hedge row across
        /// ten of world 1's eighteen areas. A robot seeded inside cover is exactly the "stuck on
        /// geometry" case the ticket names: it silently starves <see cref="MaxWorlds.Arena.DeathRunState.TryGrantAreaPart"/>
        /// for that area, because <c>PickupDirector.IsLastBruiserInArea</c> only ever fires once every
        /// Bruiser the area holds has actually been reached and killed. This runs against the real
        /// shipped config (not a fixture) so a future area/cover edit that reintroduces the overlap
        /// fails here instead of shipping quietly. Exempt from future test-culling passes — MV-465
        /// already deleted the only guards on two other known defects and both regressed.
        /// </summary>
        [Test]
        public void SeedPositions_World1_EveryGarrisonSeedClearsItsAreasAuthoredCover()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            var violations = new List<string>();

            foreach (WorldArea area in cfg.areas)
            {
                int count = Garrison.SeedCount(area.index, cfg);
                if (count <= 0) continue;

                Vector3[] positions = Garrison.SeedPositions(area, count);
                for (int i = 0; i < positions.Length; i++)
                {
                    var point = new Vector2(positions[i].x, positions[i].z);
                    foreach (WorldCover c in area.cover)
                    {
                        if (c == null) continue;

                        ArenaCover body = new MapEntity
                        {
                            x = c.x, z = c.z, width = c.width, height = c.height, depth = c.depth, shape = c.shape,
                        }.ToCover();

                        float clearance = body.DistanceTo(point);
                        if (clearance < MapValidation.SpawnClearance)
                            violations.Add($"{area.id} seed#{i} is {clearance:0.###} m from cover '{c.id}' " +
                                           $"(needs {MapValidation.SpawnClearance} m)");
                    }
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }
    }
}
