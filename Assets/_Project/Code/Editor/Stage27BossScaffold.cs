using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.Bosses;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Places the Big Bermuda boss (YT-27) into <c>Backyard_Slice.unity</c>, in the arena
    /// beyond the SubZone Gate. It stays dormant until the Mower Hutch (YT-37) is destroyed,
    /// then engages and drives the HUD boss bar. Greybox cube body. Menu / -executeMethod
    /// MaxWorlds.Editor.Stage27BossScaffold.BuildBoss.
    /// </summary>
    public static class Stage27BossScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        [MenuItem("MaxWorlds/Build Big Bermuda Boss Into Backyard Slice (YT-27)")]
        public static void BuildBoss()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            if (Object.FindFirstObjectByType<BigBermudaBoss>() == null)
            {
                var boss = GameObject.CreatePrimitive(PrimitiveType.Cube);
                boss.name = "Big Bermuda";
                boss.transform.position = new Vector3(0f, 2f, 26f); // past the gate (z=18)
                boss.transform.localScale = new Vector3(3.5f, 3f, 3.5f);
                Tint(boss, new Color(0.35f, 0.45f, 0.30f));
                boss.AddComponent<BigBermudaBoss>(); // RequireComponent adds the CharacterController
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Stage27BossScaffold] Big Bermuda boss added beyond the gate (dormant until the factory dies).");
        }

        private static void Tint(GameObject go, Color c)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;
            var mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c);
            rend.SetPropertyBlock(mpb);
        }
    }
}
