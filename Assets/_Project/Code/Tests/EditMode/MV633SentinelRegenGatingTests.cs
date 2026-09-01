using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-633: <see cref="Sentinel.Update"/> called <see cref="Sentinel.Regenerate"/> at the flat
    /// <see cref="AbilityTuning.DefaultSentinelRegenPerSec"/> ceiling unconditionally — Lee: "They
    /// recover life too quickly (even when the sentinel health ability has not been obtained)." An
    /// un-leveled (u_hp level 0) sentinel must not regen at all; once leveled, the rate must scale
    /// from 1.0 HP/sec at level 1 up to the old 3.0 HP/sec ceiling at u_hp's own max level
    /// (<c>rig_board.json</c>: 5 today), via the new pure <see cref="AbilityTuning.SentinelRegenPerSec"/>.
    ///
    /// Proven to fail on the pre-fix commit (d00ef37): <c>AbilityTuning.SentinelRegenPerSec</c> did
    /// not exist yet, so this test failed to compile — CS0117 'AbilityTuning' does not contain a
    /// definition for 'SentinelRegenPerSec' — the whole EditMode assembly failing to build until the
    /// fix added it. Failure output quoted in the fix comment.
    /// </summary>
    public sealed class MV633SentinelRegenGatingTests
    {
        [Test]
        public void RegenIsGatedBehindHealthLevelAndScalesUpToTheBoardsMaxLevel()
        {
            int maxLevel = RigBoard.MaxLevel("u_hp");
            const float delay = AbilityTuning.DefaultSentinelRegenDelaySeconds;

            // ---------------------------------------------------------------- AC1: level 0 -> no regen at all
            float rateAtLevel0 = AbilityTuning.SentinelRegenPerSec(0, maxLevel,
                AbilityTuning.DefaultSentinelRegenPerSecAtLevel1, AbilityTuning.DefaultSentinelRegenPerSec);
            Assert.That(rateAtLevel0, Is.EqualTo(0f), "an un-leveled u_hp must not regen at all");

            float hpAfterLevel0 = Sentinel.Regenerate(100f, 200f,
                timeSinceDamage: delay + 5f, delay: delay, perSec: rateAtLevel0, dt: 5f);
            Assert.That(hpAfterLevel0, Is.EqualTo(100f).Within(1e-4f),
                "HP must not increase past the regen delay while u_hp is un-leveled (level 0)");

            // ---------------------------------------------------------------- AC2: level 1 -> exactly 1.0 HP/sec
            float rateAtLevel1 = AbilityTuning.SentinelRegenPerSec(1, maxLevel,
                AbilityTuning.DefaultSentinelRegenPerSecAtLevel1, AbilityTuning.DefaultSentinelRegenPerSec);
            Assert.That(rateAtLevel1, Is.EqualTo(1.0f).Within(1e-4f));

            float hpAfterLevel1 = Sentinel.Regenerate(100f, 200f,
                timeSinceDamage: delay, delay: delay, perSec: rateAtLevel1, dt: 1f);
            Assert.That(hpAfterLevel1, Is.EqualTo(101f).Within(1e-4f), "level 1 must heal at exactly 1.0 HP/sec");

            // ---------------------------------------------------------------- AC2 (max level) -> exactly 3.0 HP/sec, matching the old flat ceiling
            float rateAtMaxLevel = AbilityTuning.SentinelRegenPerSec(maxLevel, maxLevel,
                AbilityTuning.DefaultSentinelRegenPerSecAtLevel1, AbilityTuning.DefaultSentinelRegenPerSec);
            Assert.That(rateAtMaxLevel, Is.EqualTo(AbilityTuning.DefaultSentinelRegenPerSec).Within(1e-4f),
                "a maxed Health track must still reach the old flat ceiling, 3.0 HP/sec");

            float hpAfterMaxLevel = Sentinel.Regenerate(100f, 200f,
                timeSinceDamage: delay, delay: delay, perSec: rateAtMaxLevel, dt: 1f);
            Assert.That(hpAfterMaxLevel, Is.EqualTo(100f + AbilityTuning.DefaultSentinelRegenPerSec).Within(1e-4f),
                "max level must heal at exactly 3.0 HP/sec");
        }
    }
}
