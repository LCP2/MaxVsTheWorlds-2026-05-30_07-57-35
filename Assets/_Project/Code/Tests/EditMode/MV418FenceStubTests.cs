using System.Collections.Generic;
using MaxWorlds.Arena;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-418: a2 (20 m wide, x 11-31) and a3 (24 m wide, x 11-35) share the z=26 line, but only
    /// overlap across x 11-31. <see cref="MapGeometry.Solids"/> correctly reads that as a party wall
    /// for the shared span (offset 0, both rooms) and a one-sided run for a3's remaining x 31-35
    /// (offset -half, a3 only) — individually correct, but the two runs disagree about their offset
    /// on what is meant to read as ONE continuous boundary, so the remainder renders as a short fence
    /// jogged 0.2 m sideways, standing alone with no planting or corner post tying it to its
    /// neighbour (MinPlantedFace/CornerPosts both key off the run being long/aligned, and this run is
    /// neither).
    ///
    /// A geometry-wide "no two overlapping collinear runs may disagree about their offset" version of
    /// this assertion was tried first and rejected: World 1 has other, pre-existing, differently-sized
    /// neighbours (e.g. the entry stub is narrower than Area 1) that produce the exact same jogged
    /// pattern at a room's own corner, on purpose, and are out of this ticket's scope — flagging them
    /// too would make the test fail forever, not just on 7c060cf. This asserts the one line the ticket
    /// actually reports.
    ///
    /// Confirmed FAILING on 7c060cf (pre-fix HEAD): the wall segments straddling z=26 split into two
    /// groups at z=26.0 (the x 11-31 party run) and z=25.8 (the x 31-35 one-sided stub) — a 0.2 m jog,
    /// exactly the offset the root-cause analysis above predicts (half of the 0.4 m wallThickness).
    /// </summary>
    public sealed class MV418FenceStubTests
    {
        private static MapData ShippedWorld1()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "the shipped world1_config.json failed to load — see the error log above");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            return map;
        }

        [Test]
        public void World1_A2A3BoundaryIsOneUnjoggedPartyWall_NoOrphanStub()
        {
            MapData map = ShippedWorld1();
            List<WallSegment> walls = MapGeometry.Walls(map);

            // Every AlongX wall segment within a thickness of the a2/a3 shared line (z=26) — the
            // party run plus whatever the gate g2 doorway split it into.
            var onSharedLine = walls.FindAll(w => w.AlongX && Mathf.Abs(w.Center.z - 26f) <= map.wallThickness);
            Assert.IsNotEmpty(onSharedLine, "no wall found near the a2/a3 shared line at z=26");

            float firstZ = onSharedLine[0].Center.z;
            foreach (WallSegment w in onSharedLine)
                Assert.That(w.Center.z, Is.EqualTo(firstZ).Within(0.01f),
                    $"'{w.Name}' sits at z={w.Center.z:0.##}, jogged off the other run(s) on the a2/a3 " +
                    $"line at z={firstZ:0.##} — the MV-418 orphan fence stub");

            // The gap the stub used to hide beyond (x 31-35) must now be covered by the SAME run as
            // the rest of the boundary, not left as a hole in the wall network.
            bool coversPastOldA2Edge = onSharedLine.Exists(w =>
                w.Center.x + w.Size.x * 0.5f >= 34.5f);
            Assert.IsTrue(coversPastOldA2Edge,
                "the a2/a3 boundary wall no longer reaches x=35 — a3's east end is unwalled");
        }
    }
}
