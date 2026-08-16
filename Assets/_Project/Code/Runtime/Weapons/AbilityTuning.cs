using UnityEngine;
using MaxWorlds.Player;

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

        // --- Force Field (MV-361) ---

        /// <summary>Damage the bubble absorbs before popping at Level 1, the "60-damage cap" DECISION
        /// #5 pins — roughly 30% of Max's 200 HP, enough runway to reposition against a two-Rusher
        /// swarm without being a stalemate-proof wall (see the ticket's own DPS math).</summary>
        public const float DefaultForceFieldAbsorbCap = 60f;

        /// <summary>Extra absorb cap each Force Field level beyond L1 adds — same additive "level =
        /// bigger number" shape as <see cref="TeleportDistance"/>, so a level-up is a felt difference.</summary>
        public const float DefaultForceFieldAbsorbCapPerLevel = 30f;

        /// <summary>Radius of the bubble, metres (DECISION #3, MV-361) — pinned to the live world's
        /// normal gate width (3 m diameter), NOT leveled: a chokepoint-sized bubble that grew with
        /// upgrades would eventually swallow rooms whole.</summary>
        public const float DefaultForceFieldRadius = 1.5f;

        /// <summary>Power cells Force Field spends on activation (DECISION #2, MV-361) — fixed, not
        /// leveled, same reasoning as <see cref="DefaultForceFieldRadius"/>.</summary>
        public const int DefaultForceFieldActivationCost = 10;

        /// <summary>Damage the level-3 pop deals to everything touching the bubble when it bursts
        /// (DECISION #4: "confirmed in scope", no number pinned) — an authored placeholder until Lee
        /// tunes it, same status <see cref="DefaultWaterBalloonStopDurationSeconds"/> had before its
        /// own number was dialled in.</summary>
        public const float DefaultForceFieldPopDamage = 40f;

        /// <summary>Outward knockback speed, m/s, the level-3 pop shoves every robot touching the
        /// bubble with (DECISION #4) — a real launch, not <see cref="MaxWorlds.Core.DevTuning.SprayKnockback"/>'s
        /// near-zero cosmetic stagger, since this is meant to read as a counter-attack. Authored
        /// placeholder, same status as <see cref="DefaultForceFieldPopDamage"/>.</summary>
        public const float DefaultForceFieldPopKnockbackSpeed = 8f;

        /// <summary>The bubble's absorb cap at a given Force Field level — level 1 is the DECISION's
        /// pinned 60, each level above it adds <paramref name="perLevel"/> more headroom.</summary>
        public static float ForceFieldAbsorbCap(int level, float baseCap, float perLevel) =>
            Mathf.Max(0f, baseCap) + Mathf.Max(0f, perLevel) * Mathf.Max(0, level - 1);

        /// <summary>True once Force Field is leveled enough for its pop to deal damage and knock back
        /// everything touching the bubble (DECISION #4: "stays exactly where the Upgrade track already
        /// scoped it, level 3").</summary>
        public static bool ForceFieldPopDealsDamage(int level) => level >= 3;

        /// <summary>
        /// Applies <paramref name="incoming"/> damage against the bubble's remaining absorb budget.
        /// Pure and side-effect free so the cap-then-leak maths is unit-testable without a live
        /// <see cref="PlayerAbilities"/>: everything up to <paramref name="remainingCap"/> is eaten,
        /// anything past it leaks through to <see cref="MaxWorlds.Player.PlayerHealth"/> unabsorbed —
        /// the field never goes negative and never blocks more than it has left.
        /// </summary>
        public static (float Absorbed, float Leaked) ForceFieldAbsorb(float incoming, float remainingCap)
        {
            float safeIncoming = Mathf.Max(0f, incoming);
            float safeCap = Mathf.Max(0f, remainingCap);
            float absorbed = Mathf.Min(safeIncoming, safeCap);
            return (absorbed, safeIncoming - absorbed);
        }

        // --- Deployable Sentinels (MV-362) ---

        /// <summary>Power cells the Wall sentinel costs to deploy (DECISION, 16 Aug 2026, MV-408 — was
        /// 10; Lee flattened Wall and Gunner to the same 5-cell cost) — fixed, not leveled, same
        /// "authored, not a track" shape as <see cref="DefaultForceFieldActivationCost"/>.</summary>
        public const int DefaultSentinelWallCost = 5;

        /// <summary>Power cells the Gunner sentinel costs to deploy (DECISION, 16 Aug 2026, MV-408 —
        /// was 15).</summary>
        public const int DefaultSentinelGunnerCost = 5;

        /// <summary>The Wall's HP at Wall Strength Level 1 — sized against Max's own 200 max HP
        /// (<see cref="MaxWorlds.Player.PlayerHealth"/>, MV-315-baked) so a fresh wall reads as
        /// roughly "as tough as Max himself", a legible baseline before any upgrade.</summary>
        public const float DefaultSentinelWallBaseHp = 200f;

        /// <summary>Extra Wall HP each Wall Strength level beyond L1 adds — same additive "level =
        /// bigger number" shape as <see cref="DefaultForceFieldAbsorbCapPerLevel"/>.</summary>
        public const float DefaultSentinelWallHpPerLevel = 40f;

        /// <summary>The Wall's HP at a given Wall Strength level.</summary>
        public static float SentinelWallMaxHp(int level, float baseHp, float perLevel) =>
            Mathf.Max(1f, baseHp) + Mathf.Max(0f, perLevel) * Mathf.Max(0, level - 1);

        /// <summary>The Gunner's HP — fixed, not leveled ("one upgrade track only: the power of the
        /// hose"; its survivability never changes). Deliberately BELOW the Wall's base 200: "trades
        /// durability for damage" (spec) — a couple of Bruiser hits (28 dmg each,
        /// <see cref="MaxWorlds.Enemies.EnemyArchetype.Bruiser"/>) or a single Bomber splash
        /// (<see cref="MaxWorlds.Enemies.EnemyArchetype.Bomber"/>) puts a real dent in it.</summary>
        public const float DefaultSentinelGunnerHp = 60f;

        /// <summary>Fraction of Max's CURRENT primary per-tick damage the Gunner sentinel's shot deals
        /// at Gunner Power Level 1 — always below 1.0 by construction (see
        /// <see cref="SentinelGunnerDamagePerShot"/>), which is what DECISION's "always weaker than
        /// Max's CURRENT primary... it must stay below Max's current power as he upgrades" actually
        /// means in code: the sentinel's damage is computed as a fraction of whatever the RCDA Damage
        /// track currently is, not a fixed number that could eventually catch up to it.</summary>
        public const float DefaultSentinelGunnerPowerFraction = 0.4f;

        /// <summary>Extra fraction each Gunner Power level beyond L1 adds — capped so even a maxed
        /// track (<see cref="SentinelGunnerPowerFraction"/> clamps to 1.0) can never reach, let alone
        /// exceed, Max's own current output.</summary>
        public const float DefaultSentinelGunnerPowerFractionPerLevel = 0.1f;

        /// <summary>How far the Gunner sentinel's auto-fire reaches, metres — a little past the
        /// primary's own authored base reach (<see cref="MaxWorlds.Combat.WaterBlaster.DefaultRange"/>,
        /// 5 m) so a placed turret threatens the space around it, not just an adjacent robot.</summary>
        public const float DefaultSentinelGunnerRange = 7f;

        /// <summary>Seconds between Gunner sentinel shots.</summary>
        public const float DefaultSentinelGunnerFireInterval = 0.6f;

        /// <summary>How far the aimed placement joystick's reticle reaches, metres (MV-399, reversing
        /// MV-362's "deployed at Max's position" DECISION per Lee's 15 Aug 2026 request). Fixed, not
        /// leveled — same "authored, not a track" shape as <see cref="DefaultSentinelWallCost"/>.
        /// Matches <see cref="DefaultTeleportBaseDistance"/> rather than a fresh number: both need a
        /// single drag to cover "anywhere in the current arena" from one spot in the room.</summary>
        public const float DefaultSentinelPlacementRange = DefaultTeleportBaseDistance;

        /// <summary>Fraction of Max's current primary damage-per-tick the Gunner sentinel deals per
        /// shot at a given Gunner Power level — clamped below 1.0, so whatever <paramref
        /// name="currentPrimaryDamagePerTick"/> is (it already reflects Max's live RCDA Damage track,
        /// see the Gunner sentinel's own call site), this can never equal or exceed it.</summary>
        public static float SentinelGunnerPowerFraction(int level, float baseFraction, float perLevel) =>
            Mathf.Clamp01(Mathf.Max(0f, baseFraction) + Mathf.Max(0f, perLevel) * Mathf.Max(0, level - 1));

        /// <summary>The Gunner sentinel's actual per-shot damage right now.</summary>
        public static float SentinelGunnerDamagePerShot(float currentPrimaryDamagePerTick, int level, float baseFraction, float perLevel) =>
            Mathf.Max(0f, currentPrimaryDamagePerTick) * SentinelGunnerPowerFraction(level, baseFraction, perLevel);

        /// <summary>How many sentinels (any mix of Wall/Gunner) Max may have deployed at once, at a
        /// given Deployment Count level — the DECISION's "starts at 1, upgradeable to 2, 3, 4" maps
        /// directly: the level IS the slot count.</summary>
        public static int SentinelDeploymentSlots(int level) => Mathf.Max(1, level);

        /// <summary>Seconds a sentinel must go without taking a hit before its passive regen starts
        /// (MV-398, same-day reversal of MV-362's "no repair" DECISION — passive only, still no
        /// player-triggered repair). The ticket asked for a rate "consistent with the game's existing
        /// pacing" rather than a fresh number, so this aliases <see cref="PlayerTuning.RegenDelay"/>
        /// outright — Max's own out-of-combat regen (YT-80) IS that pacing.</summary>
        public const float DefaultSentinelRegenDelaySeconds = PlayerTuning.RegenDelay;

        /// <summary>HP/sec a sentinel heals once the delay has elapsed (MV-398) — same reuse as
        /// <see cref="DefaultSentinelRegenDelaySeconds"/>, aliasing <see cref="PlayerTuning.RegenPerSec"/>.
        /// A flat rate rather than a percentage means a beefier upgraded Wall takes longer to top up
        /// than a fresh one — more HP costs more time to heal, same as it costs more to whittle down.</summary>
        public const float DefaultSentinelRegenPerSec = PlayerTuning.RegenPerSec;
    }
}
