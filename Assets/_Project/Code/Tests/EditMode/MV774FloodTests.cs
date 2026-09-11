using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-774: the World 2 FLOOD bar made real. Fails on the pre-ticket commit (a5e7992, the tip this
    /// ticket branched from) — at that commit <c>StormdrainFlood</c>/<c>FloodDamageTicker</c> do not
    /// exist, so this file fails to COMPILE (CS0246) before a single assertion runs, the same
    /// "compile failure is the proof" shape <c>MV696SludgequeenFloodTests</c>'s own doc comment uses.
    ///
    /// One test method, one narrative, same idiom as <c>MV696SludgequeenFloodTests</c>: each of the
    /// ticket's own AC bullets is its own phase, with <see cref="StormdrainFlood.Reset"/> between
    /// phases that need fresh, independent state (the Replicator-rate comparison and the warning-timing
    /// check must each start from a clean clock, not carry over drift from the banding phase before
    /// them).
    /// </summary>
    public sealed class MV774FloodTests
    {
        private sealed class FakeReceiver : IDamageable
        {
            public float TotalDamageTaken;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) => TotalDamageTaken += info.Amount;
        }

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

        /// <summary>Advances the flood, from wherever it currently sits, by exactly enough to land at
        /// <paramref name="target"/> — computed off the same public rate constants
        /// <see cref="StormdrainFlood.Tick"/> itself uses with zero Replicators/pump housings, so the
        /// test never has to guess or duplicate the "Rate" section's own formula.</summary>
        private static void DriveTo(float target)
        {
            float rate = StormdrainFlood.UnhurriedFillAtRunLength / DifficultyDirector.RunLengthSeconds;
            float remaining = target - StormdrainFlood.Level01;
            if (remaining <= 0f) return;
            StormdrainFlood.Tick(remaining / rate, liveReplicators: 0, livePumpHousings: 0);
        }

        [Test]
        public void StormdrainFlood_MakesTheFloodBarReal()
        {
            // --- AC1: driving to 26% floods only the lowest band; driving to 51% floods both. ---
            // 9 authored combat areas: 1-3 the upper route (never floods this slice), 4-6 the middle
            // third (band 2), 7-9 the last third, closest to the Wet Well (band 1).
            StormdrainFlood.Configure(9);

            DriveTo(0.26f);
            StormdrainFlood.Tick(StormdrainFlood.WarningSeconds + 0.1f, 0, 0); // let band 1's warning elapse

            Assert.IsTrue(StormdrainFlood.IsAreaIndexFlooded(9), "band 1 (area 9) must be flooded at 26%");
            Assert.IsFalse(StormdrainFlood.IsAreaIndexFlooded(5), "band 2 (area 5) must still be dry at 26%");
            Assert.IsFalse(StormdrainFlood.IsAreaIndexFlooded(2), "the upper route (area 2) must be dry at 26%");

            DriveTo(0.51f);
            StormdrainFlood.Tick(StormdrainFlood.WarningSeconds + 0.1f, 0, 0); // let band 2's warning elapse

            Assert.IsTrue(StormdrainFlood.IsAreaIndexFlooded(9), "band 1 (area 9) must still be flooded at 51%");
            Assert.IsTrue(StormdrainFlood.IsAreaIndexFlooded(5), "band 2 (area 5) must now be flooded at 51%");
            Assert.IsFalse(StormdrainFlood.IsAreaIndexFlooded(2), "the upper route (area 2) must never flood this slice");

            // --- AC2: an IDamageable standing in flooded ground loses health at the authored rate over
            // a simulated 2 s; one on dry ground loses none. Driven directly off FloodDamageTicker —
            // independent of the global flood level above, same "test the extracted, testable piece"
            // idiom as the rest of this codebase's own hazard tests. ---
            var flooded = new FakeReceiver();
            var dry = new FakeReceiver();
            var floodedTicker = new FloodDamageTicker();
            var dryTicker = new FloodDamageTicker();

            floodedTicker.Tick(2f, isFlooded: true, flooded, Vector3.zero);
            dryTicker.Tick(2f, isFlooded: false, dry, Vector3.zero);

            Assert.AreEqual(StormdrainFlood.DamagePerSecond * 2f, flooded.TotalDamageTaken, 0.001f,
                "2 s standing in flooded ground must cost exactly the authored per-second rate x 2");
            Assert.AreEqual(0f, dry.TotalDamageTaken, 0.001f, "dry ground must never apply flood damage");

            // --- AC3: with N live Replicators the fill rate is strictly greater than with N-1. ---
            StormdrainFlood.Reset();
            StormdrainFlood.Configure(1);
            StormdrainFlood.Tick(1f, liveReplicators: 2, livePumpHousings: 0);
            float levelWithTwoReplicators = StormdrainFlood.Level01;

            StormdrainFlood.Reset();
            StormdrainFlood.Configure(1);
            StormdrainFlood.Tick(1f, liveReplicators: 1, livePumpHousings: 0);
            float levelWithOneReplicator = StormdrainFlood.Level01;

            Assert.Greater(levelWithTwoReplicators, levelWithOneReplicator,
                "the fill rate with 2 live Replicators must be strictly greater than with 1 — the counter must be connected");

            // --- AC4: a surge raises its warning at least 3 s of scaled time before the ground becomes
            // harmful. ---
            StormdrainFlood.Reset();
            StormdrainFlood.Configure(9);
            DriveTo(0.26f);

            Assert.IsTrue(StormdrainFlood.Band1Surging, "must be surging the instant the threshold is crossed");
            Assert.IsFalse(StormdrainFlood.Band1Flooded, "must not be harmful yet — the 3 s warning has not elapsed");

            StormdrainFlood.Tick(StormdrainFlood.WarningSeconds - 0.1f, 0, 0);
            Assert.IsFalse(StormdrainFlood.Band1Flooded, "must still be inside the 3 s warning window");

            StormdrainFlood.Tick(0.2f, 0, 0); // crosses the 3 s mark
            Assert.IsTrue(StormdrainFlood.Band1Flooded, "must be harmful once the full 3 s warning has elapsed");

            // --- AC5: the flood does not advance while time scale is 0 (dt already scaled to 0). ---
            StormdrainFlood.Reset();
            StormdrainFlood.Configure(9);
            StormdrainFlood.Tick(0f, liveReplicators: 5, livePumpHousings: 0);
            Assert.AreEqual(0f, StormdrainFlood.Level01, "a zero, already-scaled dt must never advance the flood");
        }
    }
}
