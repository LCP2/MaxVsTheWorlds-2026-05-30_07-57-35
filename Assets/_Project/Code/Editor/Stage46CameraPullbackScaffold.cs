using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Pulls the fixed-angle camera back (YT-46) after playtest feedback that it felt too zoomed
    /// in. Adds a <see cref="FixedAngleCameraRig"/> to the "CM FixedAngle" vcam in
    /// <c>Backyard_Slice.unity</c> and applies the new follow distance, keeping the ~72° angle.
    /// Mutates the existing scene in place (does NOT rebuild it — later stages' wiring stays).
    /// Menu / -executeMethod MaxWorlds.Editor.Stage46CameraPullbackScaffold.ApplyPullback.
    /// </summary>
    public static class Stage46CameraPullbackScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        [MenuItem("MaxWorlds/Pull Camera Back In Backyard Slice (YT-46)")]
        public static void ApplyPullback()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var vcam = GameObject.Find("CM FixedAngle");
            if (vcam == null)
            {
                Debug.LogError("[Stage46CameraPullbackScaffold] 'CM FixedAngle' vcam not found.");
                return;
            }

            var rig = vcam.GetComponent<FixedAngleCameraRig>();
            if (rig == null) rig = vcam.AddComponent<FixedAngleCameraRig>();
            rig.Apply(); // bake the new follow offset + fixed pitch into the scene

            EditorUtility.SetDirty(rig);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Stage46CameraPullbackScaffold] FixedAngleCameraRig added; camera pulled back (72° angle kept).");
        }
    }
}
