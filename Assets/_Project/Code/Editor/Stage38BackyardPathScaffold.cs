using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Builds the greybox Backyard critical path (YT-38) into <c>Backyard_Slice.unity</c>: adds
    /// the code-driven <see cref="BackyardPath"/> (walled corridor → boss arena), tightens the
    /// factory's spawn ring so robots appear inside the lane, and widens the SubZone Gate so it
    /// fully seals the corridor. Menu / -executeMethod
    /// MaxWorlds.Editor.Stage38BackyardPathScaffold.BuildPath.
    /// </summary>
    public static class Stage38BackyardPathScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        [MenuItem("MaxWorlds/Build Backyard Greybox Path (YT-38)")]
        public static void BuildPath()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var layout = BackyardPathLayout.Default;

            if (Object.FindFirstObjectByType<BackyardPath>() == null)
            {
                new GameObject("Backyard Path").AddComponent<BackyardPath>();
            }

            // Robots must spawn inside the corridor, not out through the fences. Ring radius that
            // keeps them within the lane half-width.
            var spawner = Object.FindFirstObjectByType<EnemySpawner>();
            if (spawner != null)
            {
                var so = new SerializedObject(spawner);
                var radius = so.FindProperty("spawnRadius");
                if (radius != null) radius.floatValue = Mathf.Min(3.5f, layout.LaneHalfWidth - 1f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(spawner);
            }

            // Widen the gate so it seals the lane end (covers the opening + the wall thickness).
            var gate = GameObject.Find("SubZone Gate");
            if (gate != null)
            {
                var s = gate.transform.localScale;
                gate.transform.localScale = new Vector3(layout.GateSealWidth, s.y, s.z);
                EditorUtility.SetDirty(gate);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Stage38BackyardPathScaffold] Backyard greybox path added; spawn ring + gate sized to the lane.");
        }
    }
}
