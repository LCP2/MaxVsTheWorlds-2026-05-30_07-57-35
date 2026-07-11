using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Wires the in-run HUD (YT-30) into <c>Backyard_Slice.unity</c>: adds a single "HUD"
    /// GameObject carrying <see cref="HudController"/>, which builds the whole interface in
    /// code at runtime (no prefab / inspector wiring, per CODE_DRIVEN_SCENES.md). Also makes
    /// sure the scene's rendering camera is tagged <c>MainCamera</c> so floating combat text
    /// can project world → screen. Menu / -executeMethod
    /// MaxWorlds.Editor.Stage30HudScaffold.BuildHud.
    /// </summary>
    public static class Stage30HudScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        [MenuItem("MaxWorlds/Build HUD Into Backyard Slice (YT-30)")]
        public static void BuildHud()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            if (Object.FindFirstObjectByType<HudController>() == null)
            {
                var hudGo = new GameObject("HUD");
                hudGo.AddComponent<HudController>();
            }

            // Floating text needs Camera.main. Tag the rendering camera if nothing is yet.
            if (Camera.main == null)
            {
                var cam = Object.FindFirstObjectByType<Camera>();
                if (cam != null) cam.gameObject.tag = "MainCamera";
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Stage30HudScaffold] HUD (HudController) added to Backyard slice; MainCamera ensured.");
        }
    }
}
