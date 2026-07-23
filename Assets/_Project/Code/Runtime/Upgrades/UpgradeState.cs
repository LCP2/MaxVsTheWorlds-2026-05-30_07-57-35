using System;
using System.Collections.Generic;
using MaxWorlds.Core;

namespace MaxWorlds.Upgrades
{
    /// <summary>
    /// What Max has installed (YT-133), and the combined modifiers that fall out of it. Upgrades
    /// STACK — every part he installs stays on — so this is the single place the weapon and the
    /// player read to know their current numbers, rather than each part poking each system directly.
    ///
    /// Static because there is one Max and several systems (the blaster, the player, the tether, the
    /// HUD) all need the same answer without threading a reference around; and event-driven so a
    /// system that has to REBUILD on a change (the blaster re-fits its reticle) is told, while systems
    /// that just read a number each frame (the player's speed) simply pull. <see cref="Reset"/> for a
    /// new run and for test isolation.
    /// </summary>
    public static class UpgradeState
    {
        private static readonly HashSet<PartKind> s_installed = new HashSet<PartKind>();

        /// <summary>Fired whenever a part is installed (or the state is reset). Systems that cache a
        /// derived value (the blaster's reticle/VFX, the tank capacity) rebuild on this.</summary>
        public static event Action Changed;

        public static bool IsInstalled(PartKind kind) => s_installed.Contains(kind);

        /// <summary>How many of the five are installed — the HUD/records can show set completion.</summary>
        public static int InstalledCount => s_installed.Count;

        /// <summary>Everything installed right now, for a system that has to persist or display the
        /// whole set (the save slot summary, YT-151) rather than ask about one part at a time.</summary>
        public static IReadOnlyCollection<PartKind> Installed => s_installed;

        /// <summary>Install a part (idempotent — installing the same one twice is a no-op, since each
        /// drops only once anyway). Fires <see cref="Changed"/> so the live systems re-fit.</summary>
        public static void Install(PartKind kind)
        {
            if (!s_installed.Add(kind)) return;
            Changed?.Invoke();
        }

        // --- derived modifiers the systems read ---

        // The effect magnitudes read through DevTuning (YT-138 Weapons tab) so Lee can dial them live,
        // falling back to the authored UpgradeCatalog consts.
        private static float NozzleCone => DevTuning.Or(DevTuning.NozzleConeMultiplier, UpgradeCatalog.NozzleConeMultiplier);

        /// <summary>Spray cone multiplier: each installed nozzle narrows it, and they compound.</summary>
        public static float ConeMultiplier
        {
            get
            {
                float m = 1f;
                if (IsInstalled(PartKind.BeamNozzle)) m *= NozzleCone;
                if (IsInstalled(PartKind.PowerNozzle)) m *= NozzleCone;
                return m;
            }
        }

        /// <summary>Extra spray reach in metres — the Power nozzle lengthens the beam.</summary>
        public static float RangeBonus =>
            IsInstalled(PartKind.PowerNozzle) ? DevTuning.Or(DevTuning.PowerNozzleRange, UpgradeCatalog.PowerRangeBonus) : 0f;

        /// <summary>Extra water-tank capacity — the Augmentation harness.</summary>
        public static float CapacityBonus =>
            IsInstalled(PartKind.AugmentationHarness) ? DevTuning.Or(DevTuning.HarnessCapacity, UpgradeCatalog.HarnessCapacityBonus) : 0f;

        /// <summary>Move-speed multiplier — the Acceleration engine.</summary>
        public static float MoveSpeedMultiplier =>
            IsInstalled(PartKind.AccelerationEngine) ? DevTuning.Or(DevTuning.AccelSpeed, UpgradeCatalog.AccelSpeedMultiplier) : 1f;

        /// <summary>Once the Hydro device is installed the hose detaches and Max is untethered from
        /// the taps — he self-supplies water and roams free.</summary>
        public static bool Untethered => IsInstalled(PartKind.Hydro);

        /// <summary>Drop everything (new run / test isolation), and tell the live systems to re-fit.</summary>
        public static void Reset()
        {
            if (s_installed.Count == 0) return;
            s_installed.Clear();
            Changed?.Invoke();
        }
    }
}
