using System.Collections.Generic;
using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Morphing Module candidate draw (MV-424): up to <see cref="RigBoard.DraftMaxCandidates"/>
    /// distinct ids, sampled without replacement, from exactly <see cref="RigState.EligibleCapIds"/> —
    /// caps only, reached, not owned. Replaces <c>AbilityDraftTests</c>, which pinned the old per-enum
    /// <c>AbilityDraft</c> that MV-424 deletes.
    /// </summary>
    public sealed class RigDraftTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => RigState.Reset();

        [Test]
        public void FreshStateDrawsTheMaxCandidateCount()
        {
            // Run start offers exactly eight eligible caps (RigStateTests, MV-436), comfortably above
            // DraftMaxCandidates (3), so a fresh draw is capped at the max rather than the whole pool.
            Assert.That(RigDraft.DrawCandidates().Length, Is.EqualTo(RigBoard.DraftMaxCandidates));
        }

        [Test]
        public void EveryCandidateIsEligibleAtDrawTime()
        {
            RigState.AcquireCap("m_spd");

            var eligible = new HashSet<string>(RigState.EligibleCapIds());
            foreach (var candidate in RigDraft.DrawCandidates())
                Assert.That(eligible, Does.Contain(candidate),
                    $"{candidate} was offered but is not a currently-eligible cap");
        }

        [Test]
        public void ADrawNeverContainsADuplicateCandidate()
        {
            var candidates = RigDraft.DrawCandidates();
            var seen = new HashSet<string>(candidates);
            Assert.That(seen.Count, Is.EqualTo(candidates.Length), "a single draw offered the same cap twice");
        }

        [Test]
        public void DrawShrinksAsThePoolDrains()
        {
            // Schema 3 (MV-436): drafting a node immediately reaches its own children too, so
            // draining a branch means walking it all the way to its leaves, not just drafting its
            // root. Draft every node outside SUPPORT to exhaustion (RigBoard.AllIds is already
            // parent-before-child order, so one pass suffices) and leave SUPPORT entirely untouched
            // — u_sen (its own root, reached from run start) is then the only id anything can offer.
            foreach (string id in RigBoard.AllIds)
                if (RigBoard.Category(id) != "SUPPORT") RigState.AcquireCap(id);

            var candidates = RigDraft.DrawCandidates();
            Assert.That(candidates.Length, Is.EqualTo(1));
            Assert.That(candidates[0], Is.EqualTo("u_sen"));
        }

        [Test]
        public void DrawIsEmptyOnceEveryReachedCapIsOwned()
        {
            // One pass over every id in the tree's own parent-before-child order drafts the whole
            // 23-ability tree (schema 3: drafting a node reaches its children, so nothing is ever
            // left stranded by ordering).
            foreach (string id in RigBoard.AllIds) RigState.AcquireCap(id);

            Assert.That(RigDraft.DrawCandidates(), Is.Empty,
                "nothing reached-and-unowned is left to draft");
        }

        [Test]
        public void ADraftedCapIsNeverOfferedAgain()
        {
            RigState.AcquireCap("m_spd");

            for (int i = 0; i < 20; i++)
                Assert.That(RigDraft.DrawCandidates(), Does.Not.Contain("m_spd"),
                    "an owned cap must never reappear in a later draw");
        }
    }
}
