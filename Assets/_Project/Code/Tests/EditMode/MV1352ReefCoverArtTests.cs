using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1352 (Reef relook 4/4): every cover dressing the ticket names builds at least 8 renderers (the
    /// base pillar builds 7, the base console 4-6 ...), none of them from a built-in primitive mesh, and the
    /// authored collider footprint/height is untouched (the crate's body collider resolves to the authored
    /// size after the skin; every kit piece is collider-free). Asserts RESOLVED values (Tier 2).
    /// Seventh case: the small (under 6 m^2) bulkhead, which must still reach 8 renderers.
    /// </summary>
    public sealed class MV1352ReefCoverArtTests
    {
        private static readonly Regex PrimitiveMeshName = new Regex(@"^(Cube|Cylinder|Sphere|Quad|Plane|Capsule)$");

        [Test]
        public void ReefCoverPieces_AreRichlyBuilt_AndKeepTheirAuthoredFootprints()
        {
            var host = new GameObject("MV1352 Probe Root");
            try
            {
                var crateSize = new Vector3(3f, 1.8f, 1.5f);
                GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crate.name = "mv1352_crate";
                crate.transform.SetParent(host.transform, false);
                crate.transform.position = new Vector3(0f, crateSize.y * 0.5f, 0f);
                // The shape MapRuntime hands ApplyCrateSkin: scale one, collider size carries the metres.
                crate.GetComponent<BoxCollider>().size = crateSize;
                ReefKit.ApplyCrateSkin(crate, true);

                Bounds cb = crate.GetComponent<BoxCollider>().bounds;
                Assert.AreEqual(crateSize.x, cb.size.x, 0.01f, "crate collider width");
                Assert.AreEqual(crateSize.y, cb.size.y, 0.01f, "crate collider height");
                Assert.AreEqual(crateSize.z, cb.size.z, 0.01f, "crate collider depth");

                (string name, GameObject piece)[] pieces =
                {
                    ("crate", crate),
                    ("pillar", ReefKit.BuildPillar(host.transform, new Vector3(10f, 1.5f, 0f), new Vector3(2f, 3f, 2f))),
                    ("console", ReefKit.BuildConsole(host.transform, new Vector3(20f, 0.5f, 0f), new Vector3(2f, 1f, 1f))),
                    ("drum", ReefKit.BuildDrum(host.transform, new Vector3(30f, 1.2f, 0f), new Vector3(3f, 2.4f, 3f))),
                    ("bulkhead", ReefKit.BuildBulkhead(host.transform, "a3_h1", new Vector3(40f, 1.5f, 0f), new Vector3(8f, 3f, 2f))),
                    ("floor pipe", ReefKit.BuildFloorPipe(host.transform, new Vector3(50f, 0.25f, 0f), new Vector3(4f, 0.5f, 1f))),
                    ("small bulkhead", ReefKit.BuildBulkhead(host.transform, "a1_h2", new Vector3(60f, 1.5f, 0f), new Vector3(2f, 3f, 1f))),
                };

                foreach ((string name, GameObject piece) in pieces)
                {
                    Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);
                    Assert.GreaterOrEqual(renderers.Length, 8, $"{name}: only {renderers.Length} renderers");

                    foreach (MeshFilter mf in piece.GetComponentsInChildren<MeshFilter>(true))
                        if (mf.sharedMesh != null)
                            Assert.IsFalse(PrimitiveMeshName.IsMatch(mf.sharedMesh.name), $"{name}: '{mf.name}' uses built-in mesh {mf.sharedMesh.name}");

                    if (piece != crate)
                        Assert.AreEqual(0, piece.GetComponentsInChildren<Collider>(true).Length, $"{name}: kit pieces must be collider-free");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
