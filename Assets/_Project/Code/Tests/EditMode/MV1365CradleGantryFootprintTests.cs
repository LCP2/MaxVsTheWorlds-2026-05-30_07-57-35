using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1365 — the Assembly Cradle must read as a solid gantry from above: a plinth under the frame,
    /// not a few loose red sticks on the floor. Tier 2 (resolved values): every assertion reads a
    /// renderer's own resolved world-space bounds / its own resolved mesh reference.
    /// Rule 1: fails on base ea74fe6 — the open 3 x 3 m frame leaves most of the footprint uncovered
    /// when seen from above, so the coverage assertion trips there.
    /// </summary>
    // Guards MV-1365
    public sealed class MV1365CradleGantryFootprintTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(-71234f, 0f, 38765f);
        private const float FootprintMetres = 3.3f;

        private GameObject _cradleGo;

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            if (_cradleGo != null) Object.DestroyImmediate(_cradleGo);
            RobotEnemy.ResetRegistry();
        }

        [Test]
        public void BuiltCradle_UsesOnlyGeneratedMeshes_AndCoversItsFootprintFromAbove()
        {
            _cradleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cradleGo.name = "AssemblyCradleGantryLook";
            _cradleGo.transform.position = RigOrigin;
            _cradleGo.transform.localScale = new Vector3(3f, 3f, 3f);

            var cradle = _cradleGo.AddComponent<AssemblyCradle>();
            cradle.Build(); // AddComponent's own Awake never runs outside Play mode

            Transform body = _cradleGo.transform.Find("Body");
            Assert.NotNull(body, "the cradle must build a Body root");

            // 1) No built-in primitive mesh anywhere under the built cradle body.
            var builtIn = new HashSet<Mesh>();
            foreach (PrimitiveType t in new[] { PrimitiveType.Cube, PrimitiveType.Cylinder, PrimitiveType.Sphere,
                                                PrimitiveType.Quad, PrimitiveType.Plane })
            {
                GameObject probe = GameObject.CreatePrimitive(t);
                builtIn.Add(probe.GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(probe);
            }
            foreach (MeshFilter mf in body.GetComponentsInChildren<MeshFilter>(true))
                Assert.IsFalse(builtIn.Contains(mf.sharedMesh), $"{mf.name} uses a built-in primitive mesh");

            // 2) Seen from above, the renderers' own resolved XZ bounds cover the 3.3 x 3.3 m footprint.
            var rects = new List<Rect>();
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                Bounds b = r.bounds;
                rects.Add(new Rect(b.min.x, b.min.z, b.size.x, b.size.z));
            }

            const int grid = 12;
            int covered = 0, total = 0;
            for (int ix = 0; ix <= grid; ix++)
            {
                for (int iz = 0; iz <= grid; iz++)
                {
                    float x = RigOrigin.x + (ix / (float)grid - 0.5f) * FootprintMetres;
                    float z = RigOrigin.z + (iz / (float)grid - 0.5f) * FootprintMetres;
                    total++;
                    foreach (Rect rc in rects)
                        if (rc.Contains(new Vector2(x, z))) { covered++; break; }
                }
            }
            Assert.AreEqual(total, covered,
                $"from above the cradle must cover its whole {FootprintMetres} x {FootprintMetres} m footprint, covered {covered}/{total} sample points");
        }
    }
}
