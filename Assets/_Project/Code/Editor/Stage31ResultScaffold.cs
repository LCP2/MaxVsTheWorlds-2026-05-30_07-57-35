using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Adds the run tracker + Result screen (YT-31) to <c>Backyard_Slice.unity</c> — a single
    /// "RunTracker" GameObject that ends the run (Victory on boss kill, Defeat on death) and
    /// shows the code-built Result card. Menu / -executeMethod
    /// MaxWorlds.Editor.Stage31ResultScaffold.BuildResult.
    /// </summary>
    public static class Stage31ResultScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        [MenuItem("MaxWorlds/Build Result Screen Into Backyard Slice (YT-31)")]
        public static void BuildResult()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            if (Object.FindFirstObjectByType<RunTracker>() == null)
            {
                var go = new GameObject("RunTracker");
                go.AddComponent<RunTracker>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Stage31ResultScaffold] RunTracker added — Result screen shows on boss kill / death.");
        }
    }
}
