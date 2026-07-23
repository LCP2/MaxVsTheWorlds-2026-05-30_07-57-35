using UnityEngine;

namespace MaxWorlds.Upgrades
{
    /// <summary>
    /// The hose upgrade arc as the game knows it (YT-133, extended YT-164): the display data the
    /// upgrade screen reveals, and the magnitudes each effect applies. The numbers live here as
    /// authored consts — a const class the scene can't shadow, the same rule <c>BlasterTuning</c>
    /// follows. They're deliberately gathered in one place so Lee can tune the whole upgrade arc from
    /// a single file (and, where a slider makes sense, front them through <c>DevTuning</c> later
    /// without moving the authored home).
    /// </summary>
    public static class UpgradeCatalog
    {
        // --- effect magnitudes (authored; tune here) ---

        /// <summary>Each nozzle multiplies the spray's cone half-angle by this — narrower, concentrated.
        /// Two nozzles installed multiply, so the beam tightens further (upgrades stack).</summary>
        public const float NozzleConeMultiplier = 0.62f;

        /// <summary>The Power nozzle adds this much reach, in metres, on top of narrowing (YT-164: 4 m,
        /// up from 3 m — every other Power-nozzle number is unchanged).</summary>
        public const float PowerRangeBonus = 4.0f;

        /// <summary>The Range Extender adds this much further reach, in metres, on top of the Power
        /// nozzle's bonus (YT-164) — 4 m + 2 m = 6 m once both are installed.</summary>
        public const float RangeExtenderBonus = 2.0f;

        /// <summary>The Wide-Bore's own cone multiplier, stacked on top of whatever nozzles narrowed it
        /// (YT-164). Chosen so a full Beam+Power narrow (0.62 x 0.62) multiplied back by this lands at
        /// ~1x again — the cone reads WIDE, same breadth as the un-upgraded base, but now at the full
        /// extended reach.</summary>
        public const float WideBoreConeMultiplier = 2.6f;

        /// <summary>The Augmentation harness adds this much to the water tank's capacity.</summary>
        public const float HarnessCapacityBonus = 60f;

        /// <summary>The Acceleration engine multiplies Max's move speed by this.</summary>
        public const float AccelSpeedMultiplier = 1.35f;

        // --- display data ---

        public static readonly UpgradePart BeamNozzle = new UpgradePart(
            "BEAM NOZZLE", "BEAM", new Color(0.35f, 0.80f, 1.00f), PartKind.BeamNozzle);

        public static readonly UpgradePart PowerNozzle = new UpgradePart(
            "POWER NOZZLE", "PWR", new Color(0.36f, 0.62f, 1.00f), PartKind.PowerNozzle);

        public static readonly UpgradePart RangeExtender = new UpgradePart(
            "RANGE EXTENDER", "RNG", new Color(0.55f, 0.45f, 1.00f), PartKind.RangeExtender);

        public static readonly UpgradePart WideBore = new UpgradePart(
            "WIDE-BORE NOZZLE", "WIDE", new Color(1.00f, 0.35f, 0.65f), PartKind.WideBore);

        public static readonly UpgradePart AugmentationHarness = new UpgradePart(
            "AUGMENTATION HARNESS", "AUG", new Color(0.55f, 0.90f, 0.45f), PartKind.AugmentationHarness);

        public static readonly UpgradePart AccelerationEngine = new UpgradePart(
            "ACCELERATION ENGINE", "ACC", new Color(0.98f, 0.72f, 0.22f), PartKind.AccelerationEngine);

        public static readonly UpgradePart Hydro = new UpgradePart(
            "HYDRO CONDENSER", "HYDRO", new Color(0.45f, 0.95f, 1.00f), PartKind.Hydro);

        /// <summary>The seven, in a fixed order — the order the drop table dispenses them. The hose
        /// curve (BeamNozzle -> PowerNozzle -> RangeExtender -> WideBore) sits together so it drops as
        /// the intended progression (YT-164).</summary>
        public static readonly PartKind[] AllKinds =
        {
            PartKind.BeamNozzle,
            PartKind.PowerNozzle,
            PartKind.RangeExtender,
            PartKind.WideBore,
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
                case PartKind.RangeExtender: return RangeExtender;
                case PartKind.WideBore: return WideBore;
                case PartKind.AugmentationHarness: return AugmentationHarness;
                case PartKind.AccelerationEngine: return AccelerationEngine;
                case PartKind.Hydro: return Hydro;
                default: return UpgradePart.Generic;
            }
        }

        /// <summary>Which of the three families (YT-166) a part belongs to: HOSE for the nozzles and
        /// the capacity harness, MOVEMENT for the Acceleration engine, DETACH for the Hydro kit — so
        /// the upgrade screen can label a reveal at a glance.</summary>
        public static PartFamily FamilyFor(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.AccelerationEngine: return PartFamily.Movement;
                case PartKind.Hydro: return PartFamily.Detach;
                default: return PartFamily.Hose;   // BeamNozzle, PowerNozzle, AugmentationHarness
            }
        }

        /// <summary>The all-caps label the upgrade screen stamps for a family.</summary>
        public static string FamilyLabel(PartFamily family)
        {
            switch (family)
            {
                case PartFamily.Movement: return "MOVEMENT";
                case PartFamily.Detach: return "DETACH";
                default: return "HOSE";
            }
        }

        /// <summary>Max's base weapon (YT-178) — a garden hose, ties to the hosepipe identity
        /// (YT-163/YT-175). What the WEAPONS screen reads with nothing installed yet.</summary>
        public const string BaseWeaponName = "GARDEN HOSE";

        /// <summary>The weapon's current display name for the WEAPONS screen: the bare base name until
        /// any part is installed, then flagged as upgraded — the screen lists which parts alongside it,
        /// so the name doesn't need to enumerate them itself.</summary>
        public static string WeaponName(System.Collections.Generic.IReadOnlyCollection<PartKind> installed)
            => installed == null || installed.Count == 0 ? BaseWeaponName : "UPGRADED " + BaseWeaponName;
    }
}
