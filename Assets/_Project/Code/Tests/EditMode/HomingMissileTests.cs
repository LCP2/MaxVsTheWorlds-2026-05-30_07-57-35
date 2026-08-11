using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-349: the missile's colour, and the pure fuel-exhausted state transition that starts its
    /// sputter/bounce/boom sequence. The sequence's timing/physics itself (sputter coast, the bounce
    /// decay) runs on <c>Time.deltaTime</c> inside <c>Update</c> and isn't covered here — per the 11
    /// Aug ruling, PlayMode is CI's problem, not the worker's, and this project doesn't author
    /// PlayMode tests.
    /// </summary>
    public sealed class HomingMissileTests
    {
        [Test]
        public void TheMissileColour_HasNoChannelPastTheSunlitCeiling()
        {
            Color c = HomingMissile.WarnColorForTests;
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));

            Assert.LessOrEqual(peak, SunlitAlbedo.Ceiling,
                $"the missile's warhead/fin colour peaks at {peak:0.00}, past the " +
                $"{SunlitAlbedo.Ceiling:0.00} sunlit ceiling — it will clip under the yard's 1.8x key " +
                "and wash toward the drab brown/tan Lee reported instead of reading as a hot, hostile " +
                "projectile.");
        }

        /// <summary>Pins the boundary at the fuel budget itself, and that it is an ordinary, reachable
        /// number rather than 0/negative/absurd (AC3: "reachable in normal play").</summary>
        [TestCase(0f, false)]
        [TestCase(5.99f, false)]
        [TestCase(6f, true)]
        [TestCase(10f, true)]
        public void HasRunDry_TransitionsAtTheFuelBudget(float age, bool expectRunDry)
        {
            Assert.AreEqual(expectRunDry, HomingMissile.HasRunDry(age, fuelBudget: 6f),
                $"age {age}s against a 6s fuel budget should give HasRunDry = {expectRunDry}");
        }
    }
}
