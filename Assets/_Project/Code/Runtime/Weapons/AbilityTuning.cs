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

        /// <summary>Water Balloon's throw distance at Level 1 (v0.5 recut spec §9:
        /// <c>waterBalloonBaseDistance</c>), metres.</summary>
        public const float DefaultWaterBalloonBaseDistance = 4f;

        /// <summary>Extra throw distance per Water Balloon level beyond L1 (<c>waterBalloonDistancePerLevel</c>)
        /// — spec §6a: "level = throw DISTANCE", the ability's entire upgrade is this one number, not
        /// damage or rate.</summary>
        public const float DefaultWaterBalloonDistancePerLevel = 1.5f;

        /// <summary>How far a Level <paramref name="level"/> Water Balloon throws, in metres — what the
        /// on-screen arc/landing-circle (WV-241) sizes itself from, so the picture never promises a throw
        /// the ability doesn't have. Callers gate on <c>WeaponSystemState.IsAcquired</c> before showing
        /// anything; this does not treat level 0 specially.</summary>
        public static float WaterBalloonDistance(int level, float baseDistance, float perLevel) =>
            baseDistance + perLevel * Mathf.Max(0, level - 1);

        /// <summary>The splash's size relative to the large ("second") robot's own footprint radius
        /// (<c>waterBalloonSplashMult</c>, spec §6a: "an area ≈ 2× the large robot's footprint") — 2.0
        /// means the splash's radius is twice the robot's, i.e. its diameter matches the robot's own.</summary>
        public const float DefaultWaterBalloonSplashMult = 2f;

        /// <summary>The splash VFX's radius, metres, given the large robot's own footprint radius.</summary>
        public static float WaterBalloonSplashRadius(float largeRobotFootprintRadius, float splashMult) =>
            Mathf.Max(0f, largeRobotFootprintRadius) * Mathf.Max(0f, splashMult);
    }
}
