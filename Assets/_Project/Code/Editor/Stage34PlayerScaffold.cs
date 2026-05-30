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

            // Forward "nose" marker so facing/aim is visible on the symmetric greybox.
            if (subject.transform.Find("Nose") == null)
            {
                var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
                nose.name = "Nose";
                var noseCol = nose.GetComponent<Collider>();
                if (noseCol != null)
                {
                    Object.DestroyImmediate(noseCol); // don't let it fight the CharacterController
                }
                nose.transform.SetParent(subject.transform, worldPositionStays: false);
                nose.transform.localPosition = new Vector3(0f, 0.4f, 0.55f);
                nose.transform.localScale = new Vector3(0.25f, 0.25f, 0.6f);
            }

            EditorUtility.SetDirty(subject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Stage34PlayerScaffold] Max greybox wired: CharacterController + PlayerController; placeholder mover removed.");
        }
    }
}
