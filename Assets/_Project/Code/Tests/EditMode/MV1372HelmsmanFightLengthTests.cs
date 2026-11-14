using NUnit.Framework;
using MaxWorlds.Bosses;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1372 (the one new test, Tier 2): the fight model - full loadout (Light Missile COUNT 5 / POWER 8 /
    /// RATE 5 / CAPACITY 5 with MV-1367's draining gauge, Orbitals 8 orbs at POWER 5 in reach 30% of the
    /// time, 60% of shots on the boss) run against the real <see cref="HelmsmanTuning.Health"/> - must
    /// resolve a whole fight (first hit to death, both 2 s phase beats included) of 90-120 s.
    ///
    /// Fails on the base tuning (Health 18,000): the model resolves ~66 s.
    /// </summary>
    public sealed class MV1372HelmsmanFightLengthTests
    {
        [Test]
        public void HelmsmanFight_FullLoadout_ModelledTimeToKill_Is90To120Seconds()
        {
            float ttk = HelmsmanFightModel.TimeToKill(HelmsmanTuning.Health);
            Assert.That(ttk, Is.InRange(90f, 120f),
                "modelled time to kill with Health " + HelmsmanTuning.Health + " was " + ttk.ToString("F1") + " s");
        }
    }
}
