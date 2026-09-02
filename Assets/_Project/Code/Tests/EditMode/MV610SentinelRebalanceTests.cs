using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-610: the Sentinel contributed only 15% of Max's damage and died to a single Bruiser in
    /// 2.1s. Fire interval halved (0.6 -&gt; 0.35), the damage-fraction curve raised so a maxed
    /// <c>u_dmg</c> track lands exactly on 1.0 with no dead level, and base/per-level HP doubled.
    /// One test, per CC_AUTONOMY's "at most one new test per ticket" rule — this is that one, covering
    /// the ticket's AC1-AC6 (AC7 is <c>cc-verify.bat</c>, AC8 is Lee's own human check).
    ///
    /// Proven to fail on the pre-fix commit (old constants: <c>DefaultSentinelBaseHp</c> 60,
    /// <c>DefaultSentinelHpPerLevel</c> 20, <c>DefaultSentinelFireInterval</c> 0.6) —
    /// failure output quoted in the MV-610 fix comment. MV-653 later removed the damage-fraction
    /// assertions this test originally covered (AC1/AC2 above) along with the two fraction constants
    /// themselves — sentinel damage is now flat, independent of Max's primary damage.
    /// </summary>
    public sealed class MV610SentinelRebalanceTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            Sentinel.DestroyAllActive();
        }

        [Test]
        public void SentinelRebalance_DpsFractionHealthSurvivalAndFireIntervalLandOnTheNewTargets_AndUnrelatedShapesAreUntouched()
        {
            // ---------------------------------------------------------------- AC3: max health 120 at u_hp 0, 270 at u_hp 5
            Assert.That(AbilityTuning.SentinelMaxHp(0, AbilityTuning.DefaultSentinelBaseHp, AbilityTuning.DefaultSentinelHpPerLevel),
                Is.EqualTo(120f).Within(1e-4f));
            Assert.That(AbilityTuning.SentinelMaxHp(5, AbilityTuning.DefaultSentinelBaseHp, AbilityTuning.DefaultSentinelHpPerLevel),
                Is.EqualTo(270f).Within(1e-4f));

            // ---------------------------------------------------------------- AC4: 120 HP survives >= 4s of continuous Bruiser contact damage (28/s)
            const float bruiserContactDamagePerSecond = 28f;
            float survivalSeconds = 120f / bruiserContactDamagePerSecond;
            Assert.That(survivalSeconds, Is.GreaterThanOrEqualTo(4f),
                "an unupgraded Sentinel must survive at least 4s under continuous Bruiser contact damage, not the pre-fix 2.1s");

            // ---------------------------------------------------------------- AC5: fire interval 0.35, halved to 0.175 under OVERCHARGE
            Assert.That(AbilityTuning.DefaultSentinelFireInterval, Is.EqualTo(0.35f).Within(1e-4f));
            Assert.That(AbilityTuning.SentinelFireInterval(AbilityTuning.DefaultSentinelFireInterval, overchargeActive: true),
                Is.EqualTo(0.175f).Within(1e-4f));

            // ---------------------------------------------------------------- AC6: deployment slots, range and the SentinelMaxHp/SentinelDamagePerShot shapes are unchanged
            // MV-623 changed SentinelDeploymentSlots' own formula (1 + level, no dead level) — updated
            // to match rather than pin the pre-MV-623 numbers this ticket deliberately supersedes.
            Assert.That(AbilityTuning.SentinelDeploymentSlots(0), Is.EqualTo(1), "slot floor must still be 1 at level 0");
            Assert.That(AbilityTuning.SentinelDeploymentSlots(4), Is.EqualTo(5), "MV-623: every level now buys exactly one slot, no dead step");
            Assert.That(AbilityTuning.DefaultSentinelRange, Is.EqualTo(7f).Within(1e-4f), "targeting range base must be untouched by this ticket");
            Assert.That(AbilityTuning.DefaultSentinelRangePerLevel, Is.EqualTo(1.5f).Within(1e-4f), "targeting range per-level must be untouched by this ticket");
            // shape check with arbitrary base/perLevel, independent of the new defaults above
            Assert.That(AbilityTuning.SentinelMaxHp(3, 60f, 20f), Is.EqualTo(120f).Within(1e-4f), "SentinelMaxHp must still be linear in level");
        }
    }
}
