using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1157, Lee: "apply that part of the rig from world two to world one" — World 2's Cell Magneto
    /// (MV-848's <c>e_cmg</c>) is copied onto World 1's own board (<c>rig_board.json</c>) so it can be
    /// unlocked and leveled before the player ever reaches World 2. Asserts RESOLVED
    /// <see cref="RigBoard"/>/<see cref="RigState"/> values (MV-465 Tier 2): <c>e_cmg</c> exists on
    /// World 1's board as an ENERGY node under <c>e_mag</c> (PART MAGNETO), is ungrantable while
    /// <c>e_mag</c> sits at level 0, becomes grantable the moment <c>e_mag</c> reaches level 1, and a
    /// level bought on World 1 survives the World 1 -&gt; World 2 Weapon Core morph (ENERGY carries
    /// across unchanged, <see cref="WeaponSystemState.ApplyWeaponCoreMorph"/>).
    ///
    /// Fails on base commit d2edb53 (pre-fix): <c>RigBoard.Exists("e_cmg")</c> is false on World 1's
    /// board — <c>e_cmg</c> is authored only on <c>rig_board.world2.json</c> there, so the first
    /// assertion below fails with "False" where "True" is expected.
    /// </summary>
    public sealed class MV1157CellMagnetoWorld1Tests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
        }

        [Test]
        public void ECmgExistsUnderPartMagnetoOnWorldOne_AndItsLevelSurvivesTheWorld2Morph()
        {
            Assert.That(RigBoard.Exists("e_cmg"), Is.True, "e_cmg must now be authored on World 1's own board (rig_board.json)");
            Assert.That(RigBoard.Category("e_cmg"), Is.EqualTo("ENERGY"));
            Assert.That(RigBoard.Parent("e_cmg"), Is.EqualTo("e_mag"));
            Assert.That(RigBoard.MaxLevel("e_cmg"), Is.EqualTo(5));

            RigState.UnlockCategory("ENERGY");
            Assert.That(RigState.AcquireCap("e_cel"), Is.True, "sanity: e_cel must be acquirable once ENERGY is unlocked");

            Assert.That(RigState.AcquireCap("e_cmg"), Is.False, "e_cmg must not be buyable while e_mag (its parent) is still level 0");

            Assert.That(RigState.AcquireCap("e_mag"), Is.True, "sanity: e_mag must be acquirable once e_cel is owned");
            Assert.That(RigState.AcquireCap("e_cmg"), Is.True, "e_cmg must become buyable the moment e_mag reaches level 1");
            Assert.That(RigState.RaiseLevel("e_cmg"), Is.True);
            Assert.That(RigState.Level("e_cmg"), Is.EqualTo(2), "sanity: e_cmg bought to level 2 on World 1");

            WeaponSystemState.ApplyWeaponCoreMorph(1);

            Assert.That(RigState.Level("e_cmg"), Is.EqualTo(2),
                "a Cell Magneto level bought in World 1 must carry across the Weapon Core morph into World 2 (ENERGY is preserved wholesale)");
        }
    }
}
