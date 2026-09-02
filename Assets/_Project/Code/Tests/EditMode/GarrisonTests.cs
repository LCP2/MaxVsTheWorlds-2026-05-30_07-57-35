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
        ///
        /// MV-496 extended this same scan to the shed's spawn ring: <c>ClearOfCover</c> dodged
        /// authored <see cref="WorldArea.cover"/> but never checked a garrison seed against its own
        /// area's shed (<see cref="MapValidation.SpawnRadius"/> + <see cref="MapValidation.SpawnClearance"/>
        /// from <see cref="WorldArea.shed"/>), so a8's heavy-density ring placed seed #8 dead on the
        /// shed's spawn point (0 m clearance) on the shipped config. Same exemption as above.
        ///
        /// MV-655: restricted to <see cref="Garrison.Seed.Kind"/>-null (ring-fallback) slots only. An
        /// AUTHORED slot's cover clearance is now governed by <c>MapValidation</c>'s own per-robot-kind
        /// gap (a Bolter may stand 0.5 m from a hedge; a flat <see cref="MapValidation.SpawnClearance"/>
        /// 0.8 m no longer applies to it) and is already enforced at load time by
        /// <c>MapValidation.ValidateWorldConfig</c> — this test's own job is the RING dodge mechanism,
        /// which never assigns a kind up front, so it still promises the flat clearance every
        /// un-authored slot has always used.
        /// </summary>
        [Test]
        public void SeedPositions_World1_EveryGarrisonSeedClearsItsAreasAuthoredCoverAndShed()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            var violations = new List<string>();

            foreach (WorldArea area in cfg.areas)
            {
                int count = Garrison.SeedCount(area.index, cfg);
                if (count <= 0) continue;

                Garrison.Seed[] slots = Garrison.SeedSlots(area, count);
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].Kind.HasValue) continue; // authored — MapValidation's own rule covers it

                    var point = new Vector2(slots[i].Position.x, slots[i].Position.z);
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

                    if (area.hasShed && area.shed != null)
                    {
                        float toShed = Vector2.Distance(point, new Vector2(area.shed.x, area.shed.z));
                        float needed = MapValidation.SpawnRadius + MapValidation.SpawnClearance;
                        if (toShed < needed)
                            violations.Add($"{area.id} seed#{i} is {toShed:0.###} m from its shed " +
                                           $"(needs {needed} m)");
                    }
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        /// <summary>MV-496. A fixture, not world1: an area whose shed sits exactly on a ring seed's
        /// authored angle (so <c>ClearOfCover</c> must dodge it, not merely happen to avoid it), proving
        /// the shed-avoidance mechanism itself rather than re-proving the cover-dodge MV-459 already
        /// pinned above. Also checks the shed dodge is deterministic, same as the cover dodge.</summary>
        [Test]
        public void SeedPositions_DodgesTheShedsSpawnRing_WhenARingPointWouldLandInsideIt()
        {
            var area = new WorldArea
            {
                id = "shed-fixture", index = 1, role = "normal", garrisonDensity = "light",
                origin = new WorldAreaOrigin { x = 0f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                hasShed = true, shed = new WorldShed { x = 10f, z = 16f }, // dead on seed#1's ring angle (90 deg)
            };

            Vector3[] first = Garrison.SeedPositions(area, 4);
            Vector3[] second = Garrison.SeedPositions(area, 4);

            CollectionAssert.AreEqual(first, second);

            var shedXz = new Vector2(area.shed.x, area.shed.z);
            float needed = MapValidation.SpawnRadius + MapValidation.SpawnClearance;
            foreach (Vector3 p in first)
            {
                float toShed = Vector2.Distance(new Vector2(p.x, p.z), shedXz);
                Assert.GreaterOrEqual(toShed, needed,
                    $"({p.x}, {p.z}) is {toShed:0.###} m from the shed (needs {needed} m)");
            }
        }
    }
}
