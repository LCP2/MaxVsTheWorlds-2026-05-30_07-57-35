using UnityEngine;

namespace MaxWorlds.Enemies
{
    public enum EnemyKind { Rusher, Bruiser, Heavy, Brute }

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

        public EnemyArchetype(EnemyKind kind, EnemyShape shape, Vector3 bodyScale,
            float colliderHeight, float colliderRadius, float moveSpeed, float maxHealth,
            float contactDamage, float contactRadius, float lungeRange, float telegraphTime,
            float lungeSpeed, float lungeTime, float recoverTime, float knockbackDecay)
        {
            Kind = kind; Shape = shape; BodyScale = bodyScale;
            ColliderHeight = colliderHeight; ColliderRadius = colliderRadius;
            MoveSpeed = moveSpeed; MaxHealth = maxHealth;
            ContactDamage = contactDamage; ContactRadius = contactRadius;
            LungeRange = lungeRange; TelegraphTime = telegraphTime;
            LungeSpeed = lungeSpeed; LungeTime = lungeTime; RecoverTime = recoverTime;
            KnockbackDecay = knockbackDecay;
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
        /// MV-289 retunes speed to ~90% of Max's 3.01 (2.71, was YT-169's 1.85/~60%): 0.6's narrow/
        /// short weapon plus this same slow rusher made Area 1 read as a shooting gallery rather than
        /// a threat, and the retreat-gap math never made the rusher SCARY, just slow — the survivable-
        /// but-not-trivial band this ticket targets needs the rusher to actually press, with Max's
        /// widened HP pool (100, MV-289) and slow out-of-combat regen carrying the survivability
        /// instead of a wide kiting gap. Retreating still opens ground (3.01-2.71 = 0.3 m/s), just not
        /// much — standing still still ends with the swarm on top of you either way.</summary>
        public static EnemyArchetype Rusher => new EnemyArchetype(
            EnemyKind.Rusher, EnemyShape.Capsule, new Vector3(0.8f, 0.7f, 0.8f),
            colliderHeight: 1.4f, colliderRadius: 0.4f,
            moveSpeed: 2.71f,   // MV-289: ~90% of Max's 3.01 (was YT-169's 1.85/~60%)
            // MV-289: 36 -> 32 (with the live 1.42x health multiplier this lands ~45 effective HP at
            // run start, a ~1.1s TTK against the base spray's unchanged 40 DPS — AC1's target band).
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
        /// </summary>
        public static EnemyArchetype Bruiser => new EnemyArchetype(
            EnemyKind.Bruiser, EnemyShape.Box, new Vector3(1.15f, 1.15f, 1.15f),
            colliderHeight: 1.15f, colliderRadius: 0.55f,
            // Half the rusher's speed, preserved (YT-66's "fridge on legs"): the bruiser scales with
            // whatever the rusher/panel default is to stay the slow tank (was 0.925 = half of YT-169's
            // 1.85, now 1.355 = half of MV-289's 2.71). Flag: if Lee wants ALL robots flat at the
            // rusher's speed, this is the one line to change.
            moveSpeed: 1.355f, maxHealth: 150f,   // YT-194: 100 -> 150, the same 1.5x as the rusher
            contactDamage: 28f, contactRadius: 1.4f,
            lungeRange: 2.6f, telegraphTime: 1.0f,
            lungeSpeed: 9f, lungeTime: 0.35f, recoverTime: 1.4f,
            knockbackDecay: 70f);

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
            knockbackDecay: 95f);

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
            knockbackDecay: 120f);

        public static EnemyArchetype Of(EnemyKind kind) => kind switch
        {
            EnemyKind.Bruiser => Bruiser,
            EnemyKind.Heavy => Heavy,
            EnemyKind.Brute => Brute,
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
            LungeRange, TelegraphTime, LungeSpeed, LungeTime, RecoverTime, KnockbackDecay);

        /// <summary>The same archetype with only its HEALTH scaled (YT-194's "Robot health" slider) —
        /// contact damage, speed, silhouette and timing are all untouched. Kept separate from
        /// <see cref="Toughened"/>, which scales health AND damage together for the Invasion Level:
        /// this is the player-dialled baseline that escalation still layers its own toughening on
        /// top of, so the two knobs compose rather than fight.</summary>
        public EnemyArchetype WithHealthMultiplier(float multiplier) => new EnemyArchetype(
            Kind, Shape, BodyScale, ColliderHeight, ColliderRadius,
            MoveSpeed, MaxHealth * multiplier, ContactDamage, ContactRadius,
            LungeRange, TelegraphTime, LungeSpeed, LungeTime, RecoverTime, KnockbackDecay);
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
    }
}
