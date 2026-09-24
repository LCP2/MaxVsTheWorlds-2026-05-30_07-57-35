using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-931: the performance-stats overlay's persisted ON/OFF state — the single source of truth
    /// for Bootstrap's F1/top-strip toggle (<see cref="Bootstrap"/>) AND the Settings panel's
    /// "Performance stats" switch, so the two paths can never disagree.
    ///
    /// Unlike <see cref="DevTuning"/>'s knobs — a session override that only reaches disk on an
    /// explicit "Save settings" click — this writes to PlayerPrefs immediately on every flip. An
    /// ON/OFF display setting has no "try it before you commit" reason to withhold that the way a
    /// feel number does, and the ticket's own AC is that the choice survives a relaunch with no
    /// extra step.
    /// </summary>
    public static class PerfOverlaySettings
    {
        private const string Key = "PerfOverlay.Visible";

        // Cached after the first read so Bootstrap's OnGUI (called at least twice per rendered
        // frame — Layout then Repaint) never hits PlayerPrefs itself. Null means "not loaded this
        // session yet".
        private static bool? _cached;

        /// <summary>Defaults OFF for a fresh install / no saved value (MV-931 AC) — the readout
        /// covered the title/slot menu and cost 11-37 ms/frame with no discoverable way to turn it
        /// off (only F1 or a thin invisible top-centre strip).</summary>
        public static bool Visible
        {
            get
            {
                _cached ??= PlayerPrefs.GetInt(Key, 0) != 0;
                return _cached.Value;
            }
            set
            {
                _cached = value;
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Drops the in-memory cache so the next <see cref="Visible"/> read comes straight
        /// back from PlayerPrefs — lets a test simulate a relaunch without restarting the process.</summary>
        public static void ReloadForTest() => _cached = null;
    }
}
