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
    /// MV-1431: City pilot a1-a5 - Lee's approved big-idea fights (a1-a5 robots/canisters re-pinned by MV-1436 to the V7 rooms) replace the old 18-24 robot garrisons.
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
                "bolter 12.0 23.5 P a1_w",
                "bolter 33.5 23.5 P a1_e",
                "bruiser 14.6 22.1 - a1_w",
                "bruiser 31.9 21.3 - a1_e",
                "gunner 20.0 18.0 P a1_e",
                "rusher 18.0 3.2 - a1_s",
                "rusher 33.0 3.6 - a1_s"
            }, new[] {
                "a1_canister1 16.2 22.9",
                "a1_canister2 30.7 21.9"
            } },
            new object[] { "a2", new[] {
                "rusher 18.0 41.5 - a2_r",
                "bolter 10.7 63.5 P a2_n",
                "bolter 22.0 63.5 P a2_n",
                "launcher 10.7 61.5 P a2_n",
                "gunner 24.5 56.8 P a2_n",
                "gunner 25.5 58.5 P a2_n",
                "turret 21.5 60.5 P a2_n",
                "bruiser 17.0 57.5 - a2_n"
            }, new[] {
                "a2_canister1 12.4 48.6",
                "a2_canister2 27.0 56.8"
            } },
            new object[] { "a3", new[] {
                "turret 64.3 46.45 P a3_k",
                "gunner 64.5 50.0 P a3_k",
                "bolter 52.5 33.5 P a3_k",
                "launcher 63.0 42.0 P a3_k",
                "bolter 49.0 33.5 P a3_l",
                "bruiser 59.0 38.0 - a3_k",
                "bruiser 62.17 40.35 - a3_k",
                "blinker 56.5 41.6 - a3_k"
            }, new[] {
                "a3_canister1 56.6 34.4",
                "a3_canister2 63.8 38.2",
                "a3_canister3 52.2 38.6"
            } },
            new object[] { "a4", new[] {
                "bolter 53.5 75.0 P a4_e",
                "orbital 46.5 68.0 P a4_e",   // MV-1477: the a4 gunner is now the City's first Orbital
                "launcher 50.0 72.0 P a4_n",
                "arcstriker 60.0 74.5 - a4_n",
                "bruiser 51.5 79.0 - a4_n",
                "bruiser 56.5 69.0 - a4_e",
                "blinker 49.0 74.5 - a4_e"
            }, new[] {
                "a4_canister1 44.5 74.5",
                "a4_canister2 55.6 79.4"
            } },
            new object[] { "a5", new[] {
                "launcher 6.0 68.3 P a5_s",
                "gunner 2.0 91.0 P a5_n",
                "rusher 2.9 74.6 - a5_s",
                "rusher -3.3 72.8 - a5_s",
                "rusher 5.6 86.2 - a5_n",
                "rusher 11.0 86.6 - a5_n",
                "bruiser 27.5 69.6 - a5_h",
                "bruiser 24.75 81.05 - a5_h",
                "blinker 13.6 81.8 - a5_h"
            }, new[] {
                "a5_canister1 10.5 74.5",
                "a5_canister2 24.75 82.2",
                "a5_canister3 -3.2 80.5"
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
