using System.Collections.Generic;
using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// THE RIG's unified node model (MV-422): the two gates model.rules pins — a <c>cap</c> can only
    /// reach level 1 via a Morphing Module draft, never a part; a <c>stat</c> is spendable the moment
    /// its parent reaches level 1 — plus the run-start baseline and the draft-candidate eligibility
    /// pool both derive from.
    /// </summary>
    public sealed class RigStateTests
    {
        // The nine caps (spec, MV-422 description, verbatim).
        private static readonly string[] AllCaps =
            { "p_prc", "s_bal", "s_aut", "e_ff", "e_cel", "e_mag", "m_spd", "m_tp", "u_sen" };

        [SetUp]
        [TearDown]
        public void Clear() => RigState.Reset();

        /// <summary>Walks a node's own ancestor chain, acquiring any cap ancestor via
        /// <see cref="RigState.AcquireCap"/> and leveling any stat ancestor via
        /// <see cref="RigState.TrySpendPart"/>, so <paramref name="id"/> itself becomes REACHED —
        /// without this, a deep cap like <c>p_prc</c>/<c>s_aut</c>/<c>e_mag</c> would never be
        /// reached at a fresh Reset, and AC1's "never" would only be proven for the trivial
        /// already-unreached case.</summary>
        private static void Reach(string id)
        {
            string parent = RigBoard.Parent(id);
            if (string.IsNullOrEmpty(parent)) return;
            Reach(parent);
            if (RigState.Level(parent) >= 1) return;
            if (RigBoard.IsCap(parent)) RigState.AcquireCap(parent);
            else RigState.TrySpendPart(parent);
        }

        // ---------------------------------------------------------------- AC1: caps never unlock via a part

        [Test]
        public void APartCanNeverRaiseACapFromZeroToOne_ForEveryCap()
        {
            foreach (string cap in AllCaps)
            {
                Reach(cap);
                Assert.That(RigState.Level(cap), Is.EqualTo(0), $"{cap} must start at 0 once merely reached");

                bool spent = RigState.TrySpendPart(cap);

                Assert.That(spent, Is.False, $"a part must never unlock cap '{cap}'");
                Assert.That(RigState.Level(cap), Is.EqualTo(0), $"'{cap}' must remain unowned after the rejected spend");
            }
        }

        [Test]
        public void OnceDraftedACapsFurtherLevelsCanBeBoughtWithParts()
        {
            // The "never" above is specifically the 0->1 unlock — once a cap is OWNED, further
            // levels are an ordinary part spend like any stat.
            RigState.AcquireCap("e_ff");
            Assert.That(RigState.TrySpendPart("e_ff"), Is.True);
            Assert.That(RigState.Level("e_ff"), Is.EqualTo(2));
        }

        // ---------------------------------------------------------------- AC2: a stat is spendable exactly when its parent reaches level 1

        [Test]
        public void AStatBecomesSpendableExactlyWhenItsParentReachesLevelOne_NotBefore()
        {
            // p_spr's parent is p_rng (a stat), not p_dmg — so at run start (only p_dmg owned) p_spr
            // is NOT yet reached, even though its grandparent is.
            Assert.That(RigState.IsReached("p_spr"), Is.False, "p_spr must not be reached before p_rng is >= 1");
            Assert.That(RigState.TrySpendPart("p_spr"), Is.False, "an unreached stat must not accept a part");
            Assert.That(RigState.Level("p_spr"), Is.EqualTo(0));

            Assert.That(RigState.TrySpendPart("p_rng"), Is.True, "p_rng's own parent (p_dmg) is already >= 1");

            Assert.That(RigState.IsReached("p_spr"), Is.True, "the instant p_rng hits level 1, p_spr becomes reached");
            Assert.That(RigState.TrySpendPart("p_spr"), Is.True, "p_spr must now accept a part, starting from level 0");
            Assert.That(RigState.Level("p_spr"), Is.EqualTo(1));
        }

        [Test]
        public void ARootStatWithNoParentIsAlwaysReached()
        {
            Assert.That(RigState.IsReached("p_dmg"), Is.True);
        }

        // ---------------------------------------------------------------- AC3: run-start baseline

        [Test]
        public void RunStartOwnsExactlyOneAbility_PDmgAtLevelOne()
        {
            int ownedCount = 0;
            foreach (string id in RigBoard.AllIds)
            {
                if (!RigState.IsOwned(id)) continue;
                ownedCount++;
                Assert.That(id, Is.EqualTo("p_dmg"), "the only ability owned at run start must be p_dmg");
            }
            Assert.That(ownedCount, Is.EqualTo(1));
            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(1));
        }

        [Test]
        public void RunStartOffersExactlySixDraftableCaps()
        {
            var expected = new HashSet<string> { "s_bal", "e_ff", "e_cel", "m_spd", "m_tp", "u_sen" };
            var actual = new HashSet<string>(RigState.EligibleCapIds());

            Assert.That(actual, Is.EquivalentTo(expected));
        }

        // ---------------------------------------------------------------- AC4: deeper caps stay gated behind their parent

        [Test]
        public void MagnetoIsNotDraftableUntilCooldownIsAtLeastLevelOne()
        {
            Assert.That(RigState.EligibleCapIds(), Does.Not.Contain("e_mag"),
                "e_mag must not be a draft candidate before e_cd >= 1");

            RigState.AcquireCap("e_cel");
            RigState.TrySpendPart("e_cd");

            Assert.That(RigState.EligibleCapIds(), Does.Contain("e_mag"),
                "e_mag must become draftable the instant e_cd reaches level 1");
        }

        [Test]
        public void PierceIsNotDraftableUntilFlowIsAtLeastLevelOne()
        {
            Assert.That(RigState.EligibleCapIds(), Does.Not.Contain("p_prc"),
                "p_prc must not be a draft candidate before p_flw >= 1");

            RigState.TrySpendPart("p_flw");

            Assert.That(RigState.EligibleCapIds(), Does.Contain("p_prc"),
                "p_prc must become draftable the instant p_flw reaches level 1");
        }

        // ---------------------------------------------------------------- General model sanity

        [Test]
        public void ADraftedCapIsNeverOfferedAgain()
        {
            Assert.That(RigState.EligibleCapIds(), Does.Contain("m_spd"));
            RigState.AcquireCap("m_spd");
            Assert.That(RigState.EligibleCapIds(), Does.Not.Contain("m_spd"));
        }

        [Test]
        public void ResetReturnsToTheRunStartBaseline()
        {
            RigState.AcquireCap("e_ff");
            RigState.TrySpendPart("p_rng");

            RigState.Reset();

            Assert.That(RigState.Level("e_ff"), Is.EqualTo(0));
            Assert.That(RigState.Level("p_rng"), Is.EqualTo(0));
            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(1));
        }
    }
}
