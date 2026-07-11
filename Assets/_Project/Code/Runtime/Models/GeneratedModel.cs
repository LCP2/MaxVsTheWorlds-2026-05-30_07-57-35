using UnityEngine;

namespace MaxWorlds.Models
{
    /// <summary>
    /// Stamped onto the root of every prefab the import pipeline builds (YT-51), recording the key
    /// it answers to and the source file it came from.
    ///
    /// Worth having because a generated asset is disposable: it gets re-rolled, and six months from
    /// now nobody remembers which prompt or which file produced the robot in the scene. The prefab
    /// carries its own provenance.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedModel : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private string sourceAsset;

        public string Key => key;
        public string SourceAsset => sourceAsset;

        public void Set(string modelKey, string source)
        {
            key = modelKey;
            sourceAsset = source;
        }
    }
}
