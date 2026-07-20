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
    /// </summary>
    public static class DevMode
    {
        /// <summary>Master switch for the CHEATS. Everything below is gated behind this.</summary>
        public static bool Enabled { get; set; }

        /// <summary>
        /// True in a build compiled with dev tools in (YT-118). Set by the scripting define
        /// <c>MAXWORLDS_DEV_TOOLS</c>, which the iOS→TestFlight workflow injects and which the
        /// committed project settings deliberately do NOT carry — so the default, and anything
        /// built for the App Store, ships without it.
        /// </summary>
        public const bool DevToolsBuild =
#if MAXWORLDS_DEV_TOOLS
            true;
#else
            false;
#endif

        /// <summary>
        /// May the dev TOOLS be on screen — the tuning panel and its knobs (YT-118)?
        ///
        /// Deliberately a different question from <see cref="Enabled"/>, and that separation is the
        /// point. On a phone there is no <c>?dev=1</c> URL and no Ctrl+Shift+D, so the only way to
        /// reach the panel from a TestFlight build was to turn dev mode on at boot — which would
        /// also have handed Lee invincibility and an infinite tank. He would then have been tuning
        /// "life" and "water rates" in a build where neither could run out: the sliders would move
        /// and nothing he was trying to feel would change.
        ///
        /// So a beta build gets the tools and plays honestly. The cheats stay behind
        /// <see cref="Enabled"/> and stay off.
        /// </summary>
        public static bool ToolsAvailable => Enabled || DevToolsBuild;

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

        /// <summary>Back to a clean, shippable state. Also drops any tuning-panel overrides (YT-105)
        /// — leaving them set behind a switched-off dev mode would be a trap, since they'd silently
        /// come back the next time dev mode was turned on.</summary>
        public static void Reset()
        {
            Enabled = false;
            Invincible = true;
            InfiniteEnergy = true;
            AutoFire = false;
            PauseSpawns = false;
            DevTuning.Reset();
        }
    }
}
