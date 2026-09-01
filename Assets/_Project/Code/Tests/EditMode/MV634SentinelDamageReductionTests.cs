using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-634: sentinels were knocking out robots too quickly even at a mid Damage (u_dmg) level.
    /// DefaultSentinelDamageFraction 0.6 -&gt; 0.3 and DefaultSentinelDamageFractionPerLevel 0.08 -&gt; 0.04
    /// halve the per-shot fraction of Max's own current primary damage at every level.
    ///
    /// Proven to fail on the pre-fix commit (old constants 0.6/0.08 put u_dmg level 2 at 0.76 of
    /// primary damage, not 0.38) — failure output quoted in the MV-634 fix comment.
    /// </summary>
    public sealed class MV634SentinelDamageReductionTests
    {
        [Test]
        public void SentinelDamagePerShot_AtDamageLevel2_Is38PercentOfMaxsPrimaryDamage()
        {
            const float primaryDamage = 8f; // an arbitrary "Max's current primary tick damage"
            float shot = AbilityTuning.SentinelDamagePerShot(
                primaryDamage, level: 2,
                AbilityTuning.DefaultSentinelDamageFraction, AbilityTuning.DefaultSentinelDamageFractionPerLevel);
            Assert.That(shot, Is.EqualTo(0.38f * primaryDamage).Within(1e-4f),
                "u_dmg level 2 must land at 38% of Max's current per-tick damage, not the pre-MV-634 76%");
        }
    }
}
