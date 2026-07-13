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

            // Kept for the same reason StylizedCharacter keeps them: the Mower Hutch wears this shader,
            // and gameplay's tells are written to these names.
            Assert.IsTrue(mat.HasProperty("_BaseColor"), "no base colour");
            Assert.IsTrue(mat.HasProperty("_EmissionColor"), "no emission — the factory's tells go silent");

            Assert.IsTrue(mat.HasProperty("_WindStrength"), "no wind control (YT-78)");
        }

        /// <summary>
        /// The wind moves the vertex, so the SHADOW pass has to move it the same way.
        ///
        /// This shader used to inherit ShadowCaster and DepthOnly from its URP/Lit fallback, and those
        /// know nothing about the wind: the bush swayed and its shadow stayed nailed to the lawn. It
        /// was invisible while the sway was a centimetre and became the first thing you noticed the
        /// moment the sway was big enough to be worth having. If these passes ever go away, the wind
        /// has to go with them.
        /// </summary>
        /// <summary>
        /// Every pass that decides WHERE a plant is must bend it the same way.
        ///
        /// The surface shader used to inherit ShadowCaster and DepthOnly from its URP/Lit fallback,
        /// and those know nothing about the wind: the bush swayed and its shadow stayed nailed to the
        /// lawn. It was invisible while the sway was a centimetre and became the first thing you saw
        /// the moment the sway was big enough to be worth having. So the shader now owns those passes,
        /// and all three call Wind().
        ///
        /// This reads the source, which is not how anyone would prefer to test a shader — but the
        /// behavioural check isn't available: pass enumeration needs a graphics device and the whole
        /// verify runs headless. The invariant is still exactly the one that matters, and it is the
        /// one that will break: someone deletes a pass, or adds a fourth that forgets the wind.
        /// </summary>
        [Test]
        public void EveryPassThatPositionsAPlant_BendsItTheSameWay()
        {
            string src = System.IO.File.ReadAllText("Assets/_Project/Art/Shaders/StylizedSurface.shader");

            foreach (var pass in new[] { "ForwardLit", "ShadowCaster", "DepthOnly" })
            {
                Assert.That(src, Does.Contain($"Name \"{pass}\""),
                    $"the surface shader has no {pass} pass of its own, so it is inheriting one that " +
                    "cannot see the wind — the plant and its shadow will part company.");
            }

            // Three call sites: the lit pass, the shadow pass, the depth pass. If a pass positions a
            // vertex without going through Wind(), that pass disagrees with the other two about where
            // the plant is.
            int windCalls = System.Text.RegularExpressions.Regex.Matches(src, @"Wind\(TransformObjectToWorld").Count
                          + System.Text.RegularExpressions.Regex.Matches(src, @"Wind\(OUT\.texWS\)").Count;

            Assert.GreaterOrEqual(windCalls, 3,
                $"only {windCalls} of the shader's passes bend the plant. Every pass that decides where " +
                "a vertex IS — lit, shadow, depth — has to apply the same wind, or the bush moves and " +
                "its shadow doesn't.");
        }

        [Test]
        public void GroundMaterial_ExposesTheWindControls()
        {
            var mat = MaterialLibrary.Surface(SurfaceKind.Ground);

            // The lawn cannot sway — it is a plane. The wind leans its blades by moving where the
            // grass texture is sampled, which is why the knob is on the ground shader at all.
            Assert.IsTrue(mat.HasProperty("_WindStrength"), "no blade-lean control (YT-78)");
            Assert.IsTrue(mat.HasProperty("_WindSpeed"), "no gust-speed control");
            Assert.IsTrue(mat.HasProperty("_WindShimmer"), "no shimmer control");
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
