using System.Collections.Generic;
using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-357's shed draft-pick draw: up to <see cref="AbilityDraft.MaxCandidates"/> distinct unowned
    /// abilities, shrinking as the pool drains and never repeating a candidate within one draw or
    /// handing back something Max already owns.
    /// </summary>
    public sealed class AbilityDraftTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => WeaponSystemState.Reset();

        [Test]
        public void FreshStateDrawsTheMaxCandidateCount()
        {
            // MV-362 added Sentinels on top of MV-361's Force Field, taking the pool to 7 abilities
            // (WaterBalloonAutoFire excluded pre-WaterBalloon, so 6 actually offered) — still
            // comfortably above AbilityDraft.MaxCandidates, so a fresh draw is capped at the max
            // rather than the whole pool.
            Assert.That(AbilityDraft.DrawCandidates().Length, Is.EqualTo(AbilityDraft.MaxCandidates));
        }

        [Test]
        public void MaxCandidatesIsThree_MV357()
        {
            Assert.That(AbilityDraft.MaxCandidates, Is.EqualTo(3),
                "three is the shape MV-357 (and its MV-207 precursor) specified");
        }

        [Test]
        public void EveryCandidateIsCurrentlyUnacquired()
        {
            WeaponSystemState.Acquire(AbilityKind.Speed);

            foreach (var candidate in AbilityDraft.DrawCandidates())
                Assert.That(WeaponSystemState.IsAcquired(candidate), Is.False,
                    $"{candidate} was offered as a candidate but Max already owns it");
        }

        [Test]
        public void ADrawNeverContainsADuplicateCandidate_MV357()
        {
            var candidates = AbilityDraft.DrawCandidates();
            var seen = new HashSet<AbilityKind>(candidates);
            Assert.That(seen.Count, Is.EqualTo(candidates.Length), "a single draw offered the same ability twice");
        }

        [Test]
        public void DrawShrinksAsThePoolDrains()
        {
            // MV-362 added Sentinels on top of MV-361's Force Field: 7 abilities total, but
            // WaterBalloonAutoFire stays excluded until WaterBalloon is owned. Acquiring the other 5
            // leaves exactly WaterBalloon itself — 1 candidate, below MaxCandidates.
            WeaponSystemState.Acquire(AbilityKind.Speed);
            WeaponSystemState.Acquire(AbilityKind.Teleport);
            WeaponSystemState.Acquire(AbilityKind.WeaponCooldown);
            WeaponSystemState.Acquire(AbilityKind.ForceField);
            WeaponSystemState.Acquire(AbilityKind.Sentinels);

            Assert.That(AbilityDraft.DrawCandidates().Length, Is.EqualTo(1));
        }

        [Test]
        public void DrawReturnsExactlyOneWhenOnlyOneAbilityRemains_MV357()
        {
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds)
                if (kind != AbilityKind.Teleport) WeaponSystemState.Acquire(kind);

            var candidates = AbilityDraft.DrawCandidates();
            Assert.That(candidates.Length, Is.EqualTo(1));
            Assert.That(candidates[0], Is.EqualTo(AbilityKind.Teleport));
        }

        [Test]
        public void DrawIsEmptyOncePoolIsFullyDrained_MV357()
        {
            foreach (AbilityKind kind in WeaponCatalog.AllAbilityKinds)
                WeaponSystemState.Acquire(kind);

            Assert.That(AbilityDraft.DrawCandidates(), Is.Empty,
                "nothing is left to grant once every ability is owned");
        }

        [Test]
        public void FirstDrawOfARunAlwaysOffersForceField_MV412()
        {
            for (int i = 0; i < 20; i++)
                Assert.That(AbilityDraft.DrawCandidates(), Does.Contain(AbilityKind.ForceField),
                    "Force Field must be a candidate in a fresh run's very first draw, not left to chance");
        }

        [Test]
        public void FirstDrawStillFillsRemainingSlotsWithDistinctUnacquiredAbilities_MV412()
        {
            var candidates = AbilityDraft.DrawCandidates();

            Assert.That(candidates.Length, Is.EqualTo(AbilityDraft.MaxCandidates));
            var seen = new HashSet<AbilityKind>(candidates);
            Assert.That(seen.Count, Is.EqualTo(candidates.Length), "the forced Force Field slot duplicated another candidate");
            foreach (var candidate in candidates)
                Assert.That(WeaponSystemState.IsAcquired(candidate), Is.False);
        }

        [Test]
        public void LaterDrawsInARunAreNotForcedToOfferForceField_MV412()
        {
            // Once something has been acquired this is no longer the run's first draw, so Force
            // Field competes on the same uniform odds as everything else again.
            WeaponSystemState.Acquire(AbilityKind.ForceField);

            foreach (var candidate in AbilityDraft.DrawCandidates())
                Assert.That(candidate, Is.Not.EqualTo(AbilityKind.ForceField),
                    "Force Field is already owned so it can never legitimately be redrawn");
        }
    }
}
