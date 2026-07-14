using NUnit.Framework;
using MaxWorlds.Bosses;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Is the Big Bermuda fight fair? (YT-94)
    ///
    /// Lee's report was "the boss is too difficult", which is a feeling — and you cannot tune against a
    /// feeling, you tune against it and wait a day to find out. So these tests are the feeling turned
    /// into arithmetic: how long you get to react, how much lands on you if you react well, and how
    /// long the thing takes to fall.
    ///
    /// They are a model, and <see cref="BossFight"/> states every assumption it makes. They cannot tell
    /// anyone the fight is FUN. What they can do is make the unfair fight — the one where the tell is
    /// shorter than a human reaction — impossible to ship again, which is the one we shipped.
    /// </summary>
    public sealed class BossFightTests
    {
        // ---------------------------------------------------------------- the charge you couldn't dodge

        /// <summary>
        /// THE bug, in one assertion: the enraged charge could not be dodged.
        ///
        /// The tell burned for 0.49 s (0.75 base, scaled 0.65 by the enrage — the enrage sped up the
        /// WARNING as well as the attack) and the boss then crossed the gap at 22 m/s. A human needs a
        /// quarter of a second to react and about half a second to walk clear of a 2.4 m contact
        /// radius. The window was smaller than the reaction. It was not a fight you could lose by
        /// playing badly; it was one you could not win by playing well.
        /// </summary>
        [Test]
        public void TheChargeCanBeDodged_EvenWhenItIsEnraged()
        {
            Assert.Greater(BossFight.DodgeMargin(enraged: false), 0.35f,
                "there is not enough time to read the charge and get out of it");

            Assert.Greater(BossFight.DodgeMargin(enraged: true), 0.25f,
                $"the ENRAGED charge gives {BossFight.DodgeWindow(true):0.00}s and getting out of it " +
                $"takes {BossFight.TimeToDodge:0.00}s. The player cannot dodge it — they can only be " +
                "hit by it, which is not a fight, it is a toll.");
        }

        /// <summary>The enrage must never shorten the TELL below what a human can read. It may make
        /// the boss faster, angrier and more frequent — that is what an enrage is for — but the moment
        /// it eats the warning, the fight stops being readable, and readability outranks game feel
        /// (Craft Bible).</summary>
        [Test]
        public void TheEnrageMakesItFaster_NotUnreadable()
        {
            Assert.Greater(BossTuning.EnragedChargeWindup, BossFight.ReactionSeconds * 2f,
                $"an enraged wind-up of {BossTuning.EnragedChargeWindup:0.00}s is barely a human " +
                "reaction time — the tell is gone before it has been seen");

            Assert.Less(BossTuning.EnrageTimeScale, 1f, "the enrage does not speed anything up");
        }

        // ---------------------------------------------------------------- the length of the fight

        /// <summary>~2–3 minutes, which is the YT-27 target this ticket asks to return to. Measured
        /// against the gun the player actually brings: the power ramp more than doubles his DPS on the
        /// way to the boss, so a boss tuned against a level-1 blaster is tuned against a gun nobody
        /// arrives with.</summary>
        [Test]
        public void TheFightLasts_AboutTwoToThreeMinutes()
        {
            float seconds = BossFight.SecondsToKill(BossFight.LevelAtTheBoss);

            Assert.GreaterOrEqual(seconds, 100f,
                $"the boss dies in {seconds:0}s — that is not a boss, it is a big robot");

            Assert.LessOrEqual(seconds, 190f,
                $"the boss takes {seconds:0}s to kill. Past three minutes a fight this simple is a " +
                "slog, whatever its health bar says.");
        }

        // ---------------------------------------------------------------- can you win it?

        /// <summary>
        /// A competent player wins — and has something left, but not much. That is the whole ask:
        /// "challenging but fair", "beatable without invincibility".
        /// </summary>
        [Test]
        public void ACompetentPlayer_WinsIt_WithoutInvincibility()
        {
            float left = BossFight.HealthLeft(BossFight.CompetentDodge, BossFight.LevelAtTheBoss);

            Assert.Greater(left, 0f,
                $"a competent player ends the fight on {left:0} HP — he is dead, and he did nothing " +
                "wrong. This is the report: the boss is too difficult.");

            Assert.Less(left, 75f,
                $"a competent player strolls it, ending on {left:0} of 100 HP. A boss you can ignore " +
                "is not a boss.");
        }

        /// <summary>…and a careless one still dies. A fight nobody can lose is not challenging, and
        /// this ticket says "challenging but fair", not "fair".</summary>
        [Test]
        public void ACarelessPlayer_StillDies()
        {
            Assert.IsFalse(BossFight.Survives(BossFight.CarelessDodge, BossFight.LevelAtTheBoss),
                "standing in the blades and eating every charge gets you through the fight — there is " +
                "nothing here to be good at");
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
