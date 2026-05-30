using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// One-shot scaffolder for the YT-32 hello-cube smoke scene. Builds
    /// <c>Bootstrap.unity</c> (camera + directional light + ground + cube +
    /// Bootstrap component), registers it as build scene 0, and disables VSync
    /// across all quality levels. Designed to run headless:
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;proj&gt;
    ///           -executeMethod MaxWorlds.Editor.SceneScaffold.BuildBootstrap -logFile -
    /// </summary>
    public static class SceneScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Bootstrap.unity";

        public static void BuildBootstrap()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 8f, -8f), Quaternion.Euler(45f, 0f, 0f));

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Cube";
            cube.transform.position = new Vector3(0f, 0.5f, 0f);

            var bootGo = new GameObject("Bootstrap");
            bootGo.AddComponent<Bootstrap>();

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            int original = QualitySettings.GetQualityLevel();
            int levels = QualitySettings.names.Length;
            for (int i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.vSyncCount = 0;
            }
            QualitySettings.SetQualityLevel(original, false);

            AssetDatabase.SaveAssets();
            Debug.Log($"[SceneScaffold] saved={saved} scene={ScenePath} buildScenes={EditorBuildSettings.scenes.Length} vSyncLevels={levels} -> VSync disabled.");
        }
    }
}
