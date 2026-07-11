using UnityEngine;

namespace MaxWorlds.Models
{
    /// <summary>
    /// Loads generated models by stable key (YT-51).
    ///
    /// Keys are paths under a Resources folder, so a model is fetched with a string and nothing
    /// is ever dragged into an inspector slot — the load-by-key half of the code-driven rule
    /// (docs/CODE_DRIVEN_SCENES.md §4). Resources rather than Addressables because Addressables
    /// is not in the project's manifest and adding a package is a guardrail; the key is an opaque
    /// string either way, so swapping the backend later touches only this file.
    ///
    /// Missing keys are not an error. Until Lee starts generating, every key is missing, and the
    /// game keeps running on greybox — see <see cref="ModelSwap"/>.
    /// </summary>
    public static class ModelLibrary
    {
        /// <summary>Resources sub-folder the import tool writes prefabs into.</summary>
        public const string ResourceRoot = "Models";

        /// <summary>Is there a generated model for this key yet?</summary>
        public static bool Exists(string key) => Load(key) != null;

        /// <summary>The prefab for a key, or null if nothing has been generated for it yet.</summary>
        public static GameObject Load(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return Resources.Load<GameObject>($"{ResourceRoot}/{key}");
        }

        /// <summary>Instantiate the model for a key, or null if there isn't one.</summary>
        public static GameObject Instantiate(string key, Transform parent = null)
        {
            var prefab = Load(key);
            if (prefab == null) return null;

            var go = Object.Instantiate(prefab, parent);
            go.name = prefab.name;
            return go;
        }
    }

    /// <summary>
    /// The stable keys. Code refers to models through these, never through a literal path, so the
    /// day a generated asset lands there is exactly one place that had to agree on its name.
    /// </summary>
    public static class ModelKeys
    {
        public const string Max = "max";
        public const string WaterBlaster = "water_blaster";
        public const string RobotEnemy = "robot_enemy";
        public const string MowerHutch = "mower_hutch";
        public const string BigBermuda = "big_bermuda";
    }
}
