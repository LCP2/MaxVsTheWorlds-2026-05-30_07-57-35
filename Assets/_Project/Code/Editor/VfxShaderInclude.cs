using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Keeps the shaders that VFX resolve at runtime (via Shader.Find) in Graphics
    /// Settings' Always Included Shaders.
    ///
    /// Why this is needed: shader stripping only keeps shaders referenced by a committed
    /// material, a scene, or this list. This project has no committed materials at all —
    /// every material is built in code (<see cref="MaxWorlds.VFX.VfxMaterials"/>) — so
    /// without an entry here the player build strips the URP particle shader, Shader.Find
    /// returns null in the WebGL/standalone build, and all VFX silently vanish while
    /// looking perfectly fine in the editor.
    ///
    /// Idempotent. Run from the menu, or headlessly via
    /// <c>-executeMethod MaxWorlds.Editor.VfxShaderInclude.Apply</c>.
    /// </summary>
    public static class VfxShaderInclude
    {
        private static readonly string[] Required =
        {
            "Universal Render Pipeline/Particles/Unlit",   // VfxMaterials (YT-47/48)
            "Universal Render Pipeline/Lit",               // MaterialLibrary (YT-50)
            "MaxWorlds/StylizedCharacter",                 // MaterialLibrary.Character (YT-57)
            "MaxWorlds/StylizedGround",                    // MaterialLibrary ground (YT-69)
            "MaxWorlds/StylizedSky",                       // BackyardLighting sky (YT-76)
            "MaxWorlds/StylizedSurface",                   // MaterialLibrary wood/stone/dirt/metal (YT-77)

            // YT-59. URP builds its post-processing material library EAGERLY — it creates the FSR
            // upscaling material whether or not FSR is ever selected. If that shader is stripped
            // from the build it reports "not supported", and URP's response is not to skip FSR but
            // to skip POST-PROCESSING ENTIRELY:
            //
            //   "Shader 'Hidden/.../Edge Adaptive Spatial Upsampling' is not supported
            //    (in 'Blit FSR Upscaling'). PostProcessing render passes will not execute."
            //
            // Setting render scale to 1 does NOT prevent this (we tried; the warning survived),
            // because the material is built regardless of the setting. The shader simply has to be
            // in the build.
            "Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling",
        };

        [MenuItem("MaxWorlds/Include Runtime Shaders In Build (YT-47)")]
        public static void Apply()
        {
            var settings = GraphicsSettings.GetGraphicsSettings();
            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_AlwaysIncludedShaders");

            var present = new HashSet<Object>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var v = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (v != null) present.Add(v);
            }

            int added = 0;
            foreach (var name in Required)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogError($"[VfxShaderInclude] shader not found: {name}");
                    continue;
                }
                if (present.Contains(shader)) continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                present.Add(shader);
                added++;
                Debug.Log($"[VfxShaderInclude] added to Always Included Shaders: {name}");
            }

            if (added > 0)
            {
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[VfxShaderInclude] done (added={added}, total={list.arraySize})");
        }
    }
}
