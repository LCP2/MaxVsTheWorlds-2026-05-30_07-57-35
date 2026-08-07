using System.Globalization;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Display data and authored magnitudes for the RCDA primary's four tracks and the six
    /// shed-acquired abilities (v0.5 recut spec §6, WV-230). Same "authored const, front through
    /// DevTuning where a slider makes sense" rule as <see cref="MaxWorlds.Upgrades.UpgradeCatalog"/>.
    /// </summary>
    public static class WeaponCatalog
    {
        /// <summary>Max's primary from run start, renamed everywhere per the v0.5 recut (spec §6) —
        /// no longer "the garden hose". The legacy <c>UpgradeCatalog</c>/<c>UpgradeScreen</c> pairing
        /// (the pre-recut parts screen WV-228/229/232 supersede) keeps its own old name; it's replaced
        /// wholesale, not renamed in place.</summary>
        public const string PrimaryName = "RAPID CONDENSATION DIRECTIONAL ACCELERATOR";

        /// <summary>The short form the HUD/weapons screen actually has room for.</summary>
        public const string PrimaryShortName = "RCDA";

        /// <summary>Placeholder base cooldowns (v0.5 recut spec §9 names these as settings —
        /// <c>waterBalloonCooldown</c>/<c>dashCooldown</c>/<c>teleportCooldown</c> — without pinning
        /// numbers; live-tune via DevTuning once the Settings tab exists, WV-234).</summary>
        public const float DefaultWaterBalloonCooldownSeconds = 3f;
        public const float DefaultDashCooldownSeconds = 2.5f;
        public const float DefaultTeleportCooldownSeconds = 4f;

        /// <summary>The four tracks, in the order the weapons screen lists them (spec §6).</summary>
        public static readonly WeaponTrackKind[] AllTrackKinds =
        {
            WeaponTrackKind.Capacity,
            WeaponTrackKind.WeaponEfficiency,
            WeaponTrackKind.Range,
            WeaponTrackKind.Spread,
        };

        /// <summary>The six abilities, in the shed drop-pool's fixed order (spec §4/§6).</summary>
        public static readonly AbilityKind[] AllAbilityKinds =
        {
            AbilityKind.WaterBalloon,
            AbilityKind.Speed,
            AbilityKind.Dash,
            AbilityKind.Teleport,
            AbilityKind.PowerEfficiency,
            AbilityKind.WeaponCooldown,
        };

        /// <summary>The level cap for an RCDA track (spec §6): Capacity/Weapon Efficiency/Spread cap
        /// at 4, Range at 6.</summary>
        public static int MaxLevel(WeaponTrackKind kind) => kind == WeaponTrackKind.Range ? 6 : 4;

        /// <summary>Extra spray reach in metres each Range track level above 1 adds (MV-263) — layered
        /// additively on the weapon's authored base reach, the same shape as the legacy nozzle bonuses
        /// (<see cref="MaxWorlds.Upgrades.UpgradeState.RangeBonus"/>) it runs alongside during the
        /// WV-230 migration. Level 1 is every track's starting level (spec §6), so it adds nothing.
        /// Retuned (MV-280, re-retuned MV-289 against the widened 5m base) so the 5 steps up to the
        /// Range cap (level 6) still land at ~3x base (5 + 2*5 = 15) — change the two together.</summary>
        public const float DefaultRcdaRangePerLevel = 2f;

        /// <summary>Fraction each Spread track level above 1 widens the spray cone (MV-263, retuned
        /// MV-281, re-retuned MV-289): the Spread track caps at level 4
        /// (<see cref="MaxLevel(WeaponTrackKind)"/>), so 3 steps at this rate take the cone from its
        /// authored base onward — tuned against <see cref="WaterBlaster.DefaultConeHalfAngle"/> so
        /// MV-289's widened ~45° total base still opens to the same ~100° total ceiling at the maxed
        /// Spread track (change the two together).</summary>
        public const float DefaultRcdaSpreadPerLevel = 0.4074f;

        /// <summary>Effective spray reach at a given Range-track level, given the weapon's authored
        /// base reach. Found by Lee playtesting (MV-263): the Range track raised no number at all, so
        /// spending parts on it did nothing — reach, VFX and the aim-arc outline all read this.</summary>
        public static float EffectiveRange(float baseRange, int rangeLevel, float perLevel) =>
            baseRange + perLevel * (Mathf.Max(1, rangeLevel) - 1);

        /// <summary>Effective spray half-angle at a given Spread-track level, given the weapon's
        /// authored base half-angle (MV-263, same bug as <see cref="EffectiveRange"/> for Spread).</summary>
        public static float EffectiveConeHalfAngle(float baseHalfAngle, int spreadLevel, float perLevel) =>
            baseHalfAngle * (1f + perLevel * (Mathf.Max(1, spreadLevel) - 1));

        /// <summary>The level cap for an ability once acquired (spec §6): Water Balloon 3, Speed 4,
        /// Dash a single unlock (1), Teleport 2, Power Efficiency/Weapon Cooldown 5.</summary>
        public static int MaxLevel(AbilityKind kind)
        {
            switch (kind)
            {
                case AbilityKind.WaterBalloon: return 3;
                case AbilityKind.Speed: return 4;
                case AbilityKind.Dash: return 1;
                case AbilityKind.Teleport: return 2;
                case AbilityKind.PowerEfficiency: return 5;
                case AbilityKind.WeaponCooldown: return 5;
                default: return 1;
            }
        }

        /// <summary>Base cooldown before any Weapon Cooldown reduction, seconds. Water Balloon, Dash
        /// and Teleport are the three active abilities with an on-screen control (spec §6a) and a real
        /// cooldown; Speed, Power Efficiency and Weapon Cooldown are passive — continuous, no control
        /// to gate — so their base cooldown is 0.</summary>
        public static float BaseCooldownSeconds(AbilityKind kind)
        {
            switch (kind)
            {
                case AbilityKind.WaterBalloon:
                    return DevTuning.Or(DevTuning.WaterBalloonCooldownSeconds, DefaultWaterBalloonCooldownSeconds);
                case AbilityKind.Dash:
                    return DevTuning.Or(DevTuning.DashCooldownSeconds, DefaultDashCooldownSeconds);
                case AbilityKind.Teleport:
                    return DevTuning.Or(DevTuning.TeleportCooldownSeconds, DefaultTeleportCooldownSeconds);
                default:
                    return 0f;   // Speed, Power Efficiency, Weapon Cooldown — passive, no cooldown
            }
        }

        public static string DisplayName(WeaponTrackKind kind)
        {
            switch (kind)
            {
                case WeaponTrackKind.Capacity: return "CAPACITY";
                case WeaponTrackKind.WeaponEfficiency: return "WEAPON EFFICIENCY";
                case WeaponTrackKind.Range: return "RANGE";
                case WeaponTrackKind.Spread: return "SPREAD";
                default: return kind.ToString();
            }
        }

        public static string DisplayName(AbilityKind kind)
        {
            switch (kind)
            {
                case AbilityKind.WaterBalloon: return "WATER BALLOON";
                case AbilityKind.Speed: return "SPEED";
                case AbilityKind.Dash: return "DASH";
                case AbilityKind.Teleport: return "TELEPORT";
                case AbilityKind.PowerEfficiency: return "POWER EFFICIENCY";
                case AbilityKind.WeaponCooldown: return "WEAPON COOLDOWN";
                default: return kind.ToString();
            }
        }

        private static readonly TextInfo s_textInfo = CultureInfo.InvariantCulture.TextInfo;

        /// <summary>"WEAPON EFFICIENCY" -> "Weapon Efficiency". The weapons screen's v0.5 design
        /// (MV-248) reads track/ability names in Title Case; the HUD pickup toast ("DASH UNLOCKED")
        /// keeps the ALL-CAPS convention it shares with "+1 CELL"/"+1 PART", so this reformats
        /// <see cref="DisplayName(WeaponTrackKind)"/>/<see cref="DisplayName(AbilityKind)"/> for
        /// screen copy rather than changing what they return.</summary>
        public static string TitleCase(string allCaps) => s_textInfo.ToTitleCase(allCaps.ToLowerInvariant());
    }
}
