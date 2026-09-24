using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-931: the performance-stats overlay's persisted state — the single source of truth for
    /// Bootstrap's F1/top-strip toggle (<see cref="Bootstrap"/>) AND the Settings panel's
    /// "Performance stats" switch, so the two paths can never disagree.
    ///
    /// MV-933 widened the ON/OFF switch to three states (<see cref="Mode"/>) — Off, FPS-only
    /// (Bootstrap's compact first line: fps/target/build stamp) and Full (every diagnostic line) —
    /// so the readout's own cost can be avoided entirely rather than merely hidden, and so a
    /// lightweight always-on fps figure doesn't require paying for the full line set.
    ///
    /// Unlike <see cref="DevTuning"/>'s knobs — a session override that only reaches disk on an
    /// explicit "Save settings" click — this writes to PlayerPrefs immediately on every flip. A
    /// display setting has no "try it before you commit" reason to withhold that the way a feel
    /// number does, and the ticket's own AC is that the choice survives a relaunch with no extra step.
    /// </summary>
    public static class PerfOverlaySettings
    {
        public enum Mode { Off = 0, FpsOnly = 1, Full = 2 }

        private const string Key = "PerfOverlay.Mode";

        // Cached after the first read so Bootstrap's OnGUI (called at least twice per rendered
        // frame — Layout then Repaint) never hits PlayerPrefs itself. Null means "not loaded this
        // session yet".
        private static Mode? _cached;

        /// <summary>Defaults OFF for a fresh install / no saved value (MV-931 AC) — the readout
        /// covered the title/slot menu and cost 11-37 ms/frame with no discoverable way to turn it
        /// off (only F1 or a thin invisible top-centre strip).</summary>
        public static Mode CurrentMode
        {
            get
            {
                _cached ??= (Mode)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)Mode.Off), (int)Mode.Off, (int)Mode.Full);
                return _cached.Value;
            }
            set
            {
                _cached = value;
                PlayerPrefs.SetInt(Key, (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Drops the in-memory cache so the next <see cref="CurrentMode"/> read comes
        /// straight back from PlayerPrefs — lets a test simulate a relaunch without restarting the
        /// process.</summary>
        public static void ReloadForTest() => _cached = null;
    }
}
