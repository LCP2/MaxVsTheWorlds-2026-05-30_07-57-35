using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Cinemachine;
using MaxWorlds.Core;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// One-shot builder for the YT-33 fixed-angle camera rig scene. Creates
    /// <c>Backyard_Slice.unity</c> with a Cinemachine fixed ~72° camera that
    /// follows a non-rotating <see cref="CameraTargetRig"/> (look-ahead lead),
    /// plus a placeholder moving subject so follow + look-ahead are verifiable
    /// before Max exists (YT-34). Run headless:
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;proj&gt;
    ///           -executeMethod MaxWorlds.Editor.Stage33CameraScaffold.BuildBackyardSlice -logFile -
    /// </summary>
    public static class Stage33CameraScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";
        private const string BootstrapPath = "Assets/_Project/Scenes/Bootstrap.unity";

        [MenuItem("MaxWorlds/Build Backyard Slice Camera (YT-33)")]
        public static void BuildBackyardSlice()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bootGo = new GameObject("Bootstrap");
            bootGo.AddComponent<Bootstrap>();

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3f, 1f, 3f);

            var subject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            subject.name = "Subject (Placeholder Max)";
            subject.transform.position = new Vector3(0f, 1f, 0f);
            subject.AddComponent<PlaceholderSubjectMover>();

            var targetGo = new GameObject("CameraTarget");
            var rig = targetGo.AddComponent<CameraTargetRig>();
            rig.SetSubject(subject.transform);
            EditorUtility.SetDirty(rig);

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CinemachineBrain));
            camGo.tag = "MainCamera";
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 13f, -4.22f), Quaternion.Euler(72f, 0f, 0f));

            // Fixed-angle vcam: 72deg pitch, no aim component => it never rotates.
            // Follows the non-rotating CameraTarget, so the angle is locked regardless
            // of where Max later faces ("do not make it orbit-able").
            var vcamGo = new GameObject("CM FixedAngle");
            vcamGo.transform.rotation = Quaternion.Euler(72f, 0f, 0f);
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Follow = targetGo.transform;
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 13f, -4.22f);
            follow.TrackerSettings.PositionDamping = new Vector3(0.6f, 0.6f, 0.6f);
            EditorUtility.SetDirty(vcam);
            EditorUtility.SetDirty(follow);

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath || s.path == BootstrapPath);
            scenes.Insert(0, new EditorBuildSettingsScene(BootstrapPath, true));
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            AssetDatabase.SaveAssets();
            Debug.Log($"[Stage33CameraScaffold] saved={saved} scene={ScenePath} buildScenes={EditorBuildSettings.scenes.Length}");
        }
    }
}
