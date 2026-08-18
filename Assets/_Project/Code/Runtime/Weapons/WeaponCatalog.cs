using System.Globalization;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Display data and authored magnitudes for the RCDA primary's four tracks and the four
    /// shed-acquired abilities (v0.5 recut spec §6, WV-230; Dash removed by MV-359). Same "authored
    /// const, front through DevTuning where a slider makes sense" rule as
    /// <see cref="MaxWorlds.Upgrades.UpgradeCatalog"/>.
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
        /// <c>waterBalloonCooldown</c>/<c>teleportCooldown</c> — without pinning numbers; live-tune
        /// via DevTuning once the Settings tab exists, WV-234). Water Balloon's is 3x the original 3s
        /// per MV-336 feedback (Max 0.7 doc).</summary>
        public const float DefaultWaterBalloonCooldownSeconds = 9f;
        public const float DefaultTeleportCooldownSeconds = 6f;

        /// <summary>Force Field's base cooldown, seconds, before any Weapon Cooldown reduction
        /// (MV-361: "~12s cooldown"). Starts counting down only once the bubble POPS, not on
        /// activation — same shape as the legacy <see cref="MaxWorlds.Upgrades.HydroBurst"/> window.</summary>
        public const float DefaultForceFieldCooldownSeconds = 12f;

        /// <summary>The tracks, in the order the weapons screen lists them (spec §6; Capacity/Weapon
        /// Efficiency retired by MV-290; Damage added by MV-291; Depletion Rate reinstated by MV-299).</summary>
        public static readonly WeaponTrackKind[] AllTrackKinds =
        {
            WeaponTrackKind.Range,
            WeaponTrackKind.Spread,
            WeaponTrackKind.Damage,
            WeaponTrackKind.DepletionRate,
        };

        /// <summary>The abilities, in the shed drop-pool's fixed order (spec §4/§6; Power Efficiency
        /// retired by MV-290; Water Balloon and its Auto-fire sub-ability returned to the pool by
        /// MV-380 after briefly leaving it under MV-370). <see cref="AbilityKind.WeaponCooldown"/> is
        /// retired by MV-422 — THE RIG's canonical 23-ability tree has no global cooldown-reduction
        /// node, so it is excluded here and can never be offered/acquired again (see
        /// <see cref="WeaponSystemState"/>'s class doc).</summary>
        public static readonly AbilityKind[] AllAbilityKinds =
        {
            AbilityKind.Speed,
            AbilityKind.Teleport,
            AbilityKind.WaterBalloon,
            AbilityKind.WaterBalloonAutoFire,
            AbilityKind.ForceField,
            AbilityKind.Sentinels,
        };

        /// <summary>Whether <paramref name="kind"/> gets its own card in the Abilities screen's fixed
        /// grid. Water Balloon is excluded (MV-409, Lee: redundant with the dedicated WATER BALLOON
        /// upgrade section) — showing it both as a grid card and as that section duplicated its
        /// ownership/upgrade state on screen at once.</summary>
        public static bool ShowsInAbilitiesGrid(AbilityKind kind) => kind != AbilityKind.WaterBalloon;

        /// <summary>The Water Balloon primary add-on's tracks (MV-370), in the order the weapons
        /// screen lists them — same "owned from run start" shape as <see cref="AllTrackKinds"/>.</summary>
        public static readonly WaterBalloonTrackKind[] AllWaterBalloonTrackKinds =
        {
            WaterBalloonTrackKind.Range,
            WaterBalloonTrackKind.SplashArea,
            WaterBalloonTrackKind.RepeatFire,
        };

        /// <summary>The level cap for an RCDA track. Damage and Depletion Rate keep MV-291's 5 steps
        /// (levels 1-6). Range and Spread get 3 more (levels 1-9, MV-367, Lee: "introduce probably two
        /// or three more upgrade levels" so a lower ceiling still reads as steady, frequent growth
        /// rather than two giant jumps) — so unlike MV-291, the tracks no longer share one flat cap.</summary>
        public static int MaxLevel(WeaponTrackKind kind)
        {
            switch (kind)
            {
                case WeaponTrackKind.Range:
                case WeaponTrackKind.Spread:
                    return 9;
                default:
                    return 6;
            }
        }

        /// <summary>Extra spray reach in metres each Range track level above 1 adds (MV-263) — layered
        /// additively on the weapon's authored base reach, the same shape as the legacy nozzle bonuses
        /// (<see cref="MaxWorlds.Upgrades.UpgradeState.RangeBonus"/>) it runs alongside during the
        /// WV-230 migration. Level 1 is every track's starting level (spec §6), so it adds nothing.
        /// Retuned again MV-367 (Lee: max-level reach is "ridiculous," cut the top end ~20%) against
        /// the new 8-step cap (<see cref="MaxLevel(WeaponTrackKind)"/>): the steps up to the Range cap
        /// (level 9) now land at exactly 2x base (5 + 0.625*8 = 10) — 20% below MV-291's 2.5x/12.5 —
        /// change the two together.</summary>
        public const float DefaultRcdaRangePerLevel = 0.625f;

        /// <summary>Fraction each Spread track level above 1 widens the spray cone (MV-263, MV-281,
        /// MV-289, re-retuned MV-291 against the widened cap, re-retuned MV-301 against the re-narrowed
        /// base, re-retuned again MV-367 against both the re-narrowed MV-367 base and the new 8-step
        /// cap — <see cref="MaxLevel(WeaponTrackKind)"/>). Lee's MV-367 direction cuts the max-level
        /// arc ~20% below MV-301's 66° total ceiling: 8 steps at 70%/level over the new 4° base land
        /// exactly at 4*(1+0.7*8) = 26.4° half-angle, i.e. 52.8° total (66*0.8 = 52.8 — change the two
        /// together).</summary>
        public const float DefaultRcdaSpreadPerLevel = 0.7f;

        /// <summary>Fraction each Damage track level above 1 adds to the primary's per-tick damage
        /// (MV-291) — the curve's missing third axis: Range and Spread already had a visible per-level
        /// step, but the primary's damage was a flat authored constant nobody's upgrade ever touched.
        /// 5 steps at 20%/level land at 2x base, matching Range/Spread's ~2-2.5x ceiling.</summary>
        public const float DefaultRcdaDamagePerLevel = 0.2f;

        /// <summary>Fraction each Depletion Rate track level above 1 CUTS the tank's drain per second
        /// (MV-299, reinstating the tank MV-290 cut) — the inverse shape of the other tracks: they
        /// scale a number UP, this scales the drain DOWN so a spend buys longer sustained fire, not a
        /// bigger number in a combat log. 5 steps at 15%/level land at 25% of the base drain (4x the
        /// sustained-fire time) at the maxed track — see <see cref="EffectiveDrainPerSecond"/>.</summary>
        public const float DefaultRcdaDepletionRatePerLevel = 0.15f;

        /// <summary>Effective spray reach at a given Range-track level, given the weapon's authored
        /// base reach. Found by Lee playtesting (MV-263): the Range track raised no number at all, so
        /// spending parts on it did nothing — reach, VFX and the aim-arc outline all read this.</summary>
        public static float EffectiveRange(float baseRange, int rangeLevel, float perLevel) =>
            baseRange + perLevel * (Mathf.Max(1, rangeLevel) - 1);

        /// <summary>Effective spray half-angle at a given Spread-track level, given the weapon's
        /// authored base half-angle (MV-263, same bug as <see cref="EffectiveRange"/> for Spread).</summary>
        public static float EffectiveConeHalfAngle(float baseHalfAngle, int spreadLevel, float perLevel) =>
            baseHalfAngle * (1f + perLevel * (Mathf.Max(1, spreadLevel) - 1));

        /// <summary>0 at a track's starting level 1, 1 once it's maxed — a general "how far into this
        /// track's levels am I" fraction (MV-379). First use: <see cref="MaxWorlds.Combat.WaterBlaster.VisualStrength"/>
        /// reads this against the Spread track to fade the primary's visual-only stream/muzzle
        /// presentation in independently of the (now decoupled) functional cone width.</summary>
        public static float VisualStrengthFraction(int level, int maxLevel) =>
            maxLevel <= 1 ? 1f : Mathf.Clamp01((level - 1f) / (maxLevel - 1f));

        /// <summary>Effective per-tick damage at a given Damage-track level, given the weapon's
        /// authored base damage (MV-291) — same linear "roughly-equal step" shape as
        /// <see cref="EffectiveConeHalfAngle"/>, so the primary hits harder from the very first level
        /// instead of the curve staying flat until a late, explosive jump.</summary>
        public static float EffectiveDamagePerTick(float baseDamage, int damageLevel, float perLevel) =>
            baseDamage * (1f + perLevel * (Mathf.Max(1, damageLevel) - 1));

        /// <summary>Effective tank drain per second at a given Depletion Rate track level, given the
        /// weapon's authored base drain (MV-299) — level 1 is the unmodified base, same as every other
        /// track, but each level above it SUBTRACTS from the drain rather than adding to a magnitude.
        /// Floored at 20% of base so a maxed track buys a much longer tank, never a literally free
        /// one. <paramref name="outputScale"/> (MV-368, default 1x) layers the weapon's current output
        /// — see <see cref="DrainOutputScale"/> — on TOP of the track's own reduction, so upgrading
        /// Range/Spread makes the tank drain faster again even at a maxed Depletion Rate track.</summary>
        public static float EffectiveDrainPerSecond(float baseDrainPerSecond, int depletionLevel, float perLevel, float outputScale = 1f) =>
            baseDrainPerSecond * outputScale * Mathf.Max(0.2f, 1f - perLevel * (Mathf.Max(1, depletionLevel) - 1));

        /// <summary>How much the tank's drain scales for the weapon's ACTUAL current output — the
        /// effective reach and cone, not "number of upgrade levels bought" (MV-368: reading levels
        /// would go wrong the moment a track's own per-level curve retunes, as Range/Spread's did in
        /// MV-367). 1x at the authored base — both ratios are 1 there, so a fresh, un-upgraded weapon's
        /// drain is untouched (AC2) — and it climbs as the Range track, the Spread track, or a nozzle
        /// part push reach/cone past base.
        ///
        /// Averaged rather than multiplied: Spread's ratio alone reaches ~6.6x at its max level (a tiny
        /// 4° base makes any absolute widening look huge as a ratio), and multiplying that against a
        /// maxed Range ratio (2x) would empty the tank in under a second — the "unusable" failure mode
        /// the ticket explicitly warns against. Averaging keeps a maxed weapon's drain in the same
        /// order of magnitude as the Depletion Rate track's own 4x max buyback (see
        /// <see cref="EffectiveDrainPerSecond"/>), so investing in both roughly cancels out instead of
        /// one swamping the other.</summary>
        public static float DrainOutputScale(float reach, float baseReach, float coneHalfAngle, float baseConeHalfAngle) =>
            ((reach / baseReach) + (coneHalfAngle / baseConeHalfAngle)) * 0.5f;

        /// <summary>The level cap for an ability once acquired (spec §6, Teleport revised MV-339 —
        /// the v0.5 spec's 2 read as too thin, Max 0.7 feedback wants 4 distinct levels): Speed 4,
        /// Teleport 4. Water Balloon, Auto-fire (MV-380) and Sentinels (MV-362) are boolean unlocks —
        /// cap 1, the default — since their own magnitudes live on
        /// <see cref="WaterBalloonTrackKind"/> or THE RIG's own child nodes, not a leveled ability.
        /// Force Field's cap rose to 5 with MV-422's RIG restructure (<c>e_ff</c>'s own
        /// <c>maxLevel</c>) — kept as a literal here, not read from <see cref="RigBoard"/> directly,
        /// since only this one value actually diverges from its pre-MV-422 number; change the two
        /// together if <c>rig_board.json</c>'s <c>e_ff.maxLevel</c> is ever retuned.
        /// <see cref="AbilityKind.WeaponCooldown"/> has no RIG node (retired, MV-422) — its cap is
        /// dead weight, never reachable via <see cref="AllAbilityKinds"/>.</summary>
        public static int MaxLevel(AbilityKind kind)
        {
            switch (kind)
            {
                case AbilityKind.Speed: return 4;
                case AbilityKind.Teleport: return 4;
                case AbilityKind.WeaponCooldown: return 5;
                case AbilityKind.ForceField: return 5;
                default: return 1;
            }
        }

        /// <summary>The level cap for a Water Balloon track (MV-370) — 3, the same cap the single
        /// ability track it replaces used to have.</summary>
        public static int MaxLevel(WaterBalloonTrackKind kind) => 3;

        /// <summary>Base cooldown before any Weapon Cooldown reduction, seconds. Teleport is the only
        /// remaining AbilityKind with an on-screen control (spec §6a) and a real cooldown — Water
        /// Balloon's own base cooldown moved to <see cref="WaterBalloonBaseCooldownSeconds"/> when
        /// MV-370 made it a primary add-on; Speed and Weapon Cooldown are passive — continuous, no
        /// control to gate — so their base cooldown is 0.</summary>
        public static float BaseCooldownSeconds(AbilityKind kind)
        {
            switch (kind)
            {
                case AbilityKind.Teleport:
                    return DevTuning.Or(DevTuning.TeleportCooldownSeconds, DefaultTeleportCooldownSeconds);
                case AbilityKind.ForceField:
                    return DevTuning.Or(DevTuning.ForceFieldCooldownSeconds, DefaultForceFieldCooldownSeconds);
                default:
                    return 0f;   // Speed, Weapon Cooldown — passive, no cooldown
            }
        }

        /// <summary>Water Balloon's base throw cooldown before its own Repeat Fire track (MV-370
        /// moved this off the AbilityKind switch above when Water Balloon left the shed-drop pool).</summary>
        public static float WaterBalloonBaseCooldownSeconds() =>
            DevTuning.Or(DevTuning.WaterBalloonCooldownSeconds, DefaultWaterBalloonCooldownSeconds);

        public static string DisplayName(WeaponTrackKind kind)
        {
            switch (kind)
            {
                case WeaponTrackKind.Range: return "RANGE";
                case WeaponTrackKind.Spread: return "SPREAD";
                case WeaponTrackKind.Damage: return "DAMAGE";
                case WeaponTrackKind.DepletionRate: return "DEPLETION RATE";
                default: return kind.ToString();
            }
        }

        public static string DisplayName(AbilityKind kind)
        {
            switch (kind)
            {
                case AbilityKind.Speed: return "SPEED";
                case AbilityKind.Teleport: return "TELEPORT";
                case AbilityKind.WeaponCooldown: return "WEAPON COOLDOWN";
                case AbilityKind.WaterBalloon: return "WATER BALLOON";
                case AbilityKind.WaterBalloonAutoFire: return "AUTO-FIRE";
                case AbilityKind.ForceField: return "FORCE FIELD";
                case AbilityKind.Sentinels: return "SENTINELS";
                default: return kind.ToString();
            }
        }

        /// <summary>Display name for a Water Balloon track's row on the Primary Add-ons section
        /// (MV-370). RepeatFire reads "AUTO FIRE RATE" (MV-394) — the track itself is unchanged, only
        /// its label, to match the separate Auto-Fire ability (<see cref="AbilityKind.WaterBalloonAutoFire"/>)
        /// it's now gated behind (see <see cref="WeaponsScreen.Refresh"/>).</summary>
        public static string DisplayName(WaterBalloonTrackKind kind)
        {
            switch (kind)
            {
                case WaterBalloonTrackKind.Range: return "RANGE";
                case WaterBalloonTrackKind.SplashArea: return "SPLASH AREA";
                case WaterBalloonTrackKind.RepeatFire: return "AUTO FIRE RATE";
                default: return kind.ToString();
            }
        }

        /// <summary>Short glyph for an ability's card/icon tile (MV-357) — same rationale as
        /// <c>WeaponsScreen.AbilityGlyph</c>, which this mirrors; abilities have no sprite art yet.</summary>
        public static string Glyph(AbilityKind kind)
        {
            switch (kind)
            {
                case AbilityKind.Speed: return "SPD";
                case AbilityKind.Teleport: return "TP";
                case AbilityKind.WeaponCooldown: return "CD";
                case AbilityKind.WaterBalloon: return "WB";
                case AbilityKind.WaterBalloonAutoFire: return "AF";
                case AbilityKind.ForceField: return "FF";
                case AbilityKind.Sentinels: return "SEN";
                default: return "?";
            }
        }

        /// <summary>A short, plain-language line of what this ability does (MV-357) — the shed
        /// draft-pick card's effect text, mirroring
        /// <see cref="MaxWorlds.Upgrades.UpgradeCatalog.EffectLine"/> for parts.</summary>
        public static string EffectLine(AbilityKind kind)
        {
            switch (kind)
            {
                case AbilityKind.Speed: return "Passive move-speed boost.";
                case AbilityKind.Teleport: return "Blink to a nearby spot, dodging in an instant.";
                case AbilityKind.WeaponCooldown: return "Shortens the cooldown on every other active ability.";
                case AbilityKind.WaterBalloon: return "Unlocks the Water Balloon throw — splash damage that halts robots.";
                case AbilityKind.WaterBalloonAutoFire: return "Water Balloon aims and fires itself at the best cluster in range.";
                case AbilityKind.ForceField: return "A bubble that absorbs incoming damage and blocks robots — Max can still move and fire out of it.";
                case AbilityKind.Sentinels: return "Deploy a sentinel turret — robots attack it instead of you.";
                default: return string.Empty;
            }
        }

        private static readonly TextInfo s_textInfo = CultureInfo.InvariantCulture.TextInfo;

        /// <summary>"WEAPON EFFICIENCY" -> "Weapon Efficiency". The weapons screen's v0.5 design
        /// (MV-248) reads track/ability names in Title Case; the HUD pickup toast ("TELEPORT UNLOCKED")
        /// keeps the ALL-CAPS convention it shares with "+1 CELL"/"+1 PART", so this reformats
        /// <see cref="DisplayName(WeaponTrackKind)"/>/<see cref="DisplayName(AbilityKind)"/> for
        /// screen copy rather than changing what they return.</summary>
        public static string TitleCase(string allCaps) => s_textInfo.ToTitleCase(allCaps.ToLowerInvariant());
    }
}
