using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-417: some Backyard areas showed no robots at all. Root cause confirmed by static reading
    /// (this repo's autonomy contract forbids authoring PlayMode tests or harnesses, so no live 18-area
    /// playtest log backs this — see the ticket comment): AreaAccumulationDirector.Spawn() always
    /// placed a released robot at CurrentArea, the FURTHEST area reached so far, never the area it was
    /// actually queued for. Once the queue's concurrent-cap forced part of an area's population to stay
    /// queued past that area's instant fill (MV-411 grew the level from 3 sheds/10 areas to 6 sheds/20
    /// areas, so the cap binds far more often now), that overflow released later, after Update() had
    /// already advanced CurrentArea past it, and materialised in whatever area the player had since
    /// reached instead of the room it was meant for.
    ///
    /// EditMode only. Reflection drives Update() directly (same idiom as SentinelAreaCrossingTests)
    /// since Unity does not invoke a plain MonoBehaviour's Update outside Play mode, and TakeDamage
    /// kills a robot to free the queue's one concurrent-cap slot without needing the OnEnable/OnDisable
    /// lifecycle that also never runs here (see AreaAccumulationWorldConfigTests's note on
    /// RobotEnemy.ActiveCount staying 0 in EditMode). A single-kind (Rusher-only) authored composition
    /// keeps the enemy pool deterministic: the one robot killed is the only thing in that pool, so the
    /// next release is guaranteed to reuse its exact GameObject, letting the test read the answer
    /// straight off that instance's own transform.
    /// </summary>
    public sealed class MV417OverflowPlacementTests
    {
        private GameObject _directorGo;
        private GameObject _playerGo;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);

            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        private static void InvokeUpdate(AreaAccumulationDirector director) =>
            typeof(AreaAccumulationDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);

        private static void ForceReleaseTimerReady(AreaAccumulationDirector director) =>
            typeof(AreaAccumulationDirector).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(director, 999f);

        private static WorldConfig TwoAreaWorld(int area1Rushers, int area2Rushers)
        {
            return new WorldConfig
            {
                dials = new WorldDials { areaCount = 2, baseThreat = 1f, threatGrowth = 0f, pacingRhythm = new[] { 1f, 1f } },
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
                        composition = new WorldComposition { rusher = area1Rushers },
                    },
                    new WorldArea
                    {
                        id = "a2", index = 2, role = "normal",
                        origin = new WorldAreaOrigin { x = -10f, z = 20f }, size = new WorldAreaSize { w = 20f, d = 20f },
                        composition = new WorldComposition { rusher = area2Rushers },
                    },
                    new WorldArea
                    {
                        id = "boss", index = 3, role = "boss+exit",
                        origin = new WorldAreaOrigin { x = -10f, z = 40f }, size = new WorldAreaSize { w = 20f, d = 20f },
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
                        id = "g1", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "a2", wall = "S", pos = 0.5f },
                    },
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "a2", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
            };
        }

        [Test]
        public void OverflowRobot_ReleasedAfterPlayerAdvances_LandsInTheAreaItWasQueuedFor()
        {
            WorldConfig cfg = TwoAreaWorld(area1Rushers: 3, area2Rushers: 2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone area1 = map.Zone("area1");
            MapZone area2 = map.Zone("area2");
            Assert.IsNotNull(area1);
            Assert.IsNotNull(area2);

            // Force area 1's 3-robot population to overflow a 1-robot concurrent cap, reproducing what
            // MV-411's much larger level made common at the real (24-robot field-wide) cap.
            DevTuning.MaxActiveRobots = 1f;

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            Assert.AreEqual(1, director.ActiveCount, "only one robot fits under the forced 1-robot cap");
            Assert.AreEqual(2, director.QueuedCount, "area 1's other two Rushers must still be queued");

            RobotEnemy[] instantFilled = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None);
            Assert.AreEqual(1, instantFilled.Length);
            RobotEnemy activeRobot = instantFilled[0];
            Assert.IsTrue(area1.Contains(activeRobot.transform.position.x, activeRobot.transform.position.z),
                "area 1's own instant-fill robot must land in area 1");

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = area1.Center;
            InvokeUpdate(director); // establishes _target

            // Max opens the gate into area 2 (AreaGate.Opened -> EnterArea) and walks through, same as
            // SentinelAreaCrossingTests. CurrentArea now reads 2 while area 1's overflow is still queued
            // behind the field-wide cap.
            director.EnterArea(2);
            _playerGo.transform.position = area2.Center;
            InvokeUpdate(director);

            Assert.AreEqual(4, director.QueuedCount, "area 2's two Rushers must have queued behind area 1's leftover two");

            // Free the one concurrent-cap slot area 1's instant-fill robot was holding, so the next
            // Update() release actually pulls the queue's FIFO front item back off: area 1's own leftover.
            activeRobot.TakeDamage(new DamageInfo(9999f, activeRobot.transform.position, Vector3.forward, Team.Player));
            Assert.AreEqual(RobotEnemy.State.Dead, activeRobot.Current);

            ForceReleaseTimerReady(director);
            InvokeUpdate(director);

            Assert.AreEqual(3, director.QueuedCount, "one queued robot must have released");

            // Single-kind (Rusher-only) composition means the pool this release drew from held exactly
            // one entry: the robot just killed. Take() always drains the pool before creating a new
            // instance, so this exact GameObject is what the release actually placed.
            Assert.AreNotEqual(RobotEnemy.State.Dead, activeRobot.Current,
                "the freed pool slot must have been reused by the next release, reviving this instance");
            Vector3 releasedPos = activeRobot.transform.position;
            Assert.IsTrue(area1.Contains(releasedPos.x, releasedPos.z),
                "MV-417: an overflow robot queued for area 1 must land in area 1 even after CurrentArea " +
                "has advanced to area 2 - before the fix this always spawned at CurrentArea, leaving " +
                "area 1 permanently empty from the player's point of view");
            Assert.IsFalse(area2.Contains(releasedPos.x, releasedPos.z),
                "the bug this guards against: area 1's overflow must not land in area 2 just because the player moved on");
        }
    }
}
