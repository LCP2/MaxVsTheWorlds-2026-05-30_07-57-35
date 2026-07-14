using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The map is derived from the level (YT-72/YT-73), so it cannot drift from it. These pin
    /// the projection — a map that lies about where you are is worse than no map. The first section
    /// reads the level's own <see cref="MapData"/>, which is what the minimap draws now; the rest pin
    /// the layout projection the UI falls back to when there is no level in the scene.</summary>
    public sealed class ArenaMapTests
    {
        private static readonly BackyardPathLayout L = BackyardPathLayout.Default;

        private static MapData Map => MapLibrary.Load(MapLibrary.BackyardSlice);

        // --- drawn from the map ------------------------------------------------------------------

        [Test]
        public void FromAMap_ThereIsARoomOnTheMapForEveryRoomInTheLevel()
        {
            MapData map = Map;
            MapRoom[] rooms = ArenaMap.Rooms(map);

            Assert.AreEqual(map.zones.Length, rooms.Length);

            // Named the way the author named them: a map that says "Room 4" tells you nothing.
            Assert.IsTrue(rooms.Any(r => r.Name == "The Shed"), "the shed is not on the map");
            Assert.IsTrue(rooms.Any(r => r.Name == "The Beds"), "the nook is not on the map");
        }

        [Test]
        public void FromAMap_EveryRoomPlotsWhereItStands()
        {
            MapData map = Map;
            MapRoom[] rooms = ArenaMap.Rooms(map);

            foreach (MapZone zone in map.zones)
            {
                int at = ArenaMap.RoomAt(new Vector2(zone.x, zone.z), rooms);
                Assert.AreNotEqual(-1, at, $"'{zone.id}' is not on the map at all");
                Assert.AreEqual(zone.name, rooms[at].Name, $"'{zone.id}' plots as another room");
            }
        }

        [Test]
        public void FromAMap_TheBoundsHoldEveryRoomAndItsWalls()
        {
            MapData map = Map;
            Rect bounds = ArenaMap.Bounds(map);

            foreach (MapRoom room in ArenaMap.Rooms(map))
            {
                Assert.GreaterOrEqual(room.Xz.xMin, bounds.xMin + map.wallThickness - 1e-3f);
                Assert.LessOrEqual(room.Xz.xMax, bounds.xMax - map.wallThickness + 1e-3f);
                Assert.GreaterOrEqual(room.Xz.yMin, bounds.yMin + map.wallThickness - 1e-3f);
                Assert.LessOrEqual(room.Xz.yMax, bounds.yMax - map.wallThickness + 1e-3f);
            }
        }

        [Test]
        public void FromAMap_MovingARoomMovesItOnTheMap()
        {
            // The whole reason the map is derived and not drawn: reshape the level and it follows.
            MapData map = Map;
            Rect before = ArenaMap.Rooms(map).First(r => r.Name == "The Shed").Xz;

            map.Zone("shed").x += 4f;
            Rect after = ArenaMap.Rooms(map).First(r => r.Name == "The Shed").Xz;

            Assert.AreEqual(before.xMin + 4f, after.xMin, 1e-3, "the map didn't follow the level");
        }

        // --- the layout projection the bare UI falls back to --------------------------------------

        [Test]
        public void TheMapHasTheRoomsThePlayerWalks()
        {
            var rooms = ArenaMap.Rooms(L);
            Assert.AreEqual(3, rooms.Length);
            Assert.AreEqual("Patio", rooms[0].Name);
            Assert.AreEqual("Lawn", rooms[1].Name);
            Assert.AreEqual("Boss Arena", rooms[2].Name);
        }

        [Test]
        public void TheRoomsRunUpFieldInOrder_AndDontOverlap()
        {
            var rooms = ArenaMap.Rooms(L);
            for (int i = 1; i < rooms.Length; i++)
                Assert.GreaterOrEqual(rooms[i].Xz.yMin, rooms[i - 1].Xz.yMax - 1e-3f,
                    $"{rooms[i].Name} overlaps {rooms[i - 1].Name} along the path");
        }

        [Test]
        public void EveryRoomFitsInsideTheMap()
        {
            Rect bounds = ArenaMap.Bounds(L);
            foreach (var room in ArenaMap.Rooms(L))
            {
                Assert.GreaterOrEqual(room.Xz.xMin, bounds.xMin - 1e-3f, $"{room.Name} off the left edge");
                Assert.LessOrEqual(room.Xz.xMax, bounds.xMax + 1e-3f, $"{room.Name} off the right edge");
                Assert.GreaterOrEqual(room.Xz.yMin, bounds.yMin - 1e-3f, $"{room.Name} off the bottom");
                Assert.LessOrEqual(room.Xz.yMax, bounds.yMax + 1e-3f, $"{room.Name} off the top");
            }
        }

        [Test]
        public void TheCornersOfTheArenaAreTheCornersOfTheMap()
        {
            Rect b = ArenaMap.Bounds(L);
            Vector2 bottomLeft = ArenaMap.Normalize(new Vector2(b.xMin, b.yMin), b);
            Vector2 topRight = ArenaMap.Normalize(new Vector2(b.xMax, b.yMax), b);

            Assert.AreEqual(0f, bottomLeft.x, 1e-4); Assert.AreEqual(0f, bottomLeft.y, 1e-4);
            Assert.AreEqual(1f, topRight.x, 1e-4); Assert.AreEqual(1f, topRight.y, 1e-4);
        }

        [Test]
        public void UpFieldIsUp_AndTheBossIsAtTheTop()
        {
            // +Z is the way you advance, so it must be UP on the map or the map is upside down.
            Rect b = ArenaMap.Bounds(L);
            float atPatio = ArenaMap.Normalize(new Vector2(0f, L.StartZ), b).y;
            float atBoss = ArenaMap.Normalize(new Vector2(0f, L.ArenaEndZ), b).y;
            Assert.Greater(atBoss, atPatio, "the map is upside down");
        }

        [Test]
        public void MaxsStartingSpotPlotsInsideTheLawn()
        {
            // Max starts at z=-3 (YT-70), which is the lawn. If the map says otherwise, it's lying.
            var rooms = ArenaMap.Rooms(L);
            Assert.AreEqual(1, ArenaMap.RoomAt(new Vector2(0f, -3f), rooms));
        }

        [Test]
        public void TheShedAndTheBossPlotInTheRoomsTheyStandIn()
        {
            var rooms = ArenaMap.Rooms(L);
            Assert.AreEqual(1, ArenaMap.RoomAt(new Vector2(0f, 15f), rooms), "the shed isn't in the lawn");
            Assert.AreEqual(2, ArenaMap.RoomAt(new Vector2(0f, 33f), rooms), "the boss isn't in his arena");
        }

        [Test]
        public void RoomAt_ReturnsMinusOne_OutsideTheArena()
        {
            Assert.AreEqual(-1, ArenaMap.RoomAt(new Vector2(0f, 999f), ArenaMap.Rooms(L)));
        }

        [Test]
        public void NormalizeRect_MapsARoomToItsShareOfTheMap()
        {
            Rect b = ArenaMap.Bounds(L);
            Rect lawn = ArenaMap.NormalizeRect(ArenaMap.Rooms(L)[1].Xz, b);

            Assert.Greater(lawn.width, 0f); Assert.Greater(lawn.height, 0f);
            Assert.GreaterOrEqual(lawn.xMin, 0f); Assert.LessOrEqual(lawn.xMax, 1f);
            Assert.GreaterOrEqual(lawn.yMin, 0f); Assert.LessOrEqual(lawn.yMax, 1f);
            Assert.Less(lawn.width, 1f, "the lawn is not the whole arena — the boss arena is wider");
        }

        // --- Aspect: a map with the wrong proportions makes distances read wrong ---------------

        [Test]
        public void TheArenaIsTallerThanItIsWide_AndTheMapKnowsIt()
        {
            Assert.Less(ArenaMap.AspectRatio(ArenaMap.Bounds(L)), 1f);
        }

        [Test]
        public void FitPreservingAspect_FillsTheBox_WithoutSquashingTheArena()
        {
            float aspect = ArenaMap.AspectRatio(ArenaMap.Bounds(L));

            foreach (var panel in new[] { new Vector2(400f, 400f), new Vector2(900f, 200f), new Vector2(120f, 700f) })
            {
                Vector2 fit = ArenaMap.FitPreservingAspect(panel, aspect);

                Assert.AreEqual(aspect, fit.x / fit.y, 1e-3, "the arena got squashed to fill the box");
                Assert.LessOrEqual(fit.x, panel.x + 1e-3, "wider than the box it must fit inside");
                Assert.LessOrEqual(fit.y, panel.y + 1e-3, "taller than the box it must fit inside");
                // …and it actually uses the box: one axis must be flush.
                Assert.IsTrue(Mathf.Approximately(fit.x, panel.x) || Mathf.Approximately(fit.y, panel.y),
                    "the map shrank away from both edges instead of filling what it could");
            }
        }

        [Test]
        public void ADegeneratePanel_DoesNotProduceNaNs()
        {
            Vector2 fit = ArenaMap.FitPreservingAspect(Vector2.zero, 0f);
            Assert.IsFalse(float.IsNaN(fit.x) || float.IsNaN(fit.y));
        }

        [Test]
        public void TheMapFollowsTheLevel_RatherThanBeingAuthoredTwice()
        {
            // Reshape the arena and the map must reshape with it. This is the whole reason the map
            // is derived from BackyardPathLayout instead of being drawn by hand.
            var wider = BackyardPathLayout.Default;
            wider.LawnHalfWidth = 20f;

            Rect before = ArenaMap.Rooms(L)[1].Xz;
            Rect after = ArenaMap.Rooms(wider)[1].Xz;
            Assert.Greater(after.width, before.width, "the map didn't follow the level");
        }
    }
}
