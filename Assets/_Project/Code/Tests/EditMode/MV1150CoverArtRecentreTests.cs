using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1150: <see cref="StormdrainDressing.FitFootprintAndHeight"/> scales a just-built dressing's
    /// art to fill <see cref="MaxWorlds.Rendering.StormdrainKit.CoverFootprintCoverage"/> of its
    /// collider's own w/d, but scales ABOUT the collider centre without first checking that the
    /// pre-fit art is itself centred there. A Shed/Machinery module's LED pool
    /// (<see cref="MaxWorlds.Rendering.StormdrainLightKit.BuildLedPanel"/>, radius
    /// <see cref="MaxWorlds.Rendering.StormdrainLightKit.LedPoolRadius"/>) hangs further off one face
    /// than the body does, so the measured bounds are not centred on the collider — the fitted art
    /// ends up the right SIZE and the wrong POSITION, overrunning one face while leaving the other
    /// bare.
    ///
    /// One new EditMode test (testing policy MV-465, Rule 1) asserting RESOLVED (Tier 2) position: a
    /// 4x2 "machinery" cover piece built through the real <see cref="MapRuntime"/>/
    /// <see cref="StormdrainDressing"/> path (aspect 2.0 takes the modular-run path, matching the
    /// ticket's own worked example) must end up with its combined built art centred on its own
    /// collider, not offset by the ~0.3 m the LED pool drags it. Never asserts
    /// <c>CoverFootprintCoverage</c>, <c>LedPoolRadius</c> or <c>CoverageThreshold</c> themselves —
    /// those are authored constants (Tier 1); this only reads back resolved world-space bounds.
    /// </summary>
    public sealed class MV1150CoverArtRecentreTests
    {
        [Test]
        public void MachineryCoverArt_EndsUpCentredOnItsOwnCollider_NotDraggedByTheLedPool()
        {
            var map = new MapData
            {
                entities = new[]
                {
                    new MapEntity
                    {
                        id = "test_cover_4x2",
                        kind = "cover",
                        x = 10f,
                        z = 5f,
                        width = 4f,
                        height = 1.6f,
                        depth = 2f,
                        shape = "box",
                        dressing = "machinery",
                    },
                },
            };

            var host = new GameObject("MV1150 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                CoverPiece piece = build.Cover.Single();
                Assert.IsNotNull(piece.Body, "the 4x2 machinery cover piece must build a body");

                StormdrainDressing.Dress(host, map, build.Cover);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Transform coverHost = host.Find("Stormdrain Dressing/Cover");
                Assert.IsNotNull(coverHost, "the cover dressing host was never built");
                Assert.AreEqual(1, coverHost.childCount, "exactly one art root for the one authored cover piece");

                Transform artRoot = coverHost.GetChild(0);
                Renderer[] renderers = artRoot.GetComponentsInChildren<Renderer>(true);
                Assert.IsNotEmpty(renderers, "the machinery piece must have built some visible art");

                Bounds art = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) art.Encapsulate(renderers[i].bounds);

                Bounds collider = piece.Body.GetComponent<Collider>().bounds;

                Assert.AreEqual(collider.center.x, art.center.x, 0.01f,
                    $"the fitted art's combined XZ bounds centre (x={art.center.x:F3}) must land on the " +
                    $"collider's own centre (x={collider.center.x:F3})");
                Assert.AreEqual(collider.center.z, art.center.z, 0.01f,
                    $"the fitted art's combined XZ bounds centre (z={art.center.z:F3}) must land on the " +
                    $"collider's own centre (z={collider.center.z:F3})");

                Assert.GreaterOrEqual(art.min.x, collider.min.x - 0.01f,
                    $"the fitted art must not overrun the collider's -X face (art min x={art.min.x:F3}, " +
                    $"collider min x={collider.min.x:F3})");
                Assert.LessOrEqual(art.max.x, collider.max.x + 0.01f,
                    $"the fitted art must not overrun the collider's +X face (art max x={art.max.x:F3}, " +
                    $"collider max x={collider.max.x:F3})");
                Assert.GreaterOrEqual(art.min.z, collider.min.z - 0.01f,
                    $"the fitted art must not overrun the collider's -Z face (art min z={art.min.z:F3}, " +
                    $"collider min z={collider.min.z:F3})");
                Assert.LessOrEqual(art.max.z, collider.max.z + 0.01f,
                    $"the fitted art must not overrun the collider's +Z face (art max z={art.max.z:F3}, " +
                    $"collider max z={collider.max.z:F3})");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
