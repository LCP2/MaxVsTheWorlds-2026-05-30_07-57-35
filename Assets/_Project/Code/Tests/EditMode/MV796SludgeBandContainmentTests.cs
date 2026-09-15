using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-796: <c>SludgeFlowRig.Apply</c> positioned every band/chevron/foam clump by its own CENTRE
    /// only, letting up to half its own extent hang off the tile — up to 3.2m of a 6.4m band — drawing
    /// over the pavement at <c>StormdrainKit.SludgeBandY</c>, then teleporting through the tile wall on
    /// wrap. Fails on base commit f89c4e7 (see the fix comment for the captured failure output).
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting a RESOLVED value (Rule 2, Tier
    /// 2): every child piece's actual <see cref="Renderer.bounds"/> — not the authored length/radius
    /// constants — stays inside the tile's own footprint along the flow axis, sampled at every step of
    /// a sweep across more than one full wrap.
    /// </summary>
    public sealed class MV796SludgeBandContainmentTests
    {
        [Test]
        public void SludgeFlowPieces_StayInsideTheTileFootprint_AcrossAFullWrap()
        {
            var host = new GameObject("MV796 host").transform;
            try
            {
                const float width = 5f, depth = 8f;
                Vector3 center = Vector3.zero;
                SludgeFlowRig rig = StormdrainKit.DressSludgeTile(host, center, width, depth, Vector3.forward, seed: 11);

                // Flow is Vector3.forward, so the run (the axis pieces scroll and wrap along) is the
                // tile's own Z extent — depth.
                float halfRun = depth * 0.5f;
                float minZ = center.z - halfRun;
                float maxZ = center.z + halfRun;
                const float tolerance = 0.01f;

                Transform bands = rig.transform.Find("Bands");
                Transform chevrons = rig.transform.Find("Chevrons");
                Transform foam = rig.transform.Find("Foam");

                // A full wrap at the fast (band/chevron) speed of 0.35 m/s across an 8m run takes ~22.9s;
                // step small enough (0.2s, 0.07m per step at the fast speed) to sample every piece
                // continuously through both its entry and exit at the tile wall, not just land on
                // either side of it.
                const float dt = 0.2f;
                const float totalTime = 24f; // > one full wrap of the 8m run at 0.35 m/s
                int steps = Mathf.CeilToInt(totalTime / dt);

                for (int step = 0; step < steps; step++)
                {
                    rig.Tick(dt);
                    AssertGroupContained(bands, minZ, maxZ, tolerance);
                    AssertGroupContained(chevrons, minZ, maxZ, tolerance);
                    AssertGroupContained(foam, minZ, maxZ, tolerance);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host.gameObject);
            }
        }

        private static void AssertGroupContained(Transform group, float minZ, float maxZ, float tolerance)
        {
            for (int i = 0; i < group.childCount; i++)
            {
                Transform piece = group.GetChild(i);
                Renderer renderer = piece.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled) continue; // MV-796: a fully-clipped piece is hidden, not drawn

                Bounds b = renderer.bounds;
                Assert.GreaterOrEqual(b.min.z, minZ - tolerance,
                    $"{group.name}/{piece.name} must not draw before the tile's own start " +
                    $"(bounds.min.z={b.min.z:F4}, tile min={minZ:F4})");
                Assert.LessOrEqual(b.max.z, maxZ + tolerance,
                    $"{group.name}/{piece.name} must not draw past the tile's own end " +
                    $"(bounds.max.z={b.max.z:F4}, tile max={maxZ:F4})");
            }
        }
    }
}
