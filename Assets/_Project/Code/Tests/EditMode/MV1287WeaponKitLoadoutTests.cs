using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1287 (the one new test, per CC_AUTONOMY's testing policy): a loadout-composed RIG board
    /// (<see cref="RigBoard.UseLoadout"/>, reached through <see cref="WeaponSystemState.ApplyLoadout"/>)
    /// builds PRIMARY from the equipped primary's own HOME board and SECONDARY from the equipped
    /// secondary's own HOME board, regardless of which world is doing the equipping -- so a weapon kit
    /// carries its own upgrade nodes into any world instead of silently reading whichever weapon that
    /// world's own file happens to define under the same ids.
    ///
    /// AC1 part 1: <c>ApplyLoadout(cityWorldIndex, Lppe, ShoulderRack)</c> must carry LPPE's own
    /// <c>p_frk</c>/<c>p_rng</c> (with World 2's own maxLevels) onto the City's board, and must NOT
    /// carry RICOCHET's <c>p_ric</c>/<c>p_aim</c> -- the City's own native PRIMARY family, which the
    /// pre-ticket <c>RigBoard.UseWorld(cityWorldIndex)</c> path would leave untouched instead.
    ///
    /// AC1 part 2: <c>ApplyLoadout(cityWorldIndex, Ricochet, ShoulderRack)</c> -- the City's own native
    /// pair -- must still resolve to exactly the City's own board (same ids, same max levels) as a plain
    /// <c>RigBoard.UseWorld(cityWorldIndex)</c> read.
    ///
    /// Fails on base commit c49f00b (before this ticket): neither <c>RigBoard.UseLoadout</c> nor
    /// <c>WeaponSystemState.ApplyLoadout</c> exist there, so this does not compile against that commit.
    /// </summary>
    public sealed class MV1287WeaponKitLoadoutTests
    {
        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
        }

        [Test]
        public void ApplyLoadout_ComposesPrimaryAndSecondaryFromTheirOwnHomeBoards()
        {
            int cityWorldIndex = WorldCatalog.IndexOf(WorldIds.City);
            int stormdrainIndex = WorldCatalog.IndexOf(WorldIds.Stormdrain);

            // World 2's own LPPE maxLevels, read directly off its own board -- the expected values the
            // "Lppe loadout on the City" assertions below must match.
            RigBoard.UseWorld(stormdrainIndex);
            int lppePFrkMax = RigBoard.MaxLevel("p_frk");
            int lppePRngMax = RigBoard.MaxLevel("p_rng");
            RigBoard.ResetForTests();

            // --- part 1: a non-native pairing carries LPPE's own tracks onto the City's board, not the
            // City's own native RICOCHET family.
            WeaponSystemState.ApplyLoadout(cityWorldIndex, WeaponCatalog.PrimaryKind.Lppe, SecondaryKind.ShoulderRack);

            Assert.That(RigBoard.AllIds, Does.Contain("p_frk"),
                "LPPE's own ARC node must carry onto the City's board under this loadout");
            Assert.That(RigBoard.AllIds, Does.Contain("p_rng"),
                "LPPE's own RANGE node must carry onto the City's board under this loadout");
            Assert.AreEqual(lppePFrkMax, RigBoard.MaxLevel("p_frk"),
                "p_frk's maxLevel must be LPPE's own (World 2's board), not re-derived");
            Assert.AreEqual(lppePRngMax, RigBoard.MaxLevel("p_rng"),
                "p_rng's maxLevel must be LPPE's own (World 2's board), not re-derived");
            Assert.That(RigBoard.AllIds, Does.Not.Contain("p_ric"),
                "RICOCHET's own RICOCHETS node must not leak onto an LPPE loadout");
            Assert.That(RigBoard.AllIds, Does.Not.Contain("p_aim"),
                "RICOCHET's own AIMING node must not leak onto an LPPE loadout");

            // --- part 2: the City's own native pairing is untouched -- identical ids/maxLevels to a
            // plain RigBoard.UseWorld(cityWorldIndex) read.
            RigBoard.ResetForTests();
            RigBoard.UseWorld(cityWorldIndex);
            string[] nativeIds = RigBoard.AllIds.OrderBy(id => id).ToArray();
            var nativeMaxLevels = nativeIds.ToDictionary(id => id, RigBoard.MaxLevel);

            RigBoard.ResetForTests();
            WeaponSystemState.ApplyLoadout(cityWorldIndex, WeaponCatalog.PrimaryKind.Ricochet, SecondaryKind.ShoulderRack);
            string[] loadoutIds = RigBoard.AllIds.OrderBy(id => id).ToArray();

            CollectionAssert.AreEqual(nativeIds, loadoutIds,
                "the City's own native PRIMARY/SECONDARY pair must resolve to exactly the same id set as RigBoard.UseWorld");
            foreach (string id in nativeIds)
                Assert.AreEqual(nativeMaxLevels[id], RigBoard.MaxLevel(id),
                    $"{id}: maxLevel must be unchanged between the native UseWorld path and the native-pair loadout");
        }
    }
}
