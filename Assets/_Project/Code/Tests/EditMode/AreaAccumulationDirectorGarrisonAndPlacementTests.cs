using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-417, the two legs of the fix not covered by MV417OverflowPlacementTests's per-area-cap
    /// regression tests:
    ///
    /// 1. Garrison wiring — Lee kept reproducing an empty room even after the per-area cap fix landed,
    /// because nothing had ever called <see cref="Garrison.SeedCount"/>/<see cref="Garrison.SeedPositions"/>
    /// at runtime; the whole dial was decorative. A garrison, placed synchronously and independent of
    /// the ambient queue's cap, is the only thing that can guarantee a room is populated the instant the
    /// player walks into it (comment dated 2026-08-18: "the garrison change... is now clearly the right
    /// one, not merely a good idea").
    ///
    /// 2. The on-screen spawn fallback — Lee, back in Area 5: "robots are just appearing out of
    /// nowhere." <c>AreaAccumulationDirector.TryFindSpawnPoint</c> used to silently fall back to an
    /// on-screen candidate once both placement passes were exhausted; it now defers (returns the queue
    /// entry) for up to <c>MaxConsecutivePlacementFailures</c> release intervals before arriving through
    /// the door instead.
    ///
    /// EditMode only, same reflection idiom as MV417OverflowPlacementTests/SentinelAreaCrossingTests.
    /// </summary>
    public sealed class AreaAccumulationDirectorGarrisonAndPlacementTests
    {
        private GameObject _directorGo;
        private GameObject _playerGo;
        private GameObject _cameraGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            // See CameraTestUtil: an EditMode run still has whatever scene the Editor had open at
            // launch loaded (e.g. Backyard_Slice.unity's real MainCamera), so Camera.main is not
            // reliably null/absent just because a given test never created a camera of its own.
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        private static void InvokeUpdate(AreaAccumulationDirector director) =>
            typeof(AreaAccumulationDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);

        private static void ForceReleaseTimerReady(AreaAccumulationDirector director) =>
            typeof(AreaAccumulationDirector).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(director, 999f);

        /// <summary>A single combat area (a1), reached from a stub entry and leading to the boss gate -
        /// same shape as MV417OverflowPlacementTests.TwoAreaWorld, but with one real combat area so
        /// <paramref name="garrisonDensity"/> can be dialled directly.</summary>
        private static WorldConfig OneAreaWorld(int rushers, string garrisonDensity)
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
                        id = "a1", index = 1, role = "normal", garrisonDensity = garrisonDensity,
                        origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { rusher = rushers },
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

        // --- Garrison wiring (MV-417) --------------------------------------------------------------

        [Test]
        public void FillArea_SeedsGarrisonSynchronously_BypassingTheAmbientQueuesOwnCap()
        {
            WorldConfig cfg = OneAreaWorld(rushers: 8, garrisonDensity: "normal"); // NormalShare = 0.6 -> round(4.8) = 5
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            // Starve the ambient queue's own per-area cap to 1 - if garrison seeding went through the
            // ordinary release path it would be limited to 1 active robot, not 5.
            DevTuning.MaxActiveRobots = 1f;

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            Assert.AreEqual(5, director.ActiveCount,
                "the garrison (5 of 8, NormalShare=0.6) must be placed regardless of the 1-robot-per-area cap");
            Assert.AreEqual(3, director.QueuedCount, "the remainder must still be queued behind the ordinary cap");

            Vector3[] expected = Garrison.SeedPositions(cfg.AreaByIndex(1), 5);
            RobotEnemy[] active = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None);
            Assert.AreEqual(5, active.Length);

            foreach (RobotEnemy r in active)
            {
                bool matchesAnAuthoredSeedPosition = false;
                foreach (Vector3 p in expected)
                {
                    if (Mathf.Approximately(r.transform.position.x, p.x) && Mathf.Approximately(r.transform.position.z, p.z))
                    {
                        matchesAnAuthoredSeedPosition = true;
                        break;
                    }
                }
                Assert.IsTrue(matchesAnAuthoredSeedPosition,
                    $"garrison robot at {r.transform.position} must land on one of Garrison.SeedPositions's authored spots");
            }
        }

        [Test]
        public void FillArea_NoGarrisonDensity_SeedsNothing_TopUpStillGatedByTheOrdinaryCap()
        {
            WorldConfig cfg = OneAreaWorld(rushers: 5, garrisonDensity: "none");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            DevTuning.MaxActiveRobots = 2f;

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            Assert.AreEqual(2, director.ActiveCount, "no garrison - only the ordinary 2-robot-per-area cap fills instantly");
            Assert.AreEqual(3, director.QueuedCount);
        }

        [Test]
        public void RestoreArea_SeedsGarrisonAgain_WithoutDoubleCountingAgainstTheAuthoredComposition()
        {
            // 5 rushers, not 8 - RusherCap.PerLevel (MV-365) hard-caps Rushers at 10 CUMULATIVE across
            // a whole run, and re-solving this area's composition on restore queues against the same
            // running total a second time (by design - see RusherCap's own doc comment). 5 + 5 lands
            // exactly on the 10 budget without either solve clamping, isolating what this test actually
            // checks (garrison/queue accounting) from that separate, already-correct cap.
            WorldConfig cfg = OneAreaWorld(rushers: 5, garrisonDensity: "normal");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            DevTuning.MaxActiveRobots = 100f; // cap irrelevant here - this test is about the total, not release pacing

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            int firstEntryActive = director.ActiveCount;
            int firstEntryTotal = director.ActiveCount + director.QueuedCount;
            Assert.AreEqual(5, firstEntryTotal, "first entry: garrison + queued remainder == the authored composition, exactly");
            Assert.Greater(firstEntryActive, 0, "garrison must be physically present the moment first entry returns");

            director.RestoreArea(1);

            int restoredTotal = director.ActiveCount + director.QueuedCount;
            Assert.AreEqual(5, restoredTotal,
                "restore must not double-count: garrison + queued remainder must still equal the authored composition");
            Assert.AreEqual(firstEntryActive, director.ActiveCount,
                "first entry and a post-death restore must place the identical robot count - same area, same composition, " +
                "same deterministic garrison positions");
        }

        // --- On-screen spawn fallback (MV-417) ------------------------------------------------------

        [Test]
        public void Spawn_DefersWhileEveryCandidateIsOnScreen_ThenArrivesThroughTheDoorAfterThreeFailures()
        {
            // Below MinCompositionForConcealment (4) deliberately - a concealed knot (MV-363) is placed
            // via ConcealedSpawnPointInArea, which never checks on-screen at all, so a composition at or
            // above the concealment threshold would place 2 of these regardless of the camera and
            // confound this test's "every candidate defers" premise.
            WorldConfig cfg = OneAreaWorld(rushers: 3, garrisonDensity: "none");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            DevTuning.MaxActiveRobots = 10f; // cap is not the thing under test here

            MapZone zone = map.Zone("area1");
            Assert.IsNotNull(zone);

            // A huge orthographic top-down camera covering the whole zone - every random candidate in
            // the room reads as on-screen, guaranteeing both placement passes in TryFindSpawnPoint
            // exhaust every time (the "player standing in the middle of the far-side band" case from
            // the ticket).
            _cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera cam = _cameraGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 50f;
            cam.aspect = 1f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            _cameraGo.transform.position = new Vector3(zone.Center.x, 50f, zone.Center.z);
            _cameraGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = zone.Center;

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>()); // FillArea(1): 1st placement attempt, deferred

            Assert.AreEqual(0, director.ActiveCount, "attempt 1 must defer, not place, while every candidate is on-screen");
            Assert.AreEqual(3, director.QueuedCount, "the deferred entry must go back on the queue, not be lost");

            InvokeUpdate(director); // establishes _target

            ForceReleaseTimerReady(director);
            InvokeUpdate(director); // attempt 2

            Assert.AreEqual(0, director.ActiveCount, "attempt 2 must still defer - the starvation guard hasn't tripped yet");
            Assert.AreEqual(3, director.QueuedCount);

            ForceReleaseTimerReady(director);
            InvokeUpdate(director); // attempt 3 - starvation guard trips

            Assert.AreEqual(1, director.ActiveCount,
                "MV-417: after 3 consecutive on-screen-only attempts the starvation guard must place the robot " +
                "through the door rather than deferring forever or popping it in on-screen");
            Assert.AreEqual(2, director.QueuedCount);
        }
    }
}
