using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1251: <see cref="ReefKit"/>'s structure-kit builders (MV-1215, Lee's 17 Oct 2026 concept-frame
    /// approval) still built every piece from <c>GameObject.CreatePrimitive(PrimitiveType.Cube)</c> — Lee's
    /// own TestFlight screenshot (0.15.0) read as plain grey slabs "glued together from cardboard boxes".
    ///
    /// Fails on the base commit (359b4a2): every builder below still assigns Unity's own built-in cube
    /// mesh (name "Cube") to at least one <see cref="MeshFilter"/>, so the final <c>Assert.IsEmpty</c>
    /// fails with every offending renderer's own name listed.
    ///
    /// Builds one of each kit piece directly through the production <see cref="ReefKit"/> builders
    /// (pillar, manifold, drum, console, planter, floor pipe, glow hatch, wall racks, wall planters, wall
    /// pipes, observation window), plus a production crate through the real <see cref="MapRuntime"/> +
    /// <see cref="ReefDressing"/> pipeline (the same "through the production cover-build path, not a
    /// hand-rolled substitute" idiom <see cref="MV1215ReefStructureKitTests"/> already uses) so
    /// <see cref="ReefKit.ApplyCrateSkin"/> runs against a real, collider-bearing body rather than a
    /// stand-in — and asserts RESOLVED state only (testing policy Rule 2/3): walking every built
    /// <see cref="MeshFilter"/> and checking its own resolved <c>sharedMesh.name</c> is never Unity's
    /// built-in "Cube".
    /// </summary>
    public sealed class MV1251ReefStructureKitGeneratedMeshesTests
    {
        // Distinctive, far-off origin for the one piece (the crate) that goes through the real map
        // pipeline and so gets a live collider — same reasoning MV745/MV863/MV917/MV1215's own fixtures
        // give: EditMode tests share one physics scene for the whole cc-verify run, with no per-test
        // reset. The other eleven pieces below are collider-free dressing built directly at the world
        // origin, so they need no such isolation.
        private static readonly Vector3 RigOrigin = new Vector3(-74600f, 0f, 61200f);

        [Test]
        public void EveryStructureKitPiece_BuildsWithNoPrimitiveCubeMesh()
        {
            var root = new GameObject("MV-1251 Reef Structure Kit Generated-Mesh Probe Root");
            try
            {
                ReefKit.BuildPillar(root.transform, new Vector3(0f, 0f, 0f), new Vector3(2f, 3f, 2f));
                ReefKit.BuildManifold(root.transform, new Vector3(6f, 0f, 0f), new Vector3(4f, 1.8f, 2f));
                ReefKit.BuildDrum(root.transform, new Vector3(14f, 0f, 0f), new Vector3(3f, 2.4f, 3f));
                ReefKit.BuildConsole(root.transform, new Vector3(22f, 0f, 0f), new Vector3(2f, 1f, 1f));
                ReefKit.BuildPlanterRow(root.transform, new Vector3(28f, 0f, 0f), new Vector3(1f, 0.9f, 3f));
                ReefKit.BuildFloorPipe(root.transform, new Vector3(34f, 0f, 0f), new Vector3(1f, 0.5f, 4f));
                ReefKit.BuildGlowHatch(root.transform, new Vector3(40f, 0f, 0f), 2f, 2f);
                ReefKit.BuildWallRacks(root.transform, new Vector3(46f, 0f, 0f), new Vector3(3f, 1.4f, 0.4f));
                ReefKit.BuildWallPlanters(root.transform, new Vector3(52f, 0f, 0f), new Vector3(1.2f, 1f, 0.3f));
                ReefKit.BuildWallPipes(root.transform, new Vector3(58f, 0f, 0f), new Vector3(3f, 0.6f, 0.3f));
                ReefKit.BuildObservationWindow(root.transform, new Vector3(64f, 0f, 0f), new Vector3(2f, 1.5f, 0.1f));

                BuildProductionCrate(root.transform);

                var cubeMeshFilterNames = root.GetComponentsInChildren<MeshFilter>(true)
                    .Where(mf => mf.sharedMesh != null && mf.sharedMesh.name == "Cube")
                    .Select(mf => mf.transform.GetHierarchyPath())
                    .ToList();

                Assert.IsEmpty(cubeMeshFilterNames,
                    "MV-1251 structure-kit pieces still built from Unity's primitive cube mesh: " +
                    string.Join(", ", cubeMeshFilterNames));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>One "crate" cover piece, built through the exact real load path
        /// (<see cref="MapRuntime.Build"/> then <see cref="ReefDressing.DressCover"/>) — same fixture
        /// shape as <see cref="MV1215ReefStructureKitTests"/>'s own <c>Probe</c>/<c>Piece</c> helpers —
        /// so <see cref="ReefKit.ApplyCrateSkin"/> runs against a real <see cref="BoxCollider"/>-bearing
        /// body, not a hand-rolled stand-in. Reparents the crate's own body under
        /// <paramref name="probeParent"/> and discards the rest of the map (its floor/walls are built
        /// by <see cref="MapRuntime"/> itself, not <see cref="ReefKit"/>, and are out of this ticket's
        /// "structure kit" scope) so the caller's own sweep covers exactly the crate's body and corner
        /// caps.</summary>
        private static void BuildProductionCrate(Transform probeParent)
        {
            var map = new MapData
            {
                name = "MV-1251 Crate Probe",
                zones = new[]
                {
                    new MapZone { id = "room", type = "open", x = RigOrigin.x, z = RigOrigin.z, width = 20f, depth = 20f },
                },
                entities = new[]
                {
                    new MapEntity
                    {
                        id = "crate", kind = "cover",
                        x = RigOrigin.x, z = RigOrigin.z,
                        width = 1.5f, height = 1.5f, depth = 1.5f,
                        shape = "box", dressing = "reef_crate", coverKind = "solid",
                    },
                },
            };

            var host = new GameObject("MV-1251 Crate Probe Root");
            MapBuild built = MapRuntime.Build(map, host.transform);
            ReefDressing.DressCover(host.transform, built.Cover);

            CoverPiece crate = built.Cover.Find(p => p.Cover.Name == "crate");
            Assert.IsNotNull(crate.Body, "the probe's crate cover piece was never built");
            crate.Body.transform.SetParent(probeParent, true);
            Object.DestroyImmediate(host);
        }
    }

    internal static class TransformHierarchyPathExtensions
    {
        /// <summary>A short, readable path ("Root/Child/Grandchild") for a failure message — a bare
        /// GameObject name alone is ambiguous when several built pieces each have their own "Body" or
        /// "CrateCap" child.</summary>
        public static string GetHierarchyPath(this Transform t)
        {
            if (t == null) return "<null>";
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }
    }
}
