using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1369 (one new test, replacing MV1242HelmsmanLookTests which asserted the old 3.0 m box body):
    /// the rebuilt Helmsman rig uses no built-in primitive mesh, exposes exactly eight LensNode
    /// transforms, and resolves to a body at least 4.8 m wide (Tier 2: combined renderer bounds).
    ///
    /// Fails on base commit ea74fe6: HelmsmanRig builds a 3.0 m box-and-throne with no LensNode
    /// transforms, so the node count is 0 and the resolved width is 3.0 m.
    /// </summary>
    public sealed class MV1369HelmsmanWheelBodyTests
    {
        private const float AuthoredBodyWidth = 4.5f;

        [Test]
        public void HelmsmanRig_IsGeneratedWheel_WithEightLensNodes_AtLeast4p8mWide()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var strayCollider = go.GetComponent<BoxCollider>();
            if (strayCollider != null) Object.DestroyImmediate(strayCollider);
            go.transform.localScale = Vector3.one * AuthoredBodyWidth;
            var boss = go.AddComponent<HelmsmanBoss>();

            HelmsmanRig rig = HelmsmanRig.CreateFor(boss);
            try
            {
                Assert.IsNotNull(rig, "HelmsmanBoss must build a HelmsmanRig");

                Mesh[] builtin =
                {
                    Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
                    Resources.GetBuiltinResource<Mesh>("Sphere.fbx"),
                    Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"),
                    Resources.GetBuiltinResource<Mesh>("Quad.fbx"),
                    Resources.GetBuiltinResource<Mesh>("Plane.fbx"),
                };
                foreach (MeshFilter mf in rig.GetComponentsInChildren<MeshFilter>(true))
                    Assert.IsFalse(System.Array.IndexOf(builtin, mf.sharedMesh) >= 0,
                        $"{mf.name} uses a built-in primitive mesh ({mf.sharedMesh.name}); generated meshes only");

                int nodes = 0;
                foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
                    if (Regex.IsMatch(t.name, @"^LensNode[0-7]$")) nodes++;
                Assert.AreEqual(8, nodes, $"exactly 8 LensNode0..7 transforms expected, found {nodes}");

                var renderers = rig.BodyRoot.GetComponentsInChildren<MeshRenderer>();
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                Assert.GreaterOrEqual(b.size.x, 4.8f, $"resolved body width must be >= 4.8 m, measured {b.size.x:F3}");
            }
            finally
            {
                if (rig != null) Object.DestroyImmediate(rig.gameObject);
                Object.DestroyImmediate(go);
            }
        }
    }
}
