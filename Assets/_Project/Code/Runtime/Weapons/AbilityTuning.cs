using UnityEngine;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Pure formulas for the ability backbone (WV-230), free of any live ability state so they're
    /// testable without touching <see cref="WeaponSystemState"/>.
    /// </summary>
    public static class AbilityTuning
    {
        /// <summary>Fraction each Weapon Cooldown level shaves off every other active ability's
        /// cooldown (v0.5 recut spec §9: <c>weaponCooldownReductionPerLevel</c>) — 0.1 = 10%/level, so
        /// a maxed L5 ability would halve every cooldown. Same shape as
        /// <see cref="MaxWorlds.Pickups.CellEconomyTuning.DefaultPowerEfficiencyReductionPerLevel"/>.</summary>
        public const float DefaultWeaponCooldownReductionPerLevel = 0.1f;

        /// <summary>The cooldown multiplier for a given Weapon Cooldown level (clamped 0-5, one level
        /// per L1-5 of the ability). Level 0 — not owned yet — is always 1x, i.e. the base cooldown
        /// applies unshortened. Mirrors <c>CellEconomyTuning.EfficiencyMultiplier</c>.</summary>
        public static float CooldownMultiplier(int level, float reductionPerLevel) =>
            Mathf.Clamp01(1f - Mathf.Clamp(level, 0, 5) * Mathf.Max(0f, reductionPerLevel));
    }
}
