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
            // MV-370: the pool is exactly 3 abilities now (Water Balloon left it), same as
            // AbilityDraft.MaxCandidates — a fresh draw offers the whole pool.
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
            WeaponSystemState.Acquire(AbilityKind.Speed);
            // MV-370: 3 abilities total, 1 owned -> 2 left, below MaxCandidates.

            Assert.That(AbilityDraft.DrawCandidates().Length, Is.EqualTo(2));
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
    }
}
