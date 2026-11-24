using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1432: the City has no Charger. Tier 2: runs the real garrison seeding path (route walk, same idiom
    /// as MV1431CityPilotGarrisonTests) and measures the robots actually spawned in a2, a3 and a5 against the
    /// data file entry for entry, and that no area's garrison or composition names a charger.
    /// </summary>
    public sealed class MV1432CityNoChargerTests
    {
        // area, "kind x z P|- squad" per spawned robot
        private static readonly object[][] Expected =
        {
            new object[] { "a2", new[] {
                "rusher 18.0 41.5 - a2_r",
                "bolter 10.7 63.5 P a2_n",
                "bolter 22.0 63.5 P a2_n",
                "launcher 10.7 61.5 P a2_n",
                "gunner 24.5 56.8 P a2_n",
                "gunner 25.5 58.5 P a2_n",
                "turret 21.5 60.5 P a2_n",
                "bruiser 17.0 57.5 - a2_n" } },
            new object[] { "a3", new[] {
                "turret 64.3 46.45 P a3_k",
                "gunner 64.5 50.0 P a3_k",
                "bolter 52.5 33.5 P a3_k",
                "launcher 63.0 42.0 P a3_k",
                "bolter 49.0 33.5 P a3_l",
                "bruiser 59.0 38.0 - a3_k",
                "bruiser 62.17 40.35 - a3_k",
                "blinker 56.5 41.6 - a3_k" } },
            new object[] { "a5", new[] {
                "launcher 2.15 70.15 P a5_s",
                "gunner -3.39 90.69 P a5_n",
                "bruiser 15.23 82.26 - a5_m",
                "bruiser 29.3 85.5 - a5_c",
                "blinker 20.2 89.3 - a5_c",
                "bruiser 10.5 73.5 - a5_s",
                "blinker 10.0 80.5 - a5_d",
                "bruiser 24.2 74.2 - a5_m" } },
        };

        private static readonly FieldInfo HasPostField =
            typeof(RobotEnemy).GetField("_hasPost", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo UpdateMethod =
            typeof(AreaAccumulationDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _host, _areaGo, _playerGo;
        private Camera[] _suppressed;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            FactoryExitZones.ResetForTests();
            DormantWakeScheduler.ResetForTests();
            _suppressed = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            if (_host != null) Object.DestroyImmediate(_host);
            CameraTestUtil.RestoreAmbientMainCameras(_suppressed);
            RobotEnemy.ResetRegistry();
            FactoryExitZones.ResetForTests();
            DormantWakeScheduler.ResetForTests();
            DevTuning.Reset();
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        [Test]
        public void City_HasNoCharger_AndA2A3A5SeedTheDataFileRobots()
        {
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load");
            string raw = Resources.Load<TextAsset>("Worlds/city_config").text;
            // enemyOverrides legitimately keeps a charger HP entry; only areas[].garrison / composition are cut.
            string areasText = raw.Substring(raw.IndexOf("\"areas\"", System.StringComparison.Ordinal));
            Assert.IsFalse(areasText.Contains("\"charger\""), "a City area's garrison or composition still names a charger");

            Assert.IsTrue(WorldMapLoader.TryLoad(city, out MapData map, out string loadReason), loadReason);
            _host = new GameObject("MV1432 Host");
            MapBuild built = MapRuntime.Build(map, _host.transform);

            DevTuning.MaxActiveRobots = 1f;
            _areaGo = new GameObject("Area Accumulation");
            var director = _areaGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(city);
            director.Configure(map, built.Cover);

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = new Vector3(map.Zone("area1").x, 0.5f, map.Zone("area1").z);
            UpdateMethod.Invoke(director, null);
            for (int index = 2; index <= 5; index++)
            {
                MapZone z = map.Zone("area" + index);
                _playerGo.transform.position = new Vector3(z.x, 0.5f, z.z);
                UpdateMethod.Invoke(director, null);
                director.EnterArea(index);
            }
            Object.DestroyImmediate(_playerGo);
            _playerGo = null;

            RobotEnemy[] all = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.IsFalse(all.Any(r => r.Kind.ToString().ToLowerInvariant() == "charger"), "a spawned robot is a Charger");

            foreach (object[] row in Expected)
            {
                string id = (string)row[0];
                int index = int.Parse(id.Substring(1));
                string[] expected = (string[])row[1];
                RobotEnemy[] spawned = all.Where(r => r.AreaIndex == index).ToArray();
                Assert.AreEqual(expected.Length, spawned.Length, id + " spawned robot count");

                var unmatched = spawned.ToList();
                foreach (string e in expected)
                {
                    string[] t = e.Split(' ');
                    float x = F(t[1]), z = F(t[2]);
                    bool post = t[3] == "P";
                    RobotEnemy hit = unmatched.FirstOrDefault(r =>
                        r.Kind.ToString().ToLowerInvariant() == t[0]
                        && Vector2.Distance(new Vector2(r.transform.position.x, r.transform.position.z), new Vector2(x, z)) <= 0.1f
                        && r.SquadId == t[4]
                        && (bool)HasPostField.GetValue(r) == post);
                    Assert.IsNotNull(hit, id + " no spawned robot matches " + e);
                    unmatched.Remove(hit);
                }
            }
        }
    }
}
