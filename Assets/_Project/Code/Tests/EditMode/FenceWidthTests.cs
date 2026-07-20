using MaxWorlds.Arena;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-112 — the fences were too chunky.
    ///
    /// Worth being precise about WHAT was chunky, because the ticket's framing ("keep collision in
    /// sync with the new visual") assumes the fence dressing is what stops the player, and it isn't.
    /// The paling panels are scenery with every collider stripped; what the player collides with —
    /// and what dominates the silhouette from a 72° camera, because you see its whole top face — is
    /// the map's wall slab. That slab is one object, so its collision and its visual cannot drift
    /// apart: thinning it thins both, which is what the AC actually wants.
    ///
    /// These pin the relationships a width change can quietly break.
    /// </summary>
    public class FenceWidthTests
    {
        private static MapData Shipped() => MapLibrary.Load(MapLibrary.BackyardSlice);

        [Test]
        public void ShippedWallIsAboutFortyPercentNarrowerThanItWas()
        {
            // Was 1.0 m. ~40% off puts it at 0.6.
            MapData map = Shipped();

            Assert.That(map.wallThickness, Is.EqualTo(0.6f).Within(0.05f));
        }

        [Test]
        public void PalingFitsInsideTheWallItDresses()
        {
            // A panel deeper than the slab behind it pokes out the far side and reads as a fence
            // built through the fence — the failure mode of thinning the wall but not the paling.
            MapData map = Shipped();

            Assert.That(BackyardDressingSet.PanelDepth, Is.LessThan(map.wallThickness));
            Assert.That(BackyardDressingSet.PostWidth, Is.LessThan(map.wallThickness));
        }

        [Test]
        public void PalingStillStandsProudOfTheSlab()
        {
            // FenceRun sinks each panel by (PanelDepth/2 - 0.02), leaving 2 cm proud so it never
            // z-fights the slab. If PanelDepth ever fell to 4 cm or below, that sink would swallow
            // the panel and the fence would vanish into the wall.
            Assert.That(BackyardDressingSet.PanelDepth * 0.5f, Is.GreaterThan(0.02f),
                "panel is too thin to stand proud of its wall");
        }

        [Test]
        public void PostsAreNoChunkierThanThePanelsTheyJoin()
        {
            // The posts were the other half of "chunky": leaving them at full width while the panels
            // slimmed would make every corner the heaviest thing in the yard.
            Assert.That(BackyardDressingSet.PostWidth,
                Is.LessThanOrEqualTo(BackyardDressingSet.PanelDepth * 1.5f));
        }

        [Test]
        public void ThinnerWallsDoNotShrinkTheRoomsYouFightIn()
        {
            // Walls sit at the zone boundary and are offset outward, so the playable rectangle is the
            // zone rect whatever the thickness. If that ever stopped being true, thinning the fence
            // would silently resize every room in the level.
            MapData map = Shipped();

            foreach (MapZone zone in map.zones)
            {
                if (zone == null) continue;
                Assert.That(zone.XMax - zone.XMin, Is.GreaterThan(0f));
                Assert.That(zone.ZMax - zone.ZMin, Is.GreaterThan(0f));
            }

            // Every wall the geometry derives is exactly one thickness deep in its narrow axis —
            // the property that makes "thin the wall" a pure width change.
            foreach (WallSegment w in MapGeometry.Walls(map))
            {
                float narrow = w.AlongX ? w.Size.z : w.Size.x;
                Assert.That(narrow, Is.EqualTo(map.wallThickness).Within(1e-3f), w.Name);
            }
        }

        [Test]
        public void TheWallIsStillThickEnoughToReadAsAFence()
        {
            // Readability outranks visual richness: a fence thinned to a sliver stops reading as a
            // boundary from 30 m up, which is the opposite of the fix.
            MapData map = Shipped();

            Assert.That(map.wallThickness, Is.GreaterThan(0.3f));
        }
    }
}
