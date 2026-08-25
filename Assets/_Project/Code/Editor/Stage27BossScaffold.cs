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
                // MV-542: halved again from (1.75, 1.5, 1.75) (which was itself MV-410's halving of
                // (3.5, 3, 3.5)) — still too big for a fight that now has to fit 2+ bosses in the
                // same arena. BigBermudaBoss.Awake fits the CharacterController to this cube at
                // whatever scale it ends up placed at, so this number alone is safe to retune again.
                boss.transform.localScale = new Vector3(0.875f, 0.75f, 0.875f);
                Tint(boss, new Color(0.35f, 0.45f, 0.30f));

                // MV-410: CreatePrimitive adds a BoxCollider, and RequireComponent below adds a
                // CharacterController -- Unity does not support both on the same GameObject (the
                // CharacterController is documented to own collision alone). The stray BoxCollider is
                // the likely cause of "boss goes through walls": strip it so the CharacterController's
                // capsule is the boss's sole physical shape.
                var stray = boss.GetComponent<BoxCollider>();
                if (stray != null) Object.DestroyImmediate(stray);

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
