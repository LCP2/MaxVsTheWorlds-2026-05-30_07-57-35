using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-653: sentinel damage per shot was Max's live primary damage-per-tick times a fraction that
    /// only spanned x0.30 to x0.50 across the whole Damage (u_dmg) track — combined with the HUD's
    /// <c>Mathf.RoundToInt</c> display, a fully-maxed track was indistinguishable from an un-leveled
    /// one (Lee, device, 2026-09-02: primary 4.4, u_dmg 5/5, sentinels still landing 3). Sentinel
    /// damage is now flat and independent of Max's own primary damage: 2 at level 0 rising by 1 per
    /// level to 7 at level 5.
    ///
    /// Proven to fail on the pre-fix commit (415abbe): <c>AbilityTuning.DefaultSentinelBaseDamage</c>
    /// and <c>DefaultSentinelDamagePerLevel</c> did not exist yet, so this failed to compile —
    /// <c>error CS0117: 'AbilityTuning' does not contain a definition for 'DefaultSentinelBaseDamage'</c>
    /// and <c>error CS0117: 'AbilityTuning' does not contain a definition for 'DefaultSentinelDamagePerLevel'</c>
    /// — the whole EditMode assembly failing to build until the fix added them. Failure output quoted
    /// in the fix comment.
    /// </summary>
    public sealed class MV653SentinelFlatDamageTests
    {
        [TestCase(0, 2f)]
        [TestCase(1, 3f)]
        [TestCase(2, 4f)]
        [TestCase(3, 5f)]
        [TestCase(4, 6f)]
        [TestCase(5, 7f)]
        public void DamagePerShotIsFlatAndIntegerAtEveryLevel(int level, float expectedDamage)
        {
            float damage = AbilityTuning.SentinelDamagePerShot(
                level, AbilityTuning.DefaultSentinelBaseDamage, AbilityTuning.DefaultSentinelDamagePerLevel);
            Assert.That(damage, Is.EqualTo(expectedDamage).Within(1e-4f));
        }
    }
}
