using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>Unit tests for the Backyard greybox path layout (YT-38): a coherent, traversable
    /// corridor that gates into a wider boss arena.</summary>
    public sealed class BackyardPathTests
    {
        [Test]
        public void Default_IsAValidTraversablePath()
        {
            var l = BackyardPathLayout.Default;
            Assert.IsTrue(l.IsValid());
            Assert.Greater(l.CorridorLength, 0f);
            Assert.Greater(l.ArenaLength, 0f);
        }

        [Test]
        public void Default_LaneIsWideEnoughForPlayerAndEnemies()
        {
            var l = BackyardPathLayout.Default;
            Assert.GreaterOrEqual(l.LaneWidth, 3f); // Max (~1 m) plus room to dodge
        }

        [Test]
        public void Default_GateSitsInsideTheCorridor()
        {
            var l = BackyardPathLayout.Default;
            Assert.Greater(l.GateZ, l.StartZ);
            Assert.Less(l.GateZ, l.ArenaEndZ);
        }

        [Test]
        public void Default_ArenaIsBeyondTheGateAndOpensOut()
        {
            var l = BackyardPathLayout.Default;
            Assert.Greater(l.ArenaEndZ, l.GateZ);          // arena is past the gate
            Assert.Greater(l.ArenaHalfWidth, l.LaneHalfWidth); // and wider than the corridor
        }

        [Test]
        public void GateSealWidth_CoversTheLanePlusWallThickness()
        {
            var l = BackyardPathLayout.Default;
            Assert.AreEqual(l.LaneWidth + l.WallThickness * 2f, l.GateSealWidth, 1e-4);
            Assert.Greater(l.GateSealWidth, l.LaneWidth); // no gap to slip past
        }

        [Test]
        public void ArenaCenter_IsBetweenGateAndBackWall()
        {
            var l = BackyardPathLayout.Default;
            Assert.AreEqual((l.GateZ + l.ArenaEndZ) * 0.5f, l.ArenaCenter.z, 1e-4);
        }

        [Test]
        public void IsValid_RejectsBrokenLayouts()
        {
            var tooNarrow = BackyardPathLayout.Default; tooNarrow.LaneHalfWidth = 0.5f; // 1 m lane
            Assert.IsFalse(tooNarrow.IsValid());

            var gatePastArena = BackyardPathLayout.Default; gatePastArena.GateZ = 100f;
            Assert.IsFalse(gatePastArena.IsValid());

            var notOpening = BackyardPathLayout.Default; notOpening.ArenaHalfWidth = 2f; // narrower than lane
            Assert.IsFalse(notOpening.IsValid());
        }
    }
}
