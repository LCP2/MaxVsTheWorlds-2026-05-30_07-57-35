using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-673: Water Balloon must spend the Power Cells secondary bank
    /// (<see cref="PickupWallet.PowerCellsSecondary"/>, MV-672) rather than the everyday Parts
    /// balance (<see cref="PickupWallet.PowerCells"/>) it read before that ticket.
    ///
    /// Sentinel deploy was originally covered here too, on the same Power Cells secondary bank —
    /// MV-1117 (6 Oct 2026) moved it BACK onto Parts (Lee's device playtest: Magneto auto-drained
    /// every secondary cell before a deploy ever got one), which would have made this test assert
    /// the opposite of the now-correct behaviour. That half is culled; the Sentinel-spends-Parts
    /// coverage now lives in <c>MV1117SentinelPartsCostAndPriceLineTests</c> instead.
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
        public void WaterBalloonSpendsPowerCellsSecondaryNotParts()
        {
            WeaponSystemState.Acquire(AbilityKind.WaterBalloon);

            var maxGo = new GameObject("Max");
            var abilities = maxGo.AddComponent<PlayerAbilities>();
            try
            {
                // ---------------- 0 Power Cells, sufficient Parts: must fail ----------------
                PickupWallet.SetPowerCells(50);
                PickupWallet.SetPowerCellSecondary(0);

                Assert.That(abilities.TryThrowWaterBalloon(Vector3.forward), Is.False,
                    "0 Power Cells must refuse the throw even with Parts to spare");

                // -------------- sufficient Power Cells, 0 Parts: must succeed ---------------
                PickupWallet.SetPowerCells(0);
                PickupWallet.SetPowerCellSecondary(50);

                Assert.That(abilities.TryThrowWaterBalloon(Vector3.forward), Is.True,
                    "sufficient Power Cells must afford the throw even with 0 Parts");
            }
            finally
            {
                Sentinel.DestroyAllActive();
                Object.DestroyImmediate(maxGo);
            }
        }
    }
}
