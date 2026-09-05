using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-673: Water Balloon and Sentinel deploy must spend the Power Cells secondary bank
    /// (<see cref="PickupWallet.PowerCellsSecondary"/>, MV-672) rather than the everyday Parts
    /// balance (<see cref="PickupWallet.PowerCells"/>) they read before this ticket.
    ///
    /// Proven to fail on the pre-fix commit — <c>PlayerAbilities.TryThrowWaterBalloon</c> called
    /// <c>PickupWallet.TrySpendPowerCell()</c> and <c>TryDeploySentinel</c> called
    /// <c>PickupWallet.TrySpendPowerCells(SentinelCost)</c>, both reading <c>PowerCells</c> (Parts):
    /// with 50 Parts and 0 Power Cells, both calls returned True instead of the expected False; with
    /// 0 Parts and 50 Power Cells, both calls returned False instead of the expected True.
    ///
    /// Force Field (<see cref="PlayerAbilities.TryActivateForceField"/>) is explicitly out of scope
    /// for this ticket and still spends Parts unchanged — not covered here;
    /// <c>MV523ForceFieldFreeActivationTests</c> already exercises it.
    /// </summary>
    public sealed class MV673PowerCellSpendTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();   // also resets RigState
            WeaponSystemState.Reset();
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
            Sentinel.DestroyAllActive();
        }

        [Test]
        public void WaterBalloonAndSentinelSpendPowerCellsSecondaryNotParts()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);
            WeaponSystemState.Acquire(AbilityKind.Sentinels);

            var maxGo = new GameObject("Max");
            var abilities = maxGo.AddComponent<PlayerAbilities>();
            try
            {
                // ---------------- 0 Power Cells, sufficient Parts: both must fail ----------------
                PickupWallet.SetPowerCells(50);
                PickupWallet.SetPowerCellSecondary(0);

                Assert.That(abilities.TryThrowWaterBalloon(Vector3.forward), Is.False,
                    "0 Power Cells must refuse the throw even with Parts to spare");
                Assert.That(abilities.TryDeploySentinel(new Vector3(5f, 0f, 0f)), Is.False,
                    "0 Power Cells must refuse the deploy even with Parts to spare");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(0), "the refused deploy must not place a sentinel");

                // -------------- sufficient Power Cells, 0 Parts: both must succeed ---------------
                PickupWallet.SetPowerCells(0);
                PickupWallet.SetPowerCellSecondary(50);

                Assert.That(abilities.TryThrowWaterBalloon(Vector3.forward), Is.True,
                    "sufficient Power Cells must afford the throw even with 0 Parts");
                Assert.That(abilities.TryDeploySentinel(new Vector3(5f, 0f, 0f)), Is.True,
                    "sufficient Power Cells must afford the deploy even with 0 Parts");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(1));
            }
            finally
            {
                Sentinel.DestroyAllActive();
                Object.DestroyImmediate(maxGo);
            }
        }
    }
}
