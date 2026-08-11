using System.Linq;
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
        private static readonly EnemyArchetype Heavy = EnemyArchetype.Heavy;
        private static readonly EnemyArchetype Brute = EnemyArchetype.Brute;
        private static readonly EnemyArchetype Gunner = EnemyArchetype.Gunner;
        private static readonly EnemyArchetype Bomber = EnemyArchetype.Bomber;
        private static readonly EnemyArchetype Blinker = EnemyArchetype.Blinker;
        private static readonly EnemyArchetype[] AllArchetypes =
            { Rusher, Bruiser, Heavy, Brute, Gunner, Bomber, Blinker };

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

        /// <summary>PlayerController.moveSpeed (YT-106: Lee's on-device number, was 6).</summary>
        private const float MaxSpeed = 3.01f;

        [Test]
        public void MovingAwayFromTheSwarmActuallyOpensAGap()
        {
            // Not just "slower than Max" — slower BY SOME AMOUNT, so retreating still buys something.
            // MV-289 deliberately narrows this gap (YT-169's ~60% -> ~90%, 2.71 against Max's 3.01):
            // the Area-1 survivable band now leans on Max's widened HP pool and slow regen, not on a
            // wide kiting gap, to keep the fight from reading as trivial. It is still kiteable — Max
            // is strictly faster — which is the invariant this pins.
            foreach (var a in AllArchetypes)
                Assert.Less(a.MoveSpeed, MaxSpeed,
                    $"the {a.Kind} can outrun Max — kiting stops being possible at all");
            Assert.LessOrEqual(Rusher.MoveSpeed, MaxSpeed * 0.95f,
                "the rusher has crept too close to Max's speed to out-position with any comfort");
        }

        [Test]
        public void ButTheyStillCloseOnAMaxWhoStandsStill()
        {
            // The other edge. Shaving speed must not turn the swarm into scenery: a robot that can't
            // reach a stationary player is no longer a threat, and there's nothing to dodge.
            foreach (var a in AllArchetypes)
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
            foreach (var a in AllArchetypes)
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
            foreach (var a in AllArchetypes)
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
            foreach (var a in AllArchetypes)
                Assert.GreaterOrEqual(a.ColliderHeight, a.ColliderRadius * 2f - 1e-4f,
                    $"{a.Kind}'s collider would be silently clamped taller than authored");
        }

        [Test]
        public void Of_ReturnsTheMatchingArchetype()
        {
            Assert.AreEqual(EnemyKind.Bruiser, EnemyArchetype.Of(EnemyKind.Bruiser).Kind);
            Assert.AreEqual(EnemyKind.Rusher, EnemyArchetype.Of(EnemyKind.Rusher).Kind);
            Assert.AreEqual(EnemyKind.Heavy, EnemyArchetype.Of(EnemyKind.Heavy).Kind);
            Assert.AreEqual(EnemyKind.Brute, EnemyArchetype.Of(EnemyKind.Brute).Kind);
            Assert.AreEqual(EnemyKind.Gunner, EnemyArchetype.Of(EnemyKind.Gunner).Kind);
            Assert.AreEqual(EnemyKind.Bomber, EnemyArchetype.Of(EnemyKind.Bomber).Kind);
            Assert.AreEqual(EnemyKind.Blinker, EnemyArchetype.Of(EnemyKind.Blinker).Kind);
        }

        // --- Archetypes (MV-293): Gunner (ranged laser) / Bomber (homing missile) / Blinker (teleport) ---

        [Test]
        public void RangedKinds_KeepTheirDistance_MeleeKindsDoNot()
        {
            Assert.Greater(Gunner.StandoffRange, 0f, "a Gunner that never backs off is just a rusher");
            Assert.Greater(Bomber.StandoffRange, 0f, "a Bomber that never backs off is just a rusher");
            Assert.LessOrEqual(Blinker.StandoffRange, 0f, "the Blinker closes to melee, it doesn't kite");

            foreach (var a in new[] { Rusher, Bruiser, Heavy, Brute })
                Assert.LessOrEqual(a.StandoffRange, 0f, $"{a.Kind} is melee — it must not hold range");
        }

        [Test]
        public void OnlyTheBlinker_Teleports()
        {
            Assert.Greater(Blinker.TeleportCooldown, 0f);
            foreach (var a in new[] { Rusher, Bruiser, Heavy, Brute, Gunner, Bomber })
                Assert.LessOrEqual(a.TeleportCooldown, 0f, $"{a.Kind} must not blink — only the Blinker does");
        }

        [Test]
        public void RangedKinds_CanFireBeyondTheirStandoffRange()
        {
            // Otherwise the retreat-then-never-fire band would swallow itself: withholding the shot
            // (RobotEnemy.TickChase's holdsFire check) only makes sense if there is a real gap between
            // "too close to fire" and "too far to fire" for it to retreat INTO.
            Assert.Greater(Gunner.LungeRange, Gunner.StandoffRange, "no room to actually kite in");
            Assert.Greater(Bomber.LungeRange, Bomber.StandoffRange, "no room to actually kite in");
        }

        [Test]
        public void GunnerAndBomber_AreNoTougherThanARusher_SoClosingTheGapIsAlwaysThePunish()
        {
            // EnemyMixPlayTests.ABruiserIsTougherThanARusher_InTheActualGame pins that only the
            // Bruiser survives a full-health rusher's-worth of damage — every small-tier kind,
            // including the two new ranged ones, must stay a one-shot-with-a-rusher's-DPS kill so
            // that closing the distance on them is always a real answer, not a losing trade.
            Assert.LessOrEqual(Gunner.MaxHealth, Rusher.MaxHealth);
            Assert.LessOrEqual(Bomber.MaxHealth, Rusher.MaxHealth);
            Assert.LessOrEqual(Blinker.MaxHealth, Rusher.MaxHealth);
        }

        [Test]
        public void GunnerAndBomber_ReadDifferently_EvenSharingTheRushersSilhouetteFamily()
        {
            // Pillar 4 usually means silhouette; here the behaviour DATA has to diverge instead, since
            // both still share the rusher's small capsule Shape/BodyScale — that field only sizes the
            // COLLIDER and spawn height (MV-293's AC was about distinct behaviour, not new geometry).
            // The Gunner's actual on-screen model diverged from the rusher's own later (MV-312,
            // RobotRig.BuildGunner) without touching this archetype data at all — the visual silhouette
            // is authored independently of BodyScale, so that fix belongs entirely in RobotRig, not here.
            Assert.Greater(Bomber.ContactRadius, Gunner.ContactRadius,
                "the Bomber's splash must read as an AREA, wider than the Gunner's beam");
            Assert.Greater(Bomber.TelegraphTime, Gunner.TelegraphTime,
                "a lobbed missile should telegraph heavier than a snap-aimed beam");
        }

        // --- Heavy & Brute (v0.5 recut spec §2-3, MV-224) ---------------------------------------

        [Test]
        public void HeavyAndBrute_HaveHigherHealthThanTheBruiser()
        {
            // The ticket's own AC: "higher HP than the current large robot".
            Assert.Greater(Heavy.MaxHealth, Bruiser.MaxHealth);
            Assert.Greater(Brute.MaxHealth, Bruiser.MaxHealth);
        }

        [Test]
        public void BruteOutlaststHeavy_SoTheLadderKeepsEscalating()
        {
            // The spec table introduces heavy at Area 5 and brute at Area 8 — brute has to be a real
            // step up, not a reskin, or Area 8+ would not read as tougher than Area 5-7.
            Assert.Greater(Brute.MaxHealth, Heavy.MaxHealth);
        }

        [Test]
        public void HeavyAndBrute_AreLargerSilhouettesThanTheBruiser()
        {
            // Pillar 4: the three large tiers must still tell apart at a glance.
            Assert.Greater(Heavy.ColliderHeight, Bruiser.ColliderHeight);
            Assert.Greater(Brute.ColliderHeight, Heavy.ColliderHeight);
        }

        [Test]
        public void IsLarge_TreatsBruiserHeavyAndBruteAsLarge_OnlyRusherAsSmall()
        {
            // v0.5 recut spec §5: "for economy purposes they count as 'large'".
            Assert.IsFalse(EnemyArchetype.IsLarge(EnemyKind.Rusher));
            Assert.IsTrue(EnemyArchetype.IsLarge(EnemyKind.Bruiser));
            Assert.IsTrue(EnemyArchetype.IsLarge(EnemyKind.Heavy));
            Assert.IsTrue(EnemyArchetype.IsLarge(EnemyKind.Brute));
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

        // --- MixRates (MV-293): placing the three new archetypes alongside the bruiser ------------

        [Test]
        public void MixRates_AllZero_IsJustTheRusherBruiserSplit()
        {
            var rates = new EnemyMix.MixRates(4, 3, 0, 0, 0, 0, 0, 0);
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(EnemyMix.KindFor(i, 4, 3), EnemyMix.KindFor(i, rates),
                    $"emitted={i}: a zeroed-out new-kind cadence must not change the legacy mix");
        }

        [Test]
        public void MixRates_EachNewKindLandsOnItsOwnCadence()
        {
            var rates = new EnemyMix.MixRates(
                bruiserEvery: 100, firstBruiserAt: 100,     // effectively off, out of the way
                gunnerEvery: 6, firstGunnerAt: 5,
                bomberEvery: 8, firstBomberAt: 7,
                blinkerEvery: 10, firstBlinkerAt: 9);

            Assert.AreEqual(EnemyKind.Gunner, EnemyMix.KindFor(6, rates));
            Assert.AreEqual(EnemyKind.Bomber, EnemyMix.KindFor(8, rates));
            Assert.AreEqual(EnemyKind.Blinker, EnemyMix.KindFor(10, rates));
            Assert.AreEqual(EnemyKind.Rusher, EnemyMix.KindFor(1, rates), "off-cadence emits stay rusher");
        }

        [Test]
        public void MixRates_RarestKindWinsACoincidentSlot()
        {
            // 40 is divisible by both 8 (the bomber cadence) and 10 (the blinker cadence) — a genuine
            // collision between two of the new kinds' slots.
            var rates = new EnemyMix.MixRates(
                bruiserEvery: 4, firstBruiserAt: 3,
                gunnerEvery: 6, firstGunnerAt: 5,
                bomberEvery: 8, firstBomberAt: 7,
                blinkerEvery: 10, firstBlinkerAt: 9);

            Assert.AreEqual(EnemyKind.Blinker, EnemyMix.KindFor(40, rates),
                "40 is a Bomber AND a Blinker slot — the rarer kind (Blinker) must win");
        }

        [Test]
        public void IsLarge_TreatsTheThreeNewKindsAsLarge_TooLikeBruiserHeavyAndBrute()
        {
            Assert.IsTrue(EnemyArchetype.IsLarge(EnemyKind.Gunner));
            Assert.IsTrue(EnemyArchetype.IsLarge(EnemyKind.Bomber));
            Assert.IsTrue(EnemyArchetype.IsLarge(EnemyKind.Blinker));
        }

        // --- MV-325: move speed must invert with power/tier -------------------------------------

        [Test]
        public void WeakerArchetypesAreNeverSlowerThanToughterOnes()
        {
            // Sorted weakest (lowest HP) to strongest (highest HP), speed must never increase —
            // the weakest archetype overall is the fastest mover, the strongest is the slowest.
            var byHealth = AllArchetypes.OrderBy(a => a.MaxHealth).ToArray();
            for (int i = 1; i < byHealth.Length; i++)
                Assert.LessOrEqual(byHealth[i].MoveSpeed, byHealth[i - 1].MoveSpeed + 1e-4f,
                    $"{byHealth[i].Kind} (HP {byHealth[i].MaxHealth}) is tougher than " +
                    $"{byHealth[i - 1].Kind} (HP {byHealth[i - 1].MaxHealth}) but moves faster");
        }

        [Test]
        public void TheWeakestArchetypeOverall_IsTheFastestMover()
        {
            var weakest = AllArchetypes.OrderBy(a => a.MaxHealth).First();
            var fastest = AllArchetypes.OrderByDescending(a => a.MoveSpeed).First();
            Assert.AreEqual(weakest.Kind, fastest.Kind);
        }

        [Test]
        public void TheStrongestArchetypeOverall_IsTheSlowestMover()
        {
            var strongest = AllArchetypes.OrderByDescending(a => a.MaxHealth).First();
            var slowest = AllArchetypes.OrderBy(a => a.MoveSpeed).First();
            Assert.AreEqual(strongest.Kind, slowest.Kind);
        }

        // --- YT-194: the "Robot health" slider scales health only ------------------------------

        [Test]
        public void WithHealthMultiplier_ScalesHealthOnly_LeavingEverythingElseUntouched()
        {
            var scaled = Rusher.WithHealthMultiplier(2f);

            Assert.AreEqual(Rusher.MaxHealth * 2f, scaled.MaxHealth, 1e-4,
                "the health slider must actually double the health");
            Assert.AreEqual(Rusher.ContactDamage, scaled.ContactDamage, 1e-4,
                "a health-only override must not also buff the hit — that's Toughened()'s job");
            Assert.AreEqual(Rusher.MoveSpeed, scaled.MoveSpeed, 1e-4);
            Assert.AreEqual(Rusher.BodyScale, scaled.BodyScale);
        }

        [Test]
        public void WithHealthMultiplier_ComposesWithToughened()
        {
            // The two knobs stack: the player's flat baseline, then the Invasion Level's own ramp on
            // top of it — not one overriding the other.
            var composed = Rusher.WithHealthMultiplier(2f).Toughened(1.5f);
            Assert.AreEqual(Rusher.MaxHealth * 2f * 1.5f, composed.MaxHealth, 1e-4);
            Assert.AreEqual(Rusher.ContactDamage * 1.5f, composed.ContactDamage, 1e-4,
                "Toughened() still scales damage even when a health override was applied first");
        }
    }
}
