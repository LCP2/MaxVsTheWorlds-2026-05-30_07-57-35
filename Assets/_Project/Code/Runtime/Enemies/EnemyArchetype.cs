using System;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Enemies
{
    // Appended, not inserted (same rule as RobotEnemy.State) — Gunner/Launcher/Blinker/Bolter are new
    // archetype ROWS, not a renumbering of the existing tiers. Lurker (MV-688) follows the same rule.
    // Turret (MV-691) follows it too — a static, wall-mounted lobber, appended after Lurker.
    public enum EnemyKind { Rusher, Bruiser, Heavy, Brute, Gunner, Launcher, Blinker, Bolter, Lurker, Turret }

    public enum EnemyShape { Capsule, Box }

    /// <summary>Parses the lowercase kind-name strings authored content uses (MV-559's
    /// <see cref="MaxWorlds.Arena.WorldGarrisonEntry.kind"/>, same key set as
    /// <see cref="MaxWorlds.Arena.WorldComposition"/>'s field names) into an <see cref="EnemyKind"/>.</summary>
    public static class EnemyKindNames
    {
        public static bool TryParse(string s, out EnemyKind kind)
        {
            switch (s?.Trim().ToLowerInvariant())
            {
                case "rusher": kind = EnemyKind.Rusher; return true;
                case "bruiser": kind = EnemyKind.Bruiser; return true;
                case "heavy": kind = EnemyKind.Heavy; return true;
                case "brute": kind = EnemyKind.Brute; return true;
                case "gunner": kind = EnemyKind.Gunner; return true;
                case "launcher": kind = EnemyKind.Launcher; return true;
                case "blinker": kind = EnemyKind.Blinker; return true;
                case "bolter": kind = EnemyKind.Bolter; return true;
                case "lurker": kind = EnemyKind.Lurker; return true;
                case "turret": kind = EnemyKind.Turret; return true;
                default: kind = default; return false;
            }
        }
    }

    /// <summary>
    /// What one kind of domestic robot IS (YT-66) — stats and silhouette in one place, so a second
    /// enemy type is a row of data rather than a second class.
    ///
    /// The slice ran on a single enemy, and a fight of one identical blob has no texture: every
    /// threat wanted the same response, so there was no decision to make. There are now two, and
    /// they're deliberately opposites — see <see cref="Rusher"/> and <see cref="Bruiser"/>.
    ///
    /// Collider sizes are stated in WORLD metres and converted against the body scale when the robot
    /// is built, because a CharacterController silently multiplies its height/radius by the
    /// transform's scale — which is how you end up with a collider that doesn't match the thing you
    /// can see.
    /// </summary>
    public readonly struct EnemyArchetype
    {
        public readonly EnemyKind Kind;
        public readonly EnemyShape Shape;
        public readonly Vector3 BodyScale;     // the primitive's localScale — the silhouette
        public readonly float ColliderHeight;  // world metres
        public readonly float ColliderRadius;  // world metres

        public readonly float MoveSpeed;
        public readonly float MaxHealth;
        public readonly float ContactDamage;
        public readonly float ContactRadius;
        public readonly float LungeRange;
        public readonly float TelegraphTime;
        public readonly float LungeSpeed;
        public readonly float LungeTime;
        public readonly float RecoverTime;
        public readonly float KnockbackDecay;

        /// <summary>How close a RANGED kind (<see cref="EnemyKind.Gunner"/>/<see cref="EnemyKind.Launcher"/>)
        /// tries to stay from Max — inside this it backs off instead of closing, which is the whole
        /// difference between "keeps its distance" and a rusher wearing a different silhouette (MV-293).
        /// Zero for every melee kind: they have nothing to retreat from.</summary>
        public readonly float StandoffRange;

        /// <summary>How often <see cref="EnemyKind.Blinker"/> may teleport-flank while it hasn't yet
        /// closed to melee range (MV-293). Zero for every other kind — they only ever walk.</summary>
        public readonly float TeleportCooldown;

        /// <summary>Damage dealt per contact-cooldown tick while standing in touch range (MV-428) —
        /// the readability fix's Change 1: Bruiser/Heavy/Brute lose the lunge entirely and hit on a
        /// timer instead. Deliberately a SEPARATE number from <see cref="ContactDamage"/> (which
        /// still describes the old single-hit lunge and stays what <see cref="EnemyArchetypeTests"/>
        /// compares kinds by) — a repeating tick has to be worth much less per hit than a one-shot
        /// lunge, or a crowd standing in contact turns 200 HP into a near-instant death. Zero for
        /// every kind that still lunges — they never read this field.</summary>
        public readonly float TouchDamage;

        /// <summary>What this kind's nameplate reads (MV-701) — base value matches the hardcoded
        /// names <see cref="RobotEnemy.ReadoutName"/> used to own directly (a Gunner still reads
        /// "LASER", not its enum name); a world's <see cref="MaxWorlds.Arena.WorldEnemyOverride.displayName"/>
        /// replaces it via <see cref="For"/>/<see cref="WithOverride"/>.</summary>
        public readonly string DisplayName;

        /// <summary>The look a world's override paints this kind with (MV-701), e.g. World 2's
        /// <c>"stormdrain"</c> for the Rusher/Scrap Rat — empty for the base table, which wears
        /// whatever <see cref="MaxWorlds.VFX.CharacterSkin.RoleFor"/> already gives its <see cref="Kind"/>.
        /// Resolved to an actual colour in <see cref="MaxWorlds.VFX.CharacterSkin"/> (colour roles live
        /// there, not here) — this is just the tag a world authored.</summary>
        public readonly string Skin;

        /// <summary>An override's optional direct <see cref="MaxWorlds.VFX.CharacterRole"/> name (MV-701) —
        /// an escape hatch for a world that wants an existing role's colour on a different kind, rather
        /// than a brand-new named skin. Empty for the base table.</summary>
        public readonly string ColourRole;

        public EnemyArchetype(EnemyKind kind, EnemyShape shape, Vector3 bodyScale,
            float colliderHeight, float colliderRadius, float moveSpeed, float maxHealth,
            float contactDamage, float contactRadius, float lungeRange, float telegraphTime,
            float lungeSpeed, float lungeTime, float recoverTime, float knockbackDecay,
            float standoffRange = 0f, float teleportCooldown = 0f, float touchDamage = 0f,
            string displayName = null, string skin = null, string colourRole = null)
        {
            Kind = kind; Shape = shape; BodyScale = bodyScale;
            ColliderHeight = colliderHeight; ColliderRadius = colliderRadius;
            MoveSpeed = moveSpeed; MaxHealth = maxHealth;
            ContactDamage = contactDamage; ContactRadius = contactRadius;
            LungeRange = lungeRange; TelegraphTime = telegraphTime;
            LungeSpeed = lungeSpeed; LungeTime = lungeTime; RecoverTime = recoverTime;
            KnockbackDecay = knockbackDecay;
            StandoffRange = standoffRange; TeleportCooldown = teleportCooldown;
            TouchDamage = touchDamage;
            DisplayName = string.IsNullOrEmpty(displayName) ? kind.ToString().ToUpperInvariant() : displayName;
            Skin = skin ?? string.Empty;
            ColourRole = colourRole ?? string.Empty;
        }

        /// <summary>Where the body's origin must sit for its feet to touch the ground.</summary>
        public float SpawnHeight => ColliderHeight * 0.5f;

        /// <summary>Max's own size, for comparison. He is a 1 m-wide, 2 m-tall capsule. Nothing in
        /// the swarm may out-size him: a crowd of things bigger than the player reads as terrain,
        /// not as enemies (YT-74).</summary>
        public const float PlayerRadius = 0.5f;
        public const float PlayerHeight = 2f;

        /// <summary>The original robot (YT-36/YT-63): a small capsule, deliberately SMALLER than Max
        /// — he's the hero, and a swarm of knee-high machines reads as a swarm (YT-74).
        ///
        /// MV-289 retuned speed to ~90% of Max's 3.01 (2.71, was YT-169's 1.85/~60%). MV-315 then
        /// baked Lee's dialed-in playtest number, 2.04 (~68% of Max) — the tuning panel's own 70%
        /// reading, rounded, off the MV-289 default.</summary>
        public static EnemyArchetype Rusher => new EnemyArchetype(
            EnemyKind.Rusher, EnemyShape.Capsule, new Vector3(0.8f, 0.7f, 0.8f),
            colliderHeight: 1.4f, colliderRadius: 0.4f,
            moveSpeed: 0.93f,   // MV-658: baked from Lee's 2026-09-02 tuning pass (was 2.04)
            // MV-289: 36 -> 32. MV-315 also re-baked the live health multiplier to 1.26x (was
            // 1.42x), landing ~40 effective HP at run start.
            maxHealth: 32f,
            contactDamage: 12f, contactRadius: 1.0f,
            lungeRange: 2.2f, telegraphTime: 0.55f,
            lungeSpeed: 11f, lungeTime: 0.22f, recoverTime: 0.7f,
            knockbackDecay: 28f,
            displayName: "RUSHER");

        /// <summary>
        /// The contrast (YT-66): a fridge on legs. Half the rusher's speed and four times its
        /// health, so it can never catch you but it will not go away — you cannot simply back off,
        /// because backing off from the bruiser walks you into the rushers behind you. Killing it
        /// costs ~3 seconds of held spray, which is the decision the fight was missing: spend that
        /// time, or leave it alive and keep managing it.
        ///
        /// It hits for more than twice as much, behind a wind-up nearly twice as long — so the
        /// damage is fair, and reading the tell is the skill. Its recovery is long, which is your
        /// window to punish. It barely notices the blaster's knockback, so the shove that scatters
        /// rushers does not save you from this.
        ///
        /// A chunky box against the rushers' small capsules: at the fixed ~72° camera the two are
        /// unmistakable at a glance, which is the point (Pillar 4). Its threat is its HEALTH and its
        /// hit, not its footprint — it's half again the rusher's width but still no bigger than Max,
        /// because a swarm of things larger than the player stops reading as a swarm and starts
        /// reading as a moving wall (YT-74).
        ///
        /// MV-428: no longer lunges at all — "a wardrobe should not leap". It walks to contact and
        /// hits for <see cref="TouchDamage"/> on a per-robot cooldown (<see cref="RobotEnemy"/>'s
        /// <c>TickContactTouch</c>) instead of the old single-hit lunge; <see cref="ContactDamage"/>
        /// (28) is kept as the archetype's comparative "how hard does it hit" number — still what
        /// <see cref="EnemyArchetypeTests"/> checks it against the rusher's — and is otherwise unread.
        /// </summary>
        public static EnemyArchetype Bruiser => new EnemyArchetype(
            EnemyKind.Bruiser, EnemyShape.Box, new Vector3(1.15f, 1.15f, 1.15f),
            colliderHeight: 1.15f, colliderRadius: 0.55f,
            // Half the rusher's speed, preserved (YT-66's "fridge on legs"): the bruiser scales with
            // whatever the rusher/panel default is to stay the slow tank (was 0.925 = half of YT-169's
            // 1.85, then 1.355 = half of MV-289's 2.71, now 1.02 = half of MV-315's 2.04). Flag: if
            // Lee wants ALL robots flat at the rusher's speed, this is the one line to change.
            moveSpeed: 0.46f, maxHealth: 100f,   // 68 -> 100 per Lee's V12 workbook (2026-09-01); moveSpeed baked MV-658 (was 1.02)
            // (MV-540 previously cut 135 -> 68; before that MV-512's 150 -> 135, before that YT-194's 100 -> 150)
            contactDamage: 28f, contactRadius: 1.4f,
            lungeRange: 2.6f, telegraphTime: 1.0f,
            lungeSpeed: 9f, lungeTime: 0.35f, recoverTime: 1.4f,
            knockbackDecay: 70f,
            touchDamage: 10f,  // MV-428: see the fix comment for the crowd-DPS arithmetic
            displayName: "BRUISER");

        /// <summary>The first later-area tier (v0.5 recut spec §2-3, MV-224): Area 5 onward
        /// substitutes a slice of the bruiser's large slots with something that just plain outlasts
        /// it. Lee's escalation plan (spec §2) is explicitly composition-driven, not count-driven —
        /// this is the composition move, not a new fight pattern, so per the ticket "minimal distinct
        /// behaviour is fine": same shape family as the bruiser, chunkier silhouette so the two large
        /// tiers still tell apart at a glance (Pillar 4), same size ceiling as everything else in the
        /// swarm (YT-74) — it's allowed to be the biggest robot, never bigger than Max.</summary>
        public static EnemyArchetype Heavy => new EnemyArchetype(
            EnemyKind.Heavy, EnemyShape.Box, new Vector3(1.2f, 1.35f, 1.2f),
            colliderHeight: 1.55f, colliderRadius: 0.58f,
            moveSpeed: 0.39f, maxHealth: 260f,   // ~2.6x the bruiser's 100 (was ~3.82x the bruiser's 68 pre-V12); moveSpeed baked MV-658 (was 0.85)
            contactDamage: 32f, contactRadius: 1.5f,
            lungeRange: 2.6f, telegraphTime: 1.05f,
            lungeSpeed: 8.5f, lungeTime: 0.35f, recoverTime: 1.5f,
            knockbackDecay: 95f,
            touchDamage: 12f,  // MV-428: no lunge — see Bruiser's doc comment
            displayName: "HEAVY");

        /// <summary>The second later-area tier (Area 8 on, spec §2 table) — the top of the
        /// composition ladder, introduced alongside <see cref="Heavy"/> rather than replacing it (the
        /// spec table has both present from Area 8). Same "minimal distinct behaviour" idiom as
        /// <see cref="Heavy"/>, sized apart from it the same way the bruiser sizes apart from the
        /// rusher.</summary>
        public static EnemyArchetype Brute => new EnemyArchetype(
            EnemyKind.Brute, EnemyShape.Box, new Vector3(1.25f, 1.5f, 1.25f),
            colliderHeight: 1.9f, colliderRadius: 0.6f,
            moveSpeed: 0.34f, maxHealth: 420f,   // ~4.2x the bruiser's 100, well past the heavy's 260 (was ~6.18x pre-V12); moveSpeed baked MV-658 (was 0.75)
            contactDamage: 38f, contactRadius: 1.6f,
            lungeRange: 2.6f, telegraphTime: 1.15f,
            lungeSpeed: 7.5f, lungeTime: 0.35f, recoverTime: 1.6f,
            knockbackDecay: 120f,
            touchDamage: 14f,  // MV-428: no lunge — see Bruiser's doc comment
            displayName: "BRUTE");

        /// <summary>
        /// Ranged laser (MV-293), displayed to the player as "LASER" (MV-404: display-only rename,
        /// this <c>EnemyKind.Gunner</c> identifier is unchanged). Keeps its distance in the 4.5–6.3 m
        /// band (MV-497: was 4.5–9 m) — inside that it backs off rather than closing, so the answer to a Gunner is never
        /// just "walk at it" the way it is for every melee kind. Aims live while telegraphing, then
        /// commits to a LOCKED beam (the same "no info through the wind-up" rule as a lunge, see
        /// <see cref="RobotEnemy"/>'s Telegraph): stand still after the tell fires and it hits,
        /// side-step out of the beam's width or break line of sight and it doesn't.
        /// <see cref="ContactDamage"/> here means damage/second while the beam holds, not a single
        /// hit; <see cref="ContactRadius"/> means the beam's half-width.
        /// Small-tier silhouette, but MV-404 (16 Aug 2026, Lee) deliberately lifted its health ABOVE
        /// the rusher's band — was 26 (same small-tier health as the rusher's 32), now ~1.5x that
        /// original baseline. This reverses MV-293's "no small/ranged kind may out-tank the rusher"
        /// invariant for the Gunner specifically; see the loosened assertion in
        /// EnemyArchetypeTests.GunnerAndLauncher_AreNoTougherThanARusher_SoClosingTheGapIsAlwaysThePunish.
        /// </summary>
        public static EnemyArchetype Gunner => new EnemyArchetype(
            EnemyKind.Gunner, EnemyShape.Capsule, new Vector3(0.8f, 0.7f, 0.8f),
            colliderHeight: 1.4f, colliderRadius: 0.4f,
            moveSpeed: 1.00f, maxHealth: 39f,   // MV-404: 26 -> 39, ~50% harder to kill per Lee's ask; moveSpeed baked MV-658 (was 2.2)
            contactDamage: 18f,   // DPS while the beam holds
            contactRadius: 0.6f,  // beam half-width — wider than Max's 0.5 m radius, still side-steppable
            lungeRange: 6.3f,     // max fire range — MV-497: cut 30% from 9m per Lee's ask
            telegraphTime: 0.5f,  // aim wind-up — the dodge window
            lungeSpeed: 0f,       // it never moves during the shot
            lungeTime: 1.1f,      // beam duration
            recoverTime: 1.3f,
            knockbackDecay: 28f,
            standoffRange: 4.5f,
            displayName: "LASER");   // MV-404: display-only rename, EnemyKind.Gunner unchanged

        /// <summary>
        /// Lobs a slow homing missile (MV-293) rather than closing — pure area denial, forcing the
        /// player to keep moving instead of camping a good spot. Keeps a wider distance band than the
        /// Gunner (5–10 m) and its wind-up is heavier, since the payoff (a splash, not a beam-thin
        /// laser) is bigger. <see cref="ContactDamage"/>/<see cref="ContactRadius"/> here are the
        /// missile's explosion damage and splash radius; <see cref="LungeSpeed"/> is the missile's
        /// flight speed — deliberately slow (<see cref="HomingMissile"/>'s own turn rate is gentle
        /// too), so a player who's watching can outwalk or juke it rather than eat a guaranteed hit.
        /// </summary>
        public static EnemyArchetype Launcher => new EnemyArchetype(
            EnemyKind.Launcher, EnemyShape.Capsule, new Vector3(0.85f, 0.75f, 0.85f),
            colliderHeight: 1.45f, colliderRadius: 0.42f,
            // 30 -> 45 per Lee's V12 workbook (2026-09-01, MV-638), then 45 -> 40 per Lee's V12c
            // workbook (2026-09-02, MV-642) — still deliberately lifted ABOVE the rusher's 32 HP,
            // same reversal MV-404 already gave the Gunner: it no longer belongs in the
            // "one-rusher-shot kill" invariant (see EnemyArchetypeTests, updated the same day).
            // MV-325: speed must invert with power too — a Launcher has less HP than the rusher, so it
            // has to be at least as quick, not slower (was 1.8, below the rusher's 2.04).
            moveSpeed: 0.96f, maxHealth: 40f,   // moveSpeed baked MV-658 (was 2.1)
            contactDamage: 22f,   // splash damage
            contactRadius: 2.0f,  // splash radius
            lungeRange: 10f,      // max fire range
            telegraphTime: 0.7f,  // lob wind-up — heavier tell than the Gunner's aim
            lungeSpeed: 4.5f,     // missile flight speed — slow and dodgeable
            lungeTime: 0.3f,      // release beat before it recovers
            recoverTime: 2.2f,    // area-denial cadence, not rapid fire
            knockbackDecay: 28f,
            standoffRange: 5f,
            displayName: "LAUNCHER");

        /// <summary>
        /// Teleport-flanks Max (MV-293) instead of relying on raw pursuit speed — the one kind you
        /// cannot simply out-position by backing away in a straight line, because it cheats the
        /// distance instead of closing it. Otherwise a rusher: once it lands from a blink it fights
        /// with the same melee lunge as every other close-range kind (<see cref="RobotEnemy"/>'s
        /// default Lunge case covers it). <see cref="TeleportCooldown"/> is how often it may blink
        /// while it hasn't yet reached melee range; <see cref="EnemyArchetype.TelegraphTime"/> doubles
        /// as the blink's own charge-up (it has no other use for a kind that's never mid-lunge and
        /// mid-teleport at once).
        /// </summary>
        public static EnemyArchetype Blinker => new EnemyArchetype(
            EnemyKind.Blinker, EnemyShape.Capsule, new Vector3(0.75f, 0.75f, 0.75f),
            colliderHeight: 1.35f, colliderRadius: 0.4f,
            // MV-325: was 2.4 — faster than the Gunner despite having more HP (26 vs 30), which
            // inverted the "weakest is fastest" rule. Its mobility edge is the teleport, not raw
            // speed ("Otherwise a rusher"), so it sits with the Launcher just above the rusher.
            // 30 -> 45 per Lee's V12 workbook (2026-09-01, MV-638), then 45 -> 40 per Lee's V12c
            // workbook (2026-09-02, MV-642) — see Launcher's comment above; the "one-rusher-shot
            // kill" invariant no longer applies to Blinker either.
            moveSpeed: 0.96f, maxHealth: 40f,   // moveSpeed baked MV-658 (was 2.1)
            contactDamage: 14f, contactRadius: 1.0f,
            lungeRange: 2.2f, telegraphTime: 0.5f,
            lungeSpeed: 11f, lungeTime: 0.22f, recoverTime: 0.7f,
            knockbackDecay: 28f,
            teleportCooldown: 4.5f,
            displayName: "BLINKER");

        /// <summary>
        /// Fires a straight-line rod bolt (MV-539) rather than a beam or a homing splash — the third
        /// ranged answer, and the first with no tracking of any kind: once fired, the shot's direction
        /// is fixed for its whole flight (<see cref="BolterBolt"/>), so the punish for standing in a
        /// straight line is entirely on the player to read and step out of, not on the game to enforce.
        /// <see cref="LungeSpeed"/> doubles as the bolt's own flight speed (same idiom as
        /// <see cref="Launcher"/>'s missile); <see cref="ContactRadius"/> is the bolt's hit-check radius
        /// against the player. <see cref="ContactDamage"/> is deliberately left at 0 and unread — the
        /// ticket's own AC1 requires the hit amount to be resolved from the player's live max health at
        /// the moment of impact, not authored here as a flat number.
        /// MV-293's "every small-tier kind stays a one-rusher-shot kill" invariant no longer applies to
        /// the Bolter (nor to <see cref="Launcher"/>/<see cref="Blinker"/>) as of Lee's V12 workbook,
        /// 2026-09-01 — all three now sit above the Rusher's HP on purpose.
        /// </summary>
        public static EnemyArchetype Bolter => new EnemyArchetype(
            EnemyKind.Bolter, EnemyShape.Capsule, new Vector3(0.8f, 0.7f, 0.8f),
            colliderHeight: 1.4f, colliderRadius: 0.4f,
            // 30 -> 45 per Lee's V12 workbook (2026-09-01, MV-638), then 45 -> 40 per Lee's V12c
            // workbook (2026-09-02, MV-642) — same reversal as Launcher/Blinker above.
            moveSpeed: 0.96f, maxHealth: 40f,   // moveSpeed baked MV-658 (was 2.1)
            contactDamage: 0f,    // unread — bolt damage is resolved live off PlayerHealth.Max (AC1)
            contactRadius: 0.35f, // bolt hit-check radius against the player
            lungeRange: 9f,       // max fire range
            telegraphTime: 0.35f, // aim wind-up / dodge tell
            lungeSpeed: 14f,      // bolt flight speed
            lungeTime: 0f,        // instant release — the whole cadence is telegraph + recover (1.25s)
            recoverTime: 0.9f,    // ~1.25s per shot with the telegraph above — clearly faster than the Launcher's 2.2s
            knockbackDecay: 28f,
            standoffRange: 4.5f,
            displayName: "BOLTER");

        /// <summary>
        /// Grate Lurker (MV-688): a thin maintenance bot that stays SUBMERGED — invulnerable and
        /// invisible, the authored grate itself standing in as its visible body — until Max is close
        /// and it has been seen once (the universal wake rule), at which point it cycles RATTLE →
        /// EMERGE (a normal, damageable target for a short combat window) → SUBMERGE → reappear at
        /// another authored grate (<see cref="LurkerCycle"/> owns the pure timing; killing it while
        /// emerged is the only way to kill it). No lunge — a quick melee tick instead
        /// (<see cref="ContactDamage"/> is spent per hit, not per lunge). <see cref="MoveSpeed"/> is
        /// authored per the ticket but unread: this kind never chases, so kiteability doesn't apply to
        /// it the way it does every other archetype (deliberately excluded from
        /// EnemyArchetypeTests.AllArchetypes for that reason).
        /// </summary>
        public static EnemyArchetype Lurker => new EnemyArchetype(
            EnemyKind.Lurker, EnemyShape.Capsule, new Vector3(0.6f, 1.2f, 0.6f),
            colliderHeight: 1.5f, colliderRadius: 0.3f,
            moveSpeed: 2.6f, maxHealth: 45f,
            contactDamage: 11f,    // per hit, spent by RobotEnemy's Lurker-only contact tick, not a lunge
            contactRadius: 1.0f,
            lungeRange: 2.2f, telegraphTime: 0.55f,   // unread — Lurker never reaches Telegraph/Lunge
            lungeSpeed: 11f, lungeTime: 0.22f, recoverTime: 0.7f,
            knockbackDecay: 28f,
            displayName: "LURKER");

        /// <summary>
        /// Pipe Turret (MV-691): a static, wall-mounted lobber — the first ranged kind that never
        /// moves at all (MoveSpeed 0; that alone is what excludes it from the deck leash, the
        /// steering machinery and a Replicator's lure — <see cref="MaxWorlds.Factories.Replicator.TickLure"/>
        /// excludes it outright the same way it already excludes <see cref="Lurker"/>). Fires a
        /// corrosive coolant glob (<see cref="CorrosiveGlob"/>) rather than the Launcher's homing
        /// missile or the Bolter's straight rod — no tracking of any kind, aimed at Max's position at
        /// the instant it fires, and its impact leaves a puddle that marks anything standing in it
        /// CORRODED (<see cref="CorrodedStatus"/>) rather than dealing a bigger single hit.
        /// <see cref="ContactRadius"/> doubles as the glob's own splash radius (same "ranged kind's
        /// ContactRadius feeds the projectile" idiom <see cref="Launcher"/> already uses);
        /// <see cref="LungeSpeed"/> doubles as the glob's flight speed. <see cref="LungeTime"/> is 0 —
        /// same "instant release, the whole cadence is telegraph + recover" idiom as
        /// <see cref="Bolter"/> — so telegraphTime (0.4, the ticket's own nozzle-glow tell) +
        /// recoverTime (1.8) lands the ticket's authored 2.2 s cadence exactly.
        /// </summary>
        public static EnemyArchetype Turret => new EnemyArchetype(
            EnemyKind.Turret, EnemyShape.Box, new Vector3(0.9f, 0.9f, 0.9f),
            colliderHeight: 1.0f, colliderRadius: 0.5f,
            moveSpeed: 0f, maxHealth: 60f,
            contactDamage: 0f,     // no contact damage — it never gets close enough to touch Max
            contactRadius: 1.5f,   // the glob's own splash radius
            lungeRange: 11f,       // max fire range
            telegraphTime: 0.4f,   // the ticket's own nozzle-glow tell
            lungeSpeed: 7f,        // glob flight speed
            lungeTime: 0f,         // instant release — the whole cadence is telegraph + recover (2.2s)
            recoverTime: 1.8f,     // 0.4 + 1.8 = the ticket's authored 2.2s cadence
            knockbackDecay: 28f,
            displayName: "TURRET");

        public static EnemyArchetype Of(EnemyKind kind) => kind switch
        {
            EnemyKind.Bruiser => Bruiser,
            EnemyKind.Heavy => Heavy,
            EnemyKind.Brute => Brute,
            EnemyKind.Gunner => Gunner,
            EnemyKind.Launcher => Launcher,
            EnemyKind.Blinker => Blinker,
            EnemyKind.Bolter => Bolter,
            EnemyKind.Lurker => Lurker,
            EnemyKind.Turret => Turret,
            _ => Rusher,
        };

        /// <summary>Whether <paramref name="kind"/> counts as "large" for economy purposes (v0.5
        /// recut spec §5, MV-224): the bruiser, heavy and brute tiers all drop the large-kill loot
        /// and count toward the parts cadence — only the rusher is the small tier that drops
        /// nothing (WV-226).</summary>
        public static bool IsLarge(EnemyKind kind) => kind != EnemyKind.Rusher;

        /// <summary>The same archetype, tougher (YT-181 Invasion Level): health and contact damage
        /// scaled by <paramref name="multiplier"/>, everything else — speed, silhouette, timing —
        /// untouched. Speed is deliberately left alone: the kiteability tuning (YT-63/YT-80/YT-106)
        /// is a separate, already-balanced knob, and this escalation is meant to be answered by the
        /// player's growing loadout, not by making the swarm literally faster to react to.</summary>
        public EnemyArchetype Toughened(float multiplier) => new EnemyArchetype(
            Kind, Shape, BodyScale, ColliderHeight, ColliderRadius,
            MoveSpeed, MaxHealth * multiplier, ContactDamage * multiplier, ContactRadius,
            LungeRange, TelegraphTime, LungeSpeed, LungeTime, RecoverTime, KnockbackDecay,
            StandoffRange, TeleportCooldown, TouchDamage * multiplier,
            DisplayName, Skin, ColourRole);

        /// <summary>The same archetype with only its HEALTH scaled (YT-194's "Robot health" slider) —
        /// contact damage, speed, silhouette and timing are all untouched. Kept separate from
        /// <see cref="Toughened"/>, which scales health AND damage together for the Invasion Level:
        /// this is the player-dialled baseline that escalation still layers its own toughening on
        /// top of, so the two knobs compose rather than fight.</summary>
        public EnemyArchetype WithHealthMultiplier(float multiplier) => new EnemyArchetype(
            Kind, Shape, BodyScale, ColliderHeight, ColliderRadius,
            MoveSpeed, MaxHealth * multiplier, ContactDamage, ContactRadius,
            LungeRange, TelegraphTime, LungeSpeed, LungeTime, RecoverTime, KnockbackDecay,
            StandoffRange, TeleportCooldown, TouchDamage,
            DisplayName, Skin, ColourRole);

        /// <summary>The same lookup as <see cref="Of"/>, with this world's own
        /// <see cref="MaxWorlds.Arena.WorldConfig.enemyOverrides"/> applied over the base table
        /// (MV-701) — a world can restat and rename a kind (Scrap Rat is the Rusher wearing World 2's
        /// override) without forking a second archetype or a second <see cref="EnemyKind"/>. Every
        /// stat NOT named in the override stays exactly what <see cref="Of"/> already returns — this
        /// is a PARTIAL archetype, not a replacement one. <paramref name="worldConfig"/> may be null
        /// (falls back to the base table untouched) so a caller with no loaded world yet never has to
        /// null-check first.</summary>
        public static EnemyArchetype For(EnemyKind kind, MaxWorlds.Arena.WorldConfig worldConfig)
        {
            EnemyArchetype baseArchetype = Of(kind);
            MaxWorlds.Arena.WorldEnemyOverride ov = worldConfig?.EnemyOverrideFor(kind);
            return ov == null ? baseArchetype : baseArchetype.WithOverride(ov);
        }

        /// <summary>Applies one <see cref="MaxWorlds.Arena.WorldEnemyOverride"/> over this archetype
        /// (MV-701). A zero/empty field on the override means "not authored" — the same idiom
        /// <see cref="MaxWorlds.Arena.WorldConfig.wallHeight"/> already uses — so a world that only
        /// wants to rename a kind isn't forced to also repeat every one of its base stats.
        /// <see cref="Shape"/>/<see cref="ColliderHeight"/>/<see cref="ColliderRadius"/> are
        /// deliberately never overridable (the ticket's own "do not re-raise": an override cannot
        /// change <see cref="Shape"/>).</summary>
        public EnemyArchetype WithOverride(MaxWorlds.Arena.WorldEnemyOverride ov) => new EnemyArchetype(
            Kind, Shape,
            ov.bodyScale != Vector3.zero ? ov.bodyScale : BodyScale,
            ColliderHeight, ColliderRadius,
            ov.moveSpeed > 0f ? ov.moveSpeed : MoveSpeed,
            ov.maxHealth > 0f ? ov.maxHealth : MaxHealth,
            ov.contactDamage > 0f ? ov.contactDamage : ContactDamage,
            ContactRadius, LungeRange, TelegraphTime, LungeSpeed, LungeTime, RecoverTime, KnockbackDecay,
            StandoffRange, TeleportCooldown, TouchDamage,
            string.IsNullOrEmpty(ov.displayName) ? DisplayName : ov.displayName,
            string.IsNullOrEmpty(ov.skin) ? Skin : ov.skin,
            string.IsNullOrEmpty(ov.colourRole) ? ColourRole : ov.colourRole);
    }

    /// <summary>Which kind the factory emits next (YT-66). Pure, so the mix is testable.</summary>
    public static class EnemyMix
    {
        /// <summary>
        /// Every <paramref name="bruiserEvery"/>-th robot is a bruiser, but not until
        /// <paramref name="firstBruiserAt"/> robots have come out — the opening stays legible
        /// (learn the rusher first), and the bruiser arrives as an escalation rather than as part
        /// of the noise.
        /// </summary>
        public static EnemyKind KindFor(int emitted, int bruiserEvery, int firstBruiserAt)
        {
            if (bruiserEvery <= 0 || emitted < firstBruiserAt) return EnemyKind.Rusher;
            return emitted % bruiserEvery == 0 ? EnemyKind.Bruiser : EnemyKind.Rusher;
        }

        /// <summary>Every kind's "every Nth, not before firstAt" cadence (MV-293) — the same idiom
        /// <see cref="KindFor(int,int,int)"/> already used for the bruiser, so a factory's mix stays a
        /// handful of small integer knobs (the per-area placement <c>MV-284</c> would give this is a
        /// separate ticket; this is what actually drives the live spawner today).</summary>
        public readonly struct MixRates
        {
            public readonly int BruiserEvery, FirstBruiserAt;
            public readonly int GunnerEvery, FirstGunnerAt;
            public readonly int LauncherEvery, FirstLauncherAt;
            public readonly int BlinkerEvery, FirstBlinkerAt;

            public MixRates(int bruiserEvery, int firstBruiserAt,
                int gunnerEvery, int firstGunnerAt,
                int launcherEvery, int firstLauncherAt,
                int blinkerEvery, int firstBlinkerAt)
            {
                BruiserEvery = bruiserEvery; FirstBruiserAt = firstBruiserAt;
                GunnerEvery = gunnerEvery; FirstGunnerAt = firstGunnerAt;
                LauncherEvery = launcherEvery; FirstLauncherAt = firstLauncherAt;
                BlinkerEvery = blinkerEvery; FirstBlinkerAt = firstBlinkerAt;
            }
        }

        /// <summary>Places the three new archetypes (MV-293) alongside the existing bruiser mix, each
        /// checked in a fixed priority order — rarest first — so two cadences landing on the same
        /// emitted count don't silently starve one another (a Blinker slot always wins over a
        /// coincident Bruiser slot, etc.). Falls through to <see cref="KindFor(int,int,int)"/> for the
        /// bruiser/rusher split once none of the new kinds' cadences match.</summary>
        public static EnemyKind KindFor(int emitted, in MixRates rates)
        {
            if (Matches(emitted, rates.BlinkerEvery, rates.FirstBlinkerAt)) return EnemyKind.Blinker;
            if (Matches(emitted, rates.LauncherEvery, rates.FirstLauncherAt)) return EnemyKind.Launcher;
            if (Matches(emitted, rates.GunnerEvery, rates.FirstGunnerAt)) return EnemyKind.Gunner;
            return KindFor(emitted, rates.BruiserEvery, rates.FirstBruiserAt);
        }

        private static bool Matches(int emitted, int every, int firstAt) =>
            every > 0 && emitted >= firstAt && emitted % every == 0;

        /// <summary>
        /// A shed's own area-composition cadence (MV-643): the fixed global cadence above (every Nth
        /// robot is a bruiser/gunner/...) never looked at which area it was standing in, so a shed in
        /// a room authored with, say, 2 Bruisers and 5 Blinkers and NO Rushers still poured Rushers
        /// into it for its first four releases. The designer's authored <see cref="WorldComposition"/>
        /// is law: this only ever hands back a kind that area's own composition contains a non-zero
        /// count of, by deterministic weighted round-robin — each release goes to the allowed kind
        /// with the lowest emitted/authored ratio so far, so over a long run the shed's output
        /// approaches the authored proportions. Ties (including the very first releases, all sitting
        /// at 0/authored) break in <see cref="EnemyKind"/> declaration order.
        ///
        /// An area with nothing authored (no <see cref="WorldComposition"/>, or one with every count
        /// at zero) is a content bug, not a case to invent robots for (MV-643): this hands back nothing
        /// and logs a warning once rather than falling back to the old global cadence, which is exactly
        /// the fallback this ticket exists to remove.
        /// </summary>
        public sealed class AreaCadence
        {
            private static readonly EnemyKind[] AllKinds = (EnemyKind[])Enum.GetValues(typeof(EnemyKind));

            private readonly int[] _authored = new int[AllKinds.Length];
            private readonly int[] _emitted = new int[AllKinds.Length];
            private bool _warnedEmpty;

            public AreaCadence(WorldComposition composition)
            {
                if (composition == null) return;
                _authored[(int)EnemyKind.Rusher] = composition.rusher;
                _authored[(int)EnemyKind.Bruiser] = composition.bruiser;
                _authored[(int)EnemyKind.Heavy] = composition.heavy;
                _authored[(int)EnemyKind.Brute] = composition.brute;
                _authored[(int)EnemyKind.Gunner] = composition.gunner;
                _authored[(int)EnemyKind.Launcher] = composition.launcher;
                _authored[(int)EnemyKind.Blinker] = composition.blinker;
                _authored[(int)EnemyKind.Bolter] = composition.bolter;
                // MV-688: deliberately NOT bound here. A Lurker only ever exists tied to an authored
                // grate (MapValidation enforces the coincidence) — a shed's ambient release cadence must
                // never invent one with no grate under it, so this area's authored lurker count stays
                // out of AreaCadence's ratio math and is only ever drawn by name via garrison placement.
            }

            /// <summary>The next kind this shed should release — the allowed kind with the lowest
            /// emitted/authored ratio, ties broken by declaration order. False (after logging a
            /// one-time warning) if this area authors nothing at all.</summary>
            public bool TryNextKind(out EnemyKind kind)
            {
                int best = FindBestRatioKind();
                if (best < 0)
                {
                    WarnOnceIfEmpty();
                    kind = default;
                    return false;
                }

                kind = (EnemyKind)best;
                _emitted[best]++;
                return true;
            }

            /// <summary>The toughest kind this area authors, by base <see cref="EnemyArchetype.MaxHealth"/>
            /// (MV-643) — what a death-throes surge's "elite" forces instead of an unconditional
            /// Bruiser. Ties broken by declaration order, same as <see cref="TryNextKind"/>. False if
            /// this area authors nothing at all.</summary>
            public bool TryToughestAuthoredKind(out EnemyKind kind)
            {
                int best = -1;
                float bestHealth = -1f;
                for (int i = 0; i < _authored.Length; i++)
                {
                    if (_authored[i] <= 0) continue;
                    float health = EnemyArchetype.Of((EnemyKind)i).MaxHealth;
                    if (health > bestHealth) { bestHealth = health; best = i; }
                }

                if (best < 0) { WarnOnceIfEmpty(); kind = default; return false; }
                kind = (EnemyKind)best;
                return true;
            }

            private int FindBestRatioKind()
            {
                int best = -1;
                float bestRatio = float.MaxValue;
                for (int i = 0; i < _authored.Length; i++)
                {
                    if (_authored[i] <= 0) continue;
                    float ratio = (float)_emitted[i] / _authored[i];
                    if (ratio < bestRatio) { bestRatio = ratio; best = i; }
                }
                return best;
            }

            private void WarnOnceIfEmpty()
            {
                if (_warnedEmpty) return;
                _warnedEmpty = true;
                Debug.LogWarning("EnemyMix.AreaCadence: shed's area authors an empty composition — " +
                                  "emitting nothing instead of inventing an unauthored kind (MV-643).");
            }
        }
    }
}
