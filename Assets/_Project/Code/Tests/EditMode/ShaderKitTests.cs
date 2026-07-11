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
