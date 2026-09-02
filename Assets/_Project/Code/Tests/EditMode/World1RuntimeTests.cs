using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
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

            // MV-564: v4's redraw is 30 combat areas + the entry stub, one MapZone per authored area
            // (a boss-role area is one zone for its whole footprint, not a combat room plus a separate
            // boss clearing the way MV-411's 18-area world built it) and one MapLink per authored gate.
            Assert.AreEqual(31, map.zones.Length, "MV-564: 30 combat areas + entry stub");
            Assert.AreEqual(30, map.links.Length, "MV-564: g0..g29");
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
            WorldConfig cfg = LoadWorld1();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            // MV-564: v4 authors sheds[] (0-2+ per area, e.g. a9/a14/a15 carry two), so an area's shed
            // entity id is only the legacy singular "{id}_shed" when it authors exactly one — otherwise
            // it's "{id}_shed1"/"{id}_shed2" (WorldArea.ShedId). Resolve every authored shed's real id
            // rather than a hard-coded pre-v4 list of single-shed areas.
            int shedCount = 0;
            foreach (WorldArea a in cfg.areas)
            {
                WorldShed[] sheds = a.Sheds();
                for (int i = 0; i < sheds.Length; i++)
                {
                    string shedId = a.ShedId(i, sheds.Length);
                    MapEntity factory = map.Entity(shedId);
                    Assert.IsNotNull(factory, $"authored shed '{shedId}' has no factory entity");
                    Assert.AreEqual(EntityKind.Factory, factory.Kind);
                    shedCount++;
                }
            }

            Assert.AreEqual(17, shedCount, "World 1 must carry exactly 17 authored sheds");
        }

        // --- MV-437: raised world 1 from 6 sheds to 9 so a full run can reach more than 6 of the ---
        // --- 23 Rig abilities (MV-436 made every ability draft-only) and so the opening third of a ---
        // --- run isn't a single-sink DAMAGE grind before the first Morphing Module. ------------------
        // --- MV-442 (Lee, 2026-08-19): Area 1's shed was reverted — his redraw of a1 has no shed in ---
        // --- it, so world 1 is back to 8 sheds and the first Morphing Module is Area 3 again. --------
        // --- MV-564: v4's 30-area redraw raises world 1 to 37 sheds across 21 areas, most from a10 ---
        // --- carrying two or more (a18/a20/a27 carry three each). ------------------------------------
        // --- MV-639: V12's batch-1 redraw (2026-09-01) moves a8's shed off, dropping the index list's
        // --- 8 to 7 and the total to 32. -------------------------------------------------------------
        // --- MV-641: V12c removes every shed the design sheet does not draw (a6, a9 x2, a10 x2, plus
        // --- one each in a12/a14/a15/a20/a22/a23/a25/a29) and caps every remaining shed area at one,
        // --- dropping the index list to 17 areas and the total to 17 sheds. -------------------------

        [Test]
        public void World1_HasExactlyThirtySevenShedsAtTheAuthoredIndices()
        {
            WorldConfig cfg = LoadWorld1();

            var shedIndices = new System.Collections.Generic.List<int>();
            foreach (WorldArea area in cfg.areas)
                if (area.hasShed) shedIndices.Add(area.index);
            shedIndices.Sort();

            CollectionAssert.AreEqual(
                new[] { 3, 7, 11, 12, 15, 16, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 29 },
                shedIndices,
                "World 1 must carry shed areas at indices 3, 7, 11, 12, 15, 16, 18, 19, 20, 21, 22, " +
                "23, 24, 25, 26, 27, 29 (17 areas, 17 sheds total)");

            int shedTotal = 0;
            foreach (WorldArea area in cfg.areas) shedTotal += area.Sheds().Length;
            Assert.AreEqual(17, shedTotal, "World 1 must carry exactly 17 sheds in total");
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

        [Test]
        public void World1_BossBodyResolvesToTheAuthoredSize()
        {
            // MV-650: authored boss size scaled to 75% (6x6 -> 4.5x4.5), now that MV-613 makes the rig
            // actually follow authored size. Asserts the RESOLVED, constructed body — not the JSON —
            // because a config edit that never reached MapRuntime.BuildBoss would pass a JSON-only
            // assertion while the scene still spawned a mis-sized cube.
            Assert.IsTrue(WorldMapLoader.TryLoad(LoadWorld1(), out MapData map, out string reason), reason);

            var root = new GameObject("MV-621 Boss Size Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(built.Actors.TryGetValue("a12_boss1", out GameObject boss) && boss != null,
                    "world1_config.json's 'a12_boss1' was not built");

                Assert.AreEqual(4.5f, boss.transform.localScale.x, 1e-4f,
                    "a12_boss1's built width must match its authored 4.5 m size");
                Assert.AreEqual(4.5f, boss.transform.localScale.z, 1e-4f,
                    "a12_boss1's built depth must match its authored 4.5 m size");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // --- MV-644: power-up cadence (<=2 areas), proven against the RUNTIME-BUILT map, not just ---
        // --- the authored sheds — a shed-free area at the cadence limit must carry a placed parts- ---
        // --- cache pickup (PowerupCadence.EnsureCoverage, wired into WorldMapLoader), since the ---
        // --- authored sheds alone leave two legitimate 3-area shed-free stretches (a4-a5-a6, a8-a9-a10). ---
        // --- Supersedes the old World1_PowerupCadenceNeverExceedsTwoAreas, which only ever asserted ---
        // --- the authored sheds' own gap — a constraint on the level designer, not a system guarantee. ---

        [Test]
        public void World1_RuntimePickupCoverageNeverExceedsTheCadenceDial()
        {
            WorldConfig cfg = LoadWorld1();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            int areaCount = cfg.dials.areaCount;
            var hasCoverage = new bool[areaCount];
            for (int i = 0; i < areaCount; i++)
            {
                WorldArea area = cfg.AreaByIndex(i + 1);
                Assert.IsNotNull(area, $"world1_config.json has no area at index {i + 1}");

                MapZone zone = map.Zone($"area{i + 1}");
                Assert.IsNotNull(zone, $"combat area {i + 1} did not translate to the 'area{i + 1}' id");

                bool hasCache = false;
                foreach (MapEntity pickup in MapValidation.Kind(map, EntityKind.Pickup))
                {
                    if (map.ZoneAt(pickup.x, pickup.z) == zone) { hasCache = true; break; }
                }

                hasCoverage[i] = area.hasShed || hasCache;
            }

            int longestGap = PowerupCadence.LongestGap(hasCoverage);
            Assert.LessOrEqual(longestGap, 2,
                $"World 1's runtime-built map (sheds + placed parts caches) leaves a gap of " +
                $"{longestGap} consecutive areas with no power-up source — over the powerupCadence=2 guarantee");
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
            // MV-540: Bruiser health cut 135 -> 68 re-derives large.thv the same way MV-512 did
            // (2.81 -> 1.41, scaled proportionally to the health change).
            Assert.AreEqual(1.41f, cfg.enemyTypes.large.thv, 0.01f, "MV-540: large.thv must be re-derived for the Bruiser health cut");
        }

        // --- SUPERSEDED: MV-298/MV-442's "2 Bruiser tanks in Area 1" call is withdrawn by MV-564's ---
        // v4 redraw — a1 is now 4 Rusher and deliberately holds zero Bruiser. World1_Area1Composes-
        // ToALightOpening_NotASwarm removed with it; see MV365ArenaCompositionTests.
        // World1_Area1HasExactlyFourRobots for the current authored shape.

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

            // MV-568: World 1 rotated 90° to run +X (left to right) instead of +Z — every area's
            // origin moved, and each area's authored width/depth swapped along with the axis of travel.
            Assert.AreEqual(5f, a1.origin.x, "a1's origin.x moved");
            Assert.AreEqual(140f, a1.origin.z, "a1's origin.z moved");
            Assert.AreEqual(24f, a1.size.w, "a1's width must stay 24 m (was 22 m pre-rotation)");
            Assert.AreEqual(22f, a1.size.d, "a1 must have shallowed to 22 m (was 24 m pre-rotation)");
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
        public void World1_GateG1EndpointsBothResolveToX23Point5()
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

            // MV-568: the rotation swapped g1 from an E/W gate (resolving along Z) to a N/S gate
            // (resolving along X) — the 11 N/S gates became the 11 E/W gates that carry the run.
            Assert.That(posFrom, Is.EqualTo(23.5f).Within(0.05f), "g1's a1-side endpoint does not resolve to x 23.5");
            Assert.That(posTo, Is.EqualTo(23.5f).Within(0.05f), "g1's a2-side endpoint does not resolve to x 23.5");
        }

        // --- MV-640: the designer's a1-a30 drawing authors an exact position for every composed ------
        // --- robot — before this ticket, 14 of the 30 areas authored fewer garrison entries than -------
        // --- their composition, so some of each area's robots fell back to an unauthored ring spot. ---
        // --- MV-641: V12c's composition edits (a2/a3/a5/a9 robot counts) drop the total to 687. -------
        // --- MV-655: V12d's redraw (cover/shed repositioning under the new per-robot cover gap) -------
        // --- drops the total to 659. ---------------------------------------------------------------

        [Test]
        public void World1_EveryAreasGarrisonCountMatchesItsComposedTotal()
        {
            WorldConfig cfg = LoadWorld1();
            Assert.IsTrue(MapValidation.ValidateWorldConfig(cfg, out string reason), reason);

            int totalGarrisoned = 0;
            for (int index = 1; index <= 30; index++)
            {
                WorldArea area = cfg.AreaByIndex(index);
                Assert.IsNotNull(area, $"world1_config.json has no area at index {index}");

                WorldComposition c = area.composition;
                Assert.IsNotNull(c, $"area {area.id} has no authored composition");
                int composed = c.rusher + c.bruiser + c.heavy + c.brute + c.gunner + c.launcher + c.blinker + c.bolter;
                int garrisoned = area.garrison?.Length ?? 0;

                Assert.AreEqual(composed, garrisoned,
                    $"area {area.id} authors {composed} robots via composition but only {garrisoned} garrison position(s)");
                totalGarrisoned += garrisoned;
            }

            Assert.AreEqual(659, totalGarrisoned, "world1_config.json must author exactly 659 garrison positions across a1..a30");
        }

        // --- AC5 (MV-564: v4's 30-area redraw re-authored areas 1-4's composition) --------------------

        [Test]
        public void World1_AreasOneToFive_CarryLeesRedrawnComposition()
        {
            WorldConfig cfg = LoadWorld1();

            // MV-568: the designer's final composition edit dropped a2's blinkers 4 -> 2, moved a3
            // from 3 rusher/2 gunner to 5 rusher/0 gunner (bolter unchanged at 2), and dropped a5's
            // bolters 4 -> 3. MV-598's v9 redraw then raised a3's bolters 2 -> 4, a4's gunners 5 -> 6,
            // and dropped a5's rusher 4 -> 2 while raising its bolters 3 -> 6. MV-639's V12 redraw
            // (2026-09-01) then raised a2's rusher 4 -> 10 and blinker 2 -> 4. MV-640's V12b full
            // redraw then raised a3's bolters 4 -> 6. MV-641's V12c edits then dropped a2's rusher
            // 10 -> 7, a3's bolters 6 -> 5, and a5's bolters 6 -> 4 (a4's counts are unchanged — only
            // its gunner/rusher positions were rearranged).
            AssertComposition(cfg, 1, rusher: 4, bruiser: 0, gunner: 0, blinker: 0, bolter: 0);
            AssertComposition(cfg, 2, rusher: 7, bruiser: 0, gunner: 0, blinker: 4, bolter: 0);
            AssertComposition(cfg, 3, rusher: 5, bruiser: 0, gunner: 0, blinker: 0, bolter: 5);
            AssertComposition(cfg, 4, rusher: 5, bruiser: 0, gunner: 6, blinker: 0, bolter: 0);
            AssertComposition(cfg, 5, rusher: 2, bruiser: 0, gunner: 0, blinker: 0, bolter: 4);
        }

        private static void AssertComposition(WorldConfig cfg, int areaIndex, int rusher, int bruiser, int gunner, int blinker, int bolter)
        {
            WorldComposition c = cfg.AreaByIndex(areaIndex)?.composition;
            Assert.IsNotNull(c, $"area {areaIndex} has no authored composition");

            Assert.AreEqual(rusher, c.rusher, $"area {areaIndex}'s authored Rusher count");
            Assert.AreEqual(bruiser, c.bruiser, $"area {areaIndex}'s authored Bruiser count");
            Assert.AreEqual(gunner, c.gunner, $"area {areaIndex}'s authored Gunner count");
            Assert.AreEqual(blinker, c.blinker, $"area {areaIndex}'s authored Blinker count");
            Assert.AreEqual(bolter, c.bolter, $"area {areaIndex}'s authored Bolter count");
            Assert.AreEqual(0, c.heavy, $"area {areaIndex} must not author Heavy");
            Assert.AreEqual(0, c.brute, $"area {areaIndex} must not author Brute");
            Assert.AreEqual(0, c.launcher, $"area {areaIndex} must not author Launcher");
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

        // MV-490 replaced a4's cover (design pass measured narrowest channel 6.0 m), superseding the
        // MV-442 geometry this test originally asserted was under the old 6 m floor. The MinFreeChannel=3
        // floor is still the binding invariant; the "was under 6" historical claim no longer applies.
        [Test]
        public void World1_Area4TightestChannelPassesAt3()
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

        // --- MV-500: JsonUtility.FromJson silently drops any composition key that doesn't match a ---
        // --- WorldComposition field — no error, no warning, the authored robots just never spawn. ---
        // --- That is exactly how world1_config.json lost 15 robots to a stray "bomber" key (fixed ---
        // --- for EnemyKind.Bomber -> Launcher by MV-451, 5c6f938, before this ticket landed). This ---
        // --- reads the raw JSON text directly (bypassing JsonUtility) so a future typo'd or renamed ---
        // --- enemy key fails the build instead of vanishing. -----------------------------------------

        private static readonly Regex CompositionBlockPattern =
            new Regex("\"composition\"\\s*:\\s*\\{([^}]*)\\}", RegexOptions.Compiled);
        private static readonly Regex CompositionKeyPattern =
            new Regex("\"(\\w+)\"\\s*:", RegexOptions.Compiled);
        private static readonly HashSet<string> KnownCompositionFields = new HashSet<string>
        {
            "rusher", "bruiser", "heavy", "brute", "gunner", "launcher", "blinker", "bolter"
        };

        private static bool TryFindUnknownCompositionKey(string json, out string badKey)
        {
            foreach (Match block in CompositionBlockPattern.Matches(json))
            {
                foreach (Match keyMatch in CompositionKeyPattern.Matches(block.Groups[1].Value))
                {
                    string key = keyMatch.Groups[1].Value;
                    if (!KnownCompositionFields.Contains(key))
                    {
                        badKey = key;
                        return true;
                    }
                }
            }

            badKey = null;
            return false;
        }

        [Test]
        public void World1Config_EveryCompositionKeyMatchesAWorldCompositionField()
        {
            TextAsset asset = Resources.Load<TextAsset>($"{WorldLibrary.ResourceRoot}/{WorldLibrary.World1}");
            Assert.IsNotNull(asset, "world1_config.json resource is missing");

            bool foundUnknownKey = TryFindUnknownCompositionKey(asset.text, out string badKey);
            Assert.IsFalse(foundUnknownKey,
                $"composition key '{badKey}' does not match any WorldComposition field (rusher/bruiser/" +
                "heavy/brute/gunner/launcher/blinker/bolter) — JsonUtility silently drops it and its " +
                "authored robots never spawn (MV-500)");
        }

        // --- MV-641: gate g11 (a11 -> a12) opens on sheds-destroyed-before(12). Pre-V12c that set was
        // --- 8 sheds across a3/a6/a7/a9(x2)/a10(x2)/a11, five of which no design sheet ever drew, so
        // --- the gate read "SHEDS 4 / 8" and could never open. V12c's corrected sheds[] leaves exactly
        // --- 3 sheds before area 12 — one each in a3, a7 and a11 — so the condition can actually be met.

        [Test]
        public void World1_ShedsDestroyedBeforeArea12_IsSatisfiedByExactlyA3A7AndA11()
        {
            WorldConfig cfg = LoadWorld1();
            var net = new SupplyLineNetwork(cfg);

            net.ShedProgressBefore(12, out int destroyedBefore, out int totalBefore);
            Assert.AreEqual(3, totalBefore, "areas before index 12 must together author exactly 3 sheds");
            Assert.AreEqual(0, destroyedBefore);
            Assert.IsFalse(net.ShedsDestroyedBefore(12));

            foreach (string areaId in new[] { "a3", "a7", "a11" })
            {
                WorldArea area = cfg.Area(areaId);
                Assert.IsNotNull(area, $"world1_config.json has no area '{areaId}'");

                WorldShed[] sheds = area.Sheds();
                Assert.Greater(sheds.Length, 0, $"area '{areaId}' must author a shed");
                for (int i = 0; i < sheds.Length; i++)
                    net.DestroyShed(area.ShedId(i, sheds.Length));
            }

            Assert.IsTrue(net.ShedsDestroyedBefore(12),
                "destroying every shed in a3, a7 and a11 must satisfy gate g11's sheds-destroyed-before(12) condition");
        }
    }
}
