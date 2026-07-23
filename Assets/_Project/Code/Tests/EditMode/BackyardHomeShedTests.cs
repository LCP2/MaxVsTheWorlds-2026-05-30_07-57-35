using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Hose;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Max's home shed (YT-163) — the backdrop that closes the loop between the intro's "Max leaves
    /// his shed" and the playable Backyard's actual start.
    ///
    /// Pure placement logic, no scene: the same "prove it before it's real objects" split
    /// <see cref="BackyardBackdrop"/> uses (see <c>BackyardSkyTests</c>'s "the neighbourhood" section).
    /// </summary>
    public sealed class BackyardHomeShedTests
    {
        private static MapData Map => MapLibrary.Load(MapLibrary.BackyardSlice);

        [Test]
        public void TheShed_StandsWhereMaxWalkedOutOfIt()
        {
            MapData map = Map;
            Vector3 center = BackyardHomeShed.PlaceFor(map);

            Assert.IsTrue(BackyardHomeShed.Validate(map, center, out string why), why);
        }

        [Test]
        public void TheShed_IsCentredOnTheStartingTap()
        {
            // So the first hose reads as coming FROM the shed: the tap Max plugs into at spawn sits
            // directly in front of its door, not off to one side.
            MapData map = Map;
            Vector3 center = BackyardHomeShed.PlaceFor(map);

            Assert.That(center.x, Is.EqualTo(HoseDirector.StartTapPosition.x).Within(0.01f),
                "the shed isn't lined up with the tap the hose starts from");
        }

        [Test]
        public void TheShed_StandsBehindThePatiosBackWall()
        {
            MapData map = Map;
            Vector3 center = BackyardHomeShed.PlaceFor(map);

            float frontZ = center.z + BackyardHomeShed.Depth * 0.5f;
            Assert.Less(frontZ, map.Bounds().yMin, "the shed's front wall isn't behind the patio at all");
        }

        [Test]
        public void TheShed_StandsFlushAgainstTheWall_NotMetresBehindIt()
        {
            // YT-179: the YT-163 backdrop placement (BackyardBackdrop.MinClearance beyond the wall)
            // put the shed far enough back the fixed camera never saw it. It now stands just past the
            // wall's own thickness — close enough to read as replacing that section of the boundary.
            MapData map = Map;
            Vector3 center = BackyardHomeShed.PlaceFor(map);

            float frontZ = center.z + BackyardHomeShed.Depth * 0.5f;
            float gap = map.Bounds().yMin - frontZ;
            Assert.Less(gap, BackyardBackdrop.MinClearance,
                "the shed is still held off the wall by the neighbourhood's whole clearance");
        }

        [Test]
        public void TheShed_ClearsEveryRoom_NotJustThePatio()
        {
            MapData map = Map;
            Vector3 center = BackyardHomeShed.PlaceFor(map);

            Assert.IsTrue(BackyardHomeShed.Validate(map, center, out string why), why);
        }

        [Test]
        public void AShedInsideThePatioIsRejected()
        {
            // Scenery that reaches into the yard is not scenery — the same rule the neighbourhood is
            // held to (BackyardSkyTests.AHouseInTheLawnIsRejected).
            MapData map = Map;
            var badCenter = new Vector3(0f, 0f, -10f);   // the middle of the patio

            Assert.IsFalse(BackyardHomeShed.Validate(map, badCenter, out string why));
            StringAssert.Contains("reaches into the arena", why);
        }
    }
}
