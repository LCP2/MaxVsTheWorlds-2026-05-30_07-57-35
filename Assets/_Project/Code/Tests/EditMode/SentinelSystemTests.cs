using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
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
    /// old <c>SentinelTrackKind</c> enforced. The sentinel's damage fraction always stays below 1.0
    /// (the DECISION's "always weaker than Max's CURRENT primary" enforced structurally), and the
    /// Slots axis's level IS the deployment cap.
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
        public void DamageFractionNeverExceedsOneAndCapsAtHalfAtTheMaxLevel()
        {
            int maxLevel = RigBoard.MaxLevel("u_dmg");
            for (int level = 0; level <= maxLevel; level++)
            {
                float fraction = AbilityTuning.SentinelDamageFraction(
                    level, AbilityTuning.DefaultSentinelDamageFraction, AbilityTuning.DefaultSentinelDamageFractionPerLevel);
                Assert.That(fraction, Is.LessThanOrEqualTo(1f),
                    $"level {level} must never exceed Max's own current primary output");
                if (level < maxLevel)
                    Assert.That(fraction, Is.LessThan(1f), $"level {level} must stay strictly below the max level");
            }
            // MV-634: a maxed u_dmg track now lands at half of Max's own current primary output, down
            // from MV-610's exact 1.0 — sentinels were still knocking out robots too quickly.
            Assert.That(AbilityTuning.SentinelDamageFraction(
                maxLevel, AbilityTuning.DefaultSentinelDamageFraction, AbilityTuning.DefaultSentinelDamageFractionPerLevel),
                Is.EqualTo(0.5f).Within(1e-4f), "the max level must land exactly on 0.5, half of Max's output when fully invested");
        }

        [Test]
        public void DamagePerShotNeverExceedsThePrimaryItIsAFractionOf()
        {
            const float primaryDamage = 8f; // an arbitrary "Max's current primary tick damage"
            for (int level = 0; level <= RigBoard.MaxLevel("u_dmg"); level++)
            {
                float shot = AbilityTuning.SentinelDamagePerShot(
                    primaryDamage, level,
                    AbilityTuning.DefaultSentinelDamageFraction, AbilityTuning.DefaultSentinelDamageFractionPerLevel);
                Assert.That(shot, Is.LessThanOrEqualTo(primaryDamage),
                    "sentinel damage per shot must never exceed Max's own current primary");
            }
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
        public void MoveSpeedIsZeroUntilTheAxisIsLeveled()
        {
            Assert.That(AbilityTuning.SentinelMoveSpeed(0, 1.2f), Is.EqualTo(0f),
                "MV-422: the sentinel does not follow at all until Move is actually spent on — matches pre-MV-422 behaviour");
            Assert.That(AbilityTuning.SentinelMoveSpeed(1, 1.2f), Is.GreaterThan(0f));
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

            var maxGo = new GameObject("Max");
            var abilities = maxGo.AddComponent<PlayerAbilities>();
            try
            {
                Assert.That(abilities.TryDeploySentinel(), Is.True, "first deploy should succeed");
                Assert.That(PlayerAbilities.SentinelDeployedCount, Is.EqualTo(1));
                // MV-604: SentinelReady no longer checks the Slots cap — a full slot recalls the
                // furthest sentinel on redeploy rather than refusing (see SentinelSystemTests'
                // RedeployAtCap... test), so "ready" now means owned + affordable, nothing more.
                Assert.That(abilities.SentinelReady, Is.True, "a full slot no longer blocks readiness");

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

        /// <summary>MV-604 (Lee, 26 Aug 2026 playtest): "If I add a sentinel before I've enabled
        /// move... the only situation in which I gain the slot back... is if enemies destroy it...
        /// Once I do upgrade to Move... it's not given the move ability anyway." Two compounding
        /// defects in one test, since together they made the ability dead the moment every sentinel
        /// was standing in a cleared area:
        ///  (a) redeploying at the Slots cap must recall the FURTHEST sentinel from Max and place the
        ///      new one — never refuse for lack of a slot, and the recall must not be a death;
        ///  (b) an already-deployed sentinel must pick up a later Move/Range/Health upgrade live, and
        ///      a raised Health cap must not heal it.
        ///
        /// Proven to fail on the pre-fix commit: <c>TryDeploySentinel</c> returned false once
        /// <c>Sentinel.Active.Count</c> reached the cap (<c>SentinelReady</c> required
        /// <c>SentinelDeployedCount &lt; SentinelDeploymentCap</c>) — <c>Expected: True, But was:
        /// False</c> on the final deploy below — and <c>live.MoveSpeed</c>/<c>live.Range</c> stayed
        /// pinned at their deploy-time values after the RigState upgrades — <c>Expected: greater than
        /// 0, But was: 0</c> for MoveSpeed.</summary>
        [Test]
        public void RedeployAtCapRecallsFurthestWithoutADeathAndLiveUpgradesReachAnAlreadyDeployedSentinel_MV604()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);
            PickupWallet.SetPowerCells(999);

            RigState.AcquireCap("u_hp");  // reaches u_slt (u_hp's own RIG child)
            RigState.AcquireCap("u_slt"); // level 1 -> cap 1
            RigState.RaiseLevel("u_slt"); // level 2 -> cap 2
            RigState.RaiseLevel("u_slt"); // level 3 -> cap 3
            Assert.That(PlayerAbilities.SentinelDeploymentCap, Is.EqualTo(3));

            var maxGo = new GameObject("Max");
            var abilities = maxGo.AddComponent<PlayerAbilities>();
            try
            {
                // --- (a) redeploy at the cap recalls the FURTHEST sentinel, never refuses ---
                Assert.That(abilities.TryDeploySentinel(new Vector3(5f, 0f, 0f)), Is.True);
                Assert.That(abilities.TryDeploySentinel(new Vector3(20f, 0f, 0f)), Is.True);
                Assert.That(abilities.TryDeploySentinel(new Vector3(50f, 0f, 0f)), Is.True);
                Assert.That(Sentinel.Active.Count, Is.EqualTo(3), "precondition: cap reached exactly");

                Sentinel furthest = Sentinel.Active[2]; // the 50m one, deployed last
                Assert.That(furthest.transform.position.x, Is.EqualTo(50f).Within(1e-3f));
                bool recalledFiredDied = false;
                furthest.Died += _ => recalledFiredDied = true;

                bool deployedAtCap = abilities.TryDeploySentinel(new Vector3(1f, 0f, 0f));

                Assert.That(deployedAtCap, Is.True, "deployment must never be refused for lack of a slot");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(3), "must stay at the cap, never grow past it");
                foreach (Sentinel s in Sentinel.Active)
                    Assert.That(Mathf.Abs(s.transform.position.x - 50f), Is.GreaterThan(1e-3f),
                        "the 50m sentinel specifically must be gone — not the oldest, not the nearest");
                Assert.That(recalledFiredDied, Is.False, "a recall must never fire Died — it is not a death");

                // --- (b) an already-deployed sentinel picks up Move/Range/Health upgrades LIVE ---
                Sentinel live = Sentinel.Active[0]; // the 5m one, untouched since its own deploy
                Assert.That(live.MoveSpeed, Is.EqualTo(0f), "u_mov is unowned — must start stationary");

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
