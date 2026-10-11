using NUnit.Framework;
using MaxWorlds.Bosses;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1155 (the one new test): Lee, 10 Oct, playing World 1: "the Big Bermuda on World 1 is
    /// producing robots too quickly." World 1 alone slows from 3.5s to 4.5s between volleys
    /// (<see cref="MaxWorlds.Arena.WorldDefinition.BossVolleyIntervalSeconds"/>); World 3's Big Bermuda
    /// stays at <see cref="BossTuning.VolleyInterval"/>'s own 3.5s, which Lee did not ask to change.
    /// Asserts the RESOLVED interval <see cref="BroodVolley.ResolvedIntervalSeconds"/> returns for the
    /// PLAYED world index passed in — not an authored constant re-typed against itself, and not routed
    /// through <c>RigBoard.ActiveWorldIndex</c>, which a finale's Weapon Core morph can already have
    /// advanced onto the next world before the fight itself ends (MV-1078).
    ///
    /// Fails on base commit 66aa662 (before this ticket): <c>BroodVolley</c> carries no
    /// <c>ResolvedIntervalSeconds</c> seam and <c>Interval</c> takes no world index, reading only the
    /// single, world-blind <see cref="BossTuning.VolleyInterval"/> — this does not compile against that
    /// commit.
    /// </summary>
    public sealed class MV1155BossVolleyWorldIntervalTests
    {
        [SetUp]
        public void SetUp() => DevTuning.Reset();

        [TearDown]
        public void TearDown() => DevTuning.Reset();

        [Test]
        public void VolleyInterval_IsSlowerOnWorldOneOnly_AndOverrideStillWinsOnBoth()
        {
            const int world1 = 0, world3 = 2;

            Assert.AreEqual(4.5f, BroodVolley.ResolvedIntervalSeconds(enraged: false, playedWorldIndex: world1), 1e-4f,
                "World 1's calm volley interval must be slowed to 4.5s");
            Assert.AreEqual(2.7f, BroodVolley.ResolvedIntervalSeconds(enraged: true, playedWorldIndex: world1), 1e-4f,
                "World 1's enraged volley interval must scale off the slowed 4.5s base");

            Assert.AreEqual(3.5f, BroodVolley.ResolvedIntervalSeconds(enraged: false, playedWorldIndex: world3), 1e-4f,
                "World 3's calm volley interval must stay at the unslowed default");
            Assert.AreEqual(2.1f, BroodVolley.ResolvedIntervalSeconds(enraged: true, playedWorldIndex: world3), 1e-4f,
                "World 3's enraged volley interval must stay at the unslowed default");

            DevTuning.BossVolleyInterval = 6f;
            Assert.AreEqual(6f, BroodVolley.ResolvedIntervalSeconds(enraged: false, playedWorldIndex: world3), 1e-4f,
                "the Settings panel override must still win over World 3's own per-world value");
            Assert.AreEqual(6f, BroodVolley.ResolvedIntervalSeconds(enraged: false, playedWorldIndex: world1), 1e-4f,
                "the Settings panel override must still win over World 1's own per-world value");
        }
    }
}
