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

            Assert.AreEqual(20, map.zones.Length, "MV-411: 18 combat areas + entry stub + compost clearing");
            Assert.AreEqual(19, map.links.Length, "MV-411: g0..g17 plus the boss gate bg");
        }

        // --- AreaAccumulationDirector compatibility (MV-270): combat areas must round-trip to the ---
        // --- "area<N>" id convention the ambient-population system still resolves by string parsing. ---

        [Test]
        public void World1_CombatAreasTranslateToTheAreaNIdConvention()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);

            for (int n = 1; n <= 18; n++)
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

            foreach (string shedAreaId in new[] { "a1", "a3", "a6", "a8", "a9", "a11", "a14", "a15", "a17" })
            {
                MapEntity factory = map.Entity($"{shedAreaId}_shed");
                Assert.IsNotNull(factory, $"shed area '{shedAreaId}' has no factory entity");
                Assert.AreEqual(EntityKind.Factory, factory.Kind);
            }
        }

        // --- MV-437: raised world 1 from 6 sheds to 9 so a full run can reach more than 6 of the ---
        // --- 23 Rig abilities (MV-436 made every ability draft-only) and so the opening third of a ---
        // --- run isn't a single-sink DAMAGE grind before the first Morphing Module. ------------------

        [Test]
        public void World1_HasExactlyNineShedsAtTheAuthoredIndices()
        {
            WorldConfig cfg = LoadWorld1();

            var shedIndices = new System.Collections.Generic.List<int>();
            foreach (WorldArea area in cfg.areas)
                if (area.hasShed) shedIndices.Add(area.index);
            shedIndices.Sort();

            CollectionAssert.AreEqual(new[] { 1, 3, 6, 8, 9, 11, 14, 15, 17 }, shedIndices,
                "World 1 must carry exactly 9 shed areas, at indices 1, 3, 6, 8, 9, 11, 14, 15, 17");
        }

        [Test]
        public void World1_EveryShedSitsInsideItsOwnAreasBounds()
        {
            WorldConfig cfg = LoadWorld1();

            foreach (WorldArea area in cfg.areas)
            {
                if (!area.hasShed) continue;
                Assert.IsNotNull(area.shed, $"area '{area.id}' has hasShed=true but no shed object");

                Assert.That(area.shed.x, Is.InRange(area.XMin, area.XMax),
                    $"shed '{area.id}' x={area.shed.x} falls outside its area's [{area.XMin}, {area.XMax}] bounds");
                Assert.That(area.shed.z, Is.InRange(area.ZMin, area.ZMax),
                    $"shed '{area.id}' z={area.shed.z} falls outside its area's [{area.ZMin}, {area.ZMax}] bounds");
            }
        }

        [Test]
        public void World1_TheThreeNewShedAreasHaveTheShedRoleAndAName()
        {
            WorldConfig cfg = LoadWorld1();

            foreach (int index in new[] { 1, 9, 15 })
            {
                WorldArea area = cfg.AreaByIndex(index);
                Assert.IsNotNull(area, $"area at index {index} is missing");
                Assert.AreEqual("shed", area.role, $"area at index {index} must carry role \"shed\"");
                Assert.IsFalse(string.IsNullOrEmpty(area.name), $"area at index {index} has no name");
            }
        }

        [Test]
        public void World1_TheThreeNewShedAreasGarrisonAtLeastOneRobot()
        {
            WorldConfig cfg = LoadWorld1();

            foreach (int index in new[] { 1, 9, 15 })
            {
                DifficultyEngine.Composition composition = cfg.SolveComposition(index);
                Assert.Greater(composition.TotalCount, 0,
                    $"new shed area at index {index} garrisons no robots — it would be an empty arena");
            }
        }

        // --- MV-318: every combat area carries at least one shrub obstacle, and none of them turn ---
        // --- into a sealed room — MapValidation.Cover's ordinary invariants (free channel, spawn ---
        // --- ring, doorway mouth) are what actually enforce "obstructs without fully blocking". -----

        [Test]
        public void World1_EveryCombatAreaHasAShrubberyObstacle()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);

            for (int n = 1; n <= 18; n++)
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

            for (int n = 1; n <= 18; n++)
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
        // --- in the ambient arena population, not only via factory production. Range widened from ---
        // --- Areas 1-4 to the full 1-8 by MV-365: composition is now authored per area as designed ---
        // --- scenarios (a Gunner-pressure room, a Bomber centreDenial room, a Blinker set-piece) ---
        // --- deliberately spread across the whole world rather than clustered early. --------------

        [Test]
        public void World1_GunnerBomberBlinker_AllAppearAcrossTheWorld()
        {
            WorldConfig cfg = LoadWorld1();

            bool sawGunner = false, sawBomber = false, sawBlinker = false;
            for (int area = 1; area <= 18; area++)
            {
                DifficultyEngine.Composition composition = cfg.SolveComposition(area);
                if (composition.Gunner > 0) sawGunner = true;
                if (composition.Bomber > 0) sawBomber = true;
                if (composition.Blinker > 0) sawBlinker = true;
            }

            Assert.IsTrue(sawGunner, "Gunner never appears in Areas 1-18");
            Assert.IsTrue(sawBomber, "Bomber never appears in Areas 1-18");
            Assert.IsTrue(sawBlinker, "Blinker never appears in Areas 1-18");
        }
    }
}
