using UnityEngine;

namespace MaxWorlds.Audio
{
    /// <summary>
    /// MV-1444: the RIG's own soundscape. While THE RIG is open the world's music and ambience fade to
    /// silence (they keep playing at volume 0, so they keep their place) and a quiet electrical hum
    /// fades in; on close it reverses. Pure and time-injected so EditMode can drive it. The open flag is
    /// set by <c>WeaponsScreen</c> whichever way it opens (button, parts chip, ceremony, draft);
    /// <see cref="SfxDirector"/> advances the gains once per frame and the Music/Ambience directors
    /// multiply their volume by <see cref="GameGain"/>.
    /// </summary>
    public static class RigAudioMix
    {
        public const float GameFadeOutSeconds = 0.4f;   // music + ambience, on open
        public const float HumFadeInSeconds = 0.6f;     // hum, on open
        public const float HumFadeOutSeconds = 0.4f;    // hum, on close
        public const float GameFadeInSeconds = 0.6f;    // music + ambience, on close
        /// <summary>About -20 dBFS against a clip normalised to the usual -1 dBFS peak.</summary>
        public const float HumTargetVolume = 0.1f;

        /// <summary>Is THE RIG up right now (set by WeaponsScreen).</summary>
        public static bool RigOpen { get; set; }

        /// <summary>1 = world music/ambience at full, 0 = silenced for the RIG.</summary>
        public static float GameGain { get; private set; } = 1f;

        /// <summary>0..1 fade of the RIG hum toward <see cref="HumTargetVolume"/>.</summary>
        public static float HumGain { get; private set; }

        public static void Advance(bool open, float dt)
        {
            if (dt <= 0f) return;
            GameGain = Mathf.MoveTowards(GameGain, open ? 0f : 1f, dt / (open ? GameFadeOutSeconds : GameFadeInSeconds));
            HumGain = Mathf.MoveTowards(HumGain, open ? 1f : 0f, dt / (open ? HumFadeInSeconds : HumFadeOutSeconds));
        }

        public static void Reset()
        {
            RigOpen = false;
            GameGain = 1f;
            HumGain = 0f;
        }
    }
}
