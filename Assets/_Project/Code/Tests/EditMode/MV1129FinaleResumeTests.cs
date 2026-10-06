using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1129 (the one new test, per CC_AUTONOMY's testing policy): a cold-boot RESUME into a world
    /// whose final boss is already recorded defeated must not strand the run, in every one of the
    /// ticket's three resume states. Fails on base commit d30d293: <c>WorldFinaleGate</c> has no
    /// <c>ResumeFromCheckpoint</c> method at all (CS1061) — nothing seeds <c>_finalBossDefeated</c> from
    /// a restored checkpoint, because <c>BossCensus.ApplyCheckpointDefeatedAreas</c> deliberately never
    /// re-fires <c>BossDefeated</c> (see that method's own doc comment — "restoring history, not scoring
    /// a fresh kill"), so a freshly-rebuilt gate never arms and the world's one Weapon Core never
    /// reappears, clean-up never resumes, and an already-open exit never re-opens.
    ///
    /// Drives the real resume path for each case: a real World 1 map/<c>BackyardPath</c> build,
    /// <c>SaveSystem.CaptureCheckpoint</c>/<c>RestoreCheckpoint</c>, and <c>WorldFinaleGate</c>'s own
    /// public Tick methods — never a reflection-set field under test, and never an authored constant
    /// asserted back at itself. Tier 2 (resolved values): a real <c>Pickup</c>'s resolved position and
    /// kind, <c>WorldFinaleGate.IsOpen</c>/<c>WeaponMomentResolved</c>, and a real <c>RobotEnemy.IsAlive</c>.
    /// </summary>
    public sealed class MV1129FinaleResumeTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1129-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;

            Sweep();
            ResetStatics();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            Sweep();
            ResetStatics();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static void ResetStatics()
        {
            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
        }

        /// <summary>Every transient actor/director a live finale beat or a resume can spin up, torn down
        /// between a case's "previous session" half and its "cold boot" half, and again before the next
        /// case — same defensive sweep MV997WorldExitDoorTests/MV1013LastWorldFinaleTests already make,
        /// extended to the gate/pickup director/robots this test also builds live.</summary>
        private static void Sweep()
        {
            foreach (var s in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(s.gameObject);
            foreach (var s in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(s.gameObject);
            foreach (var s in Object.FindObjectsByType<WorldFinaleGate>(FindObjectsSortMode.None))
                Object.DestroyImmediate(s.gameObject);
            foreach (var s in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(s.gameObject);
            foreach (var s in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(s.gameObject);
            foreach (var s in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(s.gameObject);
            foreach (var s in Object.FindObjectsByType<SentinelBolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(s.gameObject);
            foreach (var s in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(s.gameObject);
            foreach (var s in Object.FindObjectsByType<ResultScreen>(FindObjectsSortMode.None))
                Object.DestroyImmediate(s.gameObject);
        }

        private static void InvokeAwake(Object c) =>
            c.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, null);

        private static void InvokeOnEnable(Object c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, null);

        private static void InvokeUpdate(Object c) =>
            c.GetType().GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, null);

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }

        private readonly struct World
        {
            public readonly GameObject Root;
            public readonly WorldConfig Cfg;
            public readonly MapBuild Built;

            public World(GameObject root, WorldConfig cfg, MapBuild built)
            {
                Root = root; Cfg = cfg; Built = built;
            }
        }

        /// <summary>Builds World 1's real, shipped map/geometry and wires a real (reflection-free at the
        /// call site) <c>BackyardPath</c>/<c>AreaAccumulationDirector</c> behind it — same idiom
        /// MV997WorldExitDoorTests/MV1013LastWorldFinaleTests already use for this exact component, since
        /// <c>BackyardPath.Awake</c> itself would try to reload/rebuild the whole world.</summary>
        private static World BuildWorld1()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "World 1 failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            WorldTransitions.ApplyExitDoorway(map, cfg, worldIndex: 0);

            var root = new GameObject("MV1129 World1 Root");
            MapBuild built = MapRuntime.Build(map, root.transform);
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)
            InvokeAwake(built.ExitGate);

            var pathGo = new GameObject("MV1129 BackyardPath");
            pathGo.transform.SetParent(root.transform);
            var path = pathGo.AddComponent<BackyardPath>();
            SetPrivateField(path, "_cfg", cfg);
            SetPrivateField(path, "_map", map);
            SetPrivateField(path, "_build", built);

            var areaDirector = pathGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg, worldIndex: 0);

            return new World(root, cfg, built);
        }

        private static WorldFinaleGate NewGate()
        {
            var gate = new GameObject("WorldFinaleGate Test").AddComponent<WorldFinaleGate>();
            InvokeAwake(gate);
            InvokeOnEnable(gate);
            return gate;
        }

        private static GameObject NewPlayer()
        {
            var go = new GameObject("MV1129 Max");
            go.AddComponent<CharacterController>();
            go.AddComponent<PlayerController>();
            return go;
        }

        private static (BigBermudaBoss boss1, BigBermudaBoss boss2) FinalBosses(World w)
        {
            Assert.IsTrue(w.Built.Actors.TryGetValue("a30_boss1", out GameObject b1go) && b1go != null,
                "World 1's a30_boss1 was not built");
            Assert.IsTrue(w.Built.Actors.TryGetValue("a30_boss2", out GameObject b2go) && b2go != null,
                "World 1's a30_boss2 was not built");
            return (b1go.GetComponent<BigBermudaBoss>(), b2go.GetComponent<BigBermudaBoss>());
        }

        /// <summary>Records both of a30's bosses as already defeated, straight into <c>BossCensus</c> —
        /// the same state a real death leaves behind, without replaying the kill itself.</summary>
        private static void RecordBothBossesDefeated(World w, BigBermudaBoss boss1, BigBermudaBoss boss2)
        {
            BossCensus.Register(boss1, "TEST BOSS", 1, 100f, 100f, areaIndex: w.Cfg.dials.areaCount);
            BossCensus.Register(boss2, "TEST BOSS", 1, 100f, 100f, areaIndex: w.Cfg.dials.areaCount);
            BossCensus.ReportDefeated(boss1);
            BossCensus.ReportDefeated(boss2); // the area's last boss -- fires HudSignals.BossDefeated
        }

        [Test]
        public void ResumeAfterFinalBossAlreadyDefeated_HandlesAllThreeCases()
        {
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            // ============================================================= Case 1: weapon not granted
            {
                World w = BuildWorld1();
                GameObject player = NewPlayer();
                try
                {
                    (BigBermudaBoss boss1, BigBermudaBoss boss2) = FinalBosses(w);

                    // Previous session: both bosses already dead, closed/backgrounded before any gate
                    // ever armed -- recorded straight into BossCensus, same as a real death reports it.
                    RecordBothBossesDefeated(w, boss1, boss2);

                    SaveSystem.CaptureCheckpoint(0, areaIndex: w.Cfg.dials.areaCount, worldIndex: 0);
                    SaveSlotData captured = SaveSystem.Load(0);
                    CollectionAssert.Contains(captured.CheckpointDefeatedBossAreas, w.Cfg.dials.areaCount,
                        "the checkpoint must record the final area's boss as defeated");
                    Assert.IsFalse(captured.CheckpointFinaleWeaponGranted,
                        "precondition: no gate ever reached the weapon moment this session");
                    Assert.IsFalse(captured.CheckpointFinaleExitOpen, "precondition: the exit was never open");

                    // Cold boot: the census forgets everything, same as a process restart.
                    BossCensus.Reset();

                    WorldFinaleGate gate = NewGate();
                    Assert.IsTrue(SaveSystem.RestoreCheckpoint(0), "precondition: the checkpoint must round-trip");

                    SaveSlotData resumed = SaveSystem.Load(0);
                    gate.ResumeFromCheckpoint(resumed.CheckpointFinaleWeaponGranted, resumed.CheckpointFinaleExitOpen);

                    Pickup[] cores = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None)
                        .Where(p => p.Kind == PickupKind.WeaponCore).ToArray();
                    Assert.AreEqual(1, cores.Length,
                        "MV-1129 case 1: resuming into a world whose final boss is already defeated, with " +
                        "the weapon not yet granted, must drop a fresh, collectable Weapon Core");

                    float distToBoss1 = Vector3.Distance(cores[0].transform.position, boss1.transform.position);
                    float distToBoss2 = Vector3.Distance(cores[0].transform.position, boss2.transform.position);
                    Assert.LessOrEqual(Mathf.Min(distToBoss1, distToBoss2), 2f,
                        "the resume-spawned Core must land at the final area's own boss, not somewhere arbitrary");

                    PickupDirector pickupDirector = PickupDirector.EnsureInstalled();
                    InvokeCollect(pickupDirector, cores[0]);
                    gate.TickWeaponBeat(2.5f);
                    gate.TickExitBeat(1.0f);

                    Assert.IsTrue(gate.IsOpen,
                        "MV-1129 case 1: collecting the resume-spawned Core must still carry the finale " +
                        "through clean-up to an open exit, well within the clean-up failsafe");
                }
                finally
                {
                    Object.DestroyImmediate(player);
                    Object.DestroyImmediate(w.Root);
                    Sweep();
                }
            }

            ResetStatics();

            // ================================================ Case 2: weapon granted, exit not open
            {
                World w = BuildWorld1();
                GameObject player = NewPlayer();
                try
                {
                    (BigBermudaBoss boss1, BigBermudaBoss boss2) = FinalBosses(w);

                    WorldFinaleGate prevGate = NewGate();

                    var robot = new GameObject("MV1129 a30 robot");
                    robot.transform.position = boss1.transform.position; // MV-1122: inside the final
                    // area's own footprint -- clean-up now force-kills a stamped robot it finds outside it.
                    robot.AddComponent<CharacterController>();
                    var enemy = robot.AddComponent<RobotEnemy>();
                    InvokeOnEnable(enemy); // runs ResetState(); SetAreaIndex must come after
                    enemy.SetAreaIndex(w.Cfg.dials.areaCount);

                    // Previous session, live: the last boss falls, the Core is collected, the weapon
                    // beat applies the morph -- but a30's own robot is still alive, so clean-up never
                    // reaches the exit beat before the app closes.
                    RecordBothBossesDefeated(w, boss1, boss2);
                    HudSignals.EmitWeaponCoreCollected();
                    prevGate.TickWeaponBeat(2.5f);

                    Assert.IsFalse(prevGate.IsOpen, "precondition: a30's own robot is still alive");
                    Assert.IsTrue(prevGate.WeaponMomentResolved,
                        "precondition: the weapon moment has already resolved -- clean-up is under way");

                    SaveSystem.CaptureCheckpoint(0, areaIndex: w.Cfg.dials.areaCount, worldIndex: 0);
                    SaveSlotData captured = SaveSystem.Load(0);
                    Assert.IsTrue(captured.CheckpointFinaleWeaponGranted,
                        "the checkpoint must record the weapon as already granted");
                    Assert.IsFalse(captured.CheckpointFinaleExitOpen, "precondition: the exit was never open");

                    // Cold boot: the previous session's gate/robot are gone; a fresh copy of each stands
                    // in their place, exactly as a rebuilt scene would.
                    BossCensus.Reset();
                    Object.DestroyImmediate(prevGate.gameObject);
                    Object.DestroyImmediate(robot);
                    Sweep();

                    var freshRobot = new GameObject("MV1129 a30 robot (resumed)");
                    freshRobot.transform.position = boss1.transform.position; // MV-1122: see the other robot's own comment above
                    freshRobot.AddComponent<CharacterController>();
                    var freshEnemy = freshRobot.AddComponent<RobotEnemy>();
                    InvokeOnEnable(freshEnemy);
                    freshEnemy.SetAreaIndex(w.Cfg.dials.areaCount);

                    WorldFinaleGate gate = NewGate();
                    Assert.IsTrue(SaveSystem.RestoreCheckpoint(0), "precondition: the checkpoint must round-trip");
                    SaveSlotData resumed = SaveSystem.Load(0);
                    gate.ResumeFromCheckpoint(resumed.CheckpointFinaleWeaponGranted, resumed.CheckpointFinaleExitOpen);

                    Assert.IsFalse(gate.IsOpen,
                        "MV-1129 case 2: resuming with the weapon already granted must resume at clean-up, " +
                        "not skip straight to an open exit while a robot is still alive in the final area");
                    Assert.IsTrue(freshEnemy.IsAlive, "sanity: the robot hasn't been killed yet");

                    freshEnemy.TakeDamage(new DamageInfo(999999f, freshRobot.transform.position, Vector3.forward, Team.Player));
                    InvokeUpdate(gate);
                    gate.TickExitBeat(1.0f);

                    Assert.IsTrue(gate.IsOpen,
                        "MV-1129 case 2: once clean-up's last robot is dead, the resumed finale must still " +
                        "reach an open exit, well within the clean-up failsafe");

                    Object.DestroyImmediate(freshRobot);
                }
                finally
                {
                    Object.DestroyImmediate(player);
                    Object.DestroyImmediate(w.Root);
                    Sweep();
                }
            }

            ResetStatics();

            // ===================================================================== Case 3: exit open
            {
                World w = BuildWorld1();
                GameObject player = NewPlayer();
                try
                {
                    (BigBermudaBoss boss1, BigBermudaBoss boss2) = FinalBosses(w);

                    WorldFinaleGate prevGate = NewGate();

                    // Previous session, live: the last boss falls, the Core is collected, and -- with no
                    // robot modeled this time -- clean-up finds zero left and moves straight through the
                    // exit beat to a fully open door, before the app closes.
                    RecordBothBossesDefeated(w, boss1, boss2);
                    HudSignals.EmitWeaponCoreCollected();
                    prevGate.TickWeaponBeat(2.5f);
                    prevGate.TickExitBeat(1.0f);
                    Assert.IsTrue(prevGate.IsOpen, "precondition: the exit must already be open this session");

                    SaveSystem.CaptureCheckpoint(0, areaIndex: w.Cfg.dials.areaCount, worldIndex: 0);
                    SaveSlotData captured = SaveSystem.Load(0);
                    Assert.IsTrue(captured.CheckpointFinaleWeaponGranted);
                    Assert.IsTrue(captured.CheckpointFinaleExitOpen,
                        "the checkpoint must record the exit as already open");

                    // Cold boot.
                    BossCensus.Reset();
                    Object.DestroyImmediate(prevGate.gameObject);
                    Sweep(); // clears the previous session's own WorldJoinSequence/corridor too

                    WorldFinaleGate gate = NewGate();
                    Assert.IsTrue(SaveSystem.RestoreCheckpoint(0), "precondition: the checkpoint must round-trip");
                    SaveSlotData resumed = SaveSystem.Load(0);
                    gate.ResumeFromCheckpoint(resumed.CheckpointFinaleWeaponGranted, resumed.CheckpointFinaleExitOpen);

                    Assert.IsTrue(gate.IsOpen,
                        "MV-1129 case 3: resuming with the exit already open must leave the door open, with " +
                        "no clean-up or weapon beat to replay -- the run can be finished by walking through it");
                }
                finally
                {
                    Object.DestroyImmediate(player);
                    Object.DestroyImmediate(w.Root);
                    Sweep();
                }
            }
        }
    }
}
