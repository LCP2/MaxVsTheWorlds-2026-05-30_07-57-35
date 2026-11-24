using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Audio
{
    /// <summary>
    /// MV-1437: plays <c>Resources/Audio/Ambience/&lt;World&gt;_ambience</c> (World = the world's id with a
    /// capital, e.g. <c>City_ambience</c>) as a seamless loop on the Ambience bus. Silent when the file
    /// is absent, so it needs no content to ship. Same "installs itself" idiom as the other directors.
    /// </summary>
    [DisallowMultipleComponent]
    [PerfSection("audio")]
    public sealed class AmbienceDirector : MonoBehaviour
    {
        public const float DefaultAmbienceVolume = 0.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<AmbienceDirector>() != null) return;
            new GameObject("AmbienceDirector").AddComponent<AmbienceDirector>();
        }

        private AudioSource _source;
        private BackyardPath _path;
        private int _worldIndex = -1;

        /// <summary>The bed's source (test/inspection access).</summary>
        public AudioSource Source => _source;

        /// <summary>Resource path for a world id: <c>Audio/Ambience/City_ambience</c>.</summary>
        public static string ResourcePath(string worldId) =>
            "Audio/Ambience/" + char.ToUpperInvariant(worldId[0]) + worldId.Substring(1) + "_ambience";

        private void Awake()
        {
            var go = new GameObject("AmbienceBed");
            go.transform.SetParent(transform, false);
            _source = go.AddComponent<AudioSource>();
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
            _source.loop = true;
            _source.volume = 0f;
            AudioBus.Route(_source, AudioBusId.Ambience);
        }

        private void Update()
        {
            if (_source == null) return;   // MV-1441: bed source destroyed under us
            TickWorld();
            _source.volume = Mathf.Clamp01(DevTuning.Or(DevTuning.AmbienceVolume, DefaultAmbienceVolume));
        }

        private void TickWorld()
        {
            if (_path == null) _path = FindFirstObjectByType<BackyardPath>();
            if (_path != null && _path.ResolvedWorldIndex != _worldIndex)
            {
                _worldIndex = _path.ResolvedWorldIndex;
                var clip = Resources.Load<AudioClip>(ResourcePath(WorldCatalog.Get(_worldIndex).Id));
                _source.Stop();
                _source.clip = clip;
                if (clip != null) _source.Play();
            }
        }
    }
}
