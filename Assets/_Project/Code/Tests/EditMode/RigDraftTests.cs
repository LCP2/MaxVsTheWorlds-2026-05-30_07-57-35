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
            // Run start offers exactly six eligible caps (RigStateTests), comfortably above
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
            // Run start's six root-reached caps: s_bal, e_ff, e_cel, m_spd, m_tp, u_sen. Owning s_bal
            // also newly REACHES s_aut (s_bal's own cap child), so draining the pool to exactly one
            // (u_sen) means acquiring that seventh cap too, not just the original six minus five.
            RigState.AcquireCap("s_bal");
            RigState.AcquireCap("s_aut");
            RigState.AcquireCap("e_ff");
            RigState.AcquireCap("e_cel");
            RigState.AcquireCap("m_spd");
            RigState.AcquireCap("m_tp");

            var candidates = RigDraft.DrawCandidates();
            Assert.That(candidates.Length, Is.EqualTo(1));
            Assert.That(candidates[0], Is.EqualTo("u_sen"));
        }

        [Test]
        public void DrawIsEmptyOnceEveryReachedCapIsOwned()
        {
            // s_aut only becomes reached once s_bal is owned, so it must be acquired too — otherwise
            // it is left sitting in the pool and this isn't actually "every reached cap owned".
            foreach (var id in new[] { "s_bal", "s_aut", "e_ff", "e_cel", "m_spd", "m_tp", "u_sen" })
                RigState.AcquireCap(id);

            // The deeper caps (p_prc, e_mag) are still unreached at this point — nothing left that a
            // draft can legally offer right now.
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
