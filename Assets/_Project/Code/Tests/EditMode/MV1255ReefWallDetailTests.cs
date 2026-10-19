using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1255: the Reef's room walls read as plain dark slabs (<see cref="ReefKit.DressHull"/>'s
    /// circuit spine and lamps aside) — no ribs, no panels, no conduit, against the design frames'
    /// ribbed bulkheads. Fails on the base commit: before this ticket's fix, <c>DressHull</c> builds
    /// no rib geometry at all for a wall it dresses, so the rib-count assertion below fails
    /// (0 ribs &lt; the expected minimum for a 9 m wall run) — see Logs/MV1255-failfirst.log, quoted in
    /// the fix comment.
    ///
    /// AC1's exact wording: "at least one rib per 3 m of resolved wall length" (resolved off the
    /// probe wall's own built <c>Transform.lossyScale</c>, never an authored constant) and "no
    /// MeshFilter under it uses the built-in Cube mesh" — the probe wall's own base renderer is built
    /// from a generated mesh too (<see cref="CharacterMeshes.Bevelled"/>), not <c>CreatePrimitive</c>,
    /// so the Cube check is never satisfied by accident.
    /// </summary>
    public sealed class MV1255ReefWallDetailTests
    {
        private const float WallLength = 9f;
        private const float WallHeight = 3f;
        private const float WallThickness = 0.3f;
        private const float RibPitchMetres = 3f;

        [Test]
        public void DressHull_BuildsRibsPerResolvedWallLength_UsingNoCubePrimitive()
        {
            var host = new GameObject("MV1255 Wall Run Probe");
            try
            {
                GameObject wallGo = new GameObject("Probe Wall");
                wallGo.transform.SetParent(host.transform, false);
                wallGo.transform.position = new Vector3(0f, WallHeight * 0.5f, 0f);
                wallGo.transform.localScale = new Vector3(WallLength, WallHeight, WallThickness);
                wallGo.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Bevelled(Vector3.one, 0.05f);
                wallGo.AddComponent<MeshRenderer>().sharedMaterial = WorldMaterials.M_MetalDark;
                wallGo.AddComponent<StructuralWall>().Configure(true);

                ReefKit.DressHull(host.transform);

                int ribCount = 0;
                foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Wall Rib") ribCount++;

                int expectedMinRibs = Mathf.FloorToInt(WallLength / RibPitchMetres);
                Assert.GreaterOrEqual(ribCount, expectedMinRibs,
                    $"expected at least one rib per {RibPitchMetres}m of resolved wall length " +
                    $"({WallLength}m -> >= {expectedMinRibs} ribs), found {ribCount}");

                foreach (MeshFilter mf in host.GetComponentsInChildren<MeshFilter>(true))
                    Assert.AreNotEqual("Cube", mf.sharedMesh != null ? mf.sharedMesh.name : null,
                        $"'{mf.name}' uses the built-in Cube primitive mesh — DressHull's wall dressing must use generated meshes only");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
