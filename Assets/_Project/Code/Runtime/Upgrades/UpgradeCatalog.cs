using UnityEngine;

namespace MaxWorlds.Upgrades
{
    /// <summary>
    /// The five parts as the game knows them (YT-133): the display data the upgrade screen reveals,
    /// and the magnitudes each effect applies. The numbers live here as authored consts — a const
    /// class the scene can't shadow, the same rule <c>BlasterTuning</c> follows. They're deliberately
    /// gathered in one place so Lee can tune the whole upgrade arc from a single file (and, where a
    /// slider makes sense, front them through <c>DevTuning</c> later without moving the authored home).
    /// </summary>
    public static class UpgradeCatalog
    {
        // --- effect magnitudes (authored; tune here) ---

        /// <summary>Each nozzle multiplies the spray's cone half-angle by this — narrower, concentrated.
        /// Two nozzles installed multiply, so the beam tightens further (upgrades stack).</summary>
        public const float NozzleConeMultiplier = 0.62f;

        /// <summary>The Power nozzle adds this much reach, in metres, on top of narrowing.</summary>
        public const float PowerRangeBonus = 3.0f;

        /// <summary>The Augmentation harness adds this much to the water tank's capacity.</summary>
        public const float HarnessCapacityBonus = 60f;

        /// <summary>The Acceleration engine multiplies Max's move speed by this.</summary>
        public const float AccelSpeedMultiplier = 1.35f;

        // --- display data ---

        public static readonly UpgradePart BeamNozzle = new UpgradePart(
            "BEAM NOZZLE", "BEAM", new Color(0.35f, 0.80f, 1.00f), PartKind.BeamNozzle);

        public static readonly UpgradePart PowerNozzle = new UpgradePart(
            "POWER NOZZLE", "PWR", new Color(0.36f, 0.62f, 1.00f), PartKind.PowerNozzle);

        public static readonly UpgradePart AugmentationHarness = new UpgradePart(
            "AUGMENTATION HARNESS", "AUG", new Color(0.55f, 0.90f, 0.45f), PartKind.AugmentationHarness);

        public static readonly UpgradePart AccelerationEngine = new UpgradePart(
            "ACCELERATION ENGINE", "ACC", new Color(0.98f, 0.72f, 0.22f), PartKind.AccelerationEngine);

        public static readonly UpgradePart Hydro = new UpgradePart(
            "HYDRO CONDENSER", "HYDRO", new Color(0.45f, 0.95f, 1.00f), PartKind.Hydro);

        /// <summary>The five, in a fixed order — the order the drop table dispenses them.</summary>
        public static readonly PartKind[] AllKinds =
        {
            PartKind.BeamNozzle,
            PartKind.PowerNozzle,
            PartKind.AugmentationHarness,
            PartKind.AccelerationEngine,
            PartKind.Hydro,
        };

        /// <summary>The display data for a kind.</summary>
        public static UpgradePart For(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.BeamNozzle: return BeamNozzle;
                case PartKind.PowerNozzle: return PowerNozzle;
                case PartKind.AugmentationHarness: return AugmentationHarness;
                case PartKind.AccelerationEngine: return AccelerationEngine;
                case PartKind.Hydro: return Hydro;
                default: return UpgradePart.Generic;
            }
        }
    }
}
