using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1418: Lee's City V5.1 map (23 Nov 2026) - pylons moved away from (and sometimes out of the
    /// area of) their relays, a7's ricochet corridor, a13 redrawn as nested walls. One test (Rule 1),
    /// failing on the pre-change main: the old city_config.json carries the old per-area counts.
    /// Tier 2: loaded-and-validated data, measured counts and distances.
    /// </summary>
    public sealed class MV1418CityV51LayoutTests
    {
        // area, non-wedge cover, wedges, relays, pylons standing here, garrison, canisters
        private static readonly object[][] Expected =
        {
            new object[] { "a2", 19, 5, 1, 1, 8, 2 },
            new object[] { "a3", 21, 2, 1, 1, 8, 3 },
            new object[] { "a5", 21, 3, 2, 2, 9, 3 },
            new object[] { "a6", 33, 0, 1, 1, 25, 3 }, // MV-1426: +1 arcstriker at the back wall; MV-1430: -1 cut kind (a welder/jammer/projector)
            new object[] { "a7", 12, 23, 2, 1, 24, 4 },
            new object[] { "a8", 39, 0, 3, 4, 42, 4 },
            new object[] { "a9", 21, 0, 2, 0, 25, 3 },
            new object[] { "a10", 27, 0, 1, 3, 25, 3 },
            new object[] { "a11", 120, 12, 2, 2, 35, 3 },
            new object[] { "a13", 14, 18, 2, 4, 60, 4 },
        };

        [Test]
        public void City_MatchesV51Layout()
        {
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(city, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string valReason), valReason);

            foreach (object[] row in Expected)
            {
                string id = (string)row[0];
                WorldArea a = city.areas.First(x => x.id == id);
                int wedges = a.cover.Count(c => c.shape == "wedge");
                Assert.AreEqual((int)row[1], a.cover.Length - wedges, id + " non-wedge cover");
                Assert.AreEqual((int)row[2], wedges, id + " wedges");
                Assert.AreEqual((int)row[3], a.replicators.Length, id + " relays");
                Assert.AreEqual((int)row[4], a.pylons.Length, id + " pylons");
                Assert.AreEqual((int)row[5], a.garrison.Length, id + " garrison");
                Assert.AreEqual((int)row[6], a.canisters.Length, id + " canisters");
            }

            AssertInside(city, "a9_rep1_pylon1", "a10");
            AssertInside(city, "a9_rep2_pylon1", "a10");
            AssertInside(city, "a7_rep2_pylon1", "a8");

            Vector2 centre = new Vector2(101f, 206f);
            foreach (string id in new[] { "a13_rep1_pylon1", "a13_rep2_pylon2" })
            {
                WorldPylon p = city.areas.SelectMany(a => a.pylons).First(x => x.id == id);
                Assert.LessOrEqual(Vector2.Distance(new Vector2(p.x, p.z), centre), 3f, id + " distance from a13 centre");
            }
        }

        private static void AssertInside(WorldConfig city, string pylonId, string areaId)
        {
            WorldPylon p = city.areas.SelectMany(a => a.pylons).First(x => x.id == pylonId);
            WorldArea a = city.areas.First(x => x.id == areaId);
            Assert.IsTrue(p.x >= a.origin.x && p.x <= a.origin.x + a.size.w
                          && p.z >= a.origin.z && p.z <= a.origin.z + a.size.d,
                pylonId + " at (" + p.x + ", " + p.z + ") is outside " + areaId);
        }
    }
}
