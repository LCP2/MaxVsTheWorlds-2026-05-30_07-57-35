using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-818: World 2's cover dressing drew one small prop at the centre of a long invisible
    /// collider — e.g. a3's 10 m planter (<c>a3_cover3</c>) rendered as a single ~1 m silt hopper, so
    /// Max had to walk around a shape he could not see even though the greybox floor read as open.
    /// Fails on 302e10e: <c>StormdrainKit.BuildSiltHopper</c>/<c>BuildStandpipe</c>/
    /// <c>BuildPumpHousing</c> size themselves off <c>Mathf.Min(size.x, size.z)</c> alone, and
    /// <c>BuildCollapsedGrating</c>/<c>BuildBurstMain</c> both fall short of the collider's own 1.6 m
    /// height — see the fix comment for the captured failure output.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2): dresses every World 2 cover entity for real via <see cref="StormdrainDressing.Dress"/>
    /// and reads back the combined <see cref="Renderer.bounds"/> under each piece's own built art
    /// against that piece's own authored collider footprint and height — never an authored constant,
    /// never a rendered pixel. Reports every failing id at once (Rule 3): a single early failure would
    /// hide the other twenty-one aspect &gt;= 2 pieces the ticket's own diagnosis counted.
    /// </summary>
    public sealed class MV818CoverFootprintTests
    {
        private const float MinCoverageFraction = 0.90f;
        private const float MaxOverrunMetres = 0.15f;
        private const float MinVisibleHeight = 1.0f;
        private const float Epsilon = 0.001f;

        [Test]
        public void EveryWorldTwoCoverEntity_ArtSpansItsOwnColliderFootprintAndClearsMinHeight()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV818 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                List<CoverPiece> pieces = build.Cover.Where(p => p.Body != null).ToList();
                Assert.IsNotEmpty(pieces, "World 2 must author at least one cover piece for this test to mean anything");

                StormdrainDressing.Dress(host, map, build.Cover);

                Transform coverHost = host.Find("Stormdrain Dressing/Cover");
                Assert.IsNotNull(coverHost, "the cover dressing host was never built");

                var artRoots = coverHost.Cast<Transform>().ToList();
                Assert.AreEqual(pieces.Count, artRoots.Count,
                    "the dressing pass must build exactly one art root per dressed cover piece, in order");

                var failures = new List<string>();
                int aspectAtLeastTwo = 0;

                for (int i = 0; i < pieces.Count; i++)
                {
                    ArenaCover c = pieces[i].Cover;
                    float aspect = Mathf.Max(c.Size.x, c.Size.z) / Mathf.Max(0.01f, Mathf.Min(c.Size.x, c.Size.z));
                    if (aspect >= 2f) aspectAtLeastTwo++;

                    Renderer[] renderers = artRoots[i].GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0)
                    {
                        failures.Add($"{c.Name}: built no renderer at all");
                        continue;
                    }

                    Bounds b = renderers[0].bounds;
                    for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);

                    float minW = c.Size.x * MinCoverageFraction;
                    float maxW = c.Size.x + MaxOverrunMetres;
                    float minD = c.Size.z * MinCoverageFraction;
                    float maxD = c.Size.z + MaxOverrunMetres;

                    if (b.size.x < minW - Epsilon)
                        failures.Add($"{c.Name} ({c.Dressing}, {c.Size.x:F1}x{c.Size.z:F1}): width {b.size.x:F2} m " +
                                     $"covers under 90% of its {c.Size.x:F2} m collider");
                    if (b.size.x > maxW + Epsilon)
                        failures.Add($"{c.Name} ({c.Dressing}, {c.Size.x:F1}x{c.Size.z:F1}): width {b.size.x:F2} m " +
                                     $"overruns its {c.Size.x:F2} m collider by more than {MaxOverrunMetres:F2} m");
                    if (b.size.z < minD - Epsilon)
                        failures.Add($"{c.Name} ({c.Dressing}, {c.Size.x:F1}x{c.Size.z:F1}): depth {b.size.z:F2} m " +
                                     $"covers under 90% of its {c.Size.z:F2} m collider");
                    if (b.size.z > maxD + Epsilon)
                        failures.Add($"{c.Name} ({c.Dressing}, {c.Size.x:F1}x{c.Size.z:F1}): depth {b.size.z:F2} m " +
                                     $"overruns its {c.Size.z:F2} m collider by more than {MaxOverrunMetres:F2} m");
                    if (b.size.y < MinVisibleHeight - Epsilon)
                        failures.Add($"{c.Name} ({c.Dressing}, {c.Size.x:F1}x{c.Size.z:F1}): visible height " +
                                     $"{b.size.y:F2} m is under the {MinVisibleHeight:F1} m floor");
                }

                Assert.Greater(aspectAtLeastTwo, 0,
                    "World 2 must author at least one aspect >= 2 cover piece for this test to mean anything");
                Assert.IsEmpty(failures, $"MV-818 footprint/height violations ({failures.Count}):\n" + string.Join("\n", failures));
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
