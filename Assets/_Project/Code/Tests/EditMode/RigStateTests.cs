using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// THE RIG's unified node model, schema 3 (MV-436 — retires MV-422's cap/stat split): one gate,
    /// not two. A Morphing Module draft (<see cref="RigState.AcquireCap"/>) is the only way any node
    /// reaches level 1; <see cref="RigState.TrySpendPart"/> can raise an already-owned node further
    /// but can never perform that 0-&gt;1 unlock, for any of the tree's 23 abilities — plus the
    /// run-start baseline and the draft-candidate eligibility pool both derive from.
    /// </summary>
    public sealed class RigStateTests
    {
        // model.rules' own worked example, verbatim: at run start p_dmg is the only owned ability,
        // and these eight are reached-and-unowned — the first draft's whole candidate pool.
        private static readonly string[] RunStartEligible =
            { "p_rng", "p_flw", "s_bal", "e_ff", "e_cel", "m_spd", "m_tp", "u_sen" };

        [SetUp]
        [TearDown]
        public void Clear() => RigState.Reset();

        /// <summary>Walks a node's own ancestor chain, drafting any unreached ancestor via
        /// <see cref="RigState.AcquireCap"/> so <paramref name="id"/> itself becomes REACHED —
        /// without this, a deep node like <c>p_prc</c>/<c>s_aut</c>/<c>e_mag</c> would never be
        /// reached at a fresh Reset, and AC1's "never" would only be proven for the trivial
        /// already-unreached case. Schema 3 (MV-436): every ancestor is drafted now, never leveled
        /// by a part — there is no other way to raise a node from 0.</summary>
        private static void Reach(string id)
        {
            string parent = RigBoard.Parent(id);
            if (string.IsNullOrEmpty(parent)) return;
            Reach(parent);
            if (RigState.Level(parent) >= 1) return;
            RigState.AcquireCap(parent);
        }

        // ---------------------------------------------------------------- schema/data sanity

        [Serializable] private sealed class AbilityKindWire { public string kind; }
        [Serializable] private sealed class RigBoardSchemaWire { public int schema; public AbilityKindWire[] abilities; }

        [Test]
        public void TheDataFileIsSchemaThreeAndEveryAbilityIsKindCap()
        {
            var asset = Resources.Load<TextAsset>("UI/rig_board");
            Assert.That(asset, Is.Not.Null, "Resources/UI/rig_board.json must exist");

            var wire = JsonUtility.FromJson<RigBoardSchemaWire>(asset.text);
            Assert.That(wire.schema, Is.EqualTo(3), "rig_board.json must be schema 3 (MV-436 — cap/stat split retired)");
            Assert.That(wire.abilities.Length, Is.EqualTo(23));
            foreach (var a in wire.abilities)
                Assert.That(a.kind, Is.EqualTo("cap"), "every ability must be kind 'cap' under schema 3 — the 'stat' kind no longer exists");
        }

        // ---------------------------------------------------------------- AC1: a part never performs the 0->1 unlock, for all 23 abilities

        [Test]
        public void APartCanNeverRaiseAnAbilityFromZeroToOne_ForAllTwentyThreeAbilities()
        {
            Assert.That(RigBoard.AllIds.Count, Is.EqualTo(23), "the tree must name exactly 23 abilities");

            foreach (string id in RigBoard.AllIds)
            {
                if (id == "p_dmg") continue; // p_dmg is owned from Reset() itself — see OnceDraftedAnAbilitysFurtherLevelsCanBeBoughtWithParts for its own-cap-further-levels case instead.
                RigState.Reset();
                Reach(id);
                Assert.That(RigState.Level(id), Is.EqualTo(0), $"{id} must start at 0 once merely reached");

                bool spent = RigState.TrySpendPart(id);

                Assert.That(spent, Is.False, $"a part must never unlock '{id}'");
                Assert.That(RigState.Level(id), Is.EqualTo(0), $"'{id}' must remain unowned after the rejected spend");
            }
        }

        [Test]
        public void OnceDraftedAnAbilitysFurtherLevelsCanBeBoughtWithParts()
        {
            // The "never" above is specifically the 0->1 unlock — once a node is OWNED, further
            // levels are an ordinary part spend like any other.
            RigState.AcquireCap("e_ff");
            Assert.That(RigState.TrySpendPart("e_ff"), Is.True);
            Assert.That(RigState.Level("e_ff"), Is.EqualTo(2));
        }

        // ---------------------------------------------------------------- AC5: deeper nodes stay gated behind their parent's reached-ness

        [Test]
        public void SpreadIsNotReachedUntilRangeIsAtLeastLevelOne()
        {
            // p_spr's parent is p_rng, not p_dmg — so at run start (only p_dmg owned) p_spr is NOT
            // yet reached, even though its grandparent is.
            Assert.That(RigState.IsReached("p_spr"), Is.False, "p_spr must not be reached before p_rng is >= 1");
            Assert.That(RigState.TrySpendPart("p_spr"), Is.False, "an unreached node must not accept a part");
            Assert.That(RigState.Level("p_spr"), Is.EqualTo(0));

            Assert.That(RigState.AcquireCap("p_rng"), Is.True, "p_rng's own parent (p_dmg) is already >= 1, so it is a valid draft pick");

            Assert.That(RigState.IsReached("p_spr"), Is.True, "the instant p_rng hits level 1, p_spr becomes reached");
        }

        [Test]
        public void PierceIsNotReachedUntilFlowIsAtLeastLevelOne()
        {
            Assert.That(RigState.IsReached("p_prc"), Is.False, "p_prc must not be reached before p_flw is >= 1");

            Assert.That(RigState.AcquireCap("p_flw"), Is.True);

            Assert.That(RigState.IsReached("p_prc"), Is.True, "the instant p_flw hits level 1, p_prc becomes reached");
        }

        [Test]
        public void ARootNodeWithNoParentIsAlwaysReached()
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
        public void RunStartOffersExactlyTheEightModelRulesCandidates()
        {
            var expected = new HashSet<string>(RunStartEligible);
            var actual = new HashSet<string>(RigState.EligibleCapIds());

            Assert.That(actual, Is.EquivalentTo(expected));
        }

        // ---------------------------------------------------------------- AC4: Range/Flow are draftable at run start, never parts-spendable

        [Test]
        public void RangeAndFlowAreDraftableAtRunStartAndAreNotPartsSpendable()
        {
            Assert.That(RigState.EligibleCapIds(), Does.Contain("p_rng"));
            Assert.That(RigState.EligibleCapIds(), Does.Contain("p_flw"));

            Assert.That(RigState.CanSpendPart("p_rng"), Is.False, "p_rng is unowned — a part can never unlock it");
            Assert.That(RigState.CanSpendPart("p_flw"), Is.False, "p_flw is unowned — a part can never unlock it");
            Assert.That(RigState.TrySpendPart("p_rng"), Is.False);
            Assert.That(RigState.TrySpendPart("p_flw"), Is.False);
        }

        // ---------------------------------------------------------------- deeper caps stay gated behind their parent

        [Test]
        public void MagnetoIsNotDraftableUntilCooldownIsAtLeastLevelOne()
        {
            Assert.That(RigState.EligibleCapIds(), Does.Not.Contain("e_mag"),
                "e_mag must not be a draft candidate before e_cd >= 1");

            RigState.AcquireCap("e_cel");
            RigState.AcquireCap("e_cd"); // e_cd is a cap under schema 3 too — needs its own draft, not a part

            Assert.That(RigState.EligibleCapIds(), Does.Contain("e_mag"),
                "e_mag must become draftable the instant e_cd reaches level 1");
        }

        [Test]
        public void PierceIsNotDraftableUntilFlowIsAtLeastLevelOne()
        {
            Assert.That(RigState.EligibleCapIds(), Does.Not.Contain("p_prc"),
                "p_prc must not be a draft candidate before p_flw >= 1");

            RigState.AcquireCap("p_flw");

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
            RigState.AcquireCap("p_rng");

            RigState.Reset();

            Assert.That(RigState.Level("e_ff"), Is.EqualTo(0));
            Assert.That(RigState.Level("p_rng"), Is.EqualTo(0));
            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- AC6: the amber "+" model — owned-and-not-maxed only, never an unowned node

        [Test]
        public void CanSpendPartIsTrueOnlyForOwnedNodesBelowTheirMaxLevel()
        {
            foreach (string id in RigBoard.AllIds)
            {
                bool ownedBelowMax = RigState.IsOwned(id) && RigState.Level(id) < RigBoard.MaxLevel(id);
                Assert.That(RigState.CanSpendPart(id), Is.EqualTo(ownedBelowMax), $"'{id}' CanSpendPart mismatch at run start");
            }

            RigState.AcquireCap("e_ff");
            Assert.That(RigState.CanSpendPart("e_ff"), Is.True, "a freshly-drafted node below its cap must be spendable");

            while (RigState.Level("p_dmg") < RigBoard.MaxLevel("p_dmg")) RigState.TrySpendPart("p_dmg");
            Assert.That(RigState.CanSpendPart("p_dmg"), Is.False, "must not be spendable once at its max level");
        }
    }
}
