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
    /// assertion below fails with "Expected: greater than or equal to 690 ... But was: 348".
    ///
    /// MV-844 (Lee: "make damage 8 levels") raised World 2's <c>p_dmg</c> from a 4- to an 8-level cap,
    /// keeping the same 50-cells-a-level cost this ticket's own table already charges it (ticket's own
    /// "existing formula... Keep") -- 4 extra levels at 50 cells each add exactly 200 to the total this
    /// test measures (711 -&gt; 911), so the asserted range widens by that same 200 rather than the old
    /// 690-730 band.
    ///
    /// MV-846 adds World 2's own new <c>p_cap</c> node (5 levels, standard unlock/upgrade costs, no
    /// per-node override) -- its unlock plus 4 upgrades scaled by the same 2.5x World2PrimarySecondary
    /// multiplier add ~150 cells (911 -&gt; 1061), so the range widens by that amount again.
    ///
    /// MV-947 (Lee: give World 1 more upgrade headroom) raised World 1's own <c>p_dmg</c> cap 4 -&gt; 7,
    /// same 20-cells-a-level flat rate past level 4 this ticket's own table already charges it -- 3
    /// extra levels at 20 cells each add exactly 60 to World 1's own total this test measures
    /// (365 -&gt; 425).
    ///
    /// MV-949 (Lee: an upgrade cost must never drop below the unlock cost) shifted
    /// <see cref="CellSpend.UpgradeCostFor(int)"/>'s ladder from 5/10/15/20 to 10/15/20/20 (capped at
    /// level 4, read off <c>level + 1</c>) — every node's own total rises by 5 cells per escalating
    /// upgrade level (levels 1-3 of its own climb), scaled by whatever multiplier already applied to
    /// it: World 1's total rises 425 -&gt; 515 unscaled, World 2's rises 1061 -&gt; 1329 through the same
    /// 2.5x PRIMARY/SECONDARY multiplier this file's own table already charges.</summary>
    public sealed class MV767WorldTwoEconomyTests
    {
        [TearDown]
        public void TearDown() => RigBoard.ResetForTests();

        [Test]
        public void World2Economy_MatchesTheMV767Rebalance()
        {
            int world2Total = TotalPrimarySecondaryCost(worldIndex: 1);
            Assert.That(world2Total, Is.InRange(1309, 1349),
                $"World 2's PRIMARY+SECONDARY total must land near parity with its (reduced) Parts " +
                $"supply after MV-767's 2.5x multiplier, MV-844's own +200, MV-846's own new p_cap node " +
                $"(~150 more) and MV-949's own ladder shift (+268 through the 2.5x multiplier), got {world2Total}");

            int world1Total = TotalPrimarySecondaryCost(worldIndex: 0);
            Assert.That(world1Total, Is.EqualTo(515),
                "World 1's PRIMARY+SECONDARY total must reflect MV-947's own +60 (3 more p_dmg levels at 20 cells each) " +
                "and MV-949's own +90 (the 5/10/15/20 -> 10/15/20/20 ladder shift)");

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
