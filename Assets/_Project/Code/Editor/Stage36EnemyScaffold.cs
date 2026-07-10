using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.Enemies;
using MaxWorlds.Combat;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Wires the YT-36 enemy into <c>Backyard_Slice.unity</c>: ensures Max has a
    /// <see cref="PlayerHealth"/> (so contact damage lands and dash i-frames dodge),
    /// adds an <see cref="EnemySpawner"/> targeting Max, and removes the temporary
    /// YT-35 <see cref="DamageableDummy"/> targets (the real enemy now receives the
    /// blaster). Tags Max "Player" so enemies can find it. Menu / -executeMethod
    /// MaxWorlds.Editor.Stage36EnemyScaffold.BuildEnemies.
    /// </summary>
    public static class Stage36EnemyScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        [MenuItem("MaxWorlds/Build Enemies Into Backyard Slice (YT-36)")]
        public static void BuildEnemies()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var max = GameObject.Find("Max (Greybox)");
            if (max == null)
            {
                Debug.LogError("[Stage36EnemyScaffold] Max (Greybox) not found.");
                return;
            }
            max.tag = "Player";
            if (max.GetComponent<PlayerHealth>() == null)
            {
                max.AddComponent<PlayerHealth>();
            }

            // Remove the temporary YT-35 dummies — the enemy is the damage receiver now.
            for (int i = 1; i <= 2; i++)
            {
                var dummy = GameObject.Find($"Target Dummy {i}");
                if (dummy != null) Object.DestroyImmediate(dummy);
            }

            if (Object.FindFirstObjectByType<EnemySpawner>() == null)
            {
                var spawnerGo = new GameObject("EnemySpawner");
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                var so = new SerializedObject(spawner);
                so.FindProperty("target").objectReferenceValue = max.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Stage36EnemyScaffold] Max tagged Player + PlayerHealth; EnemySpawner added; YT-35 dummies removed.");
        }
    }
}
