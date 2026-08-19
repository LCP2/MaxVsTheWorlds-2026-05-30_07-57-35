using System.Collections.Generic;
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

            foreach (string shedAreaId in new[] { "a3", "a6", "a8", "a9", "a11", "a14", "a15", "a17" })
            {
                MapEntity factory = map.Entity($"{shedAreaId}_shed");
                Assert.IsNotNull(factory, $"shed area '{shedAreaId}' has no factory entity");
                Assert.AreEqual(EntityKind.Factory, factory.Kind);
            }
        }

        // --- MV-437: raised world 1 from 6 sheds to 9 so a full run can reach more than 6 of the ---
        // --- 23 Rig abilities (MV-436 made every ability draft-only) and so the opening third of a ---
        // --- run isn't a single-sink DAMAGE grind before the first Morphing Module. ------------------
        // --- MV-442 (Lee, 2026-08-19): Area 1's shed was reverted — his redraw of a1 has no shed in ---
        // --- it, so world 1 is back to 8 sheds and the first Morphing Module is Area 3 again. --------

        [Test]
        public void World1_HasExactlyEightShedsAtTheAuthoredIndices()
        {
            WorldConfig cfg = LoadWorld1();

            var shedIndices = new System.Collections.Generic.List<int>();
            foreach (WorldArea area in cfg.areas)
                if (area.hasShed) shedIndices.Add(area.index);
            shedIndices.Sort();

            CollectionAssert.AreEqual(new[] { 3, 6, 8, 9, 11, 14, 15, 17 }, shedIndices,
                "World 1 must carry exactly 8 shed areas, at indices 3, 6, 8, 9, 11, 14, 15, 17");
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
        public void World1_TheRemainingNewShedAreasHaveTheShedRoleAndAName()
        {
            WorldConfig cfg = LoadWorld1();

            // MV-442 reverted Area 1's shed (see above) — 9 and 15 are the MV-437 additions still standing.
            foreach (int index in new[] { 9, 15 })
            {
                WorldArea area = cfg.AreaByIndex(index);
                Assert.IsNotNull(area, $"area at index {index} is missing");
                Assert.AreEqual("shed", area.role, $"area at index {index} must carry role \"shed\"");
                Assert.IsFalse(string.IsNullOrEmpty(area.name), $"area at index {index} has no name");
            }
        }

        [Test]
        public void World1_TheRemainingNewShedAreasGarrisonAtLeastOneRobot()
        {
            WorldConfig cfg = LoadWorld1();

            foreach (int index in new[] { 9, 15 })
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

        // --- MV-298: Area 1 is a light opening, not a swarm — 2-3 lowest-tier (Rusher) + a couple ---
        // --- of tanks (Bruiser), not the 9-10 robots the pre-MV-298 pacing produced. MV-442 (Lee's ---
        // --- 2026-08-19 redraw) raised the tank count from 1 to 2 Bruiser. ---------------------------

        [Test]
        public void World1_Area1ComposesToALightOpening_NotASwarm()
        {
            WorldConfig cfg = LoadWorld1();

            DifficultyEngine.Composition composition = cfg.SolveComposition(1);

            Assert.AreEqual(2, composition.Bruiser, "MV-442: Area 1 must hold exactly two tanks (large robots)");
            Assert.AreEqual(0, composition.Heavy, "Heavy is not unlocked this early");
            Assert.AreEqual(0, composition.Brute, "Brute is not unlocked this early");
            Assert.That(composition.Rusher, Is.InRange(2, 3),
                "Area 1 must hold 2-3 lowest-tier (Rusher) robots");
        }

        // --- MV-310: the shipped world1_config.json must actually surface Gunner/Launcher/Blinker ---
        // --- in the ambient arena population, not only via factory production. Range widened from ---
        // --- Areas 1-4 to the full 1-8 by MV-365: composition is now authored per area as designed ---
        // --- scenarios (a Gunner-pressure room, a Launcher centreDenial room, a Blinker set-piece) ---
        // --- deliberately spread across the whole world rather than clustered early. --------------

        [Test]
        public void World1_GunnerLauncherBlinker_AllAppearAcrossTheWorld()
        {
            WorldConfig cfg = LoadWorld1();

            bool sawGunner = false, sawLauncher = false, sawBlinker = false;
            for (int area = 1; area <= 18; area++)
            {
                DifficultyEngine.Composition composition = cfg.SolveComposition(area);
                if (composition.Gunner > 0) sawGunner = true;
                if (composition.Launcher > 0) sawLauncher = true;
                if (composition.Blinker > 0) sawBlinker = true;
            }

            Assert.IsTrue(sawGunner, "Gunner never appears in Areas 1-18");
            Assert.IsTrue(sawLauncher, "Launcher never appears in Areas 1-18");
            Assert.IsTrue(sawBlinker, "Blinker never appears in Areas 1-18");
        }

        // ------------------------------------------------------------------------------------------
        // MV-442: Lee's 2026-08-19 a1-a4 relayout (design-sheet redraw) + MinFreeChannel 6 -> 3 m.
        // ------------------------------------------------------------------------------------------

        // --- AC1 -------------------------------------------------------------------------------------

        [Test]
        public void World1_ValidatesBothAsAWorldConfigAndAsTheBuiltMap()
        {
            WorldConfig cfg = LoadWorld1();
            Assert.IsTrue(MapValidation.ValidateWorldConfig(cfg, out string configReason), configReason);

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string mapReason), mapReason);
        }

        // --- AC2 -------------------------------------------------------------------------------------

        [Test]
        public void World1_Area1IsTheRedrawnCarport_22By24_NoShed()
        {
            WorldArea a1 = LoadWorld1().AreaByIndex(1);
            Assert.IsNotNull(a1, "world1_config.json has no area at index 1");

            Assert.AreEqual(-11f, a1.origin.x, "a1's origin.x moved");
            Assert.AreEqual(0f, a1.origin.z, "a1's origin.z moved");
            Assert.AreEqual(22f, a1.size.w, "a1's width must stay 22 m");
            Assert.AreEqual(24f, a1.size.d, "a1 must have deepened to 24 m (was 18 m)");
            Assert.IsFalse(a1.hasShed, "MV-442 reverses MV-437: a1 must no longer carry a shed");
            Assert.AreEqual("normal", a1.role, "a1's role must revert to \"normal\"");

            // Not a null check on a1.shed: JsonUtility materialises a non-null default WorldShed for
            // EVERY area once any area in the array carries the field (the same round-trip quirk
            // WorldComposition.IsAuthored documents) — hasShed is the real "does this area have a
            // shed" signal, which is what WorldMapLoader itself gates the factory build on.
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);
            Assert.IsNull(map.Entity("a1_shed"), "a1 must build no factory entity once its shed is removed");
        }

        // --- AC4 -------------------------------------------------------------------------------------

        [Test]
        public void World1_GateG1EndpointsBothResolveToZ18Point5()
        {
            WorldConfig cfg = LoadWorld1();

            WorldGate g1 = null;
            foreach (WorldGate g in cfg.gates) if (g.id == "g1") { g1 = g; break; }
            Assert.IsNotNull(g1, "world1_config.json has no gate 'g1'");

            WorldArea a1 = cfg.Area("a1");
            WorldArea a2 = cfg.Area("a2");
            Assert.IsNotNull(a1); Assert.IsNotNull(a2);

            Assert.IsTrue(WallEnums.TryParse(g1.from.wall, out Wall fromWall), "g1.from has an unparseable wall");
            Assert.IsTrue(WallEnums.TryParse(g1.to.wall, out Wall toWall), "g1.to has an unparseable wall");

            Span fromSpan = a1.WallSpan(fromWall);
            Span toSpan = a2.WallSpan(toWall);

            float posFrom = fromSpan.Min + g1.from.pos * fromSpan.Length;
            float posTo = toSpan.Min + g1.to.pos * toSpan.Length;

            Assert.That(posFrom, Is.EqualTo(18.5f).Within(0.05f), "g1's a1-side endpoint does not resolve to z 18.5");
            Assert.That(posTo, Is.EqualTo(18.5f).Within(0.05f), "g1's a2-side endpoint does not resolve to z 18.5");
        }

        // --- AC5 -------------------------------------------------------------------------------------

        [Test]
        public void World1_AreasOneToFour_CarryLeesRedrawnComposition()
        {
            WorldConfig cfg = LoadWorld1();

            AssertComposition(cfg, 1, rusher: 3, bruiser: 2, gunner: 0);
            AssertComposition(cfg, 2, rusher: 2, bruiser: 2, gunner: 2);
            AssertComposition(cfg, 3, rusher: 3, bruiser: 3, gunner: 3);
            AssertComposition(cfg, 4, rusher: 5, bruiser: 0, gunner: 5);
        }

        private static void AssertComposition(WorldConfig cfg, int areaIndex, int rusher, int bruiser, int gunner)
        {
            WorldComposition c = cfg.AreaByIndex(areaIndex)?.composition;
            Assert.IsNotNull(c, $"area {areaIndex} has no authored composition");

            Assert.AreEqual(rusher, c.rusher, $"area {areaIndex}'s authored Rusher count");
            Assert.AreEqual(bruiser, c.bruiser, $"area {areaIndex}'s authored Bruiser count");
            Assert.AreEqual(gunner, c.gunner, $"area {areaIndex}'s authored Gunner count");
            Assert.AreEqual(0, c.heavy, $"area {areaIndex} must not author Heavy");
            Assert.AreEqual(0, c.brute, $"area {areaIndex} must not author Brute");
            Assert.AreEqual(0, c.launcher, $"area {areaIndex} must not author Launcher");
            Assert.AreEqual(0, c.blinker, $"area {areaIndex} must not author Blinker");
        }

        // --- AC6 -------------------------------------------------------------------------------------

        [Test]
        public void World1_AreasOneToFour_CoverStaysInBoundsAndDoesNotOverlap()
        {
            WorldConfig cfg = LoadWorld1();

            foreach (int index in new[] { 1, 2, 3, 4 })
            {
                WorldArea area = cfg.AreaByIndex(index);
                Assert.IsNotNull(area, $"area {index} is missing");

                for (int i = 0; i < area.cover.Length; i++)
                {
                    WorldCover c = area.cover[i];
                    float cMinX = c.x - c.width * 0.5f, cMaxX = c.x + c.width * 0.5f;
                    float cMinZ = c.z - c.depth * 0.5f, cMaxZ = c.z + c.depth * 0.5f;

                    Assert.GreaterOrEqual(cMinX, area.XMin - 0.01f, $"'{c.id}' spills outside {area.id}'s west wall");
                    Assert.LessOrEqual(cMaxX, area.XMax + 0.01f, $"'{c.id}' spills outside {area.id}'s east wall");
                    Assert.GreaterOrEqual(cMinZ, area.ZMin - 0.01f, $"'{c.id}' spills outside {area.id}'s south wall");
                    Assert.LessOrEqual(cMaxZ, area.ZMax + 0.01f, $"'{c.id}' spills outside {area.id}'s north wall");

                    for (int j = i + 1; j < area.cover.Length; j++)
                    {
                        WorldCover o = area.cover[j];
                        float oMinX = o.x - o.width * 0.5f, oMaxX = o.x + o.width * 0.5f;
                        float oMinZ = o.z - o.depth * 0.5f, oMaxZ = o.z + o.depth * 0.5f;

                        bool overlaps = cMinX < oMaxX && cMaxX > oMinX && cMinZ < oMaxZ && cMaxZ > oMinZ;
                        Assert.IsFalse(overlaps, $"'{c.id}' overlaps '{o.id}' in {area.id}");
                    }
                }
            }
        }

        // --- AC7 -------------------------------------------------------------------------------------

        [Test]
        public void World1_Area4TightestChannelPassesAt3AndWouldHaveFailedAt6()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);

            MapZone zone = map.Zone("area4");
            Assert.IsNotNull(zone, "combat area 4 is missing");
            List<MapEntity> cover = MapValidation.Kind(map, EntityKind.Cover);

            float narrowest = float.MaxValue;
            for (float z = zone.ZMin; z <= zone.ZMax; z += 0.5f)
            {
                float gap = MapValidation.FreeChannelAt(zone, cover, z);
                if (gap < narrowest) narrowest = gap;
            }

            Assert.GreaterOrEqual(narrowest, MapValidation.MinFreeChannel,
                $"a4's tightest channel is {narrowest:0.#} m — fails the current MinFreeChannel " +
                $"({MapValidation.MinFreeChannel} m)");
            Assert.Less(narrowest, 6f,
                $"a4's tightest channel is {narrowest:0.#} m — this must be UNDER the old 6 m floor, " +
                "or the MinFreeChannel=3 change is not actually load-bearing for this map");
        }

        // --- AC9: a2_h1/a3_h2 were both trimmed by one cell (Lee, 2026-08-19) to clear a doorway and --
        // --- a shed's spawn ring their raw drawing didn't account for. Computed from the shipped ------
        // --- config/resolved doorway, not hard-coded, so a future redraw that reintroduces either -----
        // --- violation fails loudly here instead of at map-load. -------------------------------------

        [Test]
        public void World1_A2H1ClearsG2SResolvedDoorway_AndA3H2ClearsA3SShedRing()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);

            MapEntity a2h1 = map.Entity("a2_h1");
            Assert.IsNotNull(a2h1, "world1_config.json has no cover 'a2_h1'");
            MapEntity g2 = map.Entity("g2");
            Assert.IsNotNull(g2, "world1_config.json has no gate 'g2'");

            float doorwayGap = a2h1.ToCover().DistanceTo(g2.CenterXz);
            Assert.GreaterOrEqual(doorwayGap, MapValidation.DoorwayClearance,
                $"'a2_h1' sits {doorwayGap:0.##} m from g2's resolved doorway mouth — under the " +
                $"{MapValidation.DoorwayClearance} m DoorwayClearance floor");

            MapEntity a3h2 = map.Entity("a3_h2");
            Assert.IsNotNull(a3h2, "world1_config.json has no cover 'a3_h2'");
            MapEntity a3Shed = map.Entity("a3_shed");
            Assert.IsNotNull(a3Shed, "world1_config.json has no factory 'a3_shed'");

            float shedGap = a3h2.ToCover().DistanceTo(a3Shed.CenterXz);
            float shedFloor = MapValidation.SpawnRadius + MapValidation.SpawnClearance;
            Assert.GreaterOrEqual(shedGap, shedFloor,
                $"'a3_h2' sits {shedGap:0.##} m from a3's shed spawn ring — under the {shedFloor} m " +
                "SpawnRadius+SpawnClearance floor");
        }
    }
}
