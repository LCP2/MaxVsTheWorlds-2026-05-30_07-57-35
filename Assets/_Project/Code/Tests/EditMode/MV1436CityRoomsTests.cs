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
    /// MV-1436: City a1-a5 rooms rebuilt around their big ideas (Lee's approved V7 map, 24 Nov). One test
    /// (Rule 1), failing on the pre-change main (old a1-a5 cover). Tier 2: builds the real City through
    /// MapRuntime.Build and measures the BUILT cover colliders (centre and footprint, and that no other
    /// cover collider stands in the area), that a3_c4 is built as a secret breakable piece, and runs the
    /// real garrison seeding path to measure the robots it spawned (kind, XZ, post, squad).
    /// </summary>
    public sealed class MV1436CityRoomsTests
    {
        // area, "x z width depth" per cover piece, "kind x z P|- squad" per spawned robot
        private static readonly object[][] Rooms =
        {
            new object[] { "a1", new[] {
                "10.0 7.5 8.0 1.0",
                "17.0 7.1 6.0 2.2",
                "24.5 7.5 9.0 1.0",
                "32.0 7.1 6.0 2.2",
                "36.5 7.5 3.0 1.0",
                "25.0 1.1 4.0 1.8",
                "11.0 5.6 0.4 0.4",
                "12.6 5.6 0.4 0.4",
                "41.0 1.2 0.3 0.3",
                "45.5 0.9 3.0 1.2",
                "40.5 13.2 3.0 1.4",
                "45.0 18.6 3.0 1.2",
                "46.275 10.95 0.55 1.9",
                "35.5 10.2 3.0 1.2",
                "38.0 17.4 3.0 1.2",
                "32.5 15.3 3.0 1.4",
                "30.75 14.5 0.5 3.0",
                "27.5 11.2 3.0 1.2",
                "26.0 17.5 3.0 1.4",
                "24.25 16.7 0.5 3.0",
                "20.0 13.2 0.3 0.3",
                "15.9 10.45 1.8 0.9",
                "18.5 10.45 1.8 0.9",
                "15.8 8.65 1.6 0.5",
                "16.0 17.2 3.0 1.2",
                "10.75 19.6 0.5 1.6",
                "10.75 14.0 0.5 1.6",
                "37.3 21.65 1.6 0.5",
                "29.0 21.65 1.6 0.5"
            }, new[] {
                "bolter 12.0 23.5 P a1_w",
                "bolter 33.5 23.5 P a1_e",
                "bruiser 14.6 22.1 - a1_w",
                "bruiser 31.9 21.3 - a1_e",
                "gunner 20.0 18.0 P a1_e",
                "rusher 18.0 3.2 - a1_s",
                "rusher 33.0 3.6 - a1_s"
            } },
            new object[] { "a2", new[] {
                "10.3 31.7 0.6 3.4",
                "10.875 31.35 0.55 1.9",
                "26.8 31.7 0.6 3.4",
                "26.175 31.35 0.55 1.9",
                "14.0 40.3 4.0 0.6",
                "13.15 41.475 1.9 0.55",
                "25.25 35.8 0.5 1.6",
                "29.3 36.25 1.6 0.5",
                "15.0 44.5 0.3 0.3",
                "26.0 47.5 0.3 0.3",
                "13.275 46.95 0.55 1.9",
                "22.0 46.0 0.6 0.6",
                "13.5 51.1 6.0 2.2",
                "19.5 51.1 6.0 2.2",
                "25.5 51.1 6.0 2.2",
                "10.8 55.1 0.6 5.8",
                "15.0 55.3 2.0 0.6",
                "19.5 55.3 2.0 0.6",
                "24.0 55.9 2.0 0.6",
                "25.5 62.5 1.0 1.0",
                "26.5 61.5 1.0 1.0",
                "27.5 60.5 1.0 1.0",
                "28.5 59.5 1.0 1.0",
                "29.5 58.5 1.0 1.0"
            }, new[] {
                "rusher 18.0 41.5 - a2_r",
                "bolter 10.7 63.5 P a2_n",
                "bolter 22.0 63.5 P a2_n",
                "launcher 10.7 61.5 P a2_n",
                "gunner 24.5 56.8 P a2_n",
                "gunner 25.5 58.5 P a2_n",
                "turret 21.5 60.5 P a2_n",
                "bruiser 17.0 57.5 - a2_n"
            } },
            new object[] { "a3", new[] {
                "53.0 44.0 6.0 2.2",
                "57.0 44.0 1.2 1.2",
                "61.0 44.0 1.2 1.2",
                "62.25 44.0 1.2 1.2",
                "59.0 44.0 2.0 2.0",
                "51.0 50.05 2.0 9.9",
                "62.5 48.0 1.0 4.0",
                "63.5 52.5 1.0 1.0",
                "64.5 51.5 1.0 1.0",
                "46.0 39.0 2.0 1.0",
                "49.1 35.6 1.2 1.2",
                "52.0 40.7 2.0 1.0",
                "55.1 37.0 1.2 1.2",
                "58.0 39.9 2.0 1.0",
                "60.6 35.6 1.2 1.2",
                "45.5 34.5 1.0 3.0",
                "42.25 37.0 1.2 1.2",
                "36.0 52.25 6.0 5.5",
                "41.3 50.75 1.6 0.5",
                "42.0 47.6 0.6 0.6",
                "44.5 47.2 0.72 0.72",
                "34.8 46.65 1.6 0.5",
                "45.0 44.1 3.0 1.0"
            }, new[] {
                "turret 64.3 46.45 P a3_k",
                "gunner 64.5 50.0 P a3_k",
                "bolter 52.5 33.5 P a3_k",
                "launcher 63.0 42.0 P a3_k",
                "bolter 49.0 33.5 P a3_l",
                "bruiser 59.0 38.0 - a3_k",
                "bruiser 62.17 40.35 - a3_k",
                "blinker 56.5 41.6 - a3_k"
            } },
            new object[] { "a4", new[] {
                "38.0 59.3 4.0 0.6",
                "42.6 59.3 4.0 0.6",
                "45.6 59.3 0.8 0.6",
                "36.7 62.6 0.6 4.0",
                "36.7 67.2 0.6 4.0",
                "36.7 70.8 0.6 2.0",
                "40.3 61.5 0.3 0.3",
                "44.9 61.5 0.3 0.3",
                "38.6 64.9 0.3 0.3",
                "38.6 69.5 0.3 0.3",
                "46.8 70.25 1.6 0.5",
                "51.2 70.25 1.6 0.5",
                "45.75 72.8 0.5 1.6",
                "54.25 72.8 0.5 1.6",
                "46.8 76.85 1.6 0.5",
                "51.2 76.85 1.6 0.5",
                "42.275 66.95 0.55 1.9",
                "57.275 66.95 0.55 1.9",
                "43.95 80.275 1.9 0.55",
                "50.0 64.0 0.6 0.6",
                "60.0 72.0 0.72 0.72",
                "60.775 76.95 0.55 1.9",
                "40.3 77.25 1.6 0.5",
                "49.0 83.1 3.0 1.4",
                "59.5 60.45 1.8 0.9",
                "59.5 62.95 1.8 0.9",
                "56.3 58.25 1.6 0.5"
            }, new[] {
                "bolter 53.5 75.0 P a4_e",
                "gunner 46.5 68.0 P a4_e",
                "launcher 50.0 72.0 P a4_n",
                "arcstriker 60.0 74.5 - a4_n",
                "bruiser 51.5 79.0 - a4_n",
                "bruiser 56.5 69.0 - a4_e",
                "blinker 49.0 74.5 - a4_e"
            } },
            new object[] { "a5", new[] {
                "15.0 69.9 6.0 4.2",
                "23.25 69.9 5.5 4.2",
                "30.75 69.9 4.5 4.2",
                "13.5 78.5 3.0 4.0",
                "20.5 78.5 6.0 4.0",
                "28.75 78.75 5.5 3.5",
                "15.5 85.5 7.0 5.0",
                "24.75 85.5 6.5 5.0",
                "31.75 85.5 2.5 5.0",
                "20.5 91.85 7.0 2.7",
                "29.75 91.85 6.5 2.7",
                "22.5 74.25 1.0 4.5",
                "26.5 81.75 1.0 2.5",
                "16.5 81.75 1.0 2.5",
                "21.5 89.25 1.0 2.5",
                "1.8 77.5 12.4 1.0",
                "1.8 81.25 12.4 6.5",
                "18.5 72.5 1.0 1.0",
                "20.0 76.0 1.0 1.0",
                "25.5 76.0 1.0 1.0",
                "15.5 76.0 1.0 1.0",
                "24.0 81.0 1.0 1.0",
                "19.5 82.5 1.0 1.0",
                "28.5 82.5 1.0 1.0",
                "30.0 88.5 1.0 1.0",
                "4.0 69.0 1.2 1.2",
                "-3.0 75.6 0.6 0.6",
                "3.6 89.1 1.2 1.2",
                "-3.6 86.0 0.72 0.72"
            }, new[] {
                "launcher 2.15 70.15 P a5_s",
                "gunner -3.39 90.69 P a5_n",
                "bruiser 15.23 82.26 - a5_m",
                "bruiser 29.3 85.5 - a5_c",
                "blinker 20.2 89.3 - a5_c",
                "bruiser 10.5 73.5 - a5_s",
                "blinker 10.0 80.5 - a5_d",
                "bruiser 24.2 74.2 - a5_m"
            } }
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
        public void CityA1ToA5_BuildTheApprovedRoomsAndSeedTheirRobots()
        {
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(city, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string valReason), valReason);

            _host = new GameObject("MV1436 Host");
            MapBuild built = MapRuntime.Build(map, _host.transform);

            Physics.SyncTransforms(); // EditMode: collider bounds read (0,0,0) until the transforms are pushed to the physics scene

            foreach (object[] row in Rooms)
            {
                string id = (string)row[0];
                Rect area = city.Area(id).Footprint;
                string[] cover = (string[])row[1];

                var inArea = built.Cover.Where(p => p.Body != null
                    && area.Contains(new Vector2(p.Body.transform.position.x, p.Body.transform.position.z))).ToList();
                Assert.AreEqual(cover.Length, inArea.Count, id + " built cover collider count (no other cover colliders)");

                var unmatched = inArea.ToList();
                foreach (string c in cover)
                {
                    string[] t = c.Split(' ');
                    float x = F(t[0]), z = F(t[1]), w = F(t[2]), d = F(t[3]);
                    int at = unmatched.FindIndex(p =>
                    {
                        Collider col = p.Body.GetComponent<Collider>();
                        if (col == null) return false;
                        Bounds b = col.bounds;
                        return Mathf.Abs(b.center.x - x) <= 0.05f && Mathf.Abs(b.center.z - z) <= 0.05f
                            && Mathf.Abs(b.size.x - w) <= 0.05f && Mathf.Abs(b.size.z - d) <= 0.05f;
                    });
                    Assert.GreaterOrEqual(at, 0, id + " no built cover collider matches " + c + " :: " + string.Join(" | ", unmatched.Take(4).Select(p => p.Body.name + " " + (p.Body.GetComponent<Collider>() == null ? "nocol" : p.Body.GetComponent<Collider>().bounds.ToString()))));
                    unmatched.RemoveAt(at);
                }
            }

            GameObject secret = built.Cover.Select(p => p.Body).FirstOrDefault(b => b != null && b.name == "a3_c4");
            Assert.IsNotNull(secret, "a3_c4 built");
            var breakable = secret.GetComponent<DestructibleCover>();
            Assert.IsNotNull(breakable, "a3_c4 is a breakable piece");
            Assert.AreEqual("a3_c4", breakable.Id);
            breakable.Build(); // Awake does not run in EditMode; Build is what draws the secret's hairline crack
            Assert.IsNotNull(secret.transform.Find("SecretCrack"), "a3_c4 carries the secret hairline crack");

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
            int[] counts = { 7, 8, 8, 7, 8 };
            for (int r = 0; r < Rooms.Length; r++)
            {
                string id = (string)Rooms[r][0];
                string[] expected = (string[])Rooms[r][2];
                Assert.AreEqual(counts[r], expected.Length, id + " authored robot count");
                RobotEnemy[] spawned = all.Where(x => x.AreaIndex == int.Parse(id.Substring(1))).ToArray();
                Assert.AreEqual(expected.Length, spawned.Length, id + " spawned robot count");

                var unmatched = spawned.ToList();
                foreach (string e in expected)
                {
                    string[] t = e.Split(' ');
                    float x = F(t[1]), z = F(t[2]);
                    bool post = t[3] == "P";
                    RobotEnemy hit = unmatched.FirstOrDefault(rb =>
                        rb.Kind.ToString().ToLowerInvariant() == t[0]
                        && Vector2.Distance(new Vector2(rb.transform.position.x, rb.transform.position.z), new Vector2(x, z)) <= 0.1f
                        && rb.SquadId == t[4]
                        && (bool)HasPostField.GetValue(rb) == post);
                    Assert.IsNotNull(hit, id + " no spawned robot matches " + e);
                    unmatched.Remove(hit);
                }
            }
        }
    }
}
