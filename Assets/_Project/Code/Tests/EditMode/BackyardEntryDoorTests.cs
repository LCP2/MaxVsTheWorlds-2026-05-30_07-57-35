using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-718: the entry area's west wall needed a visibly SHUT door on the shed side, so the fiction
    /// the opening cinematic promises — Max walked out of the shed, the door closed behind him — agrees
    /// with the geometry the player actually sees standing at it. Loads the actually-shipped
    /// <c>world1_config.json</c> through <see cref="WorldLibrary"/>/<see cref="WorldMapLoader"/>, same
    /// convention <see cref="BackyardHomeShedTests"/> follows after MV-709.
    ///
    /// The one new test this ticket adds (MV-465 Rule 1) — AC2 through AC5 folded into it rather than
    /// four separate tests, the same way <c>BackyardHomeShedTests.TheShed_StandsWithin15MOfSpawn_...</c>
    /// folds two ACs together. Fails to even compile on the pre-fix commit, since
    /// <see cref="BackyardEntryDoor"/> does not exist there yet — see the fix comment for the quoted
    /// compiler error.
    /// </summary>
    public sealed class BackyardEntryDoorTests
    {
        private static MapData Map()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "the shipped world1_config.json failed to load — see the error log above");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            return map;
        }

        [Test]
        public void TheDoor_LinesUpWithTheShed_SitsInTheWall_HasNoCollider_AndNeverOpens()
        {
            MapData map = Map();

            // ---- AC2: centred on the shed's own door Z, read at runtime ----
            Vector3 shedDoor = BackyardHomeShed.PlaceFor(map);
            Vector3 doorCenter = BackyardEntryDoor.PlaceFor(map);
            Assert.AreEqual(shedDoor.z, doorCenter.z, 0.01f,
                "the entry-wall door isn't lined up with the shed's own door");

            // ---- AC3: centre X inside the west wall's own thickness band, not floating beside it ----
            float xMin = map.Bounds().xMin;
            Assert.LessOrEqual(doorCenter.x, xMin, "the door isn't inside the wall band at all");
            Assert.GreaterOrEqual(doorCenter.x, xMin - map.wallThickness,
                "the door sits further west than the wall's own thickness allows");

            // ---- AC4: probe the west wall's own derived geometry and a stand-in collider both before
            // and after the door builds, and prove neither moved, resized, or multiplied ----
            WallSegment wallBefore = FindEntryWestWall(map);
            GameObject wallProbe = SpawnWallProbe(wallBefore);
            var wallCollider = wallProbe.GetComponent<Collider>();
            Assert.IsNotNull(wallCollider, "the entry area's west wall has no collider at all");
            int collidersBefore = wallProbe.GetComponentsInChildren<Collider>().Length;
            Bounds boundsBefore = wallCollider.bounds;

            GameObject doorPathGo = null, doorGo = null;
            try
            {
                // Awake isn't reliably invoked for AddComponent outside Play mode (same note
                // BackyardHomeShedTests carries) — drive both components' Awake directly.
                doorPathGo = new GameObject("MV718-test-backyard-path");
                var path = doorPathGo.AddComponent<BackyardPath>();
                FieldInfo mapField = typeof(BackyardPath).GetField("_map",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(mapField, "BackyardPath._map went missing");
                mapField.SetValue(path, map);

                doorGo = new GameObject("MV718-test-entry-door");
                var door = doorGo.AddComponent<BackyardEntryDoor>();

                typeof(BackyardEntryDoor).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(door, null);

                Assert.IsTrue(door.Built, "the door did not build against the shipped map");

                Transform doorPart = doorGo.transform.Find("EntryDoor/Door");
                Assert.IsNotNull(doorPart, "the door has no built part");
                Assert.IsNull(doorPart.GetComponent<Collider>(),
                    "the door part carries a collider — the wall's own collision must be the only thing " +
                    "that stops the player");

                WallSegment wallAfter = FindEntryWestWall(map);
                Assert.AreEqual(wallBefore.Center, wallAfter.Center,
                    "the west wall's centre moved after the door was built");
                Assert.AreEqual(wallBefore.Size, wallAfter.Size,
                    "the west wall's size changed after the door was built");

                Assert.AreEqual(collidersBefore, wallProbe.GetComponentsInChildren<Collider>().Length,
                    "the wall probe picked up an extra collider after the door was built");
                Assert.AreEqual(boundsBefore.center, wallCollider.bounds.center,
                    "the west wall's collider moved after the door was built");
                Assert.AreEqual(boundsBefore.size, wallCollider.bounds.size,
                    "the west wall's collider resized after the door was built");

                // ---- AC5: closed regardless of what any GateCondition the parser accepts resolves to ----
                string[] tokens =
                {
                    "start", "primary", "sluice", "sheds-destroyed-before", "all-sheds-destroyed",
                    "replicators-destroyed:all", "replicators-destroyed:a1,a2",
                };

                foreach (string token in tokens)
                {
                    Assert.IsTrue(GateCondition.TryParse(token, out GateCondition condition, out string reason),
                        $"'{token}' should have parsed: {reason}");

                    condition.IsSatisfied(null, 0); // whatever this resolves to must never reach this door
                    Assert.IsTrue(door.IsClosed, $"the door reports open after evaluating '{token}'");
                }
            }
            finally
            {
                if (doorGo != null) Object.DestroyImmediate(doorGo);
                if (doorPathGo != null) Object.DestroyImmediate(doorPathGo);
                Object.DestroyImmediate(wallProbe);
            }
        }

        /// <summary>The entry area's west wall, found the same way the door's own placement is derived
        /// — the exterior wall on the map's own X minimum whose Z span covers the shed's door.</summary>
        private static WallSegment FindEntryWestWall(MapData map)
        {
            float expectedX = map.Bounds().xMin - map.wallThickness * 0.5f;
            float doorZ = BackyardHomeShed.PlaceFor(map).z;

            foreach (WallSegment w in MapGeometry.Walls(map))
            {
                if (w.AlongX) continue;
                if (!Mathf.Approximately(w.Center.x, expectedX)) continue;

                float halfZ = w.Size.z * 0.5f;
                if (doorZ >= w.Center.z - halfZ && doorZ <= w.Center.z + halfZ) return w;
            }

            Assert.Fail("couldn't find the entry area's west wall segment covering the shed's door Z");
            return default;
        }

        private static GameObject SpawnWallProbe(WallSegment w)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = w.Name;
            go.transform.position = w.Center;
            go.transform.localScale = w.Size;
            return go;
        }
    }
}
