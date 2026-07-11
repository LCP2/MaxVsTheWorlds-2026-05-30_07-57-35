using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Wires the Mower Hutch factory (YT-37) into <c>Backyard_Slice.unity</c>: removes the
    /// free-floating YT-36 <see cref="EnemySpawner"/> (the factory now owns spawning), builds
    /// a greybox factory that spawns robots from its own position and takes Water-Blaster
    /// damage, and drops a greybox <see cref="SubZoneGate"/> that the factory opens on death.
    /// Menu / -executeMethod MaxWorlds.Editor.Stage37FactoryScaffold.BuildFactory.
    /// </summary>
    public static class Stage37FactoryScaffold
    {
        private const string ScenePath = "Assets/_Project/Scenes/Backyard_Slice.unity";

        [MenuItem("MaxWorlds/Build Mower Hutch Factory Into Backyard Slice (YT-37)")]
        public static void BuildFactory()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Remove the standalone YT-36 spawner — the factory is the spawn source now.
            var standalone = GameObject.Find("EnemySpawner");
            if (standalone != null && standalone.GetComponent<MowerHutch>() == null)
            {
                Object.DestroyImmediate(standalone);
            }

            // Gate first, so we can reference it from the factory.
            var gate = GameObject.Find("SubZone Gate");
            if (gate == null)
            {
                gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gate.name = "SubZone Gate";
                gate.transform.position = new Vector3(0f, 1.6f, 18f);
                gate.transform.localScale = new Vector3(8f, 3.2f, 0.6f);
                Tint(gate, new Color(0.30f, 0.22f, 0.16f));
                gate.AddComponent<SubZoneGate>();
            }

            var factory = GameObject.Find("Mower Hutch");
            if (factory == null)
            {
                factory = GameObject.CreatePrimitive(PrimitiveType.Cube);
                factory.name = "Mower Hutch";
                factory.transform.position = new Vector3(0f, 1f, 10f);
                factory.transform.localScale = new Vector3(3f, 2f, 3f);
                Tint(factory, new Color(0.45f, 0.42f, 0.40f));

                // RequireComponent adds the EnemySpawner; leaving its target null makes it
                // spawn robots around the factory itself (robots find Max by tag independently).
                var hutch = factory.AddComponent<MowerHutch>();
                var so = new SerializedObject(hutch);
                so.FindProperty("gate").objectReferenceValue = gate.GetComponent<SubZoneGate>();
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Stage37FactoryScaffold] Mower Hutch + SubZone Gate added; standalone EnemySpawner removed.");
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
