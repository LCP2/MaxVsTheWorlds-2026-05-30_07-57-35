using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-938: World 2's sludge flow dressing (bands, chevron pairs, foam) used to be 43 separately
    /// animated Transforms per tile, ticked every frame by the now-removed <c>SludgeFlowRig</c> — at
    /// World 2's a10 alone, ~2,000 renderers that could never be mesh-combined (MV-934). This fails to
    /// COMPILE on the base commit this branch was cut from: <c>StormdrainKit.DressSludgeTile</c> returns
    /// <c>SludgeFlowRig</c> there, not <c>GameObject</c>, and carries no single "Flow" child at all — the
    /// old build instead created "Bands"/"Chevrons"/"Foam" groups holding 10/24/9 separate renderers.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values (Rule 2, Tier 2,
    /// never a presence check per Rule 3): the flow dressing resolves to exactly ONE MeshRenderer with a
    /// single-submesh mesh (not one renderer per piece), and that mesh's own computed bounds — read back
    /// off the actual built geometry, not an authored constant — genuinely span the tile's own run and
    /// span rather than some degenerate placeholder quad.
    /// </summary>
    public sealed class MV938SludgeFlowMeshTests
    {
        [Test]
        public void DressSludgeTile_FlowDressing_IsOneRenderMesh()
        {
            var host = new GameObject("MV938 host").transform;
            try
            {
                const float width = 6f, depth = 20f;
                GameObject root = StormdrainKit.DressSludgeTile(host, Vector3.zero, width, depth, Vector3.forward, seed: 5);

                Transform flow = root.transform.Find("Flow");
                Assert.IsNotNull(flow, "a built sludge rect must carry a 'Flow' group for its bands/chevron/foam dressing");

                MeshRenderer[] renderers = flow.GetComponentsInChildren<MeshRenderer>(true);
                Assert.AreEqual(1, renderers.Length,
                    "the flow dressing (bands+chevrons+foam) must resolve to exactly one MeshRenderer, not one per piece");

                Mesh mesh = flow.GetComponent<MeshFilter>()?.sharedMesh;
                Assert.IsNotNull(mesh, "the flow renderer must carry a built mesh");
                Assert.AreEqual(1, mesh.subMeshCount, "the flow mesh must be a single render mesh (one submesh)");

                // Resolved value: the mesh's own computed bounds (from its actual vertex data, via
                // Mesh.RecalculateBounds) must genuinely span the tile's run/span, not a degenerate or
                // mis-sized placeholder quad.
                Bounds b = mesh.bounds;
                Assert.AreEqual(depth, b.size.z, 0.01f,
                    $"resolved flow mesh bounds must span the tile's own run ({depth}m) along the flow axis, resolved {b.size}");
                Assert.AreEqual(width, b.size.x, 0.01f,
                    $"resolved flow mesh bounds must span the tile's own span ({width}m) across the flow axis, resolved {b.size}");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
