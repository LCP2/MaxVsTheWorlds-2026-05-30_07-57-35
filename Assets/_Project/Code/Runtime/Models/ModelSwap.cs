using UnityEngine;

namespace MaxWorlds.Models
{
    /// <summary>
    /// Swaps a greybox placeholder for a generated model, the moment one exists (YT-51).
    ///
    /// This is the whole point of the pipeline. Attach it to any greybox object with a key; while
    /// no model has been generated for that key it does nothing and the greybox stands in. As soon
    /// as a model lands in Resources under that key, the placeholder's renderer is hidden and the
    /// real model is instantiated in its place — with no scene edit, no prefab re-authoring, and
    /// nothing dragged into an inspector.
    ///
    /// So "swap in the real Max" becomes: drop max.fbx in the incoming folder, run the tool, push.
    /// The collider and every gameplay component stay exactly where they were — only the visual
    /// changes, which is what keeps this an art-stream concern and not a gameplay one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModelSwap : MonoBehaviour
    {
        [Tooltip("Stable key under Resources/Models (see ModelKeys).")]
        [SerializeField] private string modelKey;

        [Tooltip("Applied to the instantiated model, to reconcile the generated model's scale " +
                 "and pivot with the greybox it replaces.")]
        [SerializeField] private Vector3 localScale = Vector3.one;
        [SerializeField] private Vector3 localOffset = Vector3.zero;
        [SerializeField] private Vector3 localEuler = Vector3.zero;

        /// <summary>The instantiated model, or null while the greybox is still standing in.</summary>
        public GameObject Spawned { get; private set; }

        /// <summary>True if a generated model replaced the placeholder.</summary>
        public bool Swapped => Spawned != null;

        /// <summary>Configure in code (no inspector wiring), then call <see cref="Apply"/>.</summary>
        public ModelSwap Bind(string key, Vector3? scale = null, Vector3? offset = null, Vector3? euler = null)
        {
            modelKey = key;
            if (scale.HasValue) localScale = scale.Value;
            if (offset.HasValue) localOffset = offset.Value;
            if (euler.HasValue) localEuler = euler.Value;
            return this;
        }

        private void Awake() => Apply();

        /// <summary>Swap if a model exists for the key. Returns whether it swapped. Safe to call twice.</summary>
        public bool Apply()
        {
            if (Swapped) return true;

            var model = ModelLibrary.Instantiate(modelKey, transform);
            if (model == null) return false;   // nothing generated yet — greybox stays. Not an error.

            model.transform.localPosition = localOffset;
            model.transform.localRotation = Quaternion.Euler(localEuler);
            model.transform.localScale = localScale;
            Spawned = model;

            // Hide the placeholder's own visual, but leave its collider and components alone:
            // gameplay still points at this GameObject.
            var placeholder = GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            return true;
        }
    }
}
