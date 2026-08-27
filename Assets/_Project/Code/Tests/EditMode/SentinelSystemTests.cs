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
        public void DamageFractionNeverReachesOrExceedsOne()
        {
            for (int level = 0; level <= RigBoard.MaxLevel("u_dmg"); level++)
            {
                float fraction = AbilityTuning.SentinelDamageFraction(
                    level, AbilityTuning.DefaultSentinelDamageFraction, AbilityTuning.DefaultSentinelDamageFractionPerLevel);
                Assert.That(fraction, Is.LessThan(1f),
                    $"level {level} must stay strictly below Max's own current primary output");
            }
        }

        [Test]
        public void DamagePerShotIsAlwaysBelowThePrimaryItIsAFractionOf()
        {
            const float primaryDamage = 8f; // an arbitrary "Max's current primary tick damage"
            for (int level = 0; level <= RigBoard.MaxLevel("u_dmg"); level++)
            {
                float shot = AbilityTuning.SentinelDamagePerShot(
                    primaryDamage, level,
                    AbilityTuning.DefaultSentinelDamageFraction, AbilityTuning.DefaultSentinelDamageFractionPerLevel);
                Assert.That(shot, Is.LessThan(primaryDamage),
                    "the DECISION: sentinel damage must never catch up to Max's own current primary");
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

        [Test]
        public void DeploymentSlotsEqualsTheAxisLevel_FlooredAtOne()
        {
            Assert.That(AbilityTuning.SentinelDeploymentSlots(0), Is.EqualTo(1),
                "u_slt starts at level 0 (a stat, not the old cap-1-from-run-start track) — still floors at 1 slot");
            Assert.That(AbilityTuning.SentinelDeploymentSlots(4), Is.EqualTo(4));
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
                Assert.That(abilities.SentinelReady, Is.False, "the single slot is now full");

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
    }
}
