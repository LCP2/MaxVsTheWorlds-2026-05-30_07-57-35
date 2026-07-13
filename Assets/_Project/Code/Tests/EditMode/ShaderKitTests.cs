using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-57 — the stylised character shader. A hand-written shader that fails to compile doesn't
    /// throw; it silently becomes the magenta error shader, or falls back and quietly loses its
    /// features. These tests are the tripwire.
    /// </summary>
    public sealed class ShaderKitTests
    {
        [Test]
        public void CharacterShader_CompilesAndIsSupported()
        {
            var shader = Shader.Find(MaterialLibrary.CharacterShaderName);

            Assert.IsNotNull(shader, $"'{MaterialLibrary.CharacterShaderName}' not found — a shader " +
                                     "with a compile error can disappear from Shader.Find entirely");
            Assert.IsTrue(shader.isSupported, "the shader failed to compile on this platform");
            Assert.That(shader.name, Does.Not.Contain("InternalErrorShader"));
        }

        [Test]
        public void GroundShader_CompilesAndIsSupported()
        {
            var shader = Shader.Find(MaterialLibrary.GroundShaderName);

            Assert.IsNotNull(shader, $"'{MaterialLibrary.GroundShaderName}' not found — a shader " +
                                     "with a compile error can disappear from Shader.Find entirely");
            Assert.IsTrue(shader.isSupported, "the shader failed to compile on this platform");
            Assert.That(shader.name, Does.Not.Contain("InternalErrorShader"));
        }

        [Test]
        public void SurfaceShader_CompilesAndIsSupported()
        {
            var shader = Shader.Find(MaterialLibrary.StylizedSurfaceShaderName);

            Assert.IsNotNull(shader, $"'{MaterialLibrary.StylizedSurfaceShaderName}' not found — a shader " +
                                     "with a compile error can disappear from Shader.Find entirely");
            Assert.IsTrue(shader.isSupported, "the shader failed to compile on this platform");
            Assert.That(shader.name, Does.Not.Contain("InternalErrorShader"));
        }

        [Test]
        public void SurfaceMaterial_ExposesTheGrainControls()
        {
            var mat = MaterialLibrary.Surface(SurfaceKind.Wood);

            // _DetailScale is tiles per METRE of world, not per UV, and it is the reason this shader
            // exists: the kit's FBXs are UV-mapped to swatches in a palette atlas, and its props are
            // rescaled at runtime, so mesh UVs can carry no detail at all.
            Assert.IsTrue(mat.HasProperty("_DetailScale"), "no grain scale control");
            Assert.IsTrue(mat.HasProperty("_NormalStrength"), "no relief control");
            Assert.IsTrue(mat.HasProperty("_Sharpness"), "no triplanar control");

            // Kept for the same reason StylizedCharacter keeps them: the Mower Hutch wears this shader,
            // and gameplay's tells are written to these names.
            Assert.IsTrue(mat.HasProperty("_BaseColor"), "no base colour");
            Assert.IsTrue(mat.HasProperty("_EmissionColor"), "no emission — the factory's tells go silent");
        }

        [Test]
        public void GroundMaterial_ExposesTheGrassControls()
        {
            var mat = MaterialLibrary.Surface(SurfaceKind.Ground);

            // These are the knobs the ground's whole look hangs off. _DetailScale in particular is
            // load-bearing: it is metres-per-tile, and it is what makes the grass immune to gameplay
            // rescaling the arena mesh (YT-68) or to the floor being two objects with different UVs.
            Assert.IsTrue(mat.HasProperty("_DetailScale"), "no grass scale control");
            Assert.IsTrue(mat.HasProperty("_NormalStrength"), "no relief control");
            Assert.IsTrue(mat.HasProperty("_MacroScale"), "no across-the-yard variation control");
            Assert.IsTrue(mat.HasProperty("_ClumpScale"), "no clump control");
            Assert.IsTrue(mat.HasProperty("_BaseColor"),
                "_BaseColor is the biome tint — the single knob YT-50 promised over every surface");
        }

        [Test]
        public void CharacterMaterial_KeepsThePropertiesGameplayTintsThrough()
        {
            var mat = MaterialLibrary.Character();
            Assert.IsNotNull(mat, "no character material — characters would keep the plain lit look");

            // Gameplay tints these renderers via MaterialPropertyBlock: hit flashes, enemy wind-up
            // tells, the factory's damage state. Rename either property and every tell goes silent.
            Assert.IsTrue(mat.HasProperty("_BaseColor"),
                "_BaseColor is what every gameplay tint writes to — it must survive the restyle");
            Assert.IsTrue(mat.HasProperty("_EmissionColor"),
                "_EmissionColor is used by the enemy/boss tells");
        }

        [Test]
        public void CharacterMaterial_ExposesTheKitsControls()
        {
            var mat = MaterialLibrary.Character();

            Assert.IsTrue(mat.HasProperty("_OutlineWidth"), "no outline control");
            Assert.IsTrue(mat.HasProperty("_RimStrength"), "no rim control");
            Assert.IsTrue(mat.HasProperty("_Dissolve"), "no dissolve control — deaths would still pop");
        }

        [Test]
        public void CharacterMaterial_IsSharedNotClonedPerCall()
        {
            Assert.AreSame(MaterialLibrary.Character(), MaterialLibrary.Character(),
                "one material serves the whole roster; per-body state goes through property blocks");
        }

        [Test]
        public void WorldAndCharacterMaterials_NeverClaimTheSameRenderer()
        {
            // Both appliers key off the same test (is it damageable?), from opposite sides. If that
            // ever drifted, a renderer could be fought over by two systems every frame.
            var enemy = new GameObject("enemy", typeof(MeshRenderer), typeof(MeshFilter));
            enemy.AddComponent<MaxWorlds.Combat.DamageableDummy>();
            var wall = new GameObject("wall", typeof(MeshRenderer), typeof(MeshFilter));
            try
            {
                var er = enemy.GetComponent<MeshRenderer>();
                var wr = wall.GetComponent<MeshRenderer>();

                Assert.IsTrue(CharacterMaterials.IsCharacter(er));
                Assert.IsFalse(WorldMaterials.IsWorldSurface(er));

                Assert.IsFalse(CharacterMaterials.IsCharacter(wr));
                Assert.IsTrue(WorldMaterials.IsWorldSurface(wr));
            }
            finally
            {
                Object.DestroyImmediate(enemy);
                Object.DestroyImmediate(wall);
            }
        }
    }
}
