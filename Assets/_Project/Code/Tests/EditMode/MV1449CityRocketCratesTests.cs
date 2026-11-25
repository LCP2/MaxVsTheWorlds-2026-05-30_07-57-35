using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1449: one Rocket Crate per City area a1-a5, placed in its stronghold (positions from
    /// MV-1449.json). One test (Rule 1), failing on the pre-change main (no area authors rocketCrates, so
    /// zero crates are built). Tier 2: builds the real City through MapRuntime.Build and measures the
    /// positions of the Rocket Crate pickups the build placed.
    /// </summary>
    public sealed class MV1449CityRocketCratesTests
    {
        // "area id x z" from the spec data file
        private static readonly string[] Crates =
        {
            "a1 a1_rocketcrate1 9.0 22.5", "a2 a2_rocketcrate1 17.5 62.8", "a3 a3_rocketcrate1 64.2 48.6",
            "a4 a4_rocketcrate1 50.5 73.5", "a5 a5_rocketcrate1 3.5 69.5",
        };

        private GameObject _host;

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            RocketCrateCensus.Reset();
            Pickup.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            foreach (PickupDirector d in Object.FindObjectsByType<PickupDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            PickupWallet.Reset();
            RocketCrateCensus.Reset();
            Pickup.ResetRegistry();
        }

        [Test]
        public void CityA1ToA5_EachBuildsOneRocketCrateAtItsStrongholdSpot()
        {
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(city, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string valReason), valReason);

            _host = new GameObject("MV1449 Host");
            MapRuntime.Build(map, _host.transform);

            var crates = Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(p => p.Kind == PickupKind.RocketCrate).ToList();
            foreach (string c in Crates)
            {
                string[] t = c.Split(' ');
                Vector2 at = new Vector2(float.Parse(t[2], System.Globalization.CultureInfo.InvariantCulture),
                                         float.Parse(t[3], System.Globalization.CultureInfo.InvariantCulture));
                var near = crates.Where(p => Vector2.Distance(
                    new Vector2(p.transform.position.x, p.transform.position.z), at) <= 0.1f).ToList();
                Assert.AreEqual(1, near.Count, t[0] + " built Rocket Crates within 0.1 m of " + at);
                Assert.AreEqual(t[1], near[0].CrateId, t[0] + " crate id");
            }
        }
    }
}
