using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-1397 (the one new test, Rule 1): a Projector never rolls the Shielded elite plate (it
    /// hangs back facing Max, so the front-arc plate made it unkillable). Guards MV-1397.</summary>
    public sealed class MV1397ProjectorNeverShieldedTests
    {
        [Test]
        public void ProjectorNeverRollsShielded()
        {
            var dials = new WorldDials { eliteAffixes = new[] { "Shielded", "Hasted" } };
            for (int i = 0; i < 500; i++)
                Assert.AreNotEqual(EliteAffix.Shielded, EliteRoll.RollAffixForced(dials, EnemyKind.Projector), "roll " + i);
        }
    }
}
