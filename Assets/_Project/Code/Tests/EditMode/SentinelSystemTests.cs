using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-362's pure/state layer, restructured onto THE RIG's six <c>u_sen</c> child axes
    /// (Damage/Range/Health/Move/Cost/Slots — <c>u_dmg</c>/<c>u_rng</c>/<c>u_hp</c>/<c>u_mov</c>/
    /// <c>u_cst</c>/<c>u_slt</c>): every axis starts at 0. Schema 3 (MV-436) makes every one of them
    /// a <c>cap</c> — reached once the <c>u_sen</c> cap (<see cref="AbilityKind.Sentinels"/>) is
    /// drafted, but each axis still needs its own Morphing Module draft (<see cref="RigState.AcquireCap"/>)
    /// before a part can raise it further, the same "unowned/locked items can't be upgraded" gate the
    /// old <c>SentinelTrackKind</c> enforced. MV-653 repealed the old "always weaker than Max's
    /// CURRENT primary" rule — the sentinel's damage is now flat and independent of Max's own
    /// primary damage — and the Slots axis's level IS the deployment cap.
    /// </summary>
    public sealed class SentinelSystemTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();   // MV-457: also calls RigState.Reset() — the category unlock below must come AFTER this
            // This suite is about the Sentinel axes' own math/gating once u_sen is owned, not MV-457's
            // shed/category-lock gate (RigStateTests owns that) — force every category open so u_sen
            // (SUPPORT's own root) stays reached, as it always was before MV-457.
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
            DevTuning.Reset();
            Sentinel.DestroyAllActive();
            Sentinel.FocusEnabled = false;
        }

        // ---------------------------------------------------------------- RigState gating

        [Test]
        public void EverySentinelAxisStartsAtZero()
        {
            foreach (string id in new[] { "u_dmg", "u_rng", "u_hp", "u_mov", "u_cst", "u_slt" })
                Assert.That(RigState.Level(id), Is.EqualTo(0), $"{id} must start unleveled");
        }

        [Test]
        public void DirectChildAxesAreNotReachedUntilSentinelsIsAcquired()
        {
            Assert.That(RigState.IsReached("u_dmg"), Is.False);
            Assert.That(RigState.RaiseLevel("u_dmg"), Is.False,
                "unowned/locked items can't be upgraded (spec §5) — u_sen isn't drafted yet");
        }

        [Test]
        public void DirectChildAxesBecomeDraftableOnceSentinelsIsAcquired_MV436()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);

            Assert.That(RigState.IsReached("u_dmg"), Is.True);
            Assert.That(RigState.CanSpendPart("u_dmg"), Is.False, "u_dmg is reached but still unowned — only a draft can unlock it");
            Assert.That(RigState.AcquireCap("u_dmg"), Is.True);
            Assert.That(RigState.Level("u_dmg"), Is.EqualTo(1));
        }

        [Test]
        public void GrandchildAxesNeedTheirOwnParentReachedToo()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);

            Assert.That(RigState.IsReached("u_mov"), Is.False, "u_mov's parent is u_dmg, still at 0");

            RigState.AcquireCap("u_dmg");
            Assert.That(RigState.IsReached("u_mov"), Is.True, "the instant u_dmg hits level 1, u_mov becomes reached");
        }

        [Test]
        public void ASentinelAxisCannotLevelPastItsCap()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            RigState.AcquireCap("u_dmg");
            int cap = RigBoard.MaxLevel("u_dmg");
            for (int i = 1; i < cap; i++)
                Assert.That(RigState.RaiseLevel("u_dmg"), Is.True);

            Assert.That(RigState.Level("u_dmg"), Is.EqualTo(cap));
            Assert.That(RigState.RaiseLevel("u_dmg"), Is.False, "already at the cap");
        }

        [Test]
        public void ResetPutsEverySentinelAxisBackToZeroAndForgetsAcquisition()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            RigState.AcquireCap("u_dmg");

            WeaponSystemState.Reset();

            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Sentinels), Is.False);
            Assert.That(RigState.Level("u_dmg"), Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- AbilityTuning math

        [Test]
        public void MaxHpGrowsLinearlyFromLevelZero()
        {
            Assert.That(AbilityTuning.SentinelMaxHp(0, 60f, 20f), Is.EqualTo(60f).Within(1e-4f));
            Assert.That(AbilityTuning.SentinelMaxHp(3, 60f, 20f), Is.EqualTo(120f).Within(1e-4f));
        }

        [Test]
        public void DamagePerShotGrowsLinearlyFromLevelZero()
        {
            // MV-653: sentinel damage is now flat and independent of Max's own primary damage — the
            // old "never exceeds the primary it is a fraction of" invariant is repealed, so this
            // covers the replacement shape instead: a linear base-plus-per-level step, same pattern
            // as MaxHpGrowsLinearlyFromLevelZero above.
            Assert.That(AbilityTuning.SentinelDamagePerShot(0, 2f, 1f), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(AbilityTuning.SentinelDamagePerShot(5, 2f, 1f), Is.EqualTo(7f).Within(1e-4f));
        }

        [Test]
        public void RangeGrowsLinearlyFromLevelZero()
        {
            float l0 = AbilityTuning.SentinelRange(0, 7f, 1.5f);
            float l2 = AbilityTuning.SentinelRange(2, 7f, 1.5f);
            Assert.That(l0, Is.EqualTo(7f).Within(1e-4f));
            Assert.Greater(l2, l0);
        }

        [Test]
        public void CostNeverGoesBelowTheFortyPercentFloor()
        {
            int cost = AbilityTuning.SentinelCost(99, 5, 0.15f);
            Assert.That(cost, Is.GreaterThanOrEqualTo(Mathf.RoundToInt(5 * 0.4f)));
        }

        /// <summary>MV-579 AC1 (DECISION, Lee 26 Aug 2026 playtest: "Cost should be 0"). Proven to fail
        /// on the pre-fix commit: <c>SentinelCost</c> used to end in <c>Mathf.Max(1, ...)</c>, a hard
        /// floor of 1 that made a 0 base cost impossible — <c>SentinelCost(level, 0, perLevel)</c> came
        /// back 1 at every level, never 0. Failure quoted in the MV-579 fix comment.</summary>
        [Test]
        public void CostIsExactlyZeroAtEveryLevelWhenTheBaseCostIsZero()
        {
            for (int level = 0; level <= RigBoard.MaxLevel("u_cst"); level++)
            {
                int cost = AbilityTuning.SentinelCost(level, 0, AbilityTuning.DefaultSentinelCostReductionPerLevel);
                Assert.That(cost, Is.EqualTo(0), $"level {level}: a 0 base cost must stay 0, never floor up to a phantom charge");
            }
        }

        [Test]
        public void MoveSpeedAtLevelZeroMatchesLevelOneFloor()
        {
            Assert.That(AbilityTuning.SentinelMoveSpeed(0, 1.2f), Is.EqualTo(1.2f),
                "MV-675: sentinels follow from the start now — level 0 shares level 1's rate, it is a floor not a fresh slope step");
            Assert.That(AbilityTuning.SentinelMoveSpeed(1, 1.2f), Is.EqualTo(1.2f));
            Assert.That(AbilityTuning.SentinelMoveSpeed(2, 1.2f), Is.EqualTo(2.4f), "the per-level slope above the floor is unchanged");
        }

        /// <summary>MV-675 AC2 — sole guard on the RIG label rename; do not cull. Must fail against
        /// pre-fix <c>rig_board.json</c> (label "MOVE"). Reads the resolved layout, not the raw JSON
        /// string, so a parsing/mapping regression would also be caught.</summary>
        [Test]
        public void UMovBoardLabelReadsSpeed()
        {
            RigAbilityLayout uMov = null;
            foreach (var ab in RigBoardLayout.Abilities)
                if (ab.Id == "u_mov") { uMov = ab; break; }

            Assert.That(uMov, Is.Not.Null, "fixture: u_mov must exist in the resolved layout");
            Assert.That(uMov.Label, Is.EqualTo("SPEED"),
                "MV-675: Lee's instruction was to rename the Sentinel's Move ability label to Speed");
        }

        [Test]
        public void StandoffStepMovesTowardTheTargetButStopsAtTheStandoffDistance()
        {
            Vector3 current = Vector3.zero;
            Vector3 target = new Vector3(10f, 0f, 0f);

            Vector3 afterOneSecond = AbilityTuning.SentinelStandoffStep(current, target, standoff: 2.5f, speed: 3f, dt: 1f);
            Assert.That(afterOneSecond.x, Is.EqualTo(3f).Within(1e-3f), "must close the gap at the given speed");

            Vector3 alreadyClose = AbilityTuning.SentinelStandoffStep(
                new Vector3(8f, 0f, 0f), target, standoff: 2.5f, speed: 3f, dt: 1f);
            Assert.That(alreadyClose.x, Is.EqualTo(8f).Within(1e-3f),
                "already within the standoff band — must not creep into Max's own feet");
        }

        // ---------------------------------------------------------------- MV-615 SentinelSeparationStep

        [Test]
        public void SeparationStepPushesCoincidentSentinelsApartButLeavesClearOnesUntouched()
        {
            // MV-615: this is exactly what the old standoff-follow step produced — two sentinels
            // converging onto the SAME point on Max's standoff ring, "effectively merged into one" per
            // Lee's report. Running the step repeatedly (as Update() does, one frame at a time) must
            // walk them back apart to at least the clearance distance, never leave them stuck together.
            Vector3 current = Vector3.zero;
            var others = new List<Vector3> { Vector3.zero };

            for (int i = 0; i < 60; i++)
                current = AbilityTuning.SentinelSeparationStep(current, others, minSeparation: 1.5f, speed: 4f, dt: 1f / 60f);

            Assert.That(Vector3.Distance(current, others[0]), Is.GreaterThanOrEqualTo(1.5f - 1e-2f),
                "two coincident sentinels must separate back out to at least the 1.5m placement clearance");

            Vector3 farCurrent = new Vector3(10f, 0f, 0f);
            var farOthers = new List<Vector3> { Vector3.zero }; // 10m away, nowhere near the 1.5m clearance
            Vector3 next = AbilityTuning.SentinelSeparationStep(farCurrent, farOthers, minSeparation: 1.5f, speed: 4f, dt: 1f);
            Assert.That(next, Is.EqualTo(farCurrent),
                "sentinels already clear of every neighbour must not be nudged at all");
        }

        [Test]
        public void DeploymentSlotsIsOnePlusLevelWithNoDeadStep()
        {
            // MV-623: replaces the old Mathf.Max(1, level) shape, whose level 0->1 step bought nothing
            // (the unlock already granted 1 slot, so level 1 also read as 1 — a dead level).
            Assert.That(AbilityTuning.SentinelDeploymentSlots(0), Is.EqualTo(1),
                "u_slt starts at level 0 (a stat, not the old cap-1-from-run-start track) — still floors at 1 slot");
            Assert.That(AbilityTuning.SentinelDeploymentSlots(1), Is.EqualTo(2), "MV-623: level 1 must buy a real second slot, not stay dead at 1");
            Assert.That(AbilityTuning.SentinelDeploymentSlots(4), Is.EqualTo(5));
        }

        [Test]
        public void DestroyingASentinelFreesItsDeploymentSlotForAnImmediateRedeploy()
        {
            // MV-397, the exact repro Lee hit: base case, one free slot (u_slt at level 0) — deploy,
            // let it die, deploy again.
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(100);
            PickupWallet.SetPowerCellSecondary(100);   // MV-673: a Sentinel deploy now spends this bank, not Parts

            var maxGo = new GameObject("Max");
            var abilities = maxGo.AddComponent<PlayerAbilities>();
            try
            {
                Assert.That(abilities.TryDeploySentinel(), Is.True, "first deploy should succeed");
                Assert.That(PlayerAbilities.SentinelDeployedCount, Is.EqualTo(1));
                // MV-604 (superseded by MV-1113): SentinelReady never checked the Slots cap — that's
                // SentinelSlotAvailable's own job now (the SENTINEL button's "FULL" gate), so a full
                // slot still doesn't affect THIS property.
                Assert.That(abilities.SentinelReady, Is.True, "a full slot does not affect SentinelReady");

                Sentinel deployed = Sentinel.Active[0];
                deployed.TakeDamage(new DamageInfo(
                    deployed.HealthCurrent, Vector3.zero, Vector3.forward, Team.Enemy));

                Assert.That(PlayerAbilities.SentinelDeployedCount, Is.EqualTo(0),
                    "the slot must be free immediately after the sentinel dies");
                Assert.That(abilities.TryDeploySentinel(), Is.True,
                    "a fresh sentinel should be deployable again once the old one is destroyed");
                Assert.That(PlayerAbilities.SentinelDeployedCount, Is.EqualTo(1));
            }
            finally
            {
                Sentinel.DestroyAllActive();
                Object.DestroyImmediate(maxGo);
            }
        }

        /// <summary>MV-604 (Lee, 26 Aug 2026 playtest) added a redeploy-at-cap recall so the ability was
        /// never dead once every sentinel stood in a cleared area. MV-1113 (6 Oct 2026, the SENTINEL
        /// button rewrite) SUPERSEDES that: the button now goes unavailable ("FULL") at the cap and a
        /// deploy attempt there simply refuses — no recall, nothing spent, nothing placed. This test now
        /// covers:
        ///  (a) redeploying at the Slots cap is refused outright — no recall, no growth past the cap;
        ///  (b) an already-deployed sentinel must still pick up a later Move/Range/Health upgrade live,
        ///      and a raised Health cap must not heal it (MV-604's other half, unaffected by MV-1113).</summary>
        [Test]
        public void RedeployAtCapIsRefusedAndLiveUpgradesReachAnAlreadyDeployedSentinel_MV1113()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(999);
            PickupWallet.SetPowerCellSecondary(999);   // MV-673: a Sentinel deploy now spends this bank, not Parts

            RigState.AcquireCap("u_hp");  // reaches u_slt (u_hp's own RIG child)
            RigState.AcquireCap("u_slt"); // level 1 -> cap 1
            RigState.RaiseLevel("u_slt"); // level 2 -> cap 2
            RigState.RaiseLevel("u_slt"); // level 3 -> cap 3
            Assert.That(PlayerAbilities.SentinelDeploymentCap, Is.EqualTo(3));

            var maxGo = new GameObject("Max");
            var abilities = maxGo.AddComponent<PlayerAbilities>();
            try
            {
                // --- (a) redeploy at the cap is refused outright, never recalls ---
                Assert.That(abilities.TryDeploySentinel(new Vector3(5f, 0f, 0f)), Is.True);
                Assert.That(abilities.TryDeploySentinel(new Vector3(20f, 0f, 0f)), Is.True);
                Assert.That(abilities.TryDeploySentinel(new Vector3(50f, 0f, 0f)), Is.True);
                Assert.That(Sentinel.Active.Count, Is.EqualTo(3), "precondition: cap reached exactly");
                Assert.That(abilities.SentinelSlotAvailable, Is.False, "MV-1113: the button's own FULL gate must read full");

                int cellsBeforeRefusal = PickupWallet.PowerCellsSecondary;
                bool deployedAtCap = abilities.TryDeploySentinel(new Vector3(1f, 0f, 0f));

                Assert.That(deployedAtCap, Is.False, "MV-1113: a deploy at the cap must now be refused, not recall");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(3), "must stay at the cap, never grow past it");
                Assert.That(PickupWallet.PowerCellsSecondary, Is.EqualTo(cellsBeforeRefusal), "a refused deploy must not spend cells");
                Assert.That(Sentinel.Active[2].transform.position.x, Is.EqualTo(50f).Within(1e-3f),
                    "MV-1113: the 50m sentinel must still be there — a refusal recalls nothing");

                // --- (b) an already-deployed sentinel picks up Move/Range/Health upgrades LIVE ---
                Sentinel live = Sentinel.Active[0]; // the 5m one, untouched since its own deploy
                Assert.That(live.MoveSpeed, Is.EqualTo(1.2f), "MV-675: u_mov unowned now still follows at the level-1 floor rate");

                live.TakeDamage(new DamageInfo(15f, Vector3.zero, Vector3.forward, Team.Enemy));
                float hpAfterDamage = live.HealthCurrent;
                float maxHpBeforeRaise = live.HealthMax;

                RigState.AcquireCap("u_dmg"); // reaches u_mov (u_dmg's own RIG child)
                RigState.AcquireCap("u_mov"); // u_mov -> level 1
                RigState.AcquireCap("u_rng"); // u_rng -> level 1 (direct child, already reached)
                RigState.RaiseLevel("u_hp");  // already owned from the cap setup above -> level 2

                float expectedRange = AbilityTuning.SentinelRange(
                    RigState.Level("u_rng"), AbilityTuning.DefaultSentinelRange, AbilityTuning.DefaultSentinelRangePerLevel);
                float expectedMoveSpeed = AbilityTuning.SentinelMoveSpeed(
                    RigState.Level("u_mov"), AbilityTuning.DefaultSentinelMoveSpeedPerLevel);
                float expectedMaxHp = AbilityTuning.SentinelMaxHp(
                    RigState.Level("u_hp"), AbilityTuning.DefaultSentinelBaseHp, AbilityTuning.DefaultSentinelHpPerLevel);

                Assert.That(live.Range, Is.EqualTo(expectedRange).Within(1e-3f),
                    "u_rng upgrade must reach the SAME already-deployed sentinel, not just a future one");
                Assert.That(live.MoveSpeed, Is.EqualTo(expectedMoveSpeed).Within(1e-3f),
                    "the exact Lee repro: a sentinel deployed before u_mov must start following the moment it's bought");
                Assert.That(live.MoveSpeed, Is.GreaterThan(0f));

                Assert.That(live.HealthMax, Is.EqualTo(expectedMaxHp).Within(1e-3f));
                Assert.That(live.HealthMax, Is.GreaterThan(maxHpBeforeRaise),
                    "the ceiling must actually rise, not just stay put while Current happens to match");
                Assert.That(live.HealthCurrent, Is.EqualTo(hpAfterDamage).Within(1e-3f),
                    "raising the HP cap must not be a free heal — Current stays exactly where the damage left it");
            }
            finally
            {
                Sentinel.DestroyAllActive();
                Object.DestroyImmediate(maxGo);
            }
        }
    }
}
