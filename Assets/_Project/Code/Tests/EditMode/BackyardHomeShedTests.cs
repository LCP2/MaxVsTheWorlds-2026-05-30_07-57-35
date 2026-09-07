using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Max's home shed (YT-163) — the backdrop that closes the loop between the intro's "Max leaves
    /// his shed" and the playable Backyard's actual start.
    ///
    /// MV-709: loads the actually-shipped <c>world1_config.json</c> through <see cref="WorldLibrary"/>
    /// / <see cref="WorldMapLoader"/> (the same real conversion <c>BackyardPath</c> runs at boot),
    /// not the retired <c>backyard_slice.json</c> fixture the previous version of this file read —
    /// that stale fixture is exactly what let the shed drift 160 m from the real spawn point
    /// unnoticed: every assertion against it kept passing while the live map moved on.
    /// </summary>
    public sealed class BackyardHomeShedTests
    {
        private static MapData Map()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "the shipped world1_config.json failed to load — see the error log above");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            return map;
        }

        /// <summary>The map's entry-role zone, found the same way <c>BackyardHomeShed.EntryAreaZ</c>
        /// does — independently re-derived here rather than reflected into, so this doesn't just
        /// mirror the production code's own bug back at it.</summary>
        private static MapZone EntryZone(MapData map)
        {
            foreach (MapZone zone in map.zones)
                if (zone != null && zone.Kind == ZoneKind.Entry) return zone;
            return null;
        }

        [Test]
        public void TheShed_StandsWhereMaxWalkedOutOfIt()
        {
            MapData map = Map();
            Vector3 center = BackyardHomeShed.PlaceFor(map);

            Assert.IsTrue(BackyardHomeShed.Validate(map, center, out string why), why);
        }

        [Test]
        public void TheShed_StandsJustWestOfTheMapsOwnBounds()
        {
            // AC2: off the WEST wall now, not the retired south one — within a few metres of it
            // (wallThickness + WallGap + half its own width), not held metres clear.
            MapData map = Map();
            Vector3 center = BackyardHomeShed.PlaceFor(map);

            Assert.Less(center.x, map.Bounds().xMin, "the shed isn't west of the map at all");
            Assert.Greater(center.x, map.Bounds().xMin - 10f,
                "the shed is held far more than its own footprint clear of the west wall");
        }

        [Test]
        public void TheShed_IsCentredOnTheEntryAreasOwnZSpan()
        {
            // AC3: read the entry area's Z span from the map at runtime — not a hard-coded 148..154 —
            // so this keeps holding if the sheet's entry area ever moves.
            MapData map = Map();
            MapZone entry = EntryZone(map);
            Assert.IsNotNull(entry, "world1_config.json has no entry-role area to measure against");

            Vector3 center = BackyardHomeShed.PlaceFor(map);

            Assert.GreaterOrEqual(center.z, entry.ZMin, "the shed sits north of the entry area");
            Assert.LessOrEqual(center.z, entry.ZMax, "the shed sits south of the entry area");
        }

        [Test]
        public void TheShed_StandsFlushAgainstTheWestWall_NotMetresBehindIt()
        {
            // YT-179's "flush, not held metres off it" guarantee, re-measured on the axis MV-709
            // moved the shed to.
            MapData map = Map();
            Vector3 center = BackyardHomeShed.PlaceFor(map);

            float eastFaceX = center.x + BackyardHomeShed.Width * 0.5f;
            float gap = map.Bounds().xMin - eastFaceX;
            Assert.Less(gap, BackyardBackdrop.MinClearance,
                "the shed is still held off the wall by the neighbourhood's whole clearance");
        }

        [Test]
        public void TheShed_ClearsEveryRoom_NotJustTheEntry()
        {
            MapData map = Map();
            Vector3 center = BackyardHomeShed.PlaceFor(map);

            Assert.IsTrue(BackyardHomeShed.Validate(map, center, out string why), why);
        }

        [Test]
        public void AShedInsideARoomIsRejected()
        {
            // Scenery that reaches into the yard is not scenery — the same rule the neighbourhood is
            // held to (BackyardSkyTests.AHouseInTheLawnIsRejected).
            MapData map = Map();
            MapZone entry = EntryZone(map);
            Assert.IsNotNull(entry, "world1_config.json has no entry-role area to measure against");
            var badCenter = new Vector3(entry.x, 0f, entry.z);   // dead centre of the entry area

            Assert.IsFalse(BackyardHomeShed.Validate(map, badCenter, out string why));
            StringAssert.Contains("reaches into the arena", why);
        }

        /// <summary>
        /// MV-709, the one new test this ticket adds. Fails on the pre-fix expression
        /// (<c>HoseDirector.StartTapPosition.x</c>, <c>Bounds().yMin</c> south of the map) — that
        /// placement measured ~162.1 m from the entry area's own centre, reported by this exact
        /// assertion before the fix landed (see the fix comment for the quoted run). Also proves the
        /// door actually moved to the shed's new front face (AC7) — folded into this same test rather
        /// than a second one, per the ticket's own note that this isn't a second test.
        /// </summary>
        [Test]
        public void TheShed_StandsWithin15MOfSpawn_AndItsDoorFacesTheEntry()
        {
            MapData map = Map();
            MapZone entry = EntryZone(map);
            Assert.IsNotNull(entry, "world1_config.json has no entry-role area to measure against");

            Vector3 center = BackyardHomeShed.PlaceFor(map);

            // ---- AC5 ----
            float distance = Vector2.Distance(new Vector2(center.x, center.z), entry.CenterXz);
            Assert.Less(distance, 15f,
                $"the shed is {distance:0.0} m from the entry area's centre — Max never sees it");

            // ---- AC7 (resolved from the actually-built hierarchy, not the authored offset) ----
            // Awake isn't reliably invoked for AddComponent outside Play mode (the same note
            // MV493DoorwayWaypointTests/MV456ShedFaucetTests carry) — drive both components' Awake
            // directly rather than relying on it firing on its own.
            var pathGo = new GameObject("MV709-test-backyard-path");
            GameObject shedGo = null;
            try
            {
                var path = pathGo.AddComponent<BackyardPath>();
                FieldInfo mapField = typeof(BackyardPath).GetField("_map",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(mapField, "BackyardPath._map went missing");
                mapField.SetValue(path, map);

                shedGo = new GameObject("MV709-test-home-shed");
                var shed = shedGo.AddComponent<BackyardHomeShed>();

                // BuildShed's Part() calls Destroy() on each primitive's auto-added collider — fine in
                // Play mode, but edit mode demands DestroyImmediate and logs an error per part (same
                // note MV586ForceFieldRamTests/MV523ForceFieldFreeActivationTests carry for this exact
                // message). One expectation per built part (Walls, Door, two RoofSlabs).
                for (int i = 0; i < 4; i++)
                    LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));

                typeof(BackyardHomeShed).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(shed, null);

                Assert.IsTrue(shed.Built, "the shed did not build against the shipped map");

                Transform door = shedGo.transform.Find("MaxsShed/Door");
                Assert.IsNotNull(door, "the shed has no Door part");
                Assert.Greater(door.position.x, shed.Center.x, "the door isn't on the +X face");
            }
            finally
            {
                if (shedGo != null) Object.DestroyImmediate(shedGo);
                Object.DestroyImmediate(pathGo);
            }
        }
    }
}
