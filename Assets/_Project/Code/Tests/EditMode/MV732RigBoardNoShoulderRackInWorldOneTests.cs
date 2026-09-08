using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-732 — World 1's rig_board.json must not carry the Shoulder Rack (<c>s_rkt</c>/<c>s_sal</c>/
    /// <c>s_rld</c>); those nodes belong only to <c>rig_board.world2.json</c>, reached once the Weapon
    /// Core swaps the active board. Sole guard on the fix; do not cull.
    ///
    /// Proven to fail on 943ba04 (pre-fix): MV-694 had added <c>s_rkt</c>/<c>s_sal</c>/<c>s_rld</c> to
    /// <c>rig_board.json</c> itself, so World 1's resolved SECONDARY set was
    /// <c>{s_bal, s_spl, s_lob, s_aut, s_rte, s_rkt, s_sal, s_rld}</c> — the first assertion below fails
    /// with those three extra ids present.
    /// </summary>
    public sealed class MV732RigBoardNoShoulderRackInWorldOneTests
    {
        [TearDown]
        public void TearDown() => RigBoard.ResetForTests();

        [Test]
        public void World1SecondaryHasNoShoulderRack_World2StillDoes()
        {
            RigBoard.ResetForTests(); // World 1 (index 0)

            Assert.That(SecondaryIds(), Is.EquivalentTo(new[] { "s_bal", "s_spl", "s_lob", "s_aut", "s_rte" }),
                "World 1's SECONDARY family must be exactly the Water Balloon tree — no s_rkt/s_sal/s_rld");

            RigBoard.UseWorld(1); // World 2

            Assert.That(SecondaryIds(), Does.Contain("s_rkt"),
                "World 2's SECONDARY family must still carry the Shoulder Rack's root, s_rkt");
        }

        private static IEnumerable<string> SecondaryIds() =>
            RigBoard.AllIds.Where(id => RigBoard.Category(id) == "SECONDARY");
    }
}
