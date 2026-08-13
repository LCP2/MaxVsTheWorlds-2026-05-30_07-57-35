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

        /// <summary>Extra throw distance per Range track level beyond L1 (<c>waterBalloonDistancePerLevel</c>)
        /// — MV-370: Water Balloon's Range track (formerly its only track under the old single-level
        /// ability, spec §6a "level = throw DISTANCE").</summary>
        public const float DefaultWaterBalloonDistancePerLevel = 1.5f;

        /// <summary>How far a Range-track Level <paramref name="level"/> Water Balloon throws, in
        /// metres — what the on-screen arc/landing-circle (WV-241) sizes itself from, so the picture
        /// never promises a throw the add-on doesn't have. Every track starts at Level 1 (MV-370: owned
        /// from run start, like the RCDA's own tracks), so this does not treat level 0 specially.</summary>
        public static float WaterBalloonDistance(int level, float baseDistance, float perLevel) =>
            baseDistance + perLevel * Mathf.Max(0, level - 1);

        /// <summary>The splash's size relative to the large ("second") robot's own footprint radius at
        /// Splash Area Level 1 (<c>waterBalloonSplashMult</c>, spec §6a: "an area ≈ 2× the large
        /// robot's footprint") — 2.0 means the splash's radius is twice the robot's, i.e. its diameter
        /// matches the robot's own.</summary>
        public const float DefaultWaterBalloonSplashMult = 2f;

        /// <summary>Fraction each Splash Area track level ABOVE 1 widens the splash radius (MV-370) —
        /// same linear "roughly-equal step" shape as <see cref="WeaponCatalog.EffectiveDamagePerTick"/>,
        /// so Splash Area reads as a real upgrade rather than the fixed multiple of the large robot's
        /// footprint the splash used to be stuck at regardless of ability level.</summary>
        public const float DefaultWaterBalloonSplashAreaPerLevel = 0.3f;

        /// <summary>The splash VFX's radius, metres, at a given Splash Area track level, given the
        /// large robot's own footprint radius — level 1 is the unmodified <paramref name="splashMult"/>
        /// multiple spec §6a originally pinned, each level above it widens further.</summary>
        public static float WaterBalloonSplashRadius(float largeRobotFootprintRadius, int level, float splashMult, float perLevel) =>
            Mathf.Max(0f, largeRobotFootprintRadius) * Mathf.Max(0f, splashMult) *
            (1f + Mathf.Max(0f, perLevel) * Mathf.Max(0, level - 1));

        /// <summary>Fraction each Repeat Fire track level CUTS the Water Balloon's throw cooldown
        /// (MV-370) — same inverse shape as <see cref="WeaponCatalog.EffectiveDrainPerSecond"/>: level
        /// 1 is the unmodified base cooldown, each level above it raises balloons-per-minute by
        /// shortening the wait between throws rather than growing a magnitude.</summary>
        public const float DefaultWaterBalloonRepeatFirePerLevel = 0.2f;

        /// <summary>The Water Balloon's throw cooldown at a given Repeat Fire track level, seconds —
        /// floored at 40% of the base so a maxed track buys noticeably faster fire, never a near-instant
        /// spam (every throw still costs a cell regardless, MV-370's actual spam brake).</summary>
        public static float WaterBalloonCooldownSeconds(int repeatFireLevel, float baseCooldown, float perLevel) =>
            Mathf.Max(0f, baseCooldown) * Mathf.Max(0.4f, 1f - perLevel * (Mathf.Max(1, repeatFireLevel) - 1));

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

        /// <summary>Teleport's blink distance at Level 1, metres (MV-292) — the long, infrequent
        /// escape/engage tool.</summary>
        public const float DefaultTeleportBaseDistance = 8f;

        /// <summary>Extra blink distance per Teleport level beyond L1 (MV-292 AC3: a level-up must be
        /// a felt difference) — 8m -> 12m -> 16m -> 20m across the 4 levels (MV-339 widened the cap
        /// from 2 to 4; same per-level step, now with two more felt jumps).</summary>
        public const float DefaultTeleportDistancePerLevel = 4f;

        /// <summary>How far a Level <paramref name="level"/> Teleport blinks, in metres — same linear
        /// shape as <see cref="WaterBalloonDistance"/>.</summary>
        public static float TeleportDistance(int level, float baseDistance, float perLevel) =>
            baseDistance + perLevel * Mathf.Max(0, level - 1);

        /// <summary>Max's walk-speed multiplier at a given Speed level — 1x at level 0 (not owned).</summary>
        public static float SpeedMultiplier(int level, float perLevel) =>
            1f + Mathf.Max(0, level) * Mathf.Max(0f, perLevel);
    }
}
