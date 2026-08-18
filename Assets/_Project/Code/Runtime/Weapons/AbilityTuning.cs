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

        /// <summary>Radius of the bubble at Level 1, metres (DECISION #3, MV-361) — originally pinned
        /// to the live world's normal gate width (3 m diameter) and never leveled; MV-422 ("Force
        /// Field radius now levels... levels raise absorb AND radius together") makes this the
        /// Level-1 starting point of a leveled axis instead of a permanent hard lock — see
        /// <see cref="ForceFieldRadius"/>.</summary>
        public const float DefaultForceFieldRadius = 1.5f;

        /// <summary>Extra bubble radius each Force Field level beyond L1 adds (MV-422) — same
        /// additive "level = bigger number" shape as <see cref="DefaultForceFieldAbsorbCapPerLevel"/>,
        /// small enough that even a maxed L5 bubble (2.5 m) still reads as a personal shield, not a
        /// room-swallowing dome.</summary>
        public const float DefaultForceFieldRadiusPerLevel = 0.25f;

        /// <summary>The bubble's radius at a given Force Field level — level 1 is the DECISION's
        /// originally-pinned 1.5 m, each level above it widens it further (MV-422).</summary>
        public static float ForceFieldRadius(int level, float baseRadius, float perLevel) =>
            Mathf.Max(0f, baseRadius) + Mathf.Max(0f, perLevel) * Mathf.Max(0, level - 1);

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

        // --- The Sentinel (MV-362, restructured MV-422) ---
        //
        // MV-422 deletes the Wall (Blocker) entirely — one sentinel only, the Gunner, now just
        // "Sentinel" — and replaces the old three tracks (Wall Strength/Gunner Power/Deployment
        // Count) with six axes under the RIG's u_sen node: Damage, Range, Health (children of
        // u_sen), then Move, Cost, Slots (children of Damage/Range/Health respectively). Every axis
        // below is keyed by its RIG id, not an enum — see RigState.

        /// <summary>Power cells deploying the sentinel costs at u_cst Level 0 (not yet leveled) —
        /// same flat 5-cell cost the old Wall/Gunner split shared (DECISION, 16 Aug 2026, MV-408).</summary>
        public const int DefaultSentinelCost = 5;

        /// <summary>Fraction each Cost (u_cst) level CUTS the deploy cost — same inverse "spend a
        /// level, pay less" shape as <see cref="DefaultRcdaDepletionRatePerLevel"/>. Floored (see
        /// <see cref="SentinelCost"/>) so a maxed track buys a much cheaper deploy, never a free one.</summary>
        public const float DefaultSentinelCostReductionPerLevel = 0.15f;

        /// <summary>The sentinel's deploy cost, power cells, at a given Cost (u_cst) level — level 0
        /// (not yet leveled, u_cst is a stat so this is its un-owned starting point) is the
        /// unmodified base; each level above it shaves off <paramref name="perLevel"/>, floored at
        /// 40% of base.</summary>
        public static int SentinelCost(int level, int baseCost, float perLevel) =>
            Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0, baseCost) * Mathf.Max(0.4f, 1f - perLevel * Mathf.Max(0, level))));

        /// <summary>The sentinel's HP at Health (u_hp) Level 0 (not yet leveled) — deliberately BELOW
        /// Max's own 200 max HP ("trades durability for damage", the old Gunner's own baseline): a
        /// couple of Bruiser hits (28 dmg each, <see cref="MaxWorlds.Enemies.EnemyArchetype.Bruiser"/>)
        /// or a single Bomber splash (<see cref="MaxWorlds.Enemies.EnemyArchetype.Bomber"/>) puts a
        /// real dent in it.</summary>
        public const float DefaultSentinelBaseHp = 60f;

        /// <summary>Extra HP each Health (u_hp) level adds — same additive "level = bigger number"
        /// shape as <see cref="DefaultForceFieldAbsorbCapPerLevel"/>. u_hp is a stat (spendable from
        /// level 0), unlike the old Wall Strength cap, so level 0 is a real, un-upgraded state, not
        /// an unreachable one.</summary>
        public const float DefaultSentinelHpPerLevel = 20f;

        /// <summary>The sentinel's max HP at a given Health (u_hp) level.</summary>
        public static float SentinelMaxHp(int level, float baseHp, float perLevel) =>
            Mathf.Max(1f, baseHp) + Mathf.Max(0f, perLevel) * Mathf.Max(0, level);

        /// <summary>Fraction of Max's CURRENT primary per-tick damage the sentinel's shot deals at
        /// Damage (u_dmg) Level 0 — always below 1.0 by construction (see
        /// <see cref="SentinelDamagePerShot"/>), same "always weaker than Max's CURRENT primary...
        /// it must stay below Max's current power as he upgrades" rule the old Gunner Power track
        /// enforced.</summary>
        public const float DefaultSentinelDamageFraction = 0.4f;

        /// <summary>Extra fraction each Damage (u_dmg) level adds — capped so even a maxed track
        /// (<see cref="SentinelDamageFraction"/> clamps to 1.0) can never reach, let alone exceed,
        /// Max's own current output.</summary>
        public const float DefaultSentinelDamageFractionPerLevel = 0.1f;

        /// <summary>Fraction of Max's current primary damage-per-tick the sentinel deals per shot at
        /// a given Damage (u_dmg) level — clamped below 1.0, so whatever <paramref
        /// name="currentPrimaryDamagePerTick"/> is (it already reflects Max's live RCDA Damage track,
        /// see the sentinel's own call site), this can never equal or exceed it.</summary>
        public static float SentinelDamageFraction(int level, float baseFraction, float perLevel) =>
            Mathf.Clamp01(Mathf.Max(0f, baseFraction) + Mathf.Max(0f, perLevel) * Mathf.Max(0, level));

        /// <summary>The sentinel's actual per-shot damage right now.</summary>
        public static float SentinelDamagePerShot(float currentPrimaryDamagePerTick, int level, float baseFraction, float perLevel) =>
            Mathf.Max(0f, currentPrimaryDamagePerTick) * SentinelDamageFraction(level, baseFraction, perLevel);

        /// <summary>How far the sentinel's auto-fire reaches at Range (u_rng) Level 0 — a little past
        /// the primary's own authored base reach (<see cref="MaxWorlds.Combat.WaterBlaster.DefaultRange"/>,
        /// 5 m) so a placed turret threatens the space around it, not just an adjacent robot.</summary>
        public const float DefaultSentinelRange = 7f;

        /// <summary>Extra auto-fire reach each Range (u_rng) level adds.</summary>
        public const float DefaultSentinelRangePerLevel = 1.5f;

        /// <summary>The sentinel's auto-fire reach at a given Range (u_rng) level.</summary>
        public static float SentinelRange(int level, float baseRange, float perLevel) =>
            Mathf.Max(0f, baseRange) + Mathf.Max(0f, perLevel) * Mathf.Max(0, level);

        /// <summary>Seconds between sentinel shots — fixed, not one of the six leveled axes.</summary>
        public const float DefaultSentinelFireInterval = 0.6f;

        /// <summary>How fast the sentinel follows Max at a given Move (u_mov) level, m/s — level 0
        /// (u_mov is a stat, un-owned starting point) means the sentinel does not follow at all
        /// (MV-422: "Move is new — Gunner HP is currently fixed and it cannot move"), matching the
        /// pre-MV-422 behaviour exactly until a player actually spends on this axis.</summary>
        public const float DefaultSentinelMoveSpeedPerLevel = 1.2f;

        /// <summary>True once the sentinel should follow Max at all (Move level &gt;= 1).</summary>
        public static bool SentinelCanMove(int level) => level >= 1;

        /// <summary>The sentinel's follow speed at a given Move (u_mov) level, m/s — 0 at level 0
        /// (stationary, matching the sentinel's pre-MV-422 behaviour), rising linearly above it.</summary>
        public static float SentinelMoveSpeed(int level, float perLevel) =>
            SentinelCanMove(level) ? Mathf.Max(0f, perLevel) * level : 0f;

        /// <summary>How far behind Max the sentinel holds station while following, metres — close
        /// enough to stay useful, far enough that it never crowds his own hitbox/aim.</summary>
        public const float DefaultSentinelStandoffDistance = 2.5f;

        /// <summary>One frame of the sentinel's follow-at-a-standoff-distance movement (MV-422) — pure
        /// so the step is unit-testable without a live Transform. Moves <paramref name="current"/>
        /// toward <paramref name="target"/> at <paramref name="speed"/>, but only far enough to close
        /// to <paramref name="standoff"/> metres away — it never walks into Max's own feet, and holds
        /// still (no overshoot/jitter) once already within the standoff band.</summary>
        public static Vector3 SentinelStandoffStep(Vector3 current, Vector3 target, float standoff, float speed, float dt)
        {
            Vector3 toTarget = target - current;
            float distance = toTarget.magnitude;
            float excess = distance - Mathf.Max(0f, standoff);
            if (excess <= 0f || speed <= 0f || dt <= 0f) return current;

            float step = Mathf.Min(excess, speed * dt);
            return current + toTarget.normalized * step;
        }

        /// <summary>How many sentinels Max may have deployed at once, at a given Slots (u_slt) level —
        /// same "the level IS the slot count, floored at 1" shape the old Deployment Count track used.</summary>
        public static int SentinelDeploymentSlots(int level) => Mathf.Max(1, level);

        /// <summary>How far the aimed placement joystick's reticle reaches, metres (MV-399, reversing
        /// MV-362's "deployed at Max's position" DECISION per Lee's 15 Aug 2026 request). Fixed, not
        /// leveled — same "authored, not a track" shape as <see cref="DefaultSentinelCost"/>.
        /// Matches <see cref="DefaultTeleportBaseDistance"/> rather than a fresh number: both need a
        /// single drag to cover "anywhere in the current arena" from one spot in the room.</summary>
        public const float DefaultSentinelPlacementRange = DefaultTeleportBaseDistance;

        // --- Magneto (MV-422, e_mag) ---

        /// <summary>Cell pull radius at Magneto Level 1, metres (MV-422: "3 m at level 1").</summary>
        public const float DefaultMagnetoPullRadiusBase = 3f;

        /// <summary>Extra pull radius each Magneto level beyond L1 adds (MV-422: "+2 m per level to
        /// 11 m at level 5" — 3 + 2*4 = 11).</summary>
        public const float DefaultMagnetoPullRadiusPerLevel = 2f;

        /// <summary>How fast a caught power cell flies to Max, m/s — fast enough to read as "pulled",
        /// not a lazy drift.</summary>
        public const float DefaultMagnetoPullSpeed = 10f;

        /// <summary>Magneto's pull radius at a given level — 0 while unowned (level 0), so an
        /// un-drafted Magneto pulls nothing.</summary>
        public static float MagnetoPullRadius(int level, float baseRadius, float perLevel) =>
            level <= 0 ? 0f : Mathf.Max(0f, baseRadius) + Mathf.Max(0f, perLevel) * Mathf.Max(0, level - 1);

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
