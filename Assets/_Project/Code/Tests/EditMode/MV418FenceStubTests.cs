using System.Collections.Generic;
using MaxWorlds.Arena;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-418, general pass.
    ///
    /// The first pass (`141428f`) widened `a2` to match `a3`, closing the one seam the ticket was
    /// originally reported on. It was reopened because the same bug kept showing up elsewhere (Area
    /// 4, on `dadd9f1`) — the underlying cause is not specific to a2/a3, it is
    /// <see cref="MapGeometry.Solids"/> treating any two rooms of different widths that share a line
    /// as a party wall for their overlap PLUS a separate, differently-offset one-sided run for
    /// whichever room is wider. Individually correct, but the two disagree about their offset on what
    /// is meant to read as one continuous boundary, so the wider room's remainder renders as a short
    /// fence jogged half a wall's thickness sideways — standing alone, with no planting or corner
    /// post tying it to its neighbour, because <c>BackyardDressingSet</c> judges each fragment on its
    /// own.
    ///
    /// A table run against `main` while reopening the ticket found 13+ seams still jogging this way
    /// (a0/a1, a6/a7, a8/a9, a9/a10, a10/a11, a11/a12, a12/a13, a13/a14, a14/a15, a15/a16, a16/a17,
    /// a17/a18, a18/a19 — see the ticket comment for the full table), so this asserts the general
    /// invariant over the WHOLE shipped world rather than pinning one seam: no two wall segments that
    /// run the same way, are close enough to be the same architectural line, and touch or overlap
    /// along it, may sit at different line coordinates. That is exactly what a jog is.
    ///
    /// Confirmed FAILING on `dadd9f1` (this test, run against that commit's `MapGeometry.Solids` with
    /// no snap step) — multiple seams from the table above trip it. Fixed by
    /// <see cref="MapGeometry.Walls"/> → <c>Solids</c> → the new <c>SnapOneSidedRunsToPartyOffset</c>
    /// step, which snaps a one-sided run's offset to match a party run it directly touches, so the two
    /// read as one line instead of jogging.
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
        public void World1_NoTwoTouchingCollinearWallsDisagreeAboutTheirOffset()
        {
            MapData map = ShippedWorld1();
            List<WallSegment> walls = MapGeometry.Walls(map);
            Assert.IsNotEmpty(walls, "World1 produced no walls at all — MapGeometry regressed");

            int checkedPairs = 0;

            for (int i = 0; i < walls.Count; i++)
            {
                WallSegment a = walls[i];
                float lineA = a.AlongX ? a.Center.z : a.Center.x;
                float aMin = a.AlongX ? a.Center.x - a.Size.x * 0.5f : a.Center.z - a.Size.z * 0.5f;
                float aMax = a.AlongX ? a.Center.x + a.Size.x * 0.5f : a.Center.z + a.Size.z * 0.5f;

                for (int j = i + 1; j < walls.Count; j++)
                {
                    WallSegment b = walls[j];
                    if (a.AlongX != b.AlongX) continue;   // a corner, not a candidate for a jog

                    float lineB = b.AlongX ? b.Center.z : b.Center.x;

                    // Close enough to plausibly be the same architectural line — real distinct lines
                    // in World1 are whole rooms apart (metres), never within a couple of wall
                    // thicknesses of each other.
                    if (Mathf.Abs(lineA - lineB) > map.wallThickness * 2f) continue;

                    float bMin = b.AlongX ? b.Center.x - b.Size.x * 0.5f : b.Center.z - b.Size.z * 0.5f;
                    float bMax = b.AlongX ? b.Center.x + b.Size.x * 0.5f : b.Center.z + b.Size.z * 0.5f;

                    bool touches = aMax >= bMin && bMax >= aMin;
                    if (!touches) continue;

                    checkedPairs++;
                    Assert.That(lineA, Is.EqualTo(lineB).Within(0.01f),
                        $"'{a.Name}' sits at {(a.AlongX ? "z" : "x")}={lineA:0.##} but the collinear, " +
                        $"touching '{b.Name}' sits at {(b.AlongX ? "z" : "x")}={lineB:0.##} — a jogged " +
                        "fence, the MV-418 orphan stub pattern");
                }
            }

            Assert.Greater(checkedPairs, 0,
                "no collinear touching wall pairs were found at all — the adjacency scan itself is broken");
        }
    }
}
