using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-438 — MV-427 built the death-continues-the-run mechanic but shipped it as an instant, silent
    /// teleport; Lee's own report on <c>8cb70d3</c>: "when dying, there must be a popup screen ... offering
    /// quit to main menu or continue." <see cref="DeathOverlayCopyTests"/> pins the overlay's pure copy
    /// (AC5); this class pins the orchestration change in <see cref="WorldRunner"/> — a death now only
    /// records itself and shows the overlay, and the AC1-listed side effects (area restore, gate
    /// reclose, sentinel wipe, respawn teleport) wait for <see cref="WorldRunner.Continue"/>.
    ///
    /// Must fail on 8cb70d3 (checked at HEAD before this fix, cba9c13): before this ticket,
    /// <c>WorldRunner.OnPlayerDied</c> ran the whole sequence synchronously and there was no
    /// <c>Continue</c> method or <c>HasPendingRespawn</c> to observe — <see cref="ADeath_DefersEverySideEffect_UntilContinueIsCalled"/>
    /// could not even compile against that commit.
    ///
    /// Builds a real two-area world through <see cref="MapRuntime.Build"/> (the same idiom
    /// <c>SentinelAreaCrossingTests</c>/<c>GateSolidityTests</c> already use in EditMode — no coroutine,
    /// no Play mode) rather than mocking <see cref="AreaAccumulationDirector"/>/<see cref="AreaGate"/>:
    /// both are sealed MonoBehaviours with no seam to intercept, so the only honest way to prove
    /// WorldRunner defers calling them is to give it the real things and watch their own observable
    /// state (gate.IsOpen, Sentinel.Active.Count, the robot's IsAlive, the player's transform, and
    /// AreaAccumulationDirector.CurrentArea for RestoreArea/SetCurrentArea) stay untouched until
    /// <see cref="WorldRunner.Continue"/> runs.
    ///
    /// This flow adds no coroutine at all (MV-438's own "left to you" note lets entry animation/timing
    /// go unbuilt for this ticket) — so the WaitForSeconds-vs-WaitForSecondsRealtime trap the ticket
    /// warns about (same one WeaponsScreen.Open() already carries) simply does not apply here; there is
    /// nothing for a coroutine test to catch.
    /// </summary>
    public sealed class MV438DeathOverlayTests
    {
        private GameObject _root;
        private GameObject _areaGo;
        private GameObject _playerGo;
        private GameObject _sentinelGo;
        private GameObject _robotGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            DeathRunState.Reset();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            Time.timeScale = 1f;
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            var overlay = Object.FindFirstObjectByType<DeathOverlay>();
            if (overlay != null) Object.DestroyImmediate(overlay.gameObject);

            if (_robotGo != null) Object.DestroyImmediate(_robotGo);
            if (_sentinelGo != null) Object.DestroyImmediate(_sentinelGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            if (_root != null) Object.DestroyImmediate(_root);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            DeathRunState.Reset();
            DevTuning.Reset();
        }

        /// <summary>Stub entry, then two ordinary combat areas stacked N/S — the smallest shape where a
        /// death in the SECOND area gives every one of MV-438's AC1 side effects an observable, non-zero
        /// change: after <c>EnterArea(2)</c> simulates Max having already walked into area2,
        /// <c>RespawnAreaIndex</c> resolves to area1, so <c>SetCurrentArea</c> actually LOWERS
        /// <see cref="AreaAccumulationDirector.CurrentArea"/> from 2 back to 1 — a death in area1 instead
        /// would fall back to the un-observable entry stub (index 0), which <c>SetCurrentArea</c>
        /// no-ops on. <c>RestoreAreaIndex</c> is area2 (so a robot planted there proves
        /// <c>RestoreArea</c>), and gate g1 (into area2) is the one that re-closes.</summary>
        private static WorldConfig TwoAreaWorld() => new WorldConfig
        {
            world = "MV-438 Test World",
            dials = new WorldDials
            {
                areaCount = 2, baseThreat = 1f, threatGrowth = 0f,
                pacingRhythm = new[] { 1f, 1f }, toughnessCurve = new WorldToughnessCurve(), powerupCadence = 1,
                band = new WorldBand(),
            },
            enemyTypes = new WorldEnemyTypes
            {
                small = new WorldEnemyTypeEntry { thv = 1f }, large = new WorldEnemyTypeEntry { thv = 1f },
                heavy = new WorldEnemyTypeEntry { thv = 1f }, brute = new WorldEnemyTypeEntry { thv = 1f },
            },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", index = 0, role = "entry",
                    origin = new WorldAreaOrigin { x = -2f, z = -6f }, size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", index = 1, role = "normal", name = "The Carport",
                    origin = new WorldAreaOrigin { x = -10f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                },
                new WorldArea
                {
                    id = "a2", index = 2, role = "normal", name = "The Greenhouse",
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
                    id = "g1", width = 3f, opensWith = "primary",
                    from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a2", wall = "S", pos = 0.5f },
                },
            },
        };

        private static void InvokeOnPlayerDied(WorldRunner runner) =>
            typeof(WorldRunner).GetMethod("OnPlayerDied", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(runner, null);

        // Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode (same note
        // WaterBlasterGateDamageTests/AreaGateTests carry for MV-386) — MapRuntime.BuildAreaGate builds
        // the gate via a plain AddComponent<AreaGate>() with no manual Awake call of its own, so a gate
        // built through it in EditMode still has a null DestructibleHealth (and every ForceOpen/TakeDamage
        // call is a silent no-op) until something drives Awake directly.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static RobotEnemy NewRobotAt(Vector3 position)
        {
            var go = new GameObject("MV-438 Robot");
            go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            robot.ResetState();
            go.transform.position = position;
            return robot;
        }

        [Test]
        public void ADeath_DefersEverySideEffect_UntilContinueIsCalled()
        {
            WorldConfig cfg = TwoAreaWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _root = new GameObject("WorldRunner Test Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);

            _areaGo = new GameObject("Area Accumulation");
            var areaDirector = _areaGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);
            areaDirector.Configure(map, built.Cover);

            var runner = _root.AddComponent<WorldRunner>();
            runner.Configure(cfg, map, built, areaDirector);

            // Gate g1 (into area2) must already be open for Reclose() to have anything to do — same
            // precondition Reclose()'s own doc comment states ("only ever called on a gate that has
            // already opened at least once"). Simulates Max having broken it to get into area2 at all.
            var gate = built.Actors["g1"].GetComponent<AreaGate>();
            InvokeAwake(gate);
            gate.ForceOpen();
            Assert.IsTrue(gate.IsOpen, "precondition: gate g1 must be open before Max can have died in area2");

            MapZone area2 = map.Zone("area2");
            Assert.IsNotNull(area2, "world must have an area2 zone for this test to place Max/the robot in");

            _playerGo = new GameObject("Player") { tag = "Player" };
            Vector3 deathPosition = area2.Center;
            _playerGo.transform.position = deathPosition;

            _sentinelGo = new GameObject("Sentinel");
            _sentinelGo.AddComponent<Sentinel>().Init(area2.Center, 200f, range: 7f, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
            Assert.That(Sentinel.Active.Count, Is.EqualTo(1), "precondition: one deployed sentinel");

            RobotEnemy robot = NewRobotAt(area2.Center);
            _robotGo = robot.gameObject;
            Assert.IsTrue(robot.IsAlive, "precondition: the planted robot starts alive");

            // Configure() already put the director in area1; Max dying IN area2 means he must already
            // have walked into it, same as a real run's gate-open/position-crossing hand-off would do.
            areaDirector.EnterArea(2);
            Assert.That(areaDirector.CurrentArea, Is.EqualTo(2), "precondition: Max has already entered area2");

            // --- the death itself: only this much should happen synchronously ---
            InvokeOnPlayerDied(runner);

            Assert.IsTrue(runner.HasPendingRespawn, "a death must leave a respawn pending until CONTINUE");
            Assert.That(DeathRunState.DeathsTaken, Is.EqualTo(1), "the death itself must still be recorded immediately");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "a death must freeze the game the instant it happens");

            // --- AC1: nothing else has happened yet ---
            Assert.IsTrue(gate.IsOpen, "AC1: Reclose() must not run before CONTINUE");
            Assert.That(Sentinel.Active.Count, Is.EqualTo(1), "AC1: Sentinel.DestroyAllActive() must not run before CONTINUE");
            Assert.IsTrue(robot.IsAlive, "AC1: RestoreArea() must not despawn area2's robots before CONTINUE");
            Assert.That(_playerGo.transform.position, Is.EqualTo(deathPosition), "AC1: RespawnPlayer() must not move Max before CONTINUE");
            Assert.That(areaDirector.CurrentArea, Is.EqualTo(2), "AC1: SetCurrentArea() must not run before CONTINUE");

            // --- CONTINUE tapped ---
            runner.Continue();

            Assert.IsFalse(runner.HasPendingRespawn, "CONTINUE must clear the pending respawn");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "AC2: CONTINUE must restore Time.timeScale to 1");
            Assert.IsFalse(gate.IsOpen, "AC2: CONTINUE must reclose gate g1, the gate into the death area");
            Assert.That(Sentinel.Active.Count, Is.EqualTo(0), "AC2: CONTINUE must run Sentinel.DestroyAllActive()");
            Assert.IsFalse(robot.IsAlive, "AC2: CONTINUE must run RestoreArea(), despawning area2's old robot");
            Assert.AreNotEqual(deathPosition, _playerGo.transform.position, "AC2: CONTINUE must run RespawnPlayer(), moving Max");
            Assert.That(areaDirector.CurrentArea, Is.EqualTo(1), "AC2: CONTINUE must run SetCurrentArea(1), the respawn area");

            // A second CONTINUE (e.g. a stray extra tap) must be inert, not re-run the sequence.
            Vector3 afterFirstContinue = _playerGo.transform.position;
            runner.Continue();
            Assert.That(_playerGo.transform.position, Is.EqualTo(afterFirstContinue), "a second CONTINUE must be a no-op");
        }
    }

    /// <summary>AC5 — the overlay's copy is a pure function of what the death actually cost, pinned
    /// without building a canvas — the same reasoning behind every other pure-function visual test in
    /// this codebase.</summary>
    public sealed class DeathOverlayCopyTests
    {
        [Test]
        public void BodyText_NamesTheRealArea_AndMentionsTheGate_WhenItRecloses()
        {
            string body = DeathOverlay.BodyText("The Greenhouse", gateRecloses: true);

            Assert.That(body, Does.Contain("The Greenhouse"), "must read the real WorldArea.name, not a hardcoded label");
            Assert.That(body, Does.Contain("gate"), "must say the gate re-closed when it actually did (RespawnPlan.RecloseGate)");
        }

        [Test]
        public void BodyText_NeverClaimsTheGateReclosed_ForTheBossRoomEdgeCase()
        {
            string body = DeathOverlay.BodyText("Compost Clearing", gateRecloses: false);

            Assert.That(body, Does.Contain("Compost Clearing"));
            Assert.That(body, Does.Not.Contain("gate"),
                "the boss gate never re-closes (RespawnPlanner's edge case 2) -- saying it did would be false");
        }

        [Test]
        public void DeathsLine_ReflectsTheRealCount_SingularAndPlural()
        {
            Assert.That(DeathOverlay.DeathsLine(0), Does.Contain("0"));
            Assert.That(DeathOverlay.DeathsLine(1), Is.EqualTo("1 death this run"), "singular must not read '1 deaths'");
            Assert.That(DeathOverlay.DeathsLine(3), Is.EqualTo("3 deaths this run"));
        }

        [Test]
        public void Show_SetsTitleBodyAndDeathsText_AndOpensTheOverlay()
        {
            var go = new GameObject("DeathOverlay");
            var overlay = go.AddComponent<DeathOverlay>();
            try
            {
                bool continued = false;
                overlay.Show("The Carport", gateRecloses: true, deathsTaken: 2, onContinue: () => continued = true);

                Assert.IsTrue(overlay.IsOpen);
                Assert.That(overlay.TitleTextValue, Is.EqualTo("MAX IS DOWN"), "must never read DEFEAT -- the run has not ended");
                Assert.That(overlay.BodyTextValue, Does.Contain("The Carport"));
                Assert.That(overlay.DeathsTextValue, Is.EqualTo("2 deaths this run"));

                Assert.IsNotNull(overlay.ContinueButton);
                Assert.IsNotNull(overlay.QuitButton);

                overlay.ContinueButton.onClick.Invoke();
                Assert.IsTrue(continued, "CONTINUE must invoke the callback WorldRunner supplied");
                Assert.IsFalse(overlay.IsOpen, "CONTINUE must dismiss the overlay");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
