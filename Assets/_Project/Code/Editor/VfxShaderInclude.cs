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
            "Universal Render Pipeline/Particles/Unlit",
        };

        [MenuItem("MaxWorlds/Include VFX Shaders In Build (YT-47)")]
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
