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
        /// numbers; live-tune via DevTuning once the Settings tab exists, WV-234). Teleport's
        /// (MV-292) is deliberately well past Dash's: Dash is the short, frequent dodge; Teleport is
        /// the long, infrequent escape/engage tool — the cooldown gap has to read as clearly as the
        /// distance gap does.</summary>
        public const float DefaultWaterBalloonCooldownSeconds = 3f;
        public const float DefaultDashCooldownSeconds = 2.5f;
        public const float DefaultTeleportCooldownSeconds = 6f;

        /// <summary>The tracks, in the order the weapons screen lists them (spec §6; Capacity/Weapon
        /// Efficiency retired by MV-290; Damage added by MV-291).</summary>
        public static readonly WeaponTrackKind[] AllTrackKinds =
        {
            WeaponTrackKind.Range,
            WeaponTrackKind.Spread,
            WeaponTrackKind.Damage,
        };

        /// <summary>The abilities, in the shed drop-pool's fixed order (spec §4/§6; Power Efficiency
        /// retired by MV-290).</summary>
        public static readonly AbilityKind[] AllAbilityKinds =
        {
            AbilityKind.WaterBalloon,
            AbilityKind.Speed,
            AbilityKind.Dash,
            AbilityKind.Teleport,
            AbilityKind.WeaponCooldown,
        };

        /// <summary>The level cap for an RCDA track (spec §6, MV-291): every track offers the same 5
        /// upgrade steps (levels 1-6) — Spread and Damage were unified onto Range's cap so no track
        /// reads as a lesser upgrade path than another.</summary>
        public static int MaxLevel(WeaponTrackKind kind) => 6;

        /// <summary>Extra spray reach in metres each Range track level above 1 adds (MV-263) — layered
        /// additively on the weapon's authored base reach, the same shape as the legacy nozzle bonuses
        /// (<see cref="MaxWorlds.Upgrades.UpgradeState.RangeBonus"/>) it runs alongside during the
        /// WV-230 migration. Level 1 is every track's starting level (spec §6), so it adds nothing.
        /// Retuned (MV-280, MV-289, re-retuned MV-291 to flatten the curve and cap it forgivingly) so
        /// the 5 steps up to the Range cap (level 6) land at ~2.5x base (5 + 1.5*5 = 12.5) instead of
        /// the old ~3x — change the two together.</summary>
        public const float DefaultRcdaRangePerLevel = 1.5f;

        /// <summary>Fraction each Spread track level above 1 widens the spray cone (MV-263, MV-281,
        /// MV-289, re-retuned MV-291 against the widened cap
        /// — <see cref="MaxLevel(WeaponTrackKind)"/> — so the 5 steps land at ~2.1x base, evenly: each
        /// step is a flat +10° on the total arc (22.5*0.2222 = 5° half-angle/level). Tuned against
        /// <see cref="WaterBlaster.DefaultConeHalfAngle"/> so MV-289's ~45° total base opens to a
        /// ~95° total ceiling at the maxed Spread track (change the two together).</summary>
        public const float DefaultRcdaSpreadPerLevel = 0.22222f;

        /// <summary>Fraction each Damage track level above 1 adds to the primary's per-tick damage
        /// (MV-291) — the curve's missing third axis: Range and Spread already had a visible per-level
        /// step, but the primary's damage was a flat authored constant nobody's upgrade ever touched.
        /// 5 steps at 20%/level land at 2x base, matching Range/Spread's ~2-2.5x ceiling.</summary>
        public const float DefaultRcdaDamagePerLevel = 0.2f;

        /// <summary>Effective spray reach at a given Range-track level, given the weapon's authored
        /// base reach. Found by Lee playtesting (MV-263): the Range track raised no number at all, so
        /// spending parts on it did nothing — reach, VFX and the aim-arc outline all read this.</summary>
        public static float EffectiveRange(float baseRange, int rangeLevel, float perLevel) =>
            baseRange + perLevel * (Mathf.Max(1, rangeLevel) - 1);

        /// <summary>Effective spray half-angle at a given Spread-track level, given the weapon's
        /// authored base half-angle (MV-263, same bug as <see cref="EffectiveRange"/> for Spread).</summary>
        public static float EffectiveConeHalfAngle(float baseHalfAngle, int spreadLevel, float perLevel) =>
            baseHalfAngle * (1f + perLevel * (Mathf.Max(1, spreadLevel) - 1));

        /// <summary>Effective per-tick damage at a given Damage-track level, given the weapon's
        /// authored base damage (MV-291) — same linear "roughly-equal step" shape as
        /// <see cref="EffectiveConeHalfAngle"/>, so the primary hits harder from the very first level
        /// instead of the curve staying flat until a late, explosive jump.</summary>
        public static float EffectiveDamagePerTick(float baseDamage, int damageLevel, float perLevel) =>
            baseDamage * (1f + perLevel * (Mathf.Max(1, damageLevel) - 1));

        /// <summary>The level cap for an ability once acquired (spec §6): Water Balloon 3, Speed 4,
        /// Dash a single unlock (1), Teleport 2, Weapon Cooldown 5.</summary>
        public static int MaxLevel(AbilityKind kind)
        {
            switch (kind)
            {
                case AbilityKind.WaterBalloon: return 3;
                case AbilityKind.Speed: return 4;
                case AbilityKind.Dash: return 1;
                case AbilityKind.Teleport: return 2;
                case AbilityKind.WeaponCooldown: return 5;
                default: return 1;
            }
        }

        /// <summary>Base cooldown before any Weapon Cooldown reduction, seconds. Water Balloon, Dash
        /// and Teleport are the three active abilities with an on-screen control (spec §6a) and a real
        /// cooldown; Speed and Weapon Cooldown are passive — continuous, no control to gate — so their
        /// base cooldown is 0.</summary>
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
                    return 0f;   // Speed, Weapon Cooldown — passive, no cooldown
            }
        }

        public static string DisplayName(WeaponTrackKind kind)
        {
            switch (kind)
            {
                case WeaponTrackKind.Range: return "RANGE";
                case WeaponTrackKind.Spread: return "SPREAD";
                case WeaponTrackKind.Damage: return "DAMAGE";
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
