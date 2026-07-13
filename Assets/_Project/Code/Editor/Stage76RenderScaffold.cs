using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Rendering;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// The render-pipeline half of YT-76 — the half that lives in an asset instead of in code.
    ///
    /// THE POINT OF THIS SCRIPT IS THE TIER IT TOUCHES. The project ships two quality levels:
    /// "Mobile" (level 0) and "PC" (level 1). The editor and the Windows build run PC. **WebGL —
    /// the link Lee actually looks at — runs Mobile** (QualitySettings' per-platform default), and
    /// the Mobile tier had:
    ///
    ///   * soft shadows switched OFF at the pipeline level, so <c>light.shadows = Soft</c> in
    ///     BackyardLighting was silently downgraded to hard, stair-stepped shadows in the build only;
    ///   * one shadow cascade across 50 m, so what texels there were went mostly on empty lawn;
    ///   * no ambient-occlusion feature at all — the SSAO on PC_Renderer has never once shipped.
    ///
    /// Every one of those looks perfect in the editor. This is the same shape of bug as YT-58's
    /// magenta and YT-59's silently-disabled post-processing: the thing that renders is not the
    /// thing you looked at. So the settings are pinned HERE, in code, and asserted by tests, rather
    /// than clicked into an inspector on one machine.
    ///
    /// Run: MaxWorlds ▸ Art ▸ Apply Render Settings (YT-76), or headless via
    /// <c>-executeMethod MaxWorlds.Editor.Stage76RenderScaffold.Run</c>. Commit the changed assets.
    /// </summary>
    public static class Stage76RenderScaffold
    {
        public const string MobileRpPath = "Assets/Settings/Mobile_RPAsset.asset";
        public const string PcRpPath = "Assets/Settings/PC_RPAsset.asset";
        public const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";
        public const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";

        /// <summary>Shadows have to reach the far end of the arena: the boss room is 22 m deep and
        /// the camera sits 20 m back from whatever it's watching.</summary>
        public const float ShadowDistance = 55f;

        /// <summary>Two cascades, not one and not four. One spends its whole shadow map on the whole
        /// arena and gives the player's own feet four blurry texels; four is a PC luxury we'd pay for
        /// in a per-frame budget that has to survive a phone.</summary>
        public const int ShadowCascades = 2;

        [MenuItem("MaxWorlds/Art/Apply Render Settings (YT-76)")]
        public static void ApplyMenu()
        {
            int n = Apply();
            EditorUtility.DisplayDialog("Render settings", $"Updated {n} asset(s).", "OK");
        }

        /// <summary>Headless entry point.</summary>
        public static void Run()
        {
            int n = Apply();
            Debug.Log($"[Stage76] updated {n} render asset(s)");
            EditorApplication.Exit(0);
        }

        public static int Apply()
        {
            int touched = 0;

            touched += Pipeline(MobileRpPath, shadowmap: 2048) ? 1 : 0;
            touched += Pipeline(PcRpPath, shadowmap: 2048) ? 1 : 0;

            touched += Occlusion(MobileRendererPath) ? 1 : 0;
            touched += Occlusion(PcRendererPath) ? 1 : 0;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return touched;
        }

        /// <summary>Soft shadows, long enough to reach the arena, sharp enough to read.</summary>
        private static bool Pipeline(string path, int shadowmap)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                Debug.LogError($"[Stage76] no render pipeline asset at {path}");
                return false;
            }

            var so = new SerializedObject(asset);

            Set(so, "m_SoftShadowsSupported", true);
            Set(so, "m_SoftShadowQuality", (int)SoftShadowQuality.Low);   // 4-tap: the one a phone can pay for
            Set(so, "m_ShadowCascadeCount", ShadowCascades);
            Set(so, "m_ShadowDistance", ShadowDistance);
            Set(so, "m_MainLightShadowmapResolution", shadowmap);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            return true;
        }

        /// <summary>Give the renderer an SSAO pass, and tune it from the same
        /// <see cref="BackyardLook"/> every other number in the look comes from.</summary>
        private static bool Occlusion(string path)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (data == null)
            {
                Debug.LogError($"[Stage76] no renderer at {path}");
                return false;
            }

            var ssao = Find(data);
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = nameof(ScreenSpaceAmbientOcclusion);
                AssetDatabase.AddObjectToAsset(ssao, data);

                var list = new SerializedObject(data);
                var features = list.FindProperty("m_RendererFeatures");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;
                list.ApplyModifiedProperties();

                // The feature map is URP's own bookkeeping between the list and the sub-assets. The
                // inspector rebuilds it after every add; headless, we have to ask it to.
                typeof(ScriptableRendererData)
                    .GetMethod("ValidateRendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(data, null);

                Debug.Log($"[Stage76] added SSAO to {path}");
            }

            BackyardLook look = BackyardLook.Default;
            var so = new SerializedObject(ssao);

            Set(so, "m_Active", true);
            Set(so, "m_Settings.Intensity", look.AoIntensity);
            Set(so, "m_Settings.Radius", look.AoRadius);
            Set(so, "m_Settings.DirectLightingStrength", 0.25f);
            Set(so, "m_Settings.Falloff", 100f);

            // The cheap configuration, chosen for the tier that ships:
            //  * Downsample — half-res AO, which is all a soft contact shadow needs.
            //  * AfterOpaque — one composite pass instead of a separate blend; markedly cheaper on
            //    the tile-based GPUs this ends up on, and indistinguishable at this radius.
            //  * Depth source with normal reconstruction — no depth-normals prepass.
            Set(so, "m_Settings.Downsample", true);
            Set(so, "m_Settings.AfterOpaque", true);
            Set(so, "m_Settings.Source", 0);          // Depth
            Set(so, "m_Settings.NormalSamples", 1);   // reconstruct: medium
            Set(so, "m_Settings.Samples", 0);         // low
            Set(so, "m_Settings.BlurQuality", 1);     // medium

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(ssao);
            EditorUtility.SetDirty(data);
            return true;
        }

        /// <summary>The renderer's SSAO feature, or null. Public so the tests can ask the same
        /// question the build will.</summary>
        public static ScreenSpaceAmbientOcclusion Find(ScriptableRendererData data)
        {
            if (data == null) return null;

            foreach (var feature in data.rendererFeatures)
                if (feature is ScreenSpaceAmbientOcclusion ssao) return ssao;

            return null;
        }

        private static void Set(SerializedObject so, string path, bool value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning($"[Stage76] no property '{path}' on {so.targetObject.name}");
        }

        private static void Set(SerializedObject so, string path, int value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.intValue = value;
            else Debug.LogWarning($"[Stage76] no property '{path}' on {so.targetObject.name}");
        }

        private static void Set(SerializedObject so, string path, float value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.floatValue = value;
            else Debug.LogWarning($"[Stage76] no property '{path}' on {so.targetObject.name}");
        }
    }
}
