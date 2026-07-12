using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Re-fits <c>Backyard_Slice.unity</c> to the reshaped arena (YT-68). The blockout itself is
    /// generated at runtime by <see cref="BackyardPath"/>, but the actors that stand IN it — the
    /// shed/factory, the boss gate, Big Bermuda — are scene objects, and the scene's serialized copy
    /// of the layout is what actually runs (it overrides
    /// <see cref="BackyardPathLayout.Default"/>). So both have to be pushed here, from the same
    /// single source of truth, or the rooms and the things in them drift apart.
    ///
    /// Idempotent: run it again after changing <c>Default</c> and the scene re-fits.
    /// Menu / -executeMethod MaxWorlds.Editor.Stage68ArenaReshapeScaffold.Reshape.
    /// </summary>
    public static class Stage68ArenaReshapeScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        /// <summary>The shed sits at the far end of the lawn, leaving the whole room as run-up.</summary>
        private const float ShedZ = 15f;

        [MenuItem("MaxWorlds/Reshape Backyard Arena (YT-68)")]
        public static void Reshape()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var layout = BackyardPathLayout.Default;

            var path = Object.FindFirstObjectByType<BackyardPath>();
            if (path == null)
            {
                Debug.LogError("[Stage68] no BackyardPath in the scene — run the YT-38 scaffold first.");
                return;
            }

            // The scene's serialized layout wins over the code default, so write the new rooms in.
            var so = new SerializedObject(path);
            SerializedProperty l = so.FindProperty("layout");
            Set(l, "StartZ", layout.StartZ);
            Set(l, "LawnStartZ", layout.LawnStartZ);
            Set(l, "GateZ", layout.GateZ);
            Set(l, "ArenaEndZ", layout.ArenaEndZ);
            Set(l, "PatioHalfWidth", layout.PatioHalfWidth);
            Set(l, "LawnHalfWidth", layout.LawnHalfWidth);
            Set(l, "GateHalfWidth", layout.GateHalfWidth);
            Set(l, "ArenaHalfWidth", layout.ArenaHalfWidth);
            Set(l, "WallHeight", layout.WallHeight);
            Set(l, "WallThickness", layout.WallThickness);
            so.FindProperty("shedZ").floatValue = ShedZ;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(path);

            // The factory moves to the far end of the lawn: the fight is the approach to it.
            Move("Mower Hutch", new Vector3(0f, 1f, ShedZ));

            // The gate closes the lawn's new end, and is sized to seal its doorway.
            var gate = GameObject.Find("SubZone Gate");
            if (gate != null)
            {
                gate.transform.position = new Vector3(0f, 1.6f, layout.GateZ);
                Vector3 s = gate.transform.localScale;
                gate.transform.localScale = new Vector3(layout.GateSealWidth, s.y, s.z);
                EditorUtility.SetDirty(gate);
            }

            // Big Bermuda waits in the middle of his (now bigger) arena.
            Move("Big Bermuda", new Vector3(0f, 2f, layout.ArenaCenter.z));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Stage68] arena reshaped: lawn {layout.LawnWidth}×{layout.LawnLength} m, " +
                      $"shed at z={ShedZ}, gate at z={layout.GateZ}, boss at z={layout.ArenaCenter.z}.");
        }

        private static void Set(SerializedProperty layout, string field, float value)
        {
            SerializedProperty p = layout.FindPropertyRelative(field);
            if (p == null) { Debug.LogError($"[Stage68] BackyardPathLayout has no field '{field}'"); return; }
            p.floatValue = value;
        }

        private static void Move(string name, Vector3 position)
        {
            var go = GameObject.Find(name);
            if (go == null) { Debug.LogWarning($"[Stage68] '{name}' not found in the scene."); return; }
            go.transform.position = position;
            EditorUtility.SetDirty(go);
        }
    }
}
