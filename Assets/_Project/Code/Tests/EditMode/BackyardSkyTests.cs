using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using MaxWorlds.Arena;
using MaxWorlds.Editor;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The sky, the sun and the neighbourhood (YT-76).
    ///
    /// Most of this file exists because of one recurring bug in this project: THE THING THAT SHIPS IS
    /// NOT THE THING YOU LOOKED AT. The editor renders with the PC quality tier; WebGL — the link Lee
    /// actually opens — renders with the Mobile one. A shader that isn't in the always-included list
    /// is stripped from the build and only the build (YT-58). Post-processing that isn't switched on
    /// per-camera is built and then ignored (YT-59). Soft shadows and ambient occlusion that live on
    /// the PC renderer never reach the phone tier at all.
    ///
    /// So these tests assert on the things the BUILD reads, not on what the editor happens to show.
    /// </summary>
    public sealed class BackyardSkyTests
    {
        private BackyardLighting _lighting;

        [TearDown]
        public void TearDown()
        {
            if (_lighting != null) Object.DestroyImmediate(_lighting.gameObject);
            RenderSettings.skybox = null;
        }

        private BackyardLighting Lit()
        {
            _lighting = new GameObject("Lighting").AddComponent<BackyardLighting>();
            _lighting.Apply(BackyardLook.Default);
            return _lighting;
        }

        // --- the sky ----------------------------------------------------------------------------

        [Test]
        public void TheYard_HasASkyAtAll()
        {
            // Without one, everything the yard's geometry doesn't cover is Unity's default flat grey
            // — and at a camera looking 72° DOWN, that's the ground past the edge of the world. It is
            // the difference between an arena and an arena floating in a void.
            BackyardLighting lighting = Lit();

            Assert.IsNotNull(lighting.Sky, "no sky material was built");
            Assert.AreSame(lighting.Sky, RenderSettings.skybox, "the sky was built and then not used");
            Assert.AreEqual(BackyardLighting.SkyShaderName, lighting.Sky.shader.name);
            Assert.IsTrue(lighting.Sky.shader.isSupported, "the sky shader can't render on this platform");
        }

        [Test]
        public void TheSunInTheSky_IsTheSunInTheScene()
        {
            // A sky whose glare comes from one direction while the shadows point another way is the
            // cheapest possible way to look fake.
            BackyardLighting lighting = Lit();

            var toSun = (Vector3)lighting.Sky.GetVector("_SunDir");
            Vector3 expected = -(Quaternion.Euler(BackyardLook.Default.KeyEuler) * Vector3.forward);

            Assert.That(Vector3.Dot(toSun.normalized, expected.normalized), Is.GreaterThan(0.999f),
                "the sun in the sky doesn't point where the key light points");
        }

        [Test]
        public void TheSkyShader_IsInTheBuild()
        {
            // Nothing but Shader.Find references it, so the player build strips it unless it is in
            // Always Included Shaders — and then the yard renders against a grey void in the build
            // only, which is the worst possible place to discover it (YT-58).
            var included = GraphicsSettings.GetGraphicsSettings();
            var so = new SerializedObject(included);
            var list = so.FindProperty("m_AlwaysIncludedShaders");

            bool found = false;
            for (int i = 0; i < list.arraySize; i++)
            {
                var shader = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (shader != null && shader.name == BackyardLighting.SkyShaderName) found = true;
            }

            Assert.IsTrue(found,
                $"'{BackyardLighting.SkyShaderName}' is not in Always Included Shaders — run " +
                "MaxWorlds ▸ Include Runtime Shaders In Build and commit GraphicsSettings.");
        }

        // --- the tier that actually ships ---------------------------------------------------------

        /// <summary>The pipeline asset WebGL renders with. Not the one the editor uses.</summary>
        private static UniversalRenderPipelineAsset ShippedPipeline()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                Stage76RenderScaffold.MobileRpPath);

            Assert.IsNotNull(asset, $"no pipeline asset at {Stage76RenderScaffold.MobileRpPath}");
            return asset;
        }

        [Test]
        public void WebGL_RendersWithTheMobileTier_NotTheOneWeLookAt()
        {
            // The whole point of this section. QualitySettings.GetQualityLevel() answers "what is the
            // EDITOR set to" (PC) — which is exactly the trap: it is not what WebGL builds with. The
            // per-platform default is only in the settings asset, so that is what gets read.
            //
            // If this ever stops being true, every other test here is asserting about the wrong
            // asset, and the yard will quietly ship with hard shadows and no AO again.
            const string path = "ProjectSettings/QualitySettings.asset";
            Assert.IsTrue(System.IO.File.Exists(path), $"{path} is missing");

            var match = System.Text.RegularExpressions.Regex.Match(
                System.IO.File.ReadAllText(path), @"WebGL:\s*(\d+)");

            Assert.IsTrue(match.Success, "no per-platform default quality for WebGL");

            int level = int.Parse(match.Groups[1].Value);
            Assert.AreEqual("Mobile", QualitySettings.names[level],
                $"WebGL builds at quality level {level}, which is no longer the mobile tier — the " +
                "render settings YT-76 pinned are on the wrong asset");
        }

        [Test]
        public void TheShippedTier_HasSoftShadows()
        {
            // BackyardLighting asks for soft shadows on the key light. URP ignores that unless the
            // PIPELINE supports them — and the mobile tier had them off, so every shadow in the
            // WebGL build was hard and stair-stepped while the editor showed soft ones.
            var so = new SerializedObject(ShippedPipeline());

            Assert.IsTrue(so.FindProperty("m_SoftShadowsSupported").boolValue,
                "the tier WebGL ships with can't draw a soft shadow, whatever the light asks for");

            Assert.GreaterOrEqual(so.FindProperty("m_ShadowDistance").floatValue, 45f,
                "shadows stop before the far end of the arena does");

            Assert.GreaterOrEqual(so.FindProperty("m_ShadowCascadeCount").intValue, 2,
                "one cascade spends the whole shadow map on the whole arena; the player's own feet " +
                "get a handful of texels");
        }

        [Test]
        public void TheShippedTier_HasAmbientOcclusion()
        {
            // The SSAO on PC_Renderer has never once shipped: WebGL doesn't use that renderer. AO is
            // what puts a dark line where a fence post meets the lawn — without it the props are
            // stickers on the grass, however good the shadows are.
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(
                Stage76RenderScaffold.MobileRendererPath);
            Assert.IsNotNull(data, "no mobile renderer asset");

            var ssao = Stage76RenderScaffold.Find(data);
            Assert.IsNotNull(ssao,
                "the tier WebGL ships with has no ambient-occlusion pass — run " +
                "MaxWorlds ▸ Art ▸ Apply Render Settings (YT-76) and commit the asset");

            Assert.IsTrue(ssao.isActive, "the AO feature is there but switched off");
        }

        // --- the neighbourhood --------------------------------------------------------------------

        private static BackyardPathLayout Layout => BackyardPathLayout.Default;

        [Test]
        public void TheNeighbourhood_IsEntirelyOutsideTheArena()
        {
            var pieces = BackyardBackdrop.Build(Layout);

            Assert.Greater(pieces.Count, 20, "there's barely anything over the fence");
            Assert.Less(pieces.Count, 200, "the backdrop has grown into a frame budget");

            Assert.IsTrue(BackyardBackdrop.Validate(Layout, pieces, out string why), why);
        }

        [Test]
        public void AHouseInTheLawnIsRejected()
        {
            // Scenery that reaches into the yard is not scenery: it is an obstacle the player can't
            // shoot and can walk straight through.
            var bad = BackyardBackdrop.Build(Layout);
            bad.Add(new BackyardBackdrop.BackdropPiece(
                new Vector3(0f, 0f, 8f), new Vector3(6f, 7f, 6f),
                BackyardBackdrop.Surface.HouseWall));

            Assert.IsFalse(BackyardBackdrop.Validate(Layout, bad, out string why));
            StringAssert.Contains("reaches into the arena", why);
        }

        [Test]
        public void TheNeighbourhood_StandsBeyondEveryWall()
        {
            // Not just "outside the rooms" — outside them by a margin, so nothing out there can ever
            // be mistaken for cover you could hide behind.
            Rect[] rooms = BackyardBackdrop.Rooms(Layout);

            foreach (var p in BackyardBackdrop.Build(Layout))
            foreach (Rect room in rooms)
                Assert.IsFalse(room.Overlaps(p.Footprint),
                    $"{p.Paint} at {p.Center} is inside the arena's clearance");
        }

        [Test]
        public void TheHedgesAreMadeOfActualShrubs()
        {
            // A long green box, seen from directly above, is not a hedge. It is a canal. The fix was
            // to build them out of the same kit foliage the yard's own hedge uses — and this is what
            // stops somebody quietly turning them back into boxes.
            var foliage = BackyardBackdrop.Build(Layout)
                                          .Where(p => p.Paint == BackyardBackdrop.Surface.Foliage)
                                          .ToList();

            Assert.Greater(foliage.Count, 20, "the neighbours' hedges aren't planted");
            Assert.IsTrue(foliage.All(p => p.Model != null), "a shrub with no model is a box again");
        }
    }
}
