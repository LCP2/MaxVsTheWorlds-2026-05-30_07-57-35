using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The robot's tell, as a pure function (YT-96).
    ///
    /// The eye is how a small enemy telegraphs — a colour tell on a twenty-pixel body does not carry, so
    /// the one bright thing on it does the work. This is the same read the boss's eyes carry and the same
    /// word the ground rings use: gold at rest, the game's warn orange winding up, white on a hit. Pure,
    /// so the guarantee can be checked without driving a whole enemy into its lunge.
    /// </summary>
    public sealed class RobotRigTests
    {
        [Test]
        public void AtRest_TheEyeIsWarmGold_NotTheWarnColour()
        {
            Color idle = RobotRig.TellColorFor(windup: 0f, flash: 0f);

            // Gold: warm, bright, and low in blue — but distinctly NOT the red-orange of the wind-up, or
            // a robot merely standing there would look like one about to hit you.
            Assert.Greater(idle.r, 0.5f, "the resting eye is too dark to read as lit.");
            Assert.Greater(idle.g, 0.5f, "the resting eye has no gold in it — gold is green-warm, not red.");
            Assert.Less(idle.b, 0.4f, "the resting eye is too blue to read as gold.");
        }

        [Test]
        public void WindingUp_TheEyeHeatsTowardTheWarnColour()
        {
            Color idle = RobotRig.TellColorFor(0f, 0f);
            Color windup = RobotRig.TellColorFor(1f, 0f);

            // Warmer, and specifically REDDER: the gap between red and green opens up as it commits.
            Assert.Greater(windup.r - windup.g, idle.r - idle.g + 0.2f,
                "the wind-up does not read hotter than idle. It is the one telegraph that costs you " +
                "health to miss, so it has to be unmistakable from doing nothing.");
            Assert.Greater(windup.r, windup.b + 0.4f, "the wind-up is not warm.");
        }

        [Test]
        public void AHit_WhitesTheEyeOut()
        {
            Color flash = RobotRig.TellColorFor(0f, 1f);

            // Neutral white, never a saturated hue — a hit flash must never be mistakable for a render
            // error (CharacterSkin makes the same promise).
            Assert.AreEqual(flash.r, flash.g, 1e-3f, "the flash is not neutral...");
            Assert.AreEqual(flash.g, flash.b, 1e-3f, "...i.e. white, not a colour.");
            Assert.Greater(flash.r, 0.9f, "the flash is not bright enough to read as a hit.");
        }
    }
}
