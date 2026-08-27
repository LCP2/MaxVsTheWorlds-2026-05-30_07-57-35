using NUnit.Framework;
using MaxWorlds.Bosses;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// How long does the Big Bermuda fight last? (YT-94, re-scoped MV-588)
    ///
    /// MV-588 removed the ram/charge entirely, and with it the dodge-window model these tests used to
    /// carry ("the enraged charge could not be dodged" — literally the bug that started YT-94). That
    /// model described an attack that no longer exists, so its tests were culled along with the code
    /// they asserted on (culling policy, CC_AUTONOMY.md — a ticket's own changes making a test redundant
    /// is exactly this case). What survives is the fight-length half of the model: how long the boss
    /// takes to fall, at the gun's un-ramped base output.
    /// </summary>
    public sealed class BossFightTests
    {
        // ---------------------------------------------------------------- the length of the fight

        /// <summary>~2–3 minutes, which is the YT-27 target this ticket asks to return to. MV-287
        /// removed the per-run level/power ramp, so this is measured against the gun's permanent,
        /// un-ramped base output (<see cref="BossTuning.Health"/> was recalibrated to hold the same
        /// target once the ramp went away).</summary>
        [Test]
        public void TheFightLasts_AboutTwoToThreeMinutes()
        {
            float seconds = BossFight.SecondsToKill();

            Assert.GreaterOrEqual(seconds, 100f,
                $"the boss dies in {seconds:0}s — that is not a boss, it is a big robot");

            Assert.LessOrEqual(seconds, 190f,
                $"the boss takes {seconds:0}s to kill. Past three minutes a fight this simple is a " +
                "slog, whatever its health bar says.");
        }

        /// <summary>
        /// …and it stays a fight whoever turns up.
        ///
        /// The length hangs on ONE guess — how much of the fight the player spends actually pointing
        /// the gun at the boss — and I do not know that number, I estimated it. So rather than pretend
        /// the point estimate is a fact, this asserts the fight survives being wrong about it: across
        /// every plausible engagement level, from a cautious player who fires a third of the time to
        /// an aggressor who fires two thirds, the boss is never a pushover and never a slog.
        ///
        /// MV-287 removed the per-run level ramp, so engagement is now the only variable left — there
        /// is no more "well-levelled" case to also sweep.
        ///
        /// If Lee reports it as either, the number to move is <see cref="BossTuning.Health"/>, and this
        /// is the test that says what moving it costs.
        /// </summary>
        [Test]
        public void TheFightIsNeverAPushover_AndNeverASlog_WhoeverTurnsUp()
        {
            foreach (float engagement in new[] { 0.3f, 0.45f, 0.6f })
            {
                float seconds = BossFight.SecondsToKill(engagement);

                Assert.Greater(seconds, 60f,
                    $"a player firing {engagement:P0} of the time melts the boss in {seconds:0}s");

                Assert.Less(seconds, 240f,
                    $"a player firing {engagement:P0} of the time is still hosing the boss " +
                    $"{seconds:0}s later — that is a health bar, not a fight");
            }
        }

        // ---------------------------------------------------------------- the zones that tick

        /// <summary>
        /// A damage zone bites every 0.4 s for its whole life, which is what turned a "12 damage" blade
        /// into 36 and made the enrage lethal. What lands on a player who reads the tell and walks out
        /// is ONE bite; what must never land is a fight's worth.
        /// </summary>
        [Test]
        public void NoSingleZoneCanTakeMoreThanAQuarterOfYourHealth()
        {
            Assert.LessOrEqual(BossTuning.BladeWorstCase, 25f,
                $"one blade can do {BossTuning.BladeWorstCase:0} damage to a 100 HP player if he is " +
                "slow out of it. It ticks — its LIFE is as much of its damage as its damage is.");

            Assert.LessOrEqual(BossTuning.GrassWorstCase, 25f,
                $"one patch of clippings can do {BossTuning.GrassWorstCase:0} damage. It is a trail to " +
                "walk out of, not a second attack.");
        }

        /// <summary>The blade rain has to be something you can walk out of: its warning must outlast a
        /// human reaction with time to spare, or it is just damage that happens to you.</summary>
        [Test]
        public void TheBladeRain_WarnsLongEnoughToWalkOutOf()
        {
            Assert.Greater(BossTuning.BladeArm, BossFight.ReactionSeconds * 2f,
                $"a blade arms in {BossTuning.BladeArm:0.00}s — you are hit before you have read it");

            Assert.Greater(BossTuning.BladeInterval, BossTuning.BladeArm * 2f,
                "the blades land faster than you can clear the last one");
        }
    }
}
