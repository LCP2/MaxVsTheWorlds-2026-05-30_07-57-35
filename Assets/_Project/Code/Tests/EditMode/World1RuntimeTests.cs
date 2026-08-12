using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-270: the actual shipped <c>Resources/Worlds/world1_config.json</c> loads through the real
    /// engines (MV-267/268/269) and produces content the live scene can run on — not just a hand-built
    /// fixture proving the schema is sound (that is what <c>WorldConfigLoaderTests</c>/
    /// <c>WorldMapLoaderTests</c> already cover). Loads the real resource file directly so this test
    /// cannot drift out of sync with what actually ships.
    /// </summary>
    public sealed class World1RuntimeTests
    {
        private static WorldConfig LoadWorld1()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "the shipped world1_config.json failed to load — see the error log above");
            return cfg;
        }

        [Test]
        public void World1_LoadsAndConvertsToAPlayableMap()
        {
            WorldConfig cfg = LoadWorld1();

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.IsTrue(MapValidation.Validate(map, out string why), why);

            Assert.AreEqual(10, map.zones.Length, "8 combat areas + entry stub + compost clearing");
            Assert.AreEqual(9, map.links.Length, "g0..g7 plus the boss gate bg");
        }

        // --- AreaAccumulationDirector compatibility (MV-270): combat areas must round-trip to the ---
        // --- "area<N>" id convention the ambient-population system still resolves by string parsing. ---

        [Test]
        public void World1_CombatAreasTranslateToTheAreaNIdConvention()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);

            for (int n = 1; n <= 8; n++)
            {
                MapZone zone = map.Zone($"area{n}");
                Assert.IsNotNull(zone, $"combat area {n} did not translate to the 'area{n}' id");
                Assert.AreEqual(n, AreaAccumulationDirector.AreaIndexOf(zone.id));
            }

            // The entry stub and the boss room must NOT collide with the area<N> convention — both are
            // meant to resolve to index 0 (never ambiently populated, never treated as a combat area).
            Assert.AreEqual(0, AreaAccumulationDirector.AreaIndexOf("stub"));
            Assert.AreEqual(0, AreaAccumulationDirector.AreaIndexOf("boss"));

            MapZone entry = map.Zone("stub");
            Assert.IsNotNull(entry);
            Assert.AreEqual(ZoneKind.Entry, entry.Kind);
        }

        [Test]
        public void World1_ShedAreasBuildAFactoryEntity()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);

            foreach (string shedAreaId in new[] { "a3", "a6", "a8" })
            {
                MapEntity factory = map.Entity($"{shedAreaId}_shed");
                Assert.IsNotNull(factory, $"shed area '{shedAreaId}' has no factory entity");
                Assert.AreEqual(EntityKind.Factory, factory.Kind);
            }
        }

        // --- MV-318: every combat area carries at least one shrub obstacle, and none of them turn ---
        // --- into a sealed room — MapValidation.Cover's ordinary invariants (free channel, spawn ---
        // --- ring, doorway mouth) are what actually enforce "obstructs without fully blocking". -----

        [Test]
        public void World1_EveryCombatAreaHasAShrubberyObstacle()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);

            for (int n = 1; n <= 8; n++)
            {
                MapZone zone = map.Zone($"area{n}");
                Assert.IsNotNull(zone, $"combat area {n} is missing");

                bool hasShrub = false;
                foreach (MapEntity cover in MapValidation.Kind(map, EntityKind.Cover))
                {
                    if (map.ZoneAt(cover.x, cover.z) == zone) { hasShrub = true; break; }
                }
                Assert.IsTrue(hasShrub, $"area {n} ('{zone.id}') has no shrubbery cover placed");
            }

            // The full-map validation already run above covers this, but the point of the ticket is
            // specifically that shrubbery never seals a required path — assert it explicitly rather
            // than relying on it as a side effect of the general Validate() call.
            Assert.IsTrue(MapValidation.Validate(map, out string why), why);
        }

        // --- MV-360: MV-318's shrubbery only lined the edges of each area, where it does nothing — ---
        // --- every combat area now also carries 3-4 rows through its interior, breaking up the open ---
        // --- middle instead of just decorating the walls. ------------------------------------------

        [Test]
        public void World1_EveryCombatAreaHasInteriorShrubRows()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);

            for (int n = 1; n <= 8; n++)
            {
                MapZone zone = map.Zone($"area{n}");
                Assert.IsNotNull(zone, $"combat area {n} is missing");

                int inZone = 0;
                foreach (MapEntity cover in MapValidation.Kind(map, EntityKind.Cover))
                    if (map.ZoneAt(cover.x, cover.z) == zone) inZone++;

                // 1 edge row from MV-318 + the 3-4 interior rows MV-360 adds.
                Assert.GreaterOrEqual(inZone, 4,
                    $"area {n} ('{zone.id}') has only {inZone} cover piece(s) — MV-360 asks for 3-4 " +
                    "rows through the interior on top of the existing edge planting");
            }

            Assert.IsTrue(MapValidation.Validate(map, out string why), why);
        }

        [Test]
        public void World1_BossEntityStandsInTheCompostClearing()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);

            MapEntity boss = map.First(EntityKind.Boss);
            Assert.IsNotNull(boss, "World 1 built no boss entity");

            MapZone zone = map.ZoneAt(boss.x, boss.z);
            Assert.IsNotNull(zone);
            Assert.AreEqual(ZoneKind.Boss, zone.Kind);
        }

        // --- AC2: power-up cadence (<=2 areas) holds across the whole world, using the world's own ---
        // --- authored sources (sheds), not just the minimum PowerupCadence would force. ---

        [Test]
        public void World1_PowerupCadenceNeverExceedsTwoAreas()
        {
            WorldConfig cfg = LoadWorld1();

            var hasSource = new bool[cfg.dials.areaCount];
            for (int i = 0; i < cfg.dials.areaCount; i++)
            {
                WorldArea area = cfg.AreaByIndex(i + 1);
                hasSource[i] = area != null && area.hasShed;
            }

            int longestGap = PowerupCadence.LongestGap(hasSource);
            Assert.LessOrEqual(longestGap, cfg.dials.powerupCadence,
                "World 1's authored sheds leave a longer power-up gap than the powerupCadence dial allows");
        }

        [Test]
        public void World1_EnemyTypesAreCalibratedRealValues_NotThePlaceholderShape()
        {
            WorldConfig cfg = LoadWorld1();

            // Not the LOCKED-at-design-time placeholders (1.0/2.5/4.5/7.0) — MV-270 replaced them with
            // real-stat-derived values (see world1_config.json's own "note" for the derivation). The
            // RATIO stays close to the placeholder shape (that's the point — see
            // World1CalibrationTests), but the magnitude must have actually moved.
            Assert.AreNotEqual(1.0f, cfg.enemyTypes.small.thv);
            Assert.Greater(cfg.enemyTypes.small.thv, 0f);
            Assert.Greater(cfg.enemyTypes.large.thv, cfg.enemyTypes.small.thv);
            Assert.Greater(cfg.enemyTypes.heavy.thv, cfg.enemyTypes.large.thv);
            Assert.Greater(cfg.enemyTypes.brute.thv, cfg.enemyTypes.heavy.thv);
        }

        // --- MV-298: Area 1 is a light opening, not a swarm — 2-3 lowest-tier (Rusher) + exactly ---
        // --- 1 tank (Bruiser), not the 9-10 robots the pre-MV-298 pacing produced. ---

        [Test]
        public void World1_Area1ComposesToALightOpening_NotASwarm()
        {
            WorldConfig cfg = LoadWorld1();

            DifficultyEngine.Composition composition = cfg.SolveComposition(1);

            Assert.AreEqual(1, composition.Bruiser, "Area 1 must hold exactly one tank (large robot)");
            Assert.AreEqual(0, composition.Heavy, "Heavy is not unlocked this early");
            Assert.AreEqual(0, composition.Brute, "Brute is not unlocked this early");
            Assert.That(composition.Rusher, Is.InRange(2, 3),
                "Area 1 must hold 2-3 lowest-tier (Rusher) robots");
        }

        // --- MV-310: the shipped world1_config.json must actually surface Gunner/Bomber/Blinker ---
        // --- in the ambient arena population, not only via factory production. --------------------

        [Test]
        public void World1_GunnerBomberBlinker_AllAppearAcrossTheFirstFewAreas()
        {
            WorldConfig cfg = LoadWorld1();

            bool sawGunner = false, sawBomber = false, sawBlinker = false;
            for (int area = 1; area <= 4; area++)
            {
                DifficultyEngine.Composition composition = cfg.SolveComposition(area);
                if (composition.Gunner > 0) sawGunner = true;
                if (composition.Bomber > 0) sawBomber = true;
                if (composition.Blinker > 0) sawBlinker = true;
            }

            Assert.IsTrue(sawGunner, "Gunner never appears in Areas 1-4");
            Assert.IsTrue(sawBomber, "Bomber never appears in Areas 1-4");
            Assert.IsTrue(sawBlinker, "Blinker never appears in Areas 1-4");
        }
    }
}
