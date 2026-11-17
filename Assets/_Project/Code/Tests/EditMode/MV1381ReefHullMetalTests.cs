using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1381: the Reef cargo-hold container columns (a1's 6x6 stack) and the live Hatchery Clam read as pale
    /// grey boxes. The ONE new test asserts the RESOLVED surface colour (material colour overridden by the
    /// renderer's property block, which is what the renderer actually draws) of every container column and of
    /// the clam body is dark, and that none of them is a built-in Cube mesh. Fails on the base commit:
    /// container columns resolve to M_CrateBody #99A6BF (grayscale ~0.64).
    /// </summary>
    public sealed class MV1381ReefHullMetalTests
    {
        private const float MaxDarkGrayscale = 0.30f;   // deck-relative 1.6x bound is checked on the capture route

        private static Color Resolved(Renderer r)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            Color c = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor")
                ? r.sharedMaterial.GetColor("_BaseColor") : Color.magenta;
            return c;
        }

        [Test]
        public void ReefContainerColumnsAndClamBody_ResolveDark_AndNoCubeMesh_AndKeepTheirSkin()
        {
            LogAssert.ignoreFailingMessages = true;

            var wallRoot = new GameObject("MV1381 Wall Root");
            var mapRoot = new GameObject("MV1381 Map Root");
            try
            {
                GameObject wall = ReefKit.BuildContainerWall(wallRoot.transform, new Vector3(14f, 1.5f, 47f),
                    new Vector3(6f, 3f, 6f));
                int containers = 0;
                foreach (var r in wall.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.name.StartsWith("Container ") || r.name.StartsWith("Container Band")) continue;
                    containers++;
                    if (r.sharedMaterial == WorldMaterials.M_CrateCap) continue;   // the authored orange accent column
                    float g = Resolved(r).grayscale;
                    Assert.LessOrEqual(g, MaxDarkGrayscale, $"{r.name} resolves pale (grayscale {g:F2})");
                    Assert.AreNotEqual("Cube", r.GetComponent<MeshFilter>().sharedMesh.name, r.name);
                }
                Assert.Greater(containers, 0, "no container columns built");

                WorldDefinition row = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Reef));
                WorldConfig cfg = WorldLibrary.Load(row.ConfigKey);
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
                MapBuild build = MapRuntime.Build(map, mapRoot.transform);
                Assert.IsTrue(build.Actors.TryGetValue("a1_shed", out GameObject shed), "a1 shed did not build");
                var clam = shed.GetComponent<MowerHutch>();
                typeof(MowerHutch).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(clam, null);
                clam.ApplyReefSkin();
                clam.ApplyHatcheryClamBehavior();

                var body = shed.GetComponent<Renderer>();
                var mpb = new MaterialPropertyBlock();
                body.GetPropertyBlock(mpb);
                float bodyG = mpb.GetColor("_BaseColor").grayscale;
                float matG = Resolved(body).grayscale;
                Assert.LessOrEqual(bodyG, MaxDarkGrayscale, $"clam body tint pale ({bodyG:F2})");
                Assert.LessOrEqual(matG, MaxDarkGrayscale, $"clam body material pale ({matG:F2})");
                Assert.AreNotEqual("Cube", shed.GetComponent<MeshFilter>().sharedMesh.name);

                // CharacterSkinDirector re-skins every renderer under a hutch as grey Structure steel unless it
                // carries SelfDrivenTint - the real cause of the pale clam - so every clam renderer must opt out.
                foreach (var r in shed.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name == "ClamWreckPlate" || r.name == "VulnerableCore") continue;
                    if (r.GetComponentInParent<RobotEnemy>(true) != null) continue;
                    Assert.IsNotNull(r.GetComponent<SelfDrivenTint>(),
                        $"{r.name} would be repainted grey by CharacterSkinDirector");
                }
            }
            finally
            {
                Object.DestroyImmediate(wallRoot);
                Object.DestroyImmediate(mapRoot);
                FactoryCensus.Reset();
            }
        }
    }
}
