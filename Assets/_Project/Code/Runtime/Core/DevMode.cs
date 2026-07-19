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
        /// <summary>Master switch. Everything else is gated behind this.</summary>
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
