using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-794: retunes <see cref="StormdrainFlood"/> so the FLOOD bar takes most of a run to fill
    /// instead of ~4 s (25 live Replicators at the old hand-set <c>ReplicatorFillBonusPerSecond</c> of
    /// 0.01f), and adds the opening-minute pacing floor (Change 3).
    ///
    /// Fails on the pre-ticket base commit (cdced3e, the tip this ticket branched from): with the old
    /// 0.01f constant, 25 live Replicators fill the bar in ~4 s, so AC1's "must not be full before
    /// 480 s" assertion fails immediately with <c>Level01</c> already clamped at 1.
    ///
    /// One test method, one narrative per AC bullet, same idiom as <c>MV774FloodTests</c> — each phase
    /// starts from a fresh <see cref="StormdrainFlood.Reset"/> so no phase's clock carries into the
    /// next.
    /// </summary>
    public sealed class MV794FloodPacingTests
    {
        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            StormdrainFlood.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            StormdrainFlood.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void StormdrainFlood_PacesTheFloodOverARunInsteadOfSeconds()
        {
            // --- AC1: all 25 Replicators alive must not fill the bar before 480 s (8 min). ---
            StormdrainFlood.Tick(479f, liveReplicators: 25, livePumpHousings: 0);
            Assert.Less(StormdrainFlood.Level01, 1f,
                "25 live Replicators must not fill the bar before 480 s of simulated time");

            // --- AC2: half destroyed (12 alive) must take at least 1.6x as long as with 25 — checked
            // against the same 479 s reference AC1 used as "as long as with 25 takes". ---
            StormdrainFlood.Reset();
            StormdrainFlood.Tick(1.6f * 479f, liveReplicators: 12, livePumpHousings: 0);
            Assert.Less(StormdrainFlood.Level01, 1f,
                "12 live Replicators must still not be full at 1.6x the time 25 alive takes to approach full");

            // --- AC3: with 0 Replicators the flood still strictly increases, and still eventually
            // fills — the base rate never stops, it only slows. ---
            StormdrainFlood.Reset();
            StormdrainFlood.Tick(100f, liveReplicators: 0, livePumpHousings: 0);
            float levelA = StormdrainFlood.Level01;
            StormdrainFlood.Tick(100f, liveReplicators: 0, livePumpHousings: 0);
            float levelB = StormdrainFlood.Level01;
            Assert.Greater(levelB, levelA, "the base rate must keep advancing the bar with no Replicators alive");

            StormdrainFlood.Tick(DifficultyDirector.AuthoredRunLengthSeconds * 2f, liveReplicators: 0, livePumpHousings: 0);
            Assert.AreEqual(1f, StormdrainFlood.Level01, 0.0001f,
                "with enough time the base rate alone must still reach full — the flood never stops, it only slows");

            // --- AC4: whatever is alive, the bar cannot pass 0.15 in the first 60 s. ---
            StormdrainFlood.Reset();
            StormdrainFlood.Tick(60f, liveReplicators: 25, livePumpHousings: 0);
            Assert.LessOrEqual(StormdrainFlood.Level01, 0.15f,
                "the opening-minute floor must cap the bar at 0.15 through the first 60 s, even at 25 live Replicators");

            // --- AC5: above PumpDrainThreshold, live pump housings must measurably slow the bar —
            // proving the pump term (StormdrainDressing.PumpHousingsAlive, wired in by this ticket) is
            // actually reachable rather than permanently multiplied by a hard-coded 0. ---
            StormdrainFlood.Reset();
            StormdrainFlood.Tick(400f, liveReplicators: 25, livePumpHousings: 0); // > 60 s and > PumpDrainThreshold (0.75)
            Assert.GreaterOrEqual(StormdrainFlood.Level01, StormdrainFlood.PumpDrainThreshold,
                "test setup must land above PumpDrainThreshold before comparing the pump term");
            float levelBeforeNoPumps = StormdrainFlood.Level01;
            StormdrainFlood.Tick(1f, liveReplicators: 25, livePumpHousings: 0);
            float deltaNoPumps = StormdrainFlood.Level01 - levelBeforeNoPumps;

            StormdrainFlood.Reset();
            StormdrainFlood.Tick(400f, liveReplicators: 25, livePumpHousings: 0);
            float levelBeforeWithPumps = StormdrainFlood.Level01;
            StormdrainFlood.Tick(1f, liveReplicators: 25, livePumpHousings: 4);
            float deltaWithPumps = StormdrainFlood.Level01 - levelBeforeWithPumps;

            Assert.Less(deltaWithPumps, deltaNoPumps,
                "4 live pump housings must raise the bar strictly less over 1 s than 0 pump housings do");
        }
    }
}
