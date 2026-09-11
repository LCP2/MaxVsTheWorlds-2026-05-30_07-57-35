using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-779: the doorway, pump housing, standpipe pipes and gate lamp were still built from raw
    /// Unity primitives (Cube/Cylinder) — unlike the replicator (already <see cref="CharacterMeshes"/>
    /// built, MV-693) and unlike a bevelled box (MV-778, which chamfers an edge but cannot turn a
    /// stacked pair of cubes into a machined housing).
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only
    /// (Rule 2, Tier 2): every built part actually carries a generated mesh, never a built-in
    /// primitive one; a lathed pipe's own mesh stands proud of its nominal radius and still holds its
    /// authored length; a rebuilt shutter actually has seven slat children, not one slab; and a
    /// rebuilt pump body actually tapers (a measurably smaller top radius than bottom), not merely
    /// "has more vertices".
    /// </summary>
    public sealed class MV779HeroFormTests
    {
        private static readonly Regex PrimitiveMeshName = new Regex(@"^(Cube|Cylinder|Quad)$");

        [Test]
        public void HeroForms_ResolveGeneratedMeshesLathedPipeShutterSlatsAndTaperedPumpBody()
        {
            AssertNoPrimitiveMeshesUnderFactoryDoorway();
            AssertTubeCollarStandsProudAndLengthIsExact();
            AssertPumpBodyTapersAndCarriesNoPrimitiveMesh();
            AssertStandpipeCarriesNoPrimitiveMesh();
            AssertGateLampCarriesNoPrimitiveMesh();
        }

        private static void AssertNoPrimitiveMeshesUnderFactoryDoorway()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var hutchGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject doorGo = null;
            try
            {
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.transform.localScale = new Vector3(60f, 1f, 60f);

                hutchGo.transform.position = new Vector3(0f, 1f, 0f);
                hutchGo.transform.localScale = new Vector3(3f, 2f, 3f);
                hutchGo.AddComponent<MowerHutch>();

                doorGo = new GameObject("FactoryDoorway");
                var door = doorGo.AddComponent<FactoryDoorway>();
                door.Bind(hutchGo.GetComponent<MowerHutch>());
                InvokePrivate(door, "Awake");

                MeshFilter[] doorwayMeshFilters = doorGo.GetComponentsInChildren<MeshFilter>(true);
                Assert.IsNotEmpty(doorwayMeshFilters, "the doorway built no geometry at all");
                foreach (MeshFilter mf in doorwayMeshFilters)
                {
                    Assert.IsNotNull(mf.sharedMesh, $"{mf.name} carries no mesh");
                    Assert.IsFalse(PrimitiveMeshName.IsMatch(mf.sharedMesh.name),
                        $"{mf.name} still wears the built-in primitive mesh '{mf.sharedMesh.name}'");
                }

                Transform shutter = doorGo.transform.Find("Shutter");
                Assert.IsNotNull(shutter, "no Shutter transform was built");
                Assert.AreEqual(7, shutter.childCount,
                    "the shutter must be rebuilt as 7 slats under one Shutter parent");
            }
            finally
            {
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(hutchGo);
                if (doorGo != null) Object.DestroyImmediate(doorGo);
            }
        }

        private static void AssertTubeCollarStandsProudAndLengthIsExact()
        {
            var tubeHost = new GameObject("MV779 tube host").transform;
            try
            {
                GameObject tube = StormdrainKit.Tube(tubeHost, "Probe Tube", Vector3.zero, 0.2f, 2f,
                    Quaternion.identity, Color.white);
                Mesh mesh = tube.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsFalse(PrimitiveMeshName.IsMatch(mesh.name), "the tube still wears a primitive mesh");

                float maxXZRadius = mesh.vertices.Max(v => new Vector2(v.x, v.z).magnitude);
                Assert.Greater(maxXZRadius, 0.2f,
                    $"the collar ring never stands proud of the nominal 0.2 radius (max found: {maxXZRadius:F3})");
                Assert.That(mesh.bounds.size.y, Is.EqualTo(2f).Within(0.001f),
                    "the pipe's own mesh Y extent must equal its authored length");
            }
            finally
            {
                Object.DestroyImmediate(tubeHost.gameObject);
            }
        }

        private static void AssertPumpBodyTapersAndCarriesNoPrimitiveMesh()
        {
            var pumpHost = new GameObject("MV779 pump host").transform;
            try
            {
                GameObject pump = StormdrainKit.BuildPumpHousing(pumpHost, Vector3.zero, new Vector3(2f, 1.6f, 1.4f));

                Transform body = pump.transform.Find("Body");
                Assert.IsNotNull(body, "the pump housing built no Body part");
                Mesh bodyMesh = body.GetComponent<MeshFilter>().sharedMesh;

                float minY = bodyMesh.bounds.min.y, maxY = bodyMesh.bounds.max.y;
                float span = maxY - minY;
                // Epsilon so the chamfer ring sitting exactly at the 10% boundary (a float multiplied
                // two different ways to reach the same nominal Y) is never excluded by rounding.
                const float epsilon = 0.001f;
                float bottomMax = bodyMesh.vertices.Where(v => v.y <= minY + span * 0.1f + epsilon)
                    .Select(v => new Vector2(v.x, v.z).magnitude).DefaultIfEmpty(0f).Max();
                float topMax = bodyMesh.vertices.Where(v => v.y >= maxY - span * 0.1f - epsilon)
                    .Select(v => new Vector2(v.x, v.z).magnitude).DefaultIfEmpty(0f).Max();

                Assert.Greater(bottomMax, 0f, "no vertices found in the body's bottom 10% of Y extent");
                Assert.Less(topMax, bottomMax * 0.9f,
                    $"the pump body does not taper: top radius {topMax:F3} is not under 0.9x the bottom's {bottomMax:F3}");

                // Only the turned-form parts this ticket rebuilds — not "Status", the pre-existing,
                // deliberately untouched Glow/Quad status lens Change 2 explicitly keeps as-is.
                foreach (string partName in new[] { "Body", "Cap", "Base Flange", "Bolt0", "Bolt1", "Bolt2", "Bolt3" })
                {
                    Transform part = pump.transform.Find(partName);
                    Assert.IsNotNull(part, $"the pump housing built no '{partName}' part");
                    Mesh mesh = part.GetComponent<MeshFilter>().sharedMesh;
                    Assert.IsFalse(PrimitiveMeshName.IsMatch(mesh.name),
                        $"{partName} under Pump Housing still wears a primitive mesh '{mesh.name}'");
                }
            }
            finally
            {
                Object.DestroyImmediate(pumpHost.gameObject);
            }
        }

        private static void AssertStandpipeCarriesNoPrimitiveMesh()
        {
            var pipeHost = new GameObject("MV779 standpipe host").transform;
            try
            {
                GameObject standpipe = StormdrainKit.BuildStandpipe(pipeHost, Vector3.zero, 3f, 1.5f);
                foreach (MeshFilter mf in standpipe.GetComponentsInChildren<MeshFilter>(true))
                    Assert.IsFalse(PrimitiveMeshName.IsMatch(mf.sharedMesh.name),
                        $"{mf.name} under Standpipe still wears a primitive mesh '{mf.sharedMesh.name}'");
            }
            finally
            {
                Object.DestroyImmediate(pipeHost.gameObject);
            }
        }

        private static void AssertGateLampCarriesNoPrimitiveMesh()
        {
            var gateGo = new GameObject("MV779 gate probe");
            try
            {
                gateGo.transform.localScale = new Vector3(4f, 1.5f, 0.64f);
                var gate = gateGo.AddComponent<AreaGate>();
                InvokePrivate(gate, "Awake");
                gate.ApplyStormdrainGateSkin();

                var lampGlow = (GameObject)GetPrivate(gate, "_lampGlow");
                Assert.IsNotNull(lampGlow, "no Gate Lamp was built");
                MeshFilter[] lampMeshFilters = lampGlow.GetComponentsInChildren<MeshFilter>(true);
                Assert.IsNotEmpty(lampMeshFilters, "the Gate Lamp built no geometry at all");
                foreach (MeshFilter mf in lampMeshFilters)
                    Assert.IsFalse(PrimitiveMeshName.IsMatch(mf.sharedMesh.name),
                        $"{mf.name} under the Gate Lamp still wears a primitive mesh '{mf.sharedMesh.name}'");
            }
            finally
            {
                Object.DestroyImmediate(gateGo);
            }
        }

        private static void InvokePrivate(object target, string methodName)
        {
            MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(target, null);
        }

        private static object GetPrivate(object target, string fieldName)
        {
            FieldInfo f = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return f.GetValue(target);
        }
    }
}
