using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-976: Lee chose B on 2026-09-27 — world scenery loses its inverted-hull outline pass
    /// ENTIRELY, not just its <c>_OutlineOn</c> value zeroed. URP was still issuing the pass's draw
    /// call for every non-ground world renderer even when the value collapsed it to nothing, and the
    /// pass's <c>clip()</c> defeated Apple's hidden-surface removal on an otherwise-opaque draw. Max,
    /// robots and bosses keep the pass — only <c>StylizedSurface.shader</c> (world scenery) loses it.
    ///
    /// Built against World 1's real area 5 (<see cref="WorldMaterials.Apply"/> dresses every renderer
    /// the same shape-classified sweep the live game runs at scene load), so this proves the ACTUAL
    /// renderers a player stands next to in a5 resolve to a shader with no outline pass — not just
    /// that the shader asset lacks one in isolation.
    ///
    /// Pass presence is read from the .shader SOURCE text, the same headless workaround
    /// <c>ShaderKitTests.EveryPassThatPositionsAPlant</c> already established: pass ENUMERATION needs
    /// a graphics device, and cc-verify's EditMode run is -batchmode -nographics.
    /// </summary>
    public sealed class MV976WorldOutlineTests
    {
        private const string SurfaceShaderPath = "Assets/_Project/Art/Shaders/StylizedSurface.shader";
        private const string CharacterShaderPath = "Assets/_Project/Art/Shaders/StylizedCharacter.shader";
        private const string OutlineLightModeTag = "\"LightMode\" = \"SRPDefaultUnlit\"";

        [Test]
        public void WorldSceneryInArea5_CarriesNoOutlinePass_CharactersStillDo()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "the shipped world1_config.json failed to load — see the error log above");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone a5 = map.Zone("area5");
            Assert.IsNotNull(a5, "World 1 has no zone 'area5'");

            var root = new GameObject("MV976 a5 outline probe root").transform;
            try
            {
                MapRuntime.Build(map, root);

                // The same shape-classified sweep the live game runs at AfterSceneLoad — dresses
                // every world-scenery renderer with the real MaterialLibrary materials, not a
                // hand-picked lookup.
                var worldMaterials = root.gameObject.AddComponent<WorldMaterials>();
                int dressed = worldMaterials.Apply(BiomePalette.Backyard);
                Assert.Greater(dressed, 0, "WorldMaterials.Apply dressed nothing — this test would prove nothing");

                bool sawSurfaceShaderInArea5 = false;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!a5.Contains(r.transform.position.x, r.transform.position.z)) continue;
                    if (!WorldMaterials.IsWorldSurface(r)) continue;

                    Material mat = r.sharedMaterial;
                    if (mat == null || mat.shader == null) continue;
                    if (mat.shader.name != MaterialLibrary.StylizedSurfaceShaderName) continue;

                    sawSurfaceShaderInArea5 = true;
                }

                Assert.IsTrue(sawSurfaceShaderInArea5,
                    "a5 built no world-scenery renderer wearing MaxWorlds/StylizedSurface — this test would prove nothing");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }

            string surfaceSrc = File.ReadAllText(SurfaceShaderPath);
            Assert.That(surfaceSrc, Does.Not.Contain(OutlineLightModeTag),
                "MaxWorlds/StylizedSurface still carries an SRPDefaultUnlit pass — every world-scenery " +
                "renderer in a5 would draw it twice, and its clip() defeats Apple's hidden-surface removal");

            Material characterMat = MaterialLibrary.Character();
            Assert.IsNotNull(characterMat, "no character material — Max and the robots would keep the plain lit look");
            Assert.AreEqual(MaterialLibrary.CharacterShaderName, characterMat.shader.name,
                "the shared character material isn't wearing the character shader");

            string characterSrc = File.ReadAllText(CharacterShaderPath);
            Assert.That(characterSrc, Does.Contain(OutlineLightModeTag),
                "MaxWorlds/StylizedCharacter must keep its SRPDefaultUnlit outline pass — Lee's call was " +
                "to drop the pass from world scenery only, not from Max, the robots or the boss");
        }
    }
}
