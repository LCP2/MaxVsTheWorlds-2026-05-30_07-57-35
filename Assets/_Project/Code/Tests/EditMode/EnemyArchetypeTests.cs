using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The two enemy types have to actually CONTRAST (YT-66) — a second robot that plays
    /// like the first adds nothing. These pin the contrast so a later tuning pass can't quietly
    /// collapse the bruiser back into a rusher.</summary>
    public sealed class EnemyArchetypeTests
    {
        private static readonly EnemyArchetype Rusher = EnemyArchetype.Rusher;
        private static readonly EnemyArchetype Bruiser = EnemyArchetype.Bruiser;

        [Test]
        public void Bruiser_IsSlowerAndTougherThanTheRusher()
        {
            Assert.Less(Bruiser.MoveSpeed, Rusher.MoveSpeed * 0.75f, "the bruiser must read as SLOW");
            Assert.Greater(Bruiser.MaxHealth, Rusher.MaxHealth * 3f, "the bruiser must soak fire");
            Assert.Greater(Bruiser.ContactDamage, Rusher.ContactDamage * 2f, "…and hit hard for it");
        }

        [Test]
        public void Bruiser_CanNeverCatchMax_SoItIsAlwaysKiteable()
        {
            const float maxSpeed = 6f; // PlayerController.moveSpeed
            Assert.Less(Bruiser.MoveSpeed, maxSpeed,
                "if it can outrun Max it stops being a positioning problem and becomes an unfair one");
        }

        [Test]
        public void Bruiser_TelegraphsLongEnoughThatItsBigHitIsFair()
        {
            // It hits for 28 of Max's 100 HP. That's only fair if you can see it coming and move.
            Assert.Greater(Bruiser.TelegraphTime, Rusher.TelegraphTime * 1.5f);
            Assert.GreaterOrEqual(Bruiser.TelegraphTime, 0.9f, "not enough time to read and dodge");
        }

        [Test]
        public void Bruiser_HasALongRecovery_SoThereIsAWindowToPunish()
        {
            Assert.Greater(Bruiser.RecoverTime, Rusher.RecoverTime,
                "a tanky enemy with no punish window is just a chore");
        }

        [Test]
        public void Bruiser_ShrugsOffTheKnockbackThatScattersRushers()
        {
            Assert.Greater(Bruiser.KnockbackDecay, Rusher.KnockbackDecay,
                "the spray shove must not solve the bruiser the way it solves the swarm");
        }

        [Test]
        public void TheTwoAreTellableApartAtAGlance()
        {
            // Pillar 4: you must read which is which from the fixed ~72° camera, so the silhouettes
            // differ in SHAPE and in SIZE, not just in stats.
            Assert.AreNotEqual(Rusher.Shape, Bruiser.Shape);
            Assert.Greater(Bruiser.BodyScale.x, Rusher.BodyScale.x * 1.5f, "not visibly bigger");
            Assert.Greater(Bruiser.ColliderRadius, Rusher.ColliderRadius * 1.5f);
        }

        [Test]
        public void EveryArchetype_StandsOnTheGround()
        {
            foreach (var a in new[] { Rusher, Bruiser })
            {
                Assert.AreEqual(a.ColliderHeight * 0.5f, a.SpawnHeight, 1e-4,
                    $"{a.Kind} would spawn buried or floating");
                Assert.Greater(a.ColliderHeight, 0f);
                Assert.Greater(a.ColliderRadius, 0f);
            }
        }

        [Test]
        public void ColliderIsNeverShorterThanItIsWide()
        {
            // Unity silently clamps a CharacterController's height up to 2*radius. If an archetype
            // relies on being squatter than that, the collider it gets is NOT the one it asked for.
            foreach (var a in new[] { Rusher, Bruiser })
                Assert.GreaterOrEqual(a.ColliderHeight, a.ColliderRadius * 2f - 1e-4f,
                    $"{a.Kind}'s collider would be silently clamped taller than authored");
        }

        [Test]
        public void Of_ReturnsTheMatchingArchetype()
        {
            Assert.AreEqual(EnemyKind.Bruiser, EnemyArchetype.Of(EnemyKind.Bruiser).Kind);
            Assert.AreEqual(EnemyKind.Rusher, EnemyArchetype.Of(EnemyKind.Rusher).Kind);
        }

        // --- The mix ---------------------------------------------------------------------------

        [Test]
        public void TheOpeningIsAllRushers_SoTheFightTeachesItselfInOrder()
        {
            for (int i = 0; i < 3; i++)
                Assert.AreEqual(EnemyKind.Rusher, EnemyMix.KindFor(i, 4, 3),
                    "the bruiser should arrive as an escalation, not in the first breath");
        }

        [Test]
        public void BruisersArePunctuation_NotTheNorm()
        {
            int bruisers = 0;
            const int n = 40;
            for (int i = 0; i < n; i++)
                if (EnemyMix.KindFor(i, 4, 3) == EnemyKind.Bruiser) bruisers++;

            Assert.Greater(bruisers, 0, "no bruisers ever appear");
            Assert.Less(bruisers, n / 2, "bruisers must not become the swarm");
        }

        [Test]
        public void BruiserEveryZero_DisablesThemRatherThanDividingByZero()
        {
            for (int i = 0; i < 10; i++)
                Assert.AreEqual(EnemyKind.Rusher, EnemyMix.KindFor(i, 0, 0));
        }
    }
}
