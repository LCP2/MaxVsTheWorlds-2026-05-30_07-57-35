using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Audio
{
    /// <summary>
    /// MV-1467: the master "All sound" switch. OFF = AudioListener.volume 0, so every source is silent,
    /// including any added later. Individual switches/sliders are untouched and take effect again when
    /// it is switched back ON. Persisted via DevTuning (PlayerPrefs). Batchmode stays muted (MV-1186).
    /// </summary>
    public static class AudioMaster
    {
        public static bool IsAllSoundOn => DevTuning.Or(DevTuning.AllSoundOn, 1f) >= 0.5f;

        public static void SetAllSoundOn(bool on)
        {
            DevTuning.AllSoundOn = on ? 1f : 0f;
            Apply(Application.isBatchMode);
        }

        /// <summary>The listener volume the switch resolves to.</summary>
        public static float ResolvedListenerVolume(bool isBatchMode) => isBatchMode || !IsAllSoundOn ? 0f : 1f;

        public static void Apply(bool isBatchMode) => AudioListener.volume = ResolvedListenerVolume(isBatchMode);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() => Apply(Application.isBatchMode);
    }
}
