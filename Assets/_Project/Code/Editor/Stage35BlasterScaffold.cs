using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Player;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Wires the Water Blaster (YT-35) into <c>Backyard_Slice.unity</c>: attaches a
    /// <see cref="WaterBlaster"/> to Max (aim-driven via the existing PlayerController)
    /// and drops two temporary <see cref="DamageableDummy"/> targets so firing,
    /// damage, and hit feedback are observable before the YT-36 enemy lands. Run via
    /// the menu or headless -executeMethod MaxWorlds.Editor.Stage35BlasterScaffold.BuildBlaster.
    /// </summary>
    public static class Stage35BlasterScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        [MenuItem("MaxWorlds/Build Water Blaster Into Backyard Slice (YT-35)")]
        public static void BuildBlaster()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var max = GameObject.Find("Max (Greybox)");
            if (max == null)
            {
                Debug.LogError("[Stage35BlasterScaffold] Max (Greybox) not found.");
                return;
            }

            var player = max.GetComponent<PlayerController>();
            if (max.GetComponent<WaterBlaster>() == null)
            {
                var blaster = max.AddComponent<WaterBlaster>();
                var so = new SerializedObject(blaster);
                so.FindProperty("aimSource").objectReferenceValue = player;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            for (int i = 0; i < 2; i++)
            {
                string name = $"Target Dummy {i + 1}";
                if (GameObject.Find(name) != null) continue;
                var dummy = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dummy.name = name;
                dummy.transform.position = new Vector3(i == 0 ? 3f : -3f, 1f, 5f);
                dummy.AddComponent<DamageableDummy>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Stage35BlasterScaffold] WaterBlaster attached to Max (aim-driven) + 2 DamageableDummy targets added.");
        }
    }
}
