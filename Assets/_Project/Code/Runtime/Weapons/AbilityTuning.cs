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
        /// a maxed L5 ability would halve every cooldown.</summary>
        public const float DefaultWeaponCooldownReductionPerLevel = 0.1f;

        /// <summary>The cooldown multiplier for a given Weapon Cooldown level (clamped 0-5, one level
        /// per L1-5 of the ability). Level 0 — not owned yet — is always 1x, i.e. the base cooldown
        /// applies unshortened.</summary>
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

        /// <summary>Water Balloon's damage as a percentage of the ROBOT'S OWN max health (spec §9:
        /// <c>waterBalloonDamagePct</c>) — a percentage rather than a flat number, so one fixed-size
        /// splash still threatens the WV-224 Heavy/Brute tiers without needing its own scaling curve.</summary>
        public const float DefaultWaterBalloonDamagePct = 50f;

        /// <summary>How long the splash halts the robots it hits, seconds (spec §9:
        /// <c>waterBalloonStopDuration</c> — spec names the setting but doesn't pin a number; an
        /// authored placeholder, same as the cooldowns above until Lee tunes it).</summary>
        public const float DefaultWaterBalloonStopDurationSeconds = 1.5f;

        /// <summary>Fraction each Speed level adds to Max's walk speed. The spec's settings list
        /// (§9) doesn't name this one explicitly the way it does Water Balloon/Power
        /// Efficiency/Weapon Cooldown; authored the same per-level-multiplier shape as those.</summary>
        public const float DefaultSpeedMultiplierPerLevel = 0.15f;

        /// <summary>Max's walk-speed multiplier at a given Speed level — 1x at level 0 (not owned).</summary>
        public static float SpeedMultiplier(int level, float perLevel) =>
            1f + Mathf.Max(0, level) * Mathf.Max(0f, perLevel);
    }
}
