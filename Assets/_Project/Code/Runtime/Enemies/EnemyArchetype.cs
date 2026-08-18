using UnityEngine;

namespace MaxWorlds.Enemies
{
    // Appended, not inserted (same rule as RobotEnemy.State) — Gunner/Bomber/Blinker are new
    // archetype ROWS, not a renumbering of the existing tiers.
    public enum EnemyKind { Rusher, Bruiser, Heavy, Brute, Gunner, Bomber, Blinker }

    public enum EnemyShape { Capsule, Box }

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

        /// <summary>How close a RANGED kind (<see cref="EnemyKind.Gunner"/>/<see cref="EnemyKind.Bomber"/>)
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

        public EnemyArchetype(EnemyKind kind, EnemyShape shape, Vector3 bodyScale,
            float colliderHeight, float colliderRadius, float moveSpeed, float maxHealth,
            float contactDamage, float contactRadius, float lungeRange, float telegraphTime,
            float lungeSpeed, float lungeTime, float recoverTime, float knockbackDecay,
            float standoffRange = 0f, float teleportCooldown = 0f, float touchDamage = 0f)
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
            moveSpeed: 2.04f,   // MV-315: baked from tuning panel (was MV-289's 2.71)
            // MV-289: 36 -> 32. MV-315 also re-baked the live health multiplier to 1.26x (was
            // 1.42x), landing ~40 effective HP at run start.
            maxHealth: 32f,
            contactDamage: 12f, contactRadius: 1.0f,
            lungeRange: 2.2f, telegraphTime: 0.55f,
            lungeSpeed: 11f, lungeTime: 0.22f, recoverTime: 0.7f,
            knockbackDecay: 28f);

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
            moveSpeed: 1.02f, maxHealth: 150f,   // YT-194: 100 -> 150, the same 1.5x as the rusher
            contactDamage: 28f, contactRadius: 1.4f,
            lungeRange: 2.6f, telegraphTime: 1.0f,
            lungeSpeed: 9f, lungeTime: 0.35f, recoverTime: 1.4f,
            knockbackDecay: 70f,
            touchDamage: 10f);  // MV-428: see the fix comment for the crowd-DPS arithmetic

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
            moveSpeed: 0.85f, maxHealth: 260f,   // ~1.73x the bruiser's 150
            contactDamage: 32f, contactRadius: 1.5f,
            lungeRange: 2.6f, telegraphTime: 1.05f,
            lungeSpeed: 8.5f, lungeTime: 0.35f, recoverTime: 1.5f,
            knockbackDecay: 95f,
            touchDamage: 12f);  // MV-428: no lunge — see Bruiser's doc comment

        /// <summary>The second later-area tier (Area 8 on, spec §2 table) — the top of the
        /// composition ladder, introduced alongside <see cref="Heavy"/> rather than replacing it (the
        /// spec table has both present from Area 8). Same "minimal distinct behaviour" idiom as
        /// <see cref="Heavy"/>, sized apart from it the same way the bruiser sizes apart from the
        /// rusher.</summary>
        public static EnemyArchetype Brute => new EnemyArchetype(
            EnemyKind.Brute, EnemyShape.Box, new Vector3(1.25f, 1.5f, 1.25f),
            colliderHeight: 1.9f, colliderRadius: 0.6f,
            moveSpeed: 0.75f, maxHealth: 420f,   // ~2.8x the bruiser's 150, well past the heavy's 260
            contactDamage: 38f, contactRadius: 1.6f,
            lungeRange: 2.6f, telegraphTime: 1.15f,
            lungeSpeed: 7.5f, lungeTime: 0.35f, recoverTime: 1.6f,
            knockbackDecay: 120f,
            touchDamage: 14f);  // MV-428: no lunge — see Bruiser's doc comment

        /// <summary>
        /// Ranged laser (MV-293), displayed to the player as "LASER" (MV-404: display-only rename,
        /// this <c>EnemyKind.Gunner</c> identifier is unchanged). Keeps its distance in the 4.5–9 m
        /// band — inside that it backs off rather than closing, so the answer to a Gunner is never
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
        /// EnemyArchetypeTests.GunnerAndBomber_AreNoTougherThanARusher_SoClosingTheGapIsAlwaysThePunish.
        /// </summary>
        public static EnemyArchetype Gunner => new EnemyArchetype(
            EnemyKind.Gunner, EnemyShape.Capsule, new Vector3(0.8f, 0.7f, 0.8f),
            colliderHeight: 1.4f, colliderRadius: 0.4f,
            moveSpeed: 2.2f, maxHealth: 39f,   // MV-404: 26 -> 39, ~50% harder to kill per Lee's ask
            contactDamage: 18f,   // DPS while the beam holds
            contactRadius: 0.6f,  // beam half-width — wider than Max's 0.5 m radius, still side-steppable
            lungeRange: 9f,       // max fire range
            telegraphTime: 0.5f,  // aim wind-up — the dodge window
            lungeSpeed: 0f,       // it never moves during the shot
            lungeTime: 1.1f,      // beam duration
            recoverTime: 1.3f,
            knockbackDecay: 28f,
            standoffRange: 4.5f);

        /// <summary>
        /// Lobs a slow homing missile (MV-293) rather than closing — pure area denial, forcing the
        /// player to keep moving instead of camping a good spot. Keeps a wider distance band than the
        /// Gunner (5–10 m) and its wind-up is heavier, since the payoff (a splash, not a beam-thin
        /// laser) is bigger. <see cref="ContactDamage"/>/<see cref="ContactRadius"/> here are the
        /// missile's explosion damage and splash radius; <see cref="LungeSpeed"/> is the missile's
        /// flight speed — deliberately slow (<see cref="HomingMissile"/>'s own turn rate is gentle
        /// too), so a player who's watching can outwalk or juke it rather than eat a guaranteed hit.
        /// </summary>
        public static EnemyArchetype Bomber => new EnemyArchetype(
            EnemyKind.Bomber, EnemyShape.Capsule, new Vector3(0.85f, 0.75f, 0.85f),
            colliderHeight: 1.45f, colliderRadius: 0.42f,
            // MV-293: kept BELOW the rusher's 32 HP on purpose — every new small-tier kind stays a
            // one-rusher-shot kill (EnemyMixPlayTests.ABruiserIsTougherThanARusher_InTheActualGame
            // pins that only the Bruiser survives a full-health rusher's-worth of damage). A Bomber's
            // threat is its range, not its ability to soak fire once you close the gap on it.
            // MV-325: speed must invert with power too — a Bomber has less HP than the rusher, so it
            // has to be at least as quick, not slower (was 1.8, below the rusher's 2.04).
            moveSpeed: 2.1f, maxHealth: 30f,
            contactDamage: 22f,   // splash damage
            contactRadius: 2.0f,  // splash radius
            lungeRange: 10f,      // max fire range
            telegraphTime: 0.7f,  // lob wind-up — heavier tell than the Gunner's aim
            lungeSpeed: 4.5f,     // missile flight speed — slow and dodgeable
            lungeTime: 0.3f,      // release beat before it recovers
            recoverTime: 2.2f,    // area-denial cadence, not rapid fire
            knockbackDecay: 28f,
            standoffRange: 5f);

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
            // speed ("Otherwise a rusher"), so it sits with the Bomber just above the rusher.
            moveSpeed: 2.1f, maxHealth: 30f,
            contactDamage: 14f, contactRadius: 1.0f,
            lungeRange: 2.2f, telegraphTime: 0.5f,
            lungeSpeed: 11f, lungeTime: 0.22f, recoverTime: 0.7f,
            knockbackDecay: 28f,
            teleportCooldown: 4.5f);

        public static EnemyArchetype Of(EnemyKind kind) => kind switch
        {
            EnemyKind.Bruiser => Bruiser,
            EnemyKind.Heavy => Heavy,
            EnemyKind.Brute => Brute,
            EnemyKind.Gunner => Gunner,
            EnemyKind.Bomber => Bomber,
            EnemyKind.Blinker => Blinker,
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
            StandoffRange, TeleportCooldown, TouchDamage * multiplier);

        /// <summary>The same archetype with only its HEALTH scaled (YT-194's "Robot health" slider) —
        /// contact damage, speed, silhouette and timing are all untouched. Kept separate from
        /// <see cref="Toughened"/>, which scales health AND damage together for the Invasion Level:
        /// this is the player-dialled baseline that escalation still layers its own toughening on
        /// top of, so the two knobs compose rather than fight.</summary>
        public EnemyArchetype WithHealthMultiplier(float multiplier) => new EnemyArchetype(
            Kind, Shape, BodyScale, ColliderHeight, ColliderRadius,
            MoveSpeed, MaxHealth * multiplier, ContactDamage, ContactRadius,
            LungeRange, TelegraphTime, LungeSpeed, LungeTime, RecoverTime, KnockbackDecay,
            StandoffRange, TeleportCooldown, TouchDamage);
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
            public readonly int BomberEvery, FirstBomberAt;
            public readonly int BlinkerEvery, FirstBlinkerAt;

            public MixRates(int bruiserEvery, int firstBruiserAt,
                int gunnerEvery, int firstGunnerAt,
                int bomberEvery, int firstBomberAt,
                int blinkerEvery, int firstBlinkerAt)
            {
                BruiserEvery = bruiserEvery; FirstBruiserAt = firstBruiserAt;
                GunnerEvery = gunnerEvery; FirstGunnerAt = firstGunnerAt;
                BomberEvery = bomberEvery; FirstBomberAt = firstBomberAt;
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
            if (Matches(emitted, rates.BomberEvery, rates.FirstBomberAt)) return EnemyKind.Bomber;
            if (Matches(emitted, rates.GunnerEvery, rates.FirstGunnerAt)) return EnemyKind.Gunner;
            return KindFor(emitted, rates.BruiserEvery, rates.FirstBruiserAt);
        }

        private static bool Matches(int emitted, int every, int firstAt) =>
            every > 0 && emitted >= firstAt && emitted % every == 0;
    }
}
