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
            Assert.Less(Bruiser.MoveSpeed, MaxSpeed,
                "if it can outrun Max it stops being a positioning problem and becomes an unfair one");
        }

        // --- Kiteability (YT-80) --------------------------------------------------------------

        /// <summary>PlayerController.moveSpeed.</summary>
        private const float MaxSpeed = 6f;

        [Test]
        public void MovingAwayFromTheSwarmActuallyOpensAGap()
        {
            // Not just "slower than Max" — slower by enough that retreating BUYS something. The
            // rusher used to run at 4.2 against Max's 6, and a 1.8 m/s edge means crossing the arena
            // to shake one off; it read as being chased rather than as out-manoeuvring anything.
            // Every robot must now cede at least a third of Max's speed.
            foreach (var a in new[] { Rusher, Bruiser })
                Assert.LessOrEqual(a.MoveSpeed, MaxSpeed * 0.65f,
                    $"the {a.Kind} is too fast to out-position — kiting it isn't a real option");
        }

        [Test]
        public void ButTheyStillCloseOnAMaxWhoStandsStill()
        {
            // The other edge. Shaving speed must not turn the swarm into scenery: a robot that can't
            // reach a stationary player is no longer a threat, and there's nothing to dodge.
            foreach (var a in new[] { Rusher, Bruiser })
                Assert.Greater(a.MoveSpeed, 0f, $"the {a.Kind} would never reach Max at all");

            // And the rusher specifically still has to feel like a rusher — quick enough to pressure
            // you into moving, not a second bruiser.
            Assert.GreaterOrEqual(Rusher.MoveSpeed, MaxSpeed * 0.5f,
                "the rusher has stopped rushing");
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
            Assert.Greater(Bruiser.BodyScale.x, Rusher.BodyScale.x * 1.25f, "not visibly bigger");
            Assert.Greater(Bruiser.ColliderRadius, Rusher.ColliderRadius * 1.2f);
        }

        // --- Size relative to Max (YT-74) -------------------------------------------------------

        [Test]
        public void NothingInTheSwarmOutSizesMax()
        {
            // A crowd of things bigger than the player stops reading as a swarm and starts reading
            // as a moving wall. This is the regression that made the game unplayable.
            foreach (var a in new[] { Rusher, Bruiser })
            {
                Assert.LessOrEqual(a.ColliderRadius, EnemyArchetype.PlayerRadius * 1.2f,
                    $"the {a.Kind} is wider than Max");
                Assert.LessOrEqual(a.ColliderHeight, EnemyArchetype.PlayerHeight,
                    $"the {a.Kind} is taller than Max");
                Assert.LessOrEqual(Mathf.Max(a.BodyScale.x, a.BodyScale.z), 1.25f,
                    $"the {a.Kind}'s body is oversized");
            }
        }

        [Test]
        public void TheRusherIsNoticeablySmallerThanMax()
        {
            // He's the hero. A swarm of knee-high machines reads as a swarm.
            Assert.Less(Rusher.ColliderRadius, EnemyArchetype.PlayerRadius);
            Assert.Less(Rusher.ColliderHeight, EnemyArchetype.PlayerHeight * 0.8f);
        }

        [Test]
        public void TheBruisersThreatIsItsHealth_NotItsFootprint()
        {
            // It's allowed to be chunkier than a rusher, but its danger has to come from soaking
            // fire and hitting hard — not from being big enough to block a doorway.
            Assert.Greater(Bruiser.MaxHealth, Rusher.MaxHealth * 3f);
            Assert.LessOrEqual(Bruiser.ColliderRadius, EnemyArchetype.PlayerRadius * 1.2f);
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
