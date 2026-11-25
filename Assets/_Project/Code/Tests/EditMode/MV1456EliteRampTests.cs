using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-1456: the City's elite chance ramps with the area (0% a1 -> 30% a30); other worlds stay flat.
    /// Asserts resolved chances from the real shipped configs.</summary>
    public class MV1456EliteRampTests
    {
        [Test]
        public void CityEliteChanceRampsAndWorld2StaysFlat()
        {
            WorldDials city = WorldLibrary.Load(WorldLibrary.City).dials;
            Assert.AreEqual(0f, city.EliteChancePctForArea(1), 0.01f, "City a1");
            Assert.AreEqual(15f, city.EliteChancePctForArea(15), 0.6f, "City a15");
            Assert.AreEqual(30f, city.EliteChancePctForArea(30), 0.01f, "City a30");

            WorldDials w2 = WorldLibrary.Load(WorldLibrary.World2).dials;
            Assert.AreEqual(w2.eliteChancePct, w2.EliteChancePctForArea(1), 0.0001f, "World 2 first area");
            Assert.AreEqual(w2.eliteChancePct, w2.EliteChancePctForArea(w2.areaCount), 0.0001f, "World 2 last area");
        }
    }
}
