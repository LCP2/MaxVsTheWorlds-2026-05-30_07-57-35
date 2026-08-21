using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-523, Lee 21 Aug 2026: "Make the cost of the force field 0 cells." Retunes
    /// <see cref="AbilityTuning.DefaultForceFieldActivationCost"/> from 10 to 0 — the RIG unlock cost
    /// for <c>e_ff</c> (MV-511) is untouched, only the per-activation power-cell spend. Trap 1 from the
    /// ticket (<see cref="PickupWallet.TrySpendPowerCells"/> already special-cases <c>amount &lt;= 0</c>
    /// as an unconditional success, so a zero cost needed no call-site fix) is what
    /// <see cref="ActivatingAtZeroBankedCellsSucceedsAndSpendsNothing"/> guards.
    /// </summary>
    public sealed class MV523ForceFieldFreeActivationTests
    {
        private GameObject _max;
        private PlayerAbilities _abilities;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);

            _max = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            _abilities = _max.GetComponent<PlayerAbilities>();
            if (_abilities == null) _abilities = _max.AddComponent<PlayerAbilities>();

            WeaponSystemState.Acquire(AbilityKind.ForceField);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_max);
            WeaponSystemState.Reset();
            DevTuning.Reset();
            PickupWallet.Reset();
        }

        [Test]
        public void ResolvedActivationCostIsZero()
        {
            // AC1: assert the resolved property, not the authored constant directly.
            Assert.That(PlayerAbilities.ForceFieldActivationCost, Is.EqualTo(0),
                "MV-523: the resolved activation cost must read 0, matching AbilityTuning.DefaultForceFieldActivationCost");
        }

        [Test]
        public void ActivatingAtZeroBankedCellsSucceedsAndSpendsNothing()
        {
            // AC2, the trap-1 regression: PickupWallet.TrySpendPowerCells(0) must succeed rather than
            // refuse, or Force Field would stop working entirely at a zero cost.
            PickupWallet.SetPowerCells(0);

            Assert.That(_abilities.TryActivateForceField(), Is.True,
                "a zero activation cost must not need any cells banked to raise the bubble");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(0),
                "activating at a zero cost must leave the (already empty) bank untouched");
        }

        [Test]
        public void ForceFieldReadyIsTrueAtZeroCellsOnceAcquiredAndOffCooldown()
        {
            // AC3: readiness depends only on acquisition and cooldown once the cell gate is moot.
            PickupWallet.SetPowerCells(0);

            Assert.That(_abilities.ForceFieldReady, Is.True,
                "MV-523: at a zero cell cost, readiness must no longer depend on the power-cell bank");
        }

        [Test]
        public void CooldownStillGatesActivationAfterThePopEvenAtZeroCost()
        {
            // AC4 regression: removing the cell cost must not have made the bubble spammable — the
            // cooldown (unchanged, DefaultForceFieldCooldownSeconds) still has to gate re-activation.
            PickupWallet.SetPowerCells(0);
            Assert.That(_abilities.TryActivateForceField(), Is.True, "precondition: the first raise must succeed");

            // PopForceField() calls Destroy() on the bubble, which is fine at runtime but logs an
            // edit-mode-only error here (same idiom MV506's test suite already uses) — expected, not
            // the thing under test.
            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));
            _abilities.AbsorbForceFieldDamage(9999f); // exceeds the absorb cap, popping the bubble now

            Assert.That(_abilities.ForceFieldCooldownRemaining, Is.GreaterThan(0f),
                "the pop must start a real cooldown even though there is no cell cost left to gate it");
            Assert.That(_abilities.TryActivateForceField(), Is.False,
                "a second activation must still be refused while the (cell-free) cooldown is running");
        }
    }
}
