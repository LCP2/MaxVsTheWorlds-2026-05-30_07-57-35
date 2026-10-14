using UnityEngine;

namespace MaxWorlds.Audio
{
    /// <summary>
    /// MV-1186: every worker verification step launches Unity or the built game with -batchmode, and
    /// none of them silenced audio - so game audio played out of the dev PC's speakers during those
    /// runs, including through Lee's client calls. This mutes only the listener's output level; it
    /// deliberately never pauses or disables the audio system, so AudioSource.isPlaying and DSP time
    /// keep running exactly as they do in normal play and the existing music/SFX timing tests are
    /// unaffected.
    /// </summary>
    public static class HeadlessAudioMute
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() => Apply(Application.isBatchMode);

        public static void Apply(bool isBatchMode)
        {
            if (!isBatchMode) return;
            AudioListener.volume = 0f;
            Debug.Log("[HeadlessAudioMute] batchmode -> AudioListener.volume 0");
        }
    }
}
