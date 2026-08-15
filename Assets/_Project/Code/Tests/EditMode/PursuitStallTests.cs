using NUnit.Framework;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The lose-sight → path-to-last-seen → give-up transition (MV-387). A robot chasing at close
    /// range whose target ducks behind nearby cover used to read "arrived" on the very first hunting
    /// tick — since the last-known spot is exactly where the chase was standing a frame ago — and
    /// spin straight into Search with no visible pursuit. <see cref="PursuitStall"/> is the pulled-out,
    /// pure decision this regresses against directly, the same way <see cref="Perception"/> is
    /// covered by SightlineTests.
    /// </summary>
    public sealed class PursuitStallTests
    {
        private const float ArriveRadius = 1.2f;
        private const float SearchTime = 2.5f;
        private const float MinHuntTime = 0.6f;

        [Test]
        public void AlreadyAtTheSpotWhenSightBreaks_DoesNotGiveUpImmediately()
        {
            // The MV-387 repro: sight breaks while already standing on the last-known spot (dist ~ 0).
            var stall = new PursuitStall();

            bool gaveUp = stall.TickHunting(0.05f, 0.016f, ArriveRadius, SearchTime, MinHuntTime);

            Assert.IsFalse(gaveUp,
                "gave up on the very first hunting tick — this is the instant stop/circle regression");
        }

        [Test]
        public void StaysWithinArriveRadius_StillKeepsHuntingUntilMinHuntTimeElapses()
        {
            var stall = new PursuitStall();

            // Several ticks, all already inside arriveRadius, none reaching minHuntTime yet.
            for (int i = 0; i < 20; i++)
            {
                bool gaveUp = stall.TickHunting(0.1f, 0.016f, ArriveRadius, SearchTime, MinHuntTime);
                Assert.IsFalse(gaveUp, $"gave up early on tick {i} (elapsed ~{(i + 1) * 0.016f:F3}s)");
            }
        }

        [Test]
        public void OnceMinHuntTimeElapses_ArrivingAtTheSpotEndsTheHunt()
        {
            var stall = new PursuitStall();

            bool gaveUp = false;
            float elapsed = 0f;
            while (elapsed < MinHuntTime + 0.2f && !gaveUp)
            {
                gaveUp = stall.TickHunting(0.1f, 0.016f, ArriveRadius, SearchTime, MinHuntTime);
                elapsed += 0.016f;
            }

            Assert.IsTrue(gaveUp, "never gave up despite sitting within arriveRadius past minHuntTime");
        }

        [Test]
        public void GenuinelyClosingDistance_KeepsHuntingPastMinHuntTime_UntilItArrives()
        {
            // Starts far away and steadily closes — should not give up just because minHuntTime has
            // passed while it is still visibly making progress and outside arriveRadius.
            var stall = new PursuitStall();
            float dist = 10f;
            const float dt = 0.1f;

            for (int i = 0; i < 30 && dist > ArriveRadius; i++)
            {
                dist -= 0.5f; // closing steadily
                bool gaveUp = stall.TickHunting(dist, dt, ArriveRadius, SearchTime, MinHuntTime);
                if (dist > ArriveRadius)
                    Assert.IsFalse(gaveUp, $"gave up while still closing, dist={dist:F2}");
            }

            // Now within arriveRadius and well past minHuntTime — must give up.
            Assert.IsTrue(stall.TickHunting(dist, dt, ArriveRadius, SearchTime, MinHuntTime));
        }

        [Test]
        public void NeverGettingCloser_GivesUpAfterSearchTime_NotBeforeMinHuntTime()
        {
            // Stuck against a fence: distance never improves, always outside arriveRadius.
            var stall = new PursuitStall();
            const float dist = 5f;
            const float dt = 0.1f;
            float elapsed = 0f;

            while (elapsed < SearchTime - 0.1f)
            {
                bool gaveUp = stall.TickHunting(dist, dt, ArriveRadius, SearchTime, MinHuntTime);
                elapsed += dt;
                Assert.IsFalse(gaveUp, $"gave up early at {elapsed:F2}s, before searchTime ({SearchTime}s)");
            }

            bool gaveUpEventually = false;
            for (int i = 0; i < 5; i++)
            {
                if (stall.TickHunting(dist, dt, ArriveRadius, SearchTime, MinHuntTime)) { gaveUpEventually = true; break; }
            }
            Assert.IsTrue(gaveUpEventually, "never gave up despite making no progress past searchTime");
        }

        [Test]
        public void ReacquiringSightMidHunt_ResetsTheClockForTheNextLoss()
        {
            var stall = new PursuitStall();

            // Hunt right up to (but not past) minHuntTime.
            for (int i = 0; i < 30; i++) stall.TickHunting(0.05f, 0.016f, ArriveRadius, SearchTime, MinHuntTime);

            // Sight regained — clears the hunt.
            stall.NoteSightHeld();

            // A brand-new loss must get its own full minHuntTime grace, not inherit the old clock.
            bool gaveUp = stall.TickHunting(0.05f, 0.016f, ArriveRadius, SearchTime, MinHuntTime);
            Assert.IsFalse(gaveUp, "reacquiring sight didn't reset the hunt clock for the next loss");
        }
    }
}
