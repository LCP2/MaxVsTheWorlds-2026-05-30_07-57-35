using UnityEngine;

namespace MaxWorlds.Enemies
{
    public enum EnemyKind { Rusher, Bruiser }

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

        /// <summary>The original robot (YT-36/YT-63): a small capsule at 60% of Max's speed. Fast
        /// enough to pressure, slow enough to kite. Dies quickly; hurts a little. Deliberately
        /// SMALLER than Max — he's the hero, and a swarm of knee-high machines reads as a swarm
        /// (YT-74).
        ///
        /// Was 4.2 (70% of Max) until YT-80, and that last 10% was the difference between kiting and
        /// merely delaying: at a 1.8 m/s deficit a rusher took ~7 s to cross a 12 m arena's worth of
        /// gap, so backing off never actually bought space, it just moved the fight. At 3.6 the gap
        /// opens at 2.4 m/s and retreating is a real option — but standing still still ends with the
        /// swarm on top of you, because they never stop closing.</summary>
        public static EnemyArchetype Rusher => new EnemyArchetype(
            EnemyKind.Rusher, EnemyShape.Capsule, new Vector3(0.8f, 0.7f, 0.8f),
            colliderHeight: 1.4f, colliderRadius: 0.4f,
            moveSpeed: 3.6f, maxHealth: 24f,
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
            moveSpeed: 1.8f, maxHealth: 100f,
            contactDamage: 28f, contactRadius: 1.4f,
            lungeRange: 2.6f, telegraphTime: 1.0f,
            lungeSpeed: 9f, lungeTime: 0.35f, recoverTime: 1.4f,
            knockbackDecay: 70f);

        public static EnemyArchetype Of(EnemyKind kind) =>
            kind == EnemyKind.Bruiser ? Bruiser : Rusher;
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
