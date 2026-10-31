using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1329 (the one new test): MV-1106 cut Shoulder Rack rocket damage to 20 + 4/level for "World 3"
    /// when that was the Reef (index 3). The City was inserted at index 2 later, and
    /// <see cref="AbilityTuning.ShoulderRackBaseDamageForWorld"/>/<see cref="AbilityTuning.ShoulderRackDamagePerLevelForWorld"/>
    /// still tested <c>worldIndex &gt;= Reef</c>, so the City ran at World 2's full 30 + 6 rate (48 at
    /// s_rkt 4) instead of the intended lower rate.
    ///
    /// AC1: City (index 2) resolves to 20 + 4/level — a rocket at s_rkt 4 does 32.
    /// AC2: Stormdrain (index 1) stays at 30 + 6/level; Reef and Mothership stay at 20 + 4.
    ///
    /// Fails on base commit (before this ticket): the City resolves to base 30 / per-level 6, and a
    /// rocket at s_rkt 4 does 48, not 32.
    /// </summary>
    public sealed class MV1329CityRocketDamageTests
    {
        [Test]
        public void CityRunsTheLowerRate_StormdrainReefAndMothershipUnchanged()
        {
            int stormdrain = WorldCatalog.IndexOf(WorldIds.Stormdrain);
            int city = WorldCatalog.IndexOf(WorldIds.City);
            int reef = WorldCatalog.IndexOf(WorldIds.Reef);
            int mothership = WorldCatalog.IndexOf(WorldIds.Mothership);

            // AC1: City (index 2) — base 20, per level 4, a rocket at s_rkt 4 does 32.
            Assert.AreEqual(20f, AbilityTuning.ShoulderRackBaseDamageForWorld(city),
                "the City must resolve the lower rocket base damage (moved off the Reef by MV-1329)");
            Assert.AreEqual(4f, AbilityTuning.ShoulderRackDamagePerLevelForWorld(city),
                "the City must resolve the lower rocket per-level damage (moved off the Reef by MV-1329)");
            Assert.AreEqual(32f, AbilityTuning.ShoulderRackRocketDamage(4,
                AbilityTuning.ShoulderRackBaseDamageForWorld(city), AbilityTuning.ShoulderRackDamagePerLevelForWorld(city)),
                "a City rocket at s_rkt 4 must deal 32 (20 + 4*3), not World 2's 48");

            // AC2: Stormdrain stays at World 1/2's full rate; Reef and Mothership keep the lower rate.
            Assert.AreEqual(30f, AbilityTuning.ShoulderRackBaseDamageForWorld(stormdrain),
                "Stormdrain must keep the full rocket base damage");
            Assert.AreEqual(6f, AbilityTuning.ShoulderRackDamagePerLevelForWorld(stormdrain),
                "Stormdrain must keep the full rocket per-level damage");

            Assert.AreEqual(20f, AbilityTuning.ShoulderRackBaseDamageForWorld(reef),
                "Reef must keep the lower rocket base damage");
            Assert.AreEqual(4f, AbilityTuning.ShoulderRackDamagePerLevelForWorld(reef),
                "Reef must keep the lower rocket per-level damage");
            Assert.AreEqual(20f, AbilityTuning.ShoulderRackBaseDamageForWorld(mothership),
                "Mothership must keep the lower rocket base damage");
            Assert.AreEqual(4f, AbilityTuning.ShoulderRackDamagePerLevelForWorld(mothership),
                "Mothership must keep the lower rocket per-level damage");
        }
    }
}
