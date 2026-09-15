using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-797: <c>StormdrainKit.DressSludgeTile</c> built each chevron pair's apex at negative local Z —
    /// upstream — while the bands and foam scroll downstream (+flow), so the "&lt;"/"&gt;" chevrons
    /// pointed against the direction the sludge visibly flows. Fails on base commit f89c4e7 (see the fix
    /// comment for the captured failure output).
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting a RESOLVED value (Rule 2, Tier
    /// 2): for a tile built with a non-axis-aligned flow direction, each chevron pair's apex — the point
    /// where its two legs' RESOLVED world transforms (<see cref="Transform.TransformPoint"/> of the built
    /// mesh's own bounds extremes, not the authored offset constants) come closest together — must sit
    /// downstream of the midpoint of that pair's two open ends by at least 0.5m along the flow axis.
    /// </summary>
    public sealed class MV797ChevronDirectionTests
    {
        [Test]
        public void ChevronApex_SitsDownstreamOfItsOpenEnds_AlongTheFlowAxis()
        {
            var host = new GameObject("MV797 host").transform;
            try
            {
                // Non-axis-aligned so the test can't pass by accident of X/Z symmetry.
                Vector3 flow = new Vector3(1f, 0f, 1f).normalized;

                // Generously large so no chevron pair sits within its own half-extent of a tile edge —
                // SludgeFlowRig would otherwise clip/rescale a pair's legs (MV-796) and disturb the very
                // geometry this test measures.
                SludgeFlowRig rig = StormdrainKit.DressSludgeTile(host, Vector3.zero, 30f, 30f, flow, seed: 7);

                Transform chevrons = rig.transform.Find("Chevrons");
                Assert.IsNotNull(chevrons, "a built sludge tile must carry a 'Chevrons' group");
                Assert.Greater(chevrons.childCount, 0, "a built sludge tile must carry at least one chevron pair");
                Assert.AreEqual(0, chevrons.childCount % 2, "chevron legs must come in L/R pairs");

                for (int i = 0; i < chevrons.childCount; i += 2)
                {
                    Transform legL = chevrons.GetChild(i);
                    Transform legR = chevrons.GetChild(i + 1);

                    Vector3[] l = LegWorldEndpoints(legL);
                    Vector3[] r = LegWorldEndpoints(legR);

                    // The two legs of a pair meet near one shared endpoint (the apex) and splay apart at
                    // the other (the open ends) — find the closest cross-leg endpoint pair to identify
                    // which is which, rather than assuming an index.
                    int bestL = 0, bestR = 0;
                    float bestDist = float.MaxValue;
                    for (int a = 0; a < 2; a++)
                    {
                        for (int b = 0; b < 2; b++)
                        {
                            float d = Vector3.Distance(l[a], r[b]);
                            if (d < bestDist) { bestDist = d; bestL = a; bestR = b; }
                        }
                    }

                    Vector3 apex = (l[bestL] + r[bestR]) * 0.5f;
                    Vector3 openMid = (l[1 - bestL] + r[1 - bestR]) * 0.5f;

                    float downstream = Vector3.Dot(apex - openMid, flow);
                    Assert.Greater(downstream, 0.5f,
                        $"chevron pair {i / 2}: apex (resolved {apex}) must sit downstream of its open " +
                        $"ends' midpoint (resolved {openMid}) by at least 0.5m along flow {flow} " +
                        $"(resolved {downstream:F3}m)");
                }
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static Vector3[] LegWorldEndpoints(Transform leg)
        {
            Bounds localBounds = leg.GetComponent<MeshFilter>().sharedMesh.bounds;
            return new[]
            {
                leg.TransformPoint(new Vector3(localBounds.max.x, 0f, 0f)),
                leg.TransformPoint(new Vector3(localBounds.min.x, 0f, 0f)),
            };
        }
    }
}
