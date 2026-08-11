using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The maths that biases ambient spawns to the side of a room opposite its entry door
    /// (MV-323). Robots and a mob arriving right at the entrance, because placement never looked at
    /// where the door into the room was, is the failure this guards.</summary>
    public sealed class SpawnBiasTests
    {
        private static MapZone Room() =>
            new MapZone { id = "area2", type = "open", x = 0f, z = 0f, width = 20f, depth = 20f };

        [Test]
        public void ZeroDoorDirection_ReturnsTheFullInsetBounds_Unbiased()
        {
            MapZone zone = Room();
            Rect bounds = SpawnBias.FarSideBounds(zone, Vector3.zero, edgeMargin: 3f);

            Assert.AreEqual(zone.XMin + 3f, bounds.xMin, 1e-4f);
            Assert.AreEqual(zone.XMax - 3f, bounds.xMax, 1e-4f);
            Assert.AreEqual(zone.ZMin + 3f, bounds.yMin, 1e-4f);
            Assert.AreEqual(zone.ZMax - 3f, bounds.yMax, 1e-4f);
        }

        [Test]
        public void DoorOnTheNegativeZSide_KeepsOnlyThePositiveZHalf()
        {
            // Entry direction +Z means the door was entered heading toward +Z, i.e. the door sits on
            // the room's -Z side — so the far side to bias toward is +Z.
            Rect bounds = SpawnBias.FarSideBounds(Room(), new Vector3(0f, 0f, 1f), edgeMargin: 3f);

            Assert.AreEqual(0f, bounds.yMin, 1e-4f, "near (door-side) half should be excluded");
            Assert.AreEqual(7f, bounds.yMax, 1e-4f);
        }

        [Test]
        public void DoorOnThePositiveXSide_KeepsOnlyTheNegativeXHalf()
        {
            Rect bounds = SpawnBias.FarSideBounds(Room(), new Vector3(-1f, 0f, 0f), edgeMargin: 3f);

            Assert.AreEqual(-7f, bounds.xMin, 1e-4f);
            Assert.AreEqual(0f, bounds.xMax, 1e-4f, "near (door-side) half should be excluded");
        }

        [Test]
        public void DiagonalDoorDirection_BiasesOnlyItsDominantAxis()
        {
            // Mostly +X with a little +Z should narrow X only, leaving Z at its full inset span —
            // doorways in this map always cut a single straight, axis-aligned wall.
            Rect bounds = SpawnBias.FarSideBounds(Room(), new Vector3(5f, 0f, 1f), edgeMargin: 3f);

            Assert.AreEqual(0f, bounds.xMin, 1e-4f);
            Assert.AreEqual(7f, bounds.xMax, 1e-4f);
            Assert.AreEqual(-7f, bounds.yMin, 1e-4f, "Z should stay untouched — the door isn't on a Z wall");
            Assert.AreEqual(7f, bounds.yMax, 1e-4f);
        }

        [Test]
        public void FarSideBounds_NeverExtendsBeyondTheRoomsInsetSpan()
        {
            MapZone zone = Room();
            Rect bounds = SpawnBias.FarSideBounds(zone, new Vector3(0f, 0f, 1f), edgeMargin: 3f);

            Assert.GreaterOrEqual(bounds.xMin, zone.XMin + 3f - 1e-4f);
            Assert.LessOrEqual(bounds.xMax, zone.XMax - 3f + 1e-4f);
            Assert.GreaterOrEqual(bounds.yMin, zone.ZMin + 3f - 1e-4f);
            Assert.LessOrEqual(bounds.yMax, zone.ZMax - 3f + 1e-4f);
        }

        // --- StaggerBand (MV-324) — robots landing at roughly the same distance from the gate all
        // closed on Max together, reading as one simultaneous mob. These prove each spawn index lands
        // in its own distance-from-gate tier, ordered nearest-the-gate first. ---

        [Test]
        public void StaggerBand_OneBand_ReturnsBoundsUnchanged()
        {
            Rect farSide = SpawnBias.FarSideBounds(Room(), new Vector3(0f, 0f, 1f), edgeMargin: 3f);
            Rect band = SpawnBias.StaggerBand(farSide, new Vector3(0f, 0f, 1f), spawnIndex: 0, totalBands: 1);

            Assert.AreEqual(farSide, band);
        }

        [Test]
        public void StaggerBand_FirstIndex_IsNearestTheGate()
        {
            // Door on the -Z side (entered heading +Z), far side is [0, 7]. The nearest-to-gate tier
            // should sit right against the cut line (z = 0), not the far wall.
            Rect farSide = SpawnBias.FarSideBounds(Room(), new Vector3(0f, 0f, 1f), edgeMargin: 3f);
            Rect band = SpawnBias.StaggerBand(farSide, new Vector3(0f, 0f, 1f), spawnIndex: 0, totalBands: 5);

            Assert.AreEqual(0f, band.yMin, 1e-4f);
            Assert.AreEqual(1.4f, band.yMax, 1e-4f);
        }

        [Test]
        public void StaggerBand_LastIndex_IsFarthestFromTheGate()
        {
            Rect farSide = SpawnBias.FarSideBounds(Room(), new Vector3(0f, 0f, 1f), edgeMargin: 3f);
            Rect band = SpawnBias.StaggerBand(farSide, new Vector3(0f, 0f, 1f), spawnIndex: 4, totalBands: 5);

            Assert.AreEqual(5.6f, band.yMin, 1e-4f);
            Assert.AreEqual(7f, band.yMax, 1e-4f);
        }

        [Test]
        public void StaggerBand_IndexBeyondBandCount_WrapsAroundRatherThanOverflowing()
        {
            Rect farSide = SpawnBias.FarSideBounds(Room(), new Vector3(0f, 0f, 1f), edgeMargin: 3f);
            Rect wrapped = SpawnBias.StaggerBand(farSide, new Vector3(0f, 0f, 1f), spawnIndex: 5, totalBands: 5);
            Rect first = SpawnBias.StaggerBand(farSide, new Vector3(0f, 0f, 1f), spawnIndex: 0, totalBands: 5);

            Assert.AreEqual(first, wrapped);
        }

        [Test]
        public void StaggerBand_EveryTier_StaysWithinTheFarSideBounds()
        {
            Rect farSide = SpawnBias.FarSideBounds(Room(), new Vector3(-1f, 0f, 0f), edgeMargin: 3f);

            for (int i = 0; i < 5; i++)
            {
                Rect band = SpawnBias.StaggerBand(farSide, new Vector3(-1f, 0f, 0f), spawnIndex: i, totalBands: 5);

                Assert.GreaterOrEqual(band.xMin, farSide.xMin - 1e-4f);
                Assert.LessOrEqual(band.xMax, farSide.xMax + 1e-4f);
                Assert.AreEqual(farSide.yMin, band.yMin, 1e-4f, "perpendicular axis should stay full-span");
                Assert.AreEqual(farSide.yMax, band.yMax, 1e-4f);
            }
        }
    }
}
