namespace MaxWorlds.Core
{
    /// <summary>
    /// Dev-only switches for reviewing and filming the game (YT-60).
    ///
    /// Exists because the slice is currently lethal enough that a run ends in ~10-15 seconds with
    /// zero kills — which makes every VFX ticket effectively unreviewable: you die before the
    /// effects you're meant to be judging ever happen.
    ///
    /// Everything here is OFF by default and inert. When <see cref="Enabled"/> is false, every
    /// check below is false, so the guards in gameplay are no-ops and a normal session behaves
    /// exactly as it did before. Turning it on is deliberate: a <c>?dev=1</c> URL parameter, or an
    /// obscure key chord. See DevModeController.
    ///
    /// It lives in Core (not the art assemblies) purely because the gameplay code that has to
    /// consult it — PlayerHealth, WaterBlaster — can only reference Core.
    ///
    /// Note the scope here: only the CHEATS (invincible, infinite energy) live behind this. The
    /// combat-feel tuning that used to hide behind a <c>MAXWORLDS_DEV_TOOLS</c> build define is now a
    /// real, always-present Settings panel (YT-120) — see <see cref="DevTuning"/> and SettingsPanel —
    /// so nothing here gates it any more. Bundling the two was the mistake that broke the iOS build:
    /// the define was injected by editing ProjectSettings.asset mid-CI, which dirtied the tree and
    /// tripped the version guard (YT-119).
    /// </summary>
    public static class DevMode
    {
        /// <summary>Master switch for the CHEATS. Everything below is gated behind this.</summary>
        public static bool Enabled { get; set; }

        /// <summary>Max takes no damage. Lets a run last long enough to watch anything.</summary>
        public static bool Invincible { get; set; } = true;

        /// <summary>The blaster's tank never empties, so the stream can be filmed continuously.</summary>
        public static bool InfiniteEnergy { get; set; } = true;

        /// <summary>Hold the trigger down without input, to film the water VFX hands-free.</summary>
        public static bool AutoFire { get; set; }

        /// <summary>Stop the factory producing new robots.</summary>
        public static bool PauseSpawns { get; set; }

        // --- what gameplay actually asks ---

        public static bool IsInvincible => Enabled && Invincible;
        public static bool IsInfiniteEnergy => Enabled && InfiniteEnergy;
        public static bool IsAutoFiring => Enabled && AutoFire;
        public static bool IsSpawnPaused => Enabled && PauseSpawns;

        /// <summary>Back to a clean, shippable CHEAT state.
        ///
        /// It no longer touches <see cref="DevTuning"/> (YT-120). The tuning overrides used to be
        /// wiped here because they hid behind dev mode and a stale one was a trap. Now they belong to
        /// the always-present Settings panel and are their own feature — a player who moved a slider
        /// should not have it silently reset because dev mode happened to toggle. The panel's own
        /// "Reset to defaults" button is the one thing that clears them.</summary>
        public static void Reset()
        {
            Enabled = false;
            Invincible = true;
            InfiniteEnergy = true;
            AutoFire = false;
            PauseSpawns = false;
        }
    }
}
