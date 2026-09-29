using System.Collections.Generic;
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
    /// MV-1027 — Lee (TestFlight v0.11.1): World 2 area 13 ("Trolley Yard (floor)") reads almost empty.
    /// Root cause, proven by this test's own reproduction below: a13 authors 61 floor garrison robots
    /// alongside 12 Replicators (24 exit-zone circles over a 30x26 m floor), and MV-998's
    /// <see cref="RobotEnemy.BeginDormant()"/> exit-zone gate re-routed a large share of them into
    /// <see cref="RobotEnemy.State.Mustering"/> the instant <see cref="AreaAccumulationDirector.PlacePendingGarrison"/>
    /// placed them — scattering Lee's own authored layout instead of leaving it standing where he drew
    /// it. Fix: authored level data is authority (<c>CC_AUTONOMY.md</c>, "Decide") —
    /// <see cref="RobotEnemy.BeginDormant(bool)"/>'s new <c>authoredSlot</c> argument (passed true from
    /// both <c>PlacePendingGarrison</c> and <c>SeedGarrison</c>) skips the exit-zone reroute entirely at
    /// placement, and <see cref="RobotEnemy.CentralWakeCheck"/>'s own Change-3 safety sweep skips it too.
    ///
    /// Same idiom as MV1002DeckDeathRespawnGarrisonTests: walks the REAL shipped World 2 route (area1 ..
    /// area12 current, then crosses into area13 and breaks its own gate — exactly how a real run places
    /// a13's garrison, via <c>FillArea(12)</c>'s own <c>PlacePendingGarrison(13)</c> head start) against
    /// the real <c>world2_config</c>. a13's 12 real built Replicators are registered with
    /// <see cref="FactoryExitZones"/> by hand, same reasoning MV998FactoryExitZoneTests documents:
    /// Awake/OnEnable never fire from AddComponent outside Play mode, so the real placement pipeline
    /// never does this in EditMode either — without it the exit-zone geometry this ticket is about
    /// would never exist for the reroute (pre-fix) or the exemption (post-fix) to be proven against.
    ///
    /// The player object is destroyed immediately after the walk, before any ticking — MV998's own Part
    /// 2 establishes why: with a live "Player"-tagged object and a suppressed main camera (this fixture
    /// has neither a rig nor a built HUD), <see cref="RobotEnemy"/>'s IsOnScreen "fails open" (true) with
    /// no camera to resolve, so a real player standing in the open 30x26m floor would legitimately wake
    /// nearby garrison via the UNRELATED MV-478/603/927 AmbushWake path — a confound this ticket's own
    /// exit-zone rule has nothing to do with. Removing the player before ticking keeps every remaining
    /// Dormant->Mustering transition attributable to the ONE rule under test.
    ///
    /// Ticks every one of a13's placed robots for a real 10 simulated seconds (600 frames at dt 1/60,
    /// vertical velocity zeroed each tick — same defensive idiom MV998FactoryExitZoneTests documents;
    /// this ticket's own exit-zone geometry (<see cref="FactoryExitZone"/>) flattens Y away entirely, so
    /// the assertions below only ever care about XZ), with a scheduler-cadence
    /// <see cref="RobotEnemy.CentralWakeCheck"/> sweep every <see cref="DormantWakeScheduler.TickInterval"/>
    /// simulated seconds so Change 3's own safety sweep is genuinely exercised too, not just the
    /// placement-time gate.
    ///
    /// ONE test (testing policy MV-465, Rule 1). Tier 2 throughout (Rule 2): every assertion reads a
    /// robot's own settled <c>transform.position</c>/<c>gameObject.activeSelf</c>/<c>IsAlive</c> after
    /// real placement and ticking, never an authored constant. // Guards MV-1027
    ///
    /// Must fail on 6998ce3 (this ticket's own base commit, before the fix) — see the fix comment for
    /// the captured failure output.
    /// </summary>
    public sealed class MV1027AuthoredGarrisonExemptFromMusterTests
    {
        private static readonly FieldInfo VerticalVelField =
            typeof(RobotEnemy).GetField("_verticalVel", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo UpdateMethod =
            typeof(AreaAccumulationDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _host;
        private GameObject _areaGo;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            FactoryExitZones.ResetForTests();
            DormantWakeScheduler.ResetForTests();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);

            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            if (_host != null) Object.DestroyImmediate(_host);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            FactoryExitZones.ResetForTests();
            DormantWakeScheduler.ResetForTests();
            DevTuning.Reset();
        }

        /// <summary>Same idiom as MV1002DeckDeathRespawnGarrisonTests.ProbePositionFor — a world
        /// position that resolves, via <see cref="MapData.ZoneAt(float,float,float)"/>, to
        /// <paramref name="zone"/>: its own centre at floor height for a level-0 room. Every area from 1
        /// to 13 is level-0, so the level&gt;0 Deck/Hatch branch never triggers here.</summary>
        private static Vector3 ProbePositionFor(MapZone zone)
        {
            Assert.IsNotNull(zone, "setup failure: ProbePositionFor given a null zone");
            return new Vector3(zone.x, 0.5f, zone.z);
        }

        private static void InvokeTick(RobotEnemy e, float dt)
        {
            e.Tick(dt);
            VerticalVelField.SetValue(e, 0f);
        }

        [Test]
        public void AuthoredA13GarrisonSurvivesExitZonePlacementAndTheSafetySweep()
        {
            const float dt = 1f / 60f;
            const int frames = 600; // MV-1027's own "tick 10 s at dt 1/60"
            const float schedulerInterval = DormantWakeScheduler.TickInterval;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            WorldArea a13 = cfg.AreaByIndex(13);
            Assert.IsNotNull(a13, "setup failure: World 2 must still author area 13");

            _host = new GameObject("MV1027 Host");
            MapBuild built = MapRuntime.Build(map, _host.transform);

            // a13's own real built Replicators, registered with FactoryExitZones by hand — see this
            // class's own doc comment for why (Awake/OnEnable never fire from AddComponent outside Play
            // mode). Without this, the exit-zone geometry the whole ticket is about would never exist.
            WorldReplicator[] replicatorsAuthored = a13.replicators ?? System.Array.Empty<WorldReplicator>();
            Assert.AreEqual(12, replicatorsAuthored.Length,
                "setup failure: a13 must still author its own 12 Replicators for this reproduction to mean anything");
            foreach (WorldReplicator r in replicatorsAuthored)
            {
                Assert.IsTrue(built.Actors.TryGetValue(r.id, out GameObject repGo) && repGo != null,
                    $"setup failure: a13's authored Replicator '{r.id}' must have been built");
                Replicator replicator = repGo.GetComponent<Replicator>();
                Assert.IsNotNull(replicator, $"setup failure: '{r.id}' must carry a Replicator component");
                FactoryExitZones.Register(replicator);
            }

            // Same "isolate garrison placement from ambient top-up" idiom as MV1002/MV559 — garrison
            // placement bypasses the queue's own cap entirely, so this just keeps ambient overflow from
            // padding (or, worse, occupying pooled instances) on top of what this test actually measures.
            DevTuning.MaxActiveRobots = 1f;

            _areaGo = new GameObject("Area Accumulation");
            var areaDirector = _areaGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);
            areaDirector.Configure(map, built.Cover);

            _playerGo = new GameObject("Player") { tag = "Player" };

            // --- walk the real route: area1 .. area12 current, then cross into area13 and break its gate ---
            for (int index = 2; index <= 13; index++)
            {
                _playerGo.transform.position = ProbePositionFor(map.Zone($"area{index}"));
                UpdateMethod.Invoke(areaDirector, null);
                areaDirector.EnterArea(index);
            }
            Assert.That(areaDirector.PhysicalArea, Is.EqualTo(13), "precondition: Max is physically standing in a13");
            Assert.That(areaDirector.CurrentArea, Is.EqualTo(13), "precondition: a13's own gate must have broken");

            // Player removed before any ticking — see this class's own doc comment on why (keeps the
            // unrelated AmbushWake sight/on-screen path from confounding this ticket's own assertions).
            Object.DestroyImmediate(_playerGo);
            _playerGo = null;

            int seedCount = Garrison.SeedCount(13, cfg);
            Garrison.Seed[] slots = Garrison.SeedSlots(a13, seedCount, cfg);
            Assert.That(slots.Length, Is.GreaterThan(0), "setup failure: a13 must author a non-zero garrison");

            RobotEnemy[] garrison = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(r => r.AreaIndex == 13).ToArray();
            Assert.That(garrison.Length, Is.EqualTo(slots.Length),
                "setup failure: a13's own PlacePendingGarrison head start must have placed its full authored roster");

            // --- tick 10 simulated seconds, with a scheduler-cadence Change-3 sweep mixed in ---
            float schedulerAccumulator = 0f;
            for (int f = 0; f < frames; f++)
            {
                foreach (RobotEnemy r in garrison) InvokeTick(r, dt);

                schedulerAccumulator += dt;
                if (schedulerAccumulator < schedulerInterval) continue;
                schedulerAccumulator -= schedulerInterval;
                foreach (RobotEnemy r in garrison)
                    if (r.IsDormant) r.CentralWakeCheck(schedulerInterval);
            }

            MapZone a13Zone = map.Zone("area13");
            Assert.IsNotNull(a13Zone, "setup failure: area13's own zone must exist");

            var positions = new List<Vector2>();
            foreach (RobotEnemy r in garrison)
            {
                if (!r.IsAlive || !r.gameObject.activeSelf) continue;
                Vector3 p = r.transform.position;
                if (!a13Zone.Contains(p.x, p.z)) continue;
                positions.Add(new Vector2(p.x, p.z));
            }

            // Excludes each robot from its own count — a13's own authored layout legitimately packs
            // tight formations (e.g. its sludger nest around (298.5-299.5, 103.5-105.43), 3 authored
            // neighbours apiece, ~1 m apart by design). What this guards against is an exit-zone reroute
            // piling OTHER robots on top of one that's merely resting where Lee put it, not a robot's
            // own authored spot counting against itself.
            int maxOthersWithin1m = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                int count = 0;
                for (int j = 0; j < positions.Count; j++)
                {
                    if (i == j) continue;
                    if (Vector2.Distance(positions[i], positions[j]) <= 1f) count++;
                }
                if (count > maxOthersWithin1m) maxOthersWithin1m = count;
            }

            float requiredMinimum = 0.9f * seedCount;
            Assert.That(positions.Count, Is.GreaterThanOrEqualTo(requiredMinimum),
                $"MV-1027: only {positions.Count}/{seedCount} of a13's authored garrison are alive, not-parked " +
                "and still inside a13's own footprint after 10s — the MV-998 exit-zone rule must never re-route " +
                "an authored garrison slot off the spot Lee drew");
            Assert.That(maxOthersWithin1m, Is.LessThanOrEqualTo(3),
                $"MV-1027: {maxOthersWithin1m} OTHER robots ended up within 1 m of a single one of a13's garrison " +
                "— an exit-zone reroute must never pile robots onto a handful of muster slots");
        }
    }
}
