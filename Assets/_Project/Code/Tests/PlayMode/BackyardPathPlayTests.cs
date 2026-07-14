using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// PlayMode checks that the Backyard the map builds is actually the space the design asks for: a
    /// walled patio that opens into a wide lawn you can circle in, a nook and a shed hanging off its
    /// sides, a gate doorway, and an enclosed boss arena.
    ///
    /// Every probe here is read OFF THE SHIPPED MAP rather than typed in. That is not tidiness: the
    /// arena has just been reshaped, and a test that carries its own copy of the level's coordinates
    /// is a test that keeps passing about a level nobody is playing. Ask the map where the doorway is
    /// and the test is still asking the right question after the next reshape.
    /// </summary>
    public sealed class BackyardPathPlayTests
    {
        private GameObject _go;

        private static MapData Shipped() => MapLibrary.Load(MapLibrary.BackyardSlice);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            yield return null;
        }

        private IEnumerator BuildPath()
        {
            _go = new GameObject("Path", typeof(BackyardPath));
            yield return null;                 // Awake builds geometry
            Physics.SyncTransforms();
            yield return null;
        }

        /// <summary>Anything solid and person-height standing at this point.</summary>
        private static bool BlockedAt(Vector3 p) =>
            Physics.OverlapSphere(p, 0.4f).Any(c => c.bounds.size.y >= 1.5f);

        private static Vector3 At(Vector2 xz) => new Vector3(xz.x, 1f, xz.y);

        [Test]
        public void TheShippedMapIsPlayable()
        {
            // Cheap guard on the data itself, so a failure below is unambiguously a geometry bug.
            Assert.IsTrue(MapValidation.Validate(Shipped(), out string why), why);
        }

        [UnityTest]
        public IEnumerator TheRouteFromTheSpawnUpTheLawnIsWalkable()
        {
            yield return BuildPath();

            MapData map = Shipped();
            MapEntity spawn = map.First(EntityKind.PlayerSpawn);
            MapEntity gate = map.First(EntityKind.Gate);

            // Straight up the middle from where Max starts to the gate he has to open: the route he
            // takes must never be blocked.
            for (float z = spawn.z + 1f; z <= gate.z - 1f; z += 1f)
                Assert.IsFalse(BlockedAt(new Vector3(spawn.x, 1f, z)), $"the mission line is blocked at z={z}");
        }

        [UnityTest]
        public IEnumerator TheLawnOpensOutIntoARoomYouCanCircleIn()
        {
            yield return BuildPath();

            MapZone lawn = Shipped().Zone("lawn");

            // At a cover-free depth just inside the lawn, sweep the full width: where the old corridor
            // had walls at ±4.5, there must now be open floor all the way out.
            float z = lawn.ZMin + 2f;
            for (float x = lawn.XMin + 0.6f; x <= lawn.XMax - 0.6f; x += 1f)
                Assert.IsFalse(BlockedAt(new Vector3(x, 1f, z)),
                    $"the lawn is still walled in at x={x} — it's a corridor, not a fight room");

            Assert.GreaterOrEqual(lawn.InscribedRadius, 8f, "no room for a circling loop");
        }

        /// <summary>
        /// The invariant that says every room really IS a room: from anywhere inside one, the only way
        /// out is into another one. Walk the outside of every zone's edge — if the point is not in a
        /// neighbouring room, it must be solid wall.
        ///
        /// This is the check that would have caught the yard's art being hard-coded to the old
        /// corridor, and it is the one that catches the next reshape too: it names no coordinate.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryRoomIsWalledWhereItDoesNotOpenIntoAnother()
        {
            yield return BuildPath();
            MapData map = Shipped();

            foreach (MapZone zone in map.zones)
            foreach (Vector2 p in JustOutside(zone))
            {
                if (map.ZoneAt(p.x, p.y) != null) continue;   // a shared edge: you walk into the next room

                Assert.IsTrue(BlockedAt(At(p)),
                    $"'{zone.id}' leaks at {p} — that is not a doorway and it is not a wall");
            }
        }

        /// <summary>Points 0.6 m beyond a room's edges, a metre apart, held clear of the corners
        /// (where two walls meet and "which wall is this" stops being a question worth asking).</summary>
        private static IEnumerable<Vector2> JustOutside(MapZone zone)
        {
            const float Step = 1f;
            const float Out = 0.6f;
            const float Corner = 0.5f;

            for (float x = zone.XMin + Corner; x <= zone.XMax - Corner; x += Step)
            {
                yield return new Vector2(x, zone.ZMin - Out);
                yield return new Vector2(x, zone.ZMax + Out);
            }

            for (float z = zone.ZMin + Corner; z <= zone.ZMax - Corner; z += Step)
            {
                yield return new Vector2(zone.XMin - Out, z);
                yield return new Vector2(zone.XMax + Out, z);
            }
        }

        [UnityTest]
        public IEnumerator TheDoorwayIntoTheShedIsOpen_AndTheWallEitherSideOfItIsSolid()
        {
            yield return BuildPath();
            MapData map = Shipped();

            Doorway(map, "lawn", "shed", out bool alongX, out float coord, out Span hole);
            Assert.IsFalse(alongX, "the shed is meant to be off the lawn's SIDE — this doorway faces the wrong way");

            Assert.IsFalse(BlockedAt(Mouth(alongX, coord, hole.Mid)), "the way into the shed is bricked up");

            // …and the wall resumes either side of it, so the shed is a room with a door and not an
            // open end of the lawn.
            Assert.IsTrue(BlockedAt(Mouth(alongX, coord, hole.Min - 1.5f)), "no shoulder below the shed doorway");
            Assert.IsTrue(BlockedAt(Mouth(alongX, coord, hole.Max + 1.5f)), "no shoulder above the shed doorway");
        }

        [UnityTest]
        public IEnumerator TheBossGateIsADoorwayInAWall()
        {
            yield return BuildPath();
            MapData map = Shipped();

            Doorway(map, "lawn", "gatehouse", out bool alongX, out float coord, out Span hole);

            // Open in the middle…
            Assert.IsFalse(BlockedAt(Mouth(alongX, coord, hole.Mid)), "the gate doorway is walled shut");

            // …and shouldered off to the sides, so the boss arena is sealed until the gate opens —
            // including out past the gatehouse, with no slipping round the end of the lawn's wall.
            Assert.IsTrue(BlockedAt(Mouth(alongX, coord, hole.Min - 1.5f)), "no left gate shoulder");
            Assert.IsTrue(BlockedAt(Mouth(alongX, coord, hole.Max + 1.5f)), "no right gate shoulder");
            Assert.IsTrue(BlockedAt(Mouth(alongX, coord, map.Zone("lawn").XMax - 2f)), "gap beside the lawn wall");
        }

        [UnityTest]
        public IEnumerator TheBossArenaIsEnclosedAndEmpty()
        {
            yield return BuildPath();

            MapZone arena = Shipped().Zone("compost");

            Assert.IsTrue(BlockedAt(new Vector3(arena.x, 1f, arena.ZMax + 0.6f)), "no arena back wall");
            Assert.IsTrue(BlockedAt(new Vector3(arena.XMin - 0.6f, 1f, arena.z)), "no left arena wall");
            Assert.IsTrue(BlockedAt(new Vector3(arena.XMax + 0.6f, 1f, arena.z)), "no right arena wall");

            // And it's clear inside — the boss needs room to charge and drop AoEs.
            Assert.IsFalse(BlockedAt(new Vector3(arena.x, 1f, arena.z)), "something is standing in the boss arena");
        }

        [UnityTest]
        public IEnumerator EveryPieceOfCoverInTheMapIsSomethingYouRunInto()
        {
            yield return BuildPath();
            MapData map = Shipped();

            var names = _go.GetComponentsInChildren<Transform>().Select(t => t.name).ToArray();

            foreach (MapEntity c in MapValidation.Kind(map, EntityKind.Cover))
            {
                Assert.IsTrue(Physics.OverlapSphere(At(c.CenterXz), 0.3f).Length > 0,
                    $"{c.id} has no collider — you'd run straight through it");

                Assert.Contains(c.id, names, "cover piece missing from the blockout");
            }
        }

        private static void Doorway(MapData map, string from, string to,
                                    out bool alongX, out float coord, out Span hole)
        {
            foreach (MapLink link in map.links)
            {
                bool joins = (link.from == from && link.to == to) || (link.from == to && link.to == from);
                if (joins && MapGeometry.Doorway(map, link, out alongX, out coord, out hole)) return;
            }

            Assert.Fail($"the map has no doorway between '{from}' and '{to}'");
            alongX = false; coord = 0f; hole = default;
        }

        /// <summary>A point on a doorway's wall line, <paramref name="along"/> the line.</summary>
        private static Vector3 Mouth(bool alongX, float coord, float along) =>
            alongX ? new Vector3(along, 1f, coord) : new Vector3(coord, 1f, along);
    }
}
