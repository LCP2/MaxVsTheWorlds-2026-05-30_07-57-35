using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Moves Max's start to the lawn entry (YT-70). He used to begin at z=0 — 10 m from the Mower
    /// Hutch, close enough that robots appeared basically on top of him. He now starts at the mouth
    /// of the lawn with the whole fight room between him and the factory, so the robots streaming
    /// out of it have to cross the space to reach him: room to read, room to kite.
    ///
    /// Menu / -executeMethod MaxWorlds.Editor.Stage70PlayerStartScaffold.MovePlayerStart.
    /// </summary>
    public static class Stage70PlayerStartScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        /// <summary>Just inside the lawn, clear of the patio mouth's shoulders.</summary>
        private const float StartZ = -3f;

        [MenuItem("MaxWorlds/Move Max To The Lawn Entry (YT-70)")]
        public static void MovePlayerStart()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var layout = BackyardPathLayout.Default;

            var max = GameObject.FindWithTag("Player");
            if (max == null)
            {
                Debug.LogError("[Stage70] no GameObject tagged 'Player' — run the YT-34 scaffold first.");
                return;
            }

            if (StartZ < layout.LawnStartZ || StartZ > layout.GateZ)
            {
                Debug.LogError($"[Stage70] start z={StartZ} is outside the lawn — Max would spawn in a wall.");
                return;
            }

            max.transform.position = new Vector3(0f, 1f, StartZ);
            EditorUtility.SetDirty(max);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            var hutch = GameObject.Find("Mower Hutch");
            float gap = hutch != null ? hutch.transform.position.z - StartZ : 0f;
            Debug.Log($"[Stage70] Max starts at z={StartZ}; {gap} m of lawn between him and the factory.");
        }
    }
}
