using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1345 AC1 — the ORBITAL's own body: four launch tubes under a crown transform, built entirely
    /// from generated <see cref="CharacterMeshes"/> (never a Unity built-in Cube/Sphere/Cylinder/Capsule).
    /// Looks the crown and tubes up by name so it fails by assertion (not by compile error) on the base
    /// commit, where ORBITAL still wears the Launcher's placeholder body.
    /// </summary>
    public sealed class MV1345OrbitalBodyTests
    {
        [Test]
        public void OrbitalBody_HasFourTubeRenderersUnderACrown_AndNoBuiltInPrimitiveMesh()
        {
            var m = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            var root = new GameObject("RawOrbitalBody").transform;
            try
            {
                RobotBodies.Build(EnemyKind.Orbital, root, new RobotPalette(m, m, m, m));

                Transform crown = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "OrbitalCrown");
                Assert.IsNotNull(crown, "the ORBITAL body has no crown transform");

                int tubes = crown.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.name == "OrbitalTube");
                Assert.AreEqual(4, tubes, "expected exactly four launch-tube renderers under the crown");

                Mesh[] builtIn =
                {
                    Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
                    Resources.GetBuiltinResource<Mesh>("Sphere.fbx"),
                    Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"),
                    Resources.GetBuiltinResource<Mesh>("Capsule.fbx"),
                };
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                    Assert.IsFalse(builtIn.Contains(mf.sharedMesh),
                        $"{mf.name} uses a Unity built-in primitive mesh ({mf.sharedMesh?.name}); ORBITAL must be generated meshes only");
            }
            finally { Object.DestroyImmediate(root.gameObject); Object.DestroyImmediate(m); }
        }
    }
}
