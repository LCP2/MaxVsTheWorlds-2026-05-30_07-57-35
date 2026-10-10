using NUnit.Framework;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1155 (the one new test): Lee, 10 Oct, playing World 1: "the Big Bermuda on World 1 is
    /// producing robots too quickly." World 1 alone slows from 3.5s to 4.5s between volleys
    /// (<see cref="MaxWorlds.Arena.WorldDefinition.BossVolleyIntervalSeconds"/>); World 2/3's Big
    /// Bermuda stays at <see cref="BossTuning.VolleyInterval"/>'s own 3.5s, which Lee did not ask to
    /// change. Asserts the RESOLVED interval <see cref="BroodVolley.ResolvedIntervalSeconds"/>
    /// returns — not an authored constant re-typed against itself.
    ///
    /// Fails on base commit 3e2fa9a (before this ticket): <c>BroodVolley</c> carries no
    /// <c>ResolvedIntervalSeconds</c> seam and reads only the single, world-blind
    /// <see cref="BossTuning.VolleyInterval"/>, so this does not compile against that commit.
    /// </summary>
    public sealed class MV1155BossVolleyWorldIntervalTests
    {
        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RigBoard.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            DevTuning.Reset();
            RigBoard.ResetForTests();
        }

        [Test]
        public void VolleyInterval_IsSlowerOnWorldOneOnly_AndOverrideStillWinsOnBoth()
        {
            RigBoard.UseWorld(0); // World 1
            Assert.AreEqual(4.5f, BroodVolley.ResolvedIntervalSeconds(enraged: false), 1e-4f,
                "World 1's calm volley interval must be slowed to 4.5s");
            Assert.AreEqual(2.7f, BroodVolley.ResolvedIntervalSeconds(enraged: true), 1e-4f,
                "World 1's enraged volley interval must scale off the slowed 4.5s base");

            RigBoard.UseWorld(2); // World 3
            Assert.AreEqual(3.5f, BroodVolley.ResolvedIntervalSeconds(enraged: false), 1e-4f,
                "World 3's calm volley interval must stay at the unslowed default");
            Assert.AreEqual(2.1f, BroodVolley.ResolvedIntervalSeconds(enraged: true), 1e-4f,
                "World 3's enraged volley interval must stay at the unslowed default");

            DevTuning.BossVolleyInterval = 6f;
            Assert.AreEqual(6f, BroodVolley.ResolvedIntervalSeconds(enraged: false), 1e-4f,
                "the Settings panel override must still win over World 3's own per-world value");
            RigBoard.UseWorld(0);
            Assert.AreEqual(6f, BroodVolley.ResolvedIntervalSeconds(enraged: false), 1e-4f,
                "the Settings panel override must still win over World 1's own per-world value");
        }
    }
}
