using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-778: 87 <c>GameObject.CreatePrimitive</c> calls across the environment, all axis-aligned,
    /// unbevelled Unity cubes, and <c>StylizedSurface.shader</c> — which every wall, floor, kerb, pipe
    /// and kit prop wears — carrying no outline pass, unlike the character shader.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1: at most one new test per ticket), asserting
    /// RESOLVED values only (Rule 2, Tier 2): a chamfered mesh actually has more geometry than a flat
    /// cube and does not inflate the box it was asked for; a wall built by <see cref="MapGeometry.Walls"/>
    /// actually wears that mesh, not the built-in primitive one; the outline is actually switched on for
    /// a wall material's resolved <c>_OutlineOn</c> and off for the ground's; and the axis-break rotation
    /// is actually a function of position (two different positions resolve to different yaws; the same
    /// position resolves to the same yaw on a second pass), not a fixed constant or a random re-roll.
    /// </summary>
    public sealed class MV778SilhouetteTests
    {
        [Test]
        public void SilhouetteFix_ResolvesBevelledMeshWallMaterialAndAxisBreak()
        {
            // ---- 1. Bevelled() chamfers inward; it does not inflate the box ----
            Mesh bevelled = CharacterMeshes.Bevelled(new Vector3(1f, 1f, 1f), 0.05f);

            Assert.Greater(bevelled.vertexCount, 24,
                $"a plain Unity cube has exactly 24 vertices; a chamfered one must have more " +
                $"({bevelled.vertexCount} found) or the edges were never actually cut.");
            Assert.That(bevelled.bounds.size.x, Is.EqualTo(1f).Within(0.001f), "the bevel inflated the box on X.");
            Assert.That(bevelled.bounds.size.y, Is.EqualTo(1f).Within(0.001f), "the bevel inflated the box on Y.");
            Assert.That(bevelled.bounds.size.z, Is.EqualTo(1f).Within(0.001f), "the bevel inflated the box on Z.");

            // ---- 2. a built wall segment actually wears a chamfered mesh, not the primitive cube ----
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var wallHost = new GameObject("MV778 wall host").transform;
            try
            {
                MapRuntime.Build(map, wallHost);

                StructuralWall[] walls = wallHost.GetComponentsInChildren<StructuralWall>(true);
                Assert.IsNotEmpty(walls, "World 2 must build at least one structural wall for this test to mean anything");

                Mesh primitiveCube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                foreach (StructuralWall w in walls)
                {
                    var mf = w.GetComponent<MeshFilter>();
                    Assert.IsNotNull(mf, $"{w.name} carries no MeshFilter to resolve a mesh from");
                    Assert.AreNotSame(primitiveCube, mf.sharedMesh,
                        $"{w.name} still wears the flat built-in primitive cube mesh.");
                    Assert.Greater(mf.sharedMesh.vertexCount, 24,
                        $"{w.name}'s mesh has only {mf.sharedMesh.vertexCount} vertices — that is a plain " +
                        "cube's count, not a chamfered one.");
                }
            }
            finally
            {
                Object.DestroyImmediate(wallHost.gameObject);
            }

            // ---- 3. the outline is on for a wall, off for the ground ----
            Material wallMat = MaterialLibrary.Surface(SurfaceKind.Wall);
            Assert.IsNotNull(wallMat, "the wall surface has no material at all");
            Assert.IsTrue(wallMat.HasProperty("_OutlineOn"), "the wall's shader carries no _OutlineOn property");
            Assert.AreEqual(1f, wallMat.GetFloat("_OutlineOn"), "every wall must opt into the world outline pass");

            Material groundMat = MaterialLibrary.Surface(SurfaceKind.Ground);
            Assert.IsNotNull(groundMat, "the ground surface has no material at all");
            float groundOutline = groundMat.HasProperty("_OutlineOn") ? groundMat.GetFloat("_OutlineOn") : 0f;
            Assert.AreEqual(0f, groundOutline,
                "the floor has no silhouette to draw and must never carry an outline");

            // ---- 4. the axis-break rotation is a function of position: differs by position, repeats by
            // re-dressing the same position ----
            var coverA = new ArenaCover("A", new Vector2(9.3f, 11.7f), new Vector3(1f, 1.6f, 1f),
                CoverShape.Box, CoverDressing.Tree);
            var coverB = new ArenaCover("B", new Vector2(41.6f, 26.2f), new Vector3(1f, 1.6f, 1f),
                CoverShape.Box, CoverDressing.Tree);

            var dressingHost = new GameObject("MV778 dressing host").transform;
            GameObject bodyA = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject bodyB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var cover = new List<CoverPiece> { new CoverPiece(coverA, bodyA), new CoverPiece(coverB, bodyB) };

                StormdrainDressing.Dress(dressingHost, map, cover);
                Transform[] standpipes = dressingHost.Find("Stormdrain Dressing/Cover")
                    .GetComponentsInChildren<Transform>(true).Where(t => t.name == "Standpipe").ToArray();
                Assert.AreEqual(2, standpipes.Length, "both Tree-dressed cover pieces must build a standpipe");

                float yawA = standpipes[0].eulerAngles.y;
                float yawB = standpipes[1].eulerAngles.y;
                Assert.AreNotEqual(yawA, yawB,
                    "two standpipes at different positions must not land at the exact same yaw — the " +
                    "axis-break rotation is not being derived from position at all.");

                // Re-dressing the same map with the same cover list must REPRODUCE, not re-roll, the
                // same piece's rotation — the formula is hashed from position, never Random.
                StormdrainDressing.Dress(dressingHost, map, cover);
                Transform[] standpipesAgain = dressingHost.Find("Stormdrain Dressing/Cover")
                    .GetComponentsInChildren<Transform>(true).Where(t => t.name == "Standpipe").ToArray();
                Assert.AreEqual(2, standpipesAgain.Length);
                Assert.That(standpipesAgain[0].eulerAngles.y, Is.EqualTo(yawA).Within(0.01f),
                    "dressing the same map twice must not re-roll the same standpipe's rotation.");
            }
            finally
            {
                Object.DestroyImmediate(dressingHost.gameObject);
                Object.DestroyImmediate(bodyA);
                Object.DestroyImmediate(bodyB);
            }
        }
    }
}
