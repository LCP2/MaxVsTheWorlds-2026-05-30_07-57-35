using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-363 — robots concealed behind cover stay dormant (no path, no fire, no telegraph) until
    /// each individually sees Max (MV-603 retired the group chain-wake this file used to also cover).
    /// EditMode only (contract: PlayMode tests are never authored in this repo): the dormant/alert
    /// transitions are driven directly through
    /// RobotEnemy's public API plus reflection into its private Tick* methods, the same idiom
    /// <c>WaterBlasterGateDamageTests</c> uses for <c>FireTick</c> — Unity does not invoke
    /// Awake/OnEnable/Update for a plain MonoBehaviour outside Play mode (see
    /// <c>EnemyFriendlyFireTests.NewEnemy</c>'s note), so nothing here can rely on the real frame loop.
    /// </summary>
    public sealed class MV363DormantRobotTests
    {
        private static RobotEnemy NewEnemy(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.ResetState(); // EditMode has no Awake/OnEnable lifecycle — init explicitly
            return e;
        }

        private static void InvokeTickDormant(RobotEnemy e) =>
            typeof(RobotEnemy).GetMethod("TickDormant", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, null);

        private static void InvokeTickAlert(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickAlert", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        // TickAlert (like every other Tick* method) reads _stateTimer but never advances it — that's
        // Update()'s job, done once before the switch. Reflection bypasses Update() entirely, so a
        // test driving TickAlert directly has to advance the timer itself first.
        private static void SetStateTimer(RobotEnemy e, float value) =>
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(e, value);

        // ------------------------------------------------------------------- RobotEnemy state machine

        [Test]
        public void BeginDormant_PutsARobotIntoTheDormantState()
        {
            RobotEnemy e = NewEnemy("Enemy");
            try
            {
                Assert.AreEqual(RobotEnemy.State.Chase, e.Current, "fresh robots start in Chase");
                e.BeginDormant();
                Assert.AreEqual(RobotEnemy.State.Dormant, e.Current);
                Assert.IsTrue(e.IsDormant);
            }
            finally { Object.DestroyImmediate(e.gameObject); }
        }

        [Test]
        public void TickDormant_StaysAsleep_WithoutSight()
        {
            RobotEnemy e = NewEnemy("Enemy");
            try
            {
                e.BeginDormant();
                e.Sight.Tick(false, Vector3.zero, 0.1f); // still no sight-line
                InvokeTickDormant(e);
                Assert.IsTrue(e.IsDormant, "AC2: a dormant robot must not wake on its own without sight");
            }
            finally { Object.DestroyImmediate(e.gameObject); }
        }

        [Test]
        public void TickDormant_WakesItself_TheMomentItsOwnSightLineOpens()
        {
            // No camera in this scene (suppressed in SetUp) - MV-478's fail-open rule (AC8) means
            // "on screen" reads true, so sight alone is enough to wake it here.
            RobotEnemy e = NewEnemy("Enemy");
            try
            {
                e.BeginDormant();
                e.Sight.Tick(true, Vector3.forward * 5f, 0.1f); // a sight-line opened this tick
                InvokeTickDormant(e);
                Assert.AreEqual(RobotEnemy.State.Alert, e.Current,
                    "AC1: dormant ends the moment it has line of sight to Max");
            }
            finally { Object.DestroyImmediate(e.gameObject); }
        }

        [Test]
        public void TickDormant_StaysAsleep_WithClearSightButOutsideTheCameraFrustum()
        {
            // MV-478's actual bug: LineOfSight is symmetric geometry, so "sight clear" alone woke
            // every dormant robot the instant the player's OWN raycast to it cleared - whether or not
            // the player had ever looked at it. This proves the camera-frustum half of the AND is
            // actually enforced, not just documented.
            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            RobotEnemy e = NewEnemy("Enemy");
            try
            {
                Camera cam = cameraGo.AddComponent<Camera>();
                cam.transform.position = Vector3.zero;
                cam.transform.rotation = Quaternion.identity; // looks down +Z

                e.transform.position = new Vector3(0f, 0f, -20f); // behind the camera - outside the frustum
                e.BeginDormant();
                e.Sight.Tick(true, Vector3.forward * 5f, 0.1f); // sight-line clear
                InvokeTickDormant(e);

                Assert.AreEqual(RobotEnemy.State.Dormant, e.Current,
                    "AC3: a clear sight-line must not wake a robot the camera frustum doesn't cover");
            }
            finally
            {
                Object.DestroyImmediate(e.gameObject);
                Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void Activate_MovesToAlert_NotStraightToChase()
        {
            RobotEnemy e = NewEnemy("Enemy");
            try
            {
                e.BeginDormant();
                e.Activate();
                Assert.AreEqual(RobotEnemy.State.Alert, e.Current,
                    "Lee, 12 Aug: give the player a beat to react before the robot actually joins the chase");
            }
            finally { Object.DestroyImmediate(e.gameObject); }
        }

        [Test]
        public void Activate_OnANonDormantRobot_IsANoOp()
        {
            RobotEnemy e = NewEnemy("Enemy"); // fresh: Current == Chase
            try
            {
                e.Activate();
                Assert.AreEqual(RobotEnemy.State.Chase, e.Current,
                    "Activate() must be idempotent so a caller can call it unconditionally " +
                    "(e.g. ActivateGarrisonFor on every pre-placed member) without checking who's still asleep");
            }
            finally { Object.DestroyImmediate(e.gameObject); }
        }

        [Test]
        public void TickAlert_JoinsTheChase_OnceTheWakeBeatElapses()
        {
            RobotEnemy e = NewEnemy("Enemy");
            try
            {
                e.BeginDormant();
                e.Activate();
                SetStateTimer(e, 10f); // comfortably past alertTime
                InvokeTickAlert(e, 0f);
                Assert.AreEqual(RobotEnemy.State.Chase, e.Current);
            }
            finally { Object.DestroyImmediate(e.gameObject); }
        }

        // ------------------------------------------------------------------- ConcealmentBias (pure)

        private static MapZone Zone20x20() => new MapZone { id = "z", type = "open", x = 0f, z = 0f, width = 20f, depth = 20f };

        [Test]
        public void TryBehindDeepestCover_PicksThePieceFarthestFromTheDoor()
        {
            var near = new ArenaCover("Near", new Vector2(0f, 1f), new Vector3(2f, 1.5f, 2f), CoverShape.Box);
            var deep = new ArenaCover("Deep", new Vector2(1f, 4f), new Vector3(2f, 1.5f, 2f), CoverShape.Box);

            bool found = ConcealmentBias.TryBehindDeepestCover(
                new[] { near, deep }, Zone20x20(), new Vector3(0f, 0f, 1f), edgeMargin: 3f, out Vector2 point);

            Assert.IsTrue(found);
            Assert.AreEqual(deep.CenterXz.x, point.x, 1e-3f, "must anchor off the deep piece, not the near one");
            Assert.Greater(point.y, deep.CenterXz.y, "must land past the cover's own centre, on the far side from the door");
        }

        [Test]
        public void TryBehindDeepestCover_IgnoresCoverOutsideTheZone()
        {
            var outside = new ArenaCover("Outside", new Vector2(0f, 500f), new Vector3(2f, 1.5f, 2f), CoverShape.Box);

            bool found = ConcealmentBias.TryBehindDeepestCover(
                new[] { outside }, Zone20x20(), new Vector3(0f, 0f, 1f), edgeMargin: 3f, out _);

            Assert.IsFalse(found, "a cover piece outside the zone must never be picked as its anchor");
        }

        [Test]
        public void TryBehindDeepestCover_ReturnsFalse_WhenTheRoomHasNoCover()
        {
            bool found = ConcealmentBias.TryBehindDeepestCover(
                new ArenaCover[0], Zone20x20(), new Vector3(0f, 0f, 1f), edgeMargin: 3f, out _);

            Assert.IsFalse(found, "callers must fall back to the ordinary far-band placement");
        }

        // ------------------------------------------------------------------- AreaAccumulationDirector wiring

        private GameObject _directorGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            // MV-478: TickDormant now reads Camera.main. See CameraTestUtil - an EditMode run still
            // has whatever scene the Editor had open at launch loaded, so Camera.main is not reliably
            // absent just because a given test never created a camera of its own.
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        private static WorldConfig OneAreaWorld(int rusherCount)
        {
            return new WorldConfig
            {
                dials = new WorldDials { areaCount = 1, baseThreat = 1f, threatGrowth = 0f, pacingRhythm = new[] { 1f } },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", index = 0, role = "entry",
                        origin = new WorldAreaOrigin { x = -2f, z = -6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "a1", index = 1, role = "normal",
                        origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { rusher = rusherCount },
                    },
                    new WorldArea
                    {
                        id = "boss", index = 2, role = "boss+exit",
                        origin = new WorldAreaOrigin { x = -10f, z = 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
                    },
                },
                gates = new[]
                {
                    new WorldGate
                    {
                        id = "g0", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                    },
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
            };
        }

        [Test]
        public void FillArea_SparesAConcealedKnot_WhenTheRoomIsBigEnough()
        {
            WorldConfig cfg = OneAreaWorld(rusherCount: 6);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            RobotEnemy[] robots = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None);
            int dormantCount = 0;
            foreach (RobotEnemy r in robots) if (r.IsDormant) dormantCount++;

            Assert.AreEqual(2, dormantCount, "a 6-Rusher room must spare a 2-robot concealed knot");
        }

        [Test]
        public void FillArea_SparesNoConcealedKnot_WhenTheRoomIsTooSmall()
        {
            WorldConfig cfg = OneAreaWorld(rusherCount: 3);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            RobotEnemy[] robots = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None);
            foreach (RobotEnemy r in robots)
                Assert.IsFalse(r.IsDormant, "not every robot in an area should be hidden — a 3-robot room is too small to spare any");
        }
    }
}
