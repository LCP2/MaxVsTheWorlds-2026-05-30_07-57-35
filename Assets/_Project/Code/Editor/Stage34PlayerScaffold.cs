using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Wires real Max locomotion into <c>Backyard_Slice.unity</c> (YT-34):
    /// strips the temporary <see cref="PlaceholderSubjectMover"/> off the stand-in
    /// capsule and adds a <c>CharacterController</c> + <see cref="PlayerController"/>.
    /// The camera's <see cref="CameraTargetRig"/> already tracks this transform, so
    /// the follow + look-ahead now react to player input. Run via the menu or
    /// headless -executeMethod MaxWorlds.Editor.Stage34PlayerScaffold.BuildPlayer.
    /// </summary>
    public static class Stage34PlayerScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        [MenuItem("MaxWorlds/Build Player Into Backyard Slice (YT-34)")]
        public static void BuildPlayer()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            GameObject subject = GameObject.Find("Subject (Placeholder Max)");
            if (subject == null)
            {
                subject = GameObject.Find("Max (Greybox)");
            }
            if (subject == null)
            {
                Debug.LogError("[Stage34PlayerScaffold] No placeholder subject found in Backyard_Slice.");
                return;
            }

            var mover = subject.GetComponent<PlaceholderSubjectMover>();
            if (mover != null)
            {
                Object.DestroyImmediate(mover);
            }

            subject.name = "Max (Greybox)";
            if (subject.GetComponent<CharacterController>() == null)
            {
                subject.AddComponent<CharacterController>();
            }
            if (subject.GetComponent<PlayerController>() == null)
            {
                subject.AddComponent<PlayerController>();
            }

            EditorUtility.SetDirty(subject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Stage34PlayerScaffold] Max greybox wired: CharacterController + PlayerController; placeholder mover removed.");
        }
    }
}
