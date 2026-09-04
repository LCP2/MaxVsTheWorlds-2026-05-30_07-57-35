using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-665: destroying sheds is no longer a prerequisite for opening any gate. Loads the SHIPPED
    /// world1 config through the real loader and asserts it validates while no gate's RESOLVED
    /// <c>opensWith</c> carries a shed condition — the boss areas a12, a20 and a30 must each be
    /// reachable through a gate whose opensWith is 'primary', not 'sheds-destroyed-before' or
    /// 'all-sheds-destroyed'.
    /// </summary>
    public sealed class MV665NoShedGatesTests
    {
        [Test]
        public void World1_ValidatesWithNoGateCarryingAShedCondition()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "world1 config failed to load");

            bool ok = MapValidation.ValidateWorldConfig(cfg, out string reason);
            Assert.IsTrue(ok, reason);

            foreach (WorldGate g in cfg.gates)
            {
                Assert.AreNotEqual("sheds-destroyed-before", g.opensWith, $"gate '{g.id}' still carries a shed condition");
                Assert.AreNotEqual("all-sheds-destroyed", g.opensWith, $"gate '{g.id}' still carries a shed condition");
            }
        }
    }
}
