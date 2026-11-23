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
    /// MV-1431: City pilot a1-a5 - Lee's approved big-idea fights replace the old 18-24 robot garrisons.
    /// One test (Rule 1), failing on the pre-change main (old a1-a5 garrisons/canisters). Tier 2: runs the
    /// real garrison seeding path (the route walk, same idiom as MV1027) and measures the robots it
    /// actually spawned - count, kind, XZ, post flag, squad id - and the built canisters' positions.
    /// </summary>
    public sealed class MV1431CityPilotGarrisonTests
    {
        // area, "kind x z P|- squad" per spawned robot, "id x z" per canister
        private static readonly object[][] Pilot =
        {
            new object[] { "a1", new[] {
                "bolter 7.0 20.0 P a1_w",
                "bolter 29.5 23.5 P a1_e",
                "bruiser 9.0 18.0 - a1_w",
                "bruiser 27.5 21.5 - a1_e",
                "gunner 20.0 15.0 P a1_e",
                "rusher 17.0 2.5 - a1_s",
                "rusher 13.0 10.5 - a1_s"
            }, new[] {
                "a1_canister1 10.2 18.8",
                "a1_canister2 26.3 22.3"
            } },
            new object[] { "a2", new[] {
                "rusher 18.2 36.0 - a2_r",
                "rusher 18.2 41.0 - a2_r",
                "bolter 23.0 63.5 P a2_n",
                "launcher 20.5 63.5 P a2_n",
                "gunner 19.0 57.5 P a2_n",
                "turret 21.0 58.0 P a2_n",
                "bruiser 16.5 58.0 - a2_n",
                "bolter 13.5 56.0 P a2_n"
            }, new[] {
                "a2_canister1 26.6 52.6",
                "a2_canister2 14.6 61.2"
            } },
            new object[] { "a3", new[] {
                "gunner 64.5 50.0 P a3_k",
                "bolter 60.0 34.0 P a3_k",
                "launcher 61.5 37.0 P a3_k",
                "turret 64.3 46.45 P a3_k",
                "bruiser 58.0 38.5 - a3_k",
                "bruiser 62.5 40.5 - a3_k",
                "blinker 56.0 41.0 - a3_k",
                "bolter 51.5 40.0 P a3_l"
            }, new[] {
                "a3_canister1 44.0 44.0",
                "a3_canister2 57.0 40.0",
                "a3_canister3 63.0 35.0"
            } },
            new object[] { "a4", new[] {
                "arcstriker 53.0 78.5 P a4_n",
                "bolter 54.5 76.0 P a4_e",
                "gunner 47.0 69.5 P a4_e",
                "launcher 51.0 73.5 P a4_n",
                "bruiser 50.18 79.18 - a4_n",
                "bruiser 57.19 67.72 - a4_e",
                "blinker 48.0 73.0 - a4_e"
            }, new[] {
                "a4_canister1 44.0 74.0",
                "a4_canister2 40.5 79.6"
            } },
            new object[] { "a5", new[] {
                "launcher 7.15 69.65 P a5_s",
                "gunner -2.39 91.19 P a5_n",
                "bruiser 19.29 86.74 - a5_c",
                "blinker -2.5 80.4 - a5_d",
                "bruiser 22.0 85.0 - a5_m",
                "bruiser 9.57 79.75 - a5_m",
                "blinker 23.0 78.5 - a5_m",
                "bruiser 5.5 74.5 - a5_s"
            }, new[] {
                "a5_canister1 0.6 87.8",
                "a5_canister2 10.6 80.4"
            } },
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
        public void CityA1ToA5_SeedExactlyTheApprovedPilotGarrisons()
        {
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(city, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string valReason), valReason);

            _host = new GameObject("MV1431 Host");
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

            foreach (object[] row in Pilot)
            {
                string id = (string)row[0];
                int index = int.Parse(id.Substring(1));
                string[] expected = (string[])row[1];
                RobotEnemy[] spawned = all.Where(r => r.AreaIndex == index).ToArray();
                Assert.AreEqual(expected.Length, spawned.Length, id + " spawned robot count (no ring-filled extras)");

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

                foreach (string c in (string[])row[2])
                {
                    string[] t = c.Split(' ');
                    Assert.IsTrue(built.Actors.TryGetValue(t[0], out GameObject go) && go != null, id + " canister " + t[0] + " built");
                    Vector3 p = go.transform.position;
                    Assert.LessOrEqual(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(F(t[1]), F(t[2]))), 0.1f, id + " canister " + t[0] + " position");
                }
            }
        }
    }
}
