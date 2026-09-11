using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-767 — World 2's Parts economy was running a 2.33x oversupply (828 supplied against 355
    /// needed to max PRIMARY+SECONDARY) while Shoulder Rack ammo dropped at 40% of the Parts rate.
    /// This asserts the three resolved effects of the fix: the PRIMARY+SECONDARY weapon-board total
    /// lands near parity with the (now-reduced) Parts supply, World 1's own board is untouched, and
    /// the per-area Supercell — the largest single lever on that supply — now fires every second
    /// area in World 2+ only, still every area in World 1.
    ///
    /// Proven to fail on 1536764a483e70256b8d59e84ccf105e3451a9e0 (pre-fix): World 2's PRIMARY+SECONDARY
    /// total there is 348 cells (<see cref="CellSpend"/>'s multiplier was still 1.25x), so the first
    /// assertion below (690-730) fails with "Expected: greater than or equal to 690 ... But was: 348".
    /// </summary>
    public sealed class MV767WorldTwoEconomyTests
    {
        [TearDown]
        public void TearDown() => RigBoard.ResetForTests();

        [Test]
        public void World2Economy_MatchesTheMV767Rebalance()
        {
            int world2Total = TotalPrimarySecondaryCost(worldIndex: 1);
            Assert.That(world2Total, Is.InRange(690, 730),
                $"World 2's PRIMARY+SECONDARY total must land near parity with its (reduced) Parts " +
                $"supply after MV-767's 2.5x multiplier, got {world2Total}");

            int world1Total = TotalPrimarySecondaryCost(worldIndex: 0);
            Assert.That(world1Total, Is.EqualTo(365),
                "World 1's PRIMARY+SECONDARY total is not in question here and must be unchanged");

            Assert.That(CellEconomyTuning.DefaultPowerCellDropRatio, Is.EqualTo(1.0f),
                "Rack ammo must drop at parity with Parts, not 40% of it");

            Assert.That(Resources.Load<TextAsset>(RigBoardLibrary.World1ResourcePath).text,
                Does.Not.Contain("PART STORAGE"), "e_cel's label must read PART CAPACITY, not STORAGE");
            Assert.That(Resources.Load<TextAsset>(RigBoardLibrary.World2ResourcePath).text,
                Does.Not.Contain("PART STORAGE"), "e_cel's label must read PART CAPACITY, not STORAGE");

            RigBoard.UseWorld(1);
            Assert.That(GrantsSupercellForArea(1), Is.True, "World 2 area 1 must still grant a Supercell");
            Assert.That(GrantsSupercellForArea(2), Is.False, "World 2 area 2 must NOT grant a Supercell");
            Assert.That(GrantsSupercellForArea(3), Is.True, "World 2 area 3 must grant a Supercell");

            RigBoard.ResetForTests();
            Assert.That(GrantsSupercellForArea(1), Is.True, "World 1 area 1 must grant a Supercell");
            Assert.That(GrantsSupercellForArea(2), Is.True, "World 1 area 2 must also grant a Supercell");
        }

        private static int TotalPrimarySecondaryCost(int worldIndex)
        {
            RigBoard.UseWorld(worldIndex);
            int total = 0;
            foreach (string id in RigBoard.AllIds)
            {
                string category = RigBoard.Category(id);
                if (category != "PRIMARY" && category != "SECONDARY") continue;

                int level = RigBoard.StartLevel(id);
                int maxLevel = RigBoard.MaxLevel(id);
                if (level == 0)
                {
                    total += CellSpend.UnlockCostFor(id);
                    level = 1;
                }
                while (level < maxLevel)
                {
                    total += CellSpend.UpgradeCostFor(id, level);
                    level++;
                }
            }
            return total;
        }

        private static bool GrantsSupercellForArea(int areaIndex) =>
            (bool)typeof(PickupDirector)
                .GetMethod("GrantsSupercellForArea", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { areaIndex });
    }
}
