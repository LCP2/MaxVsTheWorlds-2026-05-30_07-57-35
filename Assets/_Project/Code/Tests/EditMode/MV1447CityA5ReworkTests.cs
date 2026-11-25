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
    /// MV-1447: City a5 reworked (each pylon behind its relay, relay fodder, 3 wedges banking onto the posted
    /// guns). One test (Rule 1), failing on the pre-change main (MV-1436's a5: 29 cover pieces, 8 wedges
    /// facing the wrong way, 8 robots). Tier 2: builds the real City through MapRuntime.Build, measures the
    /// BUILT cover colliders, the built wedge hypotenuse normals, and the robots the real garrison seeding
    /// path spawned.
    /// </summary>
    public sealed class MV1447CityA5ReworkTests
    {
        // "x z width depth", solid corner ("" for non-wedge)
        private static readonly string[][] Cover =
        {
            new[] { "15.0 69.9 6.0 4.2", "" }, new[] { "23.25 69.9 5.5 4.2", "" }, new[] { "30.75 69.9 4.5 4.2", "" },
            new[] { "13.5 78.5 3.0 4.0", "" }, new[] { "20.5 78.5 6.0 4.0", "" }, new[] { "28.75 78.75 5.5 3.5", "" },
            new[] { "15.5 85.5 7.0 5.0", "" }, new[] { "24.75 85.5 6.5 5.0", "" }, new[] { "31.75 85.5 2.5 5.0", "" },
            new[] { "20.5 91.85 7.0 2.7", "" }, new[] { "29.75 91.85 6.5 2.7", "" }, new[] { "22.5 74.25 1.0 4.5", "" },
            new[] { "26.5 81.75 1.0 2.5", "" }, new[] { "16.5 81.75 1.0 2.5", "" }, new[] { "21.5 89.25 1.0 2.5", "" },
            new[] { "8.5 72.4 1.0 9.2", "" }, new[] { "3.0 77.5 10.0 1.0", "" }, new[] { "3.0 81.25 10.0 6.5", "" },
            new[] { "4.0 75.6 1.2 1.2", "" }, new[] { "-3.3 86.2 0.72 0.72", "" }, new[] { "3.6 89.2 1.2 1.2", "" },
            new[] { "26.5 68.5 1.0 1.0", "SW" }, new[] { "25.5 76.5 1.0 1.0", "SW" }, new[] { "11.5 82.5 1.0 1.0", "SW" },
        };

        // "kind x z P|- squad"
        private static readonly string[] Robots =
        {
            "launcher 6.0 68.3 P a5_s", "gunner 2.0 91.0 P a5_n", "rusher 2.9 74.6 - a5_s", "rusher -3.3 72.8 - a5_s",
            "rusher 5.6 86.2 - a5_n", "rusher 11.0 86.6 - a5_n", "bruiser 27.5 69.6 - a5_h",
            "bruiser 24.75 81.05 - a5_h", "blinker 13.6 81.8 - a5_h",
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
        public void CityA5_BuildsTheReworkedCoverWedgesAndRobots()
        {
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(city, out MapData map, out string loadReason), loadReason);
            Assert.IsTrue(MapValidation.Validate(map, out string valReason), valReason);

            _host = new GameObject("MV1447 Host");
            MapBuild built = MapRuntime.Build(map, _host.transform);
            Physics.SyncTransforms(); // EditMode: collider bounds read (0,0,0) until the transforms are pushed

            Rect area = city.Area("a5").Footprint;
            var inArea = built.Cover.Where(p => p.Body != null
                && area.Contains(new Vector2(p.Body.transform.position.x, p.Body.transform.position.z))).ToList();
            Assert.AreEqual(Cover.Length, inArea.Count, "a5 built cover collider count");

            foreach (string[] c in Cover)
            {
                string[] t = c[0].Split(' ');
                float x = F(t[0]), z = F(t[1]), w = F(t[2]), d = F(t[3]);
                int at = inArea.FindIndex(p =>
                {
                    Collider col = p.Body.GetComponent<Collider>();
                    if (col == null) return false;
                    Bounds b = col.bounds;
                    return Mathf.Abs(b.center.x - x) <= 0.05f && Mathf.Abs(b.center.z - z) <= 0.05f
                        && Mathf.Abs(b.size.x - w) <= 0.05f && Mathf.Abs(b.size.z - d) <= 0.05f;
                });
                Assert.GreaterOrEqual(at, 0, "no built cover collider matches " + c[0]);

                if (c[1] != "")
                {
                    // The hypotenuse is the one built face whose normal is diagonal in XZ; it must point into
                    // the quadrant opposite the solid corner (SW wedge -> NE).
                    GameObject body = inArea[at].Body;
                    Mesh mesh = body.GetComponent<MeshFilter>().sharedMesh;
                    Vector3 diag = mesh.normals.Select(n => body.transform.TransformDirection(n))
                        .FirstOrDefault(n => Mathf.Abs(n.y) < 0.01f && Mathf.Abs(n.x) > 0.5f && Mathf.Abs(n.z) > 0.5f);
                    Assert.AreNotEqual(Vector3.zero, diag, c[0] + " has no diagonal face");
                    float sx = c[1].EndsWith("W") ? -1f : 1f, sz = c[1].StartsWith("S") ? -1f : 1f;
                    Assert.Greater(diag.x * -sx, 0.5f, c[0] + " " + c[1] + " hypotenuse normal x " + diag);
                    Assert.Greater(diag.z * -sz, 0.5f, c[0] + " " + c[1] + " hypotenuse normal z " + diag);
                }
                inArea.RemoveAt(at);
            }

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

            RobotEnemy[] spawned = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(x => x.AreaIndex == 5).ToArray();
            Assert.AreEqual(Robots.Length, spawned.Length, "a5 spawned robot count");
            var unmatched = spawned.ToList();
            foreach (string e in Robots)
            {
                string[] t = e.Split(' ');
                Vector2 at = new Vector2(F(t[1]), F(t[2]));
                bool post = t[3] == "P";
                RobotEnemy hit = unmatched.FirstOrDefault(rb =>
                    rb.Kind.ToString().ToLowerInvariant() == t[0]
                    && Vector2.Distance(new Vector2(rb.transform.position.x, rb.transform.position.z), at) <= 0.1f
                    && rb.SquadId == t[4]
                    && (bool)HasPostField.GetValue(rb) == post);
                Assert.IsNotNull(hit, "no spawned robot matches " + e);
                unmatched.Remove(hit);
            }
        }
    }
}
