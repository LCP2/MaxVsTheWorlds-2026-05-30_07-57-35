using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1274 AC7 (one new EditMode test, MV-465 Rule 1): the ticket's own "generated meshes
    /// (Lathe/Prism/Beam per CharacterMeshes.cs technique), never Unity primitives" — same "no
    /// primitives" contract <see cref="MV786NoBoxesTests"/> and <see cref="MV1259ShipFloorAtlasTests"/>
    /// already hold their own dressing to.
    ///
    /// Fails on base commit (871e3b7): <see cref="ShipKit.BuildSeams"/> (called by
    /// <see cref="ShipKit.BuildAtlasFloor"/> for every Hangar area's own floor) draws its floor-grid
    /// "Seam Glow" lines with a bare <see cref="StormdrainKit.Glow"/> — a Unity <c>PrimitiveType.Quad</c>
    /// whose mesh is never swapped out afterward, unlike every other glow/lamp piece in
    /// <c>ShipKit.cs</c>, which all route through <see cref="CharacterMeshes"/>. See
    /// Logs/MV1274-failfirst.log, quoted in the fix comment, for this test's own failing output.
    ///
    /// Calls <see cref="ShipKit.BuildAtlasFloor"/>/<see cref="ShipKit.BuildBulkheadPanel"/> DIRECTLY on a
    /// throwaway host, rather than through <see cref="MaxWorlds.Arena.ShipDressing.Dress"/> — that full
    /// production pipeline ends with <c>StaticBatchingUtility.Combine</c>, which replaces every combined
    /// renderer's own <c>MeshFilter.sharedMesh</c> with a synthetic "Combined Mesh (root: ...)" reference
    /// (confirmed empirically: this test's own first draft, run through the full pipeline, read back
    /// "Combined Mesh (root: Ship Floors) N" for every mesh under a built floor, on BOTH the buggy and
    /// the fixed tree — the static-batch step erases the very evidence a no-primitives check needs). The
    /// two <see cref="ShipKit"/> calls below are still a resolved value (testing policy Rule 2, Tier 2 —
    /// the kit's own real built mesh references, never an authored constant); they are simply read before
    /// a later, unrelated pass (static batching) overwrites them.
    ///
    /// One consolidated test (testing policy Rule 1) asserting RESOLVED state only: no
    /// <see cref="MeshFilter"/> under a directly-built Hangar floor or wall run carries Unity's built-in
    /// Cube/Cylinder/Sphere/Quad/Plane primitive mesh — <see cref="MV786NoBoxesTests"/>'s own regex,
    /// widened to include "Sphere" (not just "Cylinder/Quad/Cube/Plane"), since a lamp/eye glow piece in
    /// this kit could just as easily have been left on a primitive sphere.
    /// </summary>
    public sealed class MV1274HangarNoPrimitivesTests
    {
        private static readonly Regex PrimitiveMeshName = new Regex(@"^(Cube|Cylinder|Sphere|Quad|Plane)$");

        [Test]
        public void HangarFloorAndWallBuilders_GenerateNoPrimitiveMeshes()
        {
            var host = new GameObject("MV1274 Host").transform;
            try
            {
                // A 20x20 m area — bigger than BuildSeams' own 8 m seam spacing on both axes, so its
                // seam-glow lines actually get built (a smaller area would silently build none, and the
                // test would pass for the wrong reason). areaIndex 1 = Hangar Bay itself.
                var floorArea = new Rect(0f, 0f, 20f, 20f);
                GameObject floor = ShipKit.BuildAtlasFloor(host, floorArea, areaIndex: 1, seamGlow: Color.yellow);
                AssertNoPrimitiveMeshes(floor.transform, "a directly-built Hangar floor");

                // A 12 m wall run — 4 panels at BuildBulkheadPanel's own ~3 m pitch, so it exercises the
                // wall-top cap/rib/lamp/rack pass fully.
                GameObject wall = ShipKit.BuildBulkheadPanel(host, new Vector2(0f, 0f), new Vector2(12f, 0f),
                    inward: new Vector3(0f, 0f, 1f), wallHeight: 3f, wallThickness: 0.3f,
                    deckGlow: Color.yellow, isOuterEdge: true, wallSeed: 1);
                AssertNoPrimitiveMeshes(wall.transform, "a directly-built Hangar wall run");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static void AssertNoPrimitiveMeshes(Transform root, string label)
        {
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Assert.IsNotNull(mf.sharedMesh, $"{mf.name} under {label} carries no mesh");
                Assert.IsFalse(PrimitiveMeshName.IsMatch(mf.sharedMesh.name),
                    $"{mf.name} under {label} still wears the built-in primitive mesh '{mf.sharedMesh.name}'");
            }
        }
    }
}
