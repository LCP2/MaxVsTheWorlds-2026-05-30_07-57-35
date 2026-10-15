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
    /// whose final boss is already recorded defeated must not strand the run. Originally two carried
    /// three resume states (weapon not granted / weapon granted, exit not open / exit open); MV-1183
    /// collapsed the middle one away — collecting the Core now destroys the final area's remaining
    /// robots and opens the door in the SAME call, so "weapon granted" and "exit open" are the same
    /// moment and this test now covers exactly two cases: before that moment (re-drop the Core) and
    /// after it (re-open the door and re-destroy whatever the cold-boot rebuild just placed back into
    /// the final area).
    ///
    /// Drives the real resume path for each case: a real World 1 map/<c>BackyardPath</c> build,
    /// <c>SaveSystem.CaptureCheckpoint</c>/<c>RestoreCheckpoint</c>, and <c>WorldFinaleGate</c>'s own
    /// public Tick methods — never a reflection-set field under test, and never an authored constant
    /// asserted back at itself. Tier 2 (resolved values): a real <c>Pickup</c>'s resolved position and
    /// kind, <c>WorldFinaleGate.IsOpen</c>, and a real <c>RobotEnemy.IsAlive</c>.
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
        public void ResumeAfterFinalBossAlreadyDefeated_HandlesBothCases()
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

                    // MV-1183: collecting it resolves the whole finale in the same call -- no clean-up
                    // or exit beat left for the door to wait on.
                    PickupDirector pickupDirector = PickupDirector.EnsureInstalled();
                    InvokeCollect(pickupDirector, cores[0]);

                    Assert.IsTrue(gate.IsOpen,
                        "MV-1129 case 1: collecting the resume-spawned Core must open the exit immediately");
                }
                finally
                {
                    Object.DestroyImmediate(player);
                    Object.DestroyImmediate(w.Root);
                    Sweep();
                }
            }

            ResetStatics();

            // ========================================= Case 2: weapon granted (== exit already open)
            {
                World w = BuildWorld1();
                GameObject player = NewPlayer();
                try
                {
                    (BigBermudaBoss boss1, BigBermudaBoss boss2) = FinalBosses(w);

                    WorldFinaleGate prevGate = NewGate();

                    // Previous session, live: the last boss falls and the Core is collected -- MV-1183:
                    // that single call already destroyed every robot alive in a30 and opened the door,
                    // before the app closed.
                    RecordBothBossesDefeated(w, boss1, boss2);
                    HudSignals.EmitWeaponCoreCollected();

                    Assert.IsTrue(prevGate.IsOpen, "precondition: the exit must already be open this session");

                    SaveSystem.CaptureCheckpoint(0, areaIndex: w.Cfg.dials.areaCount, worldIndex: 0);
                    SaveSlotData captured = SaveSystem.Load(0);
                    Assert.IsTrue(captured.CheckpointFinaleWeaponGranted,
                        "the checkpoint must record the weapon as already granted");
                    Assert.IsTrue(captured.CheckpointFinaleExitOpen,
                        "MV-1183: weapon-granted and exit-open are the same moment now, so both must be true");

                    // Cold boot: the previous session's gate is gone, and the level's own authored a30
                    // robot is placed back into the (rebuilt-from-scratch) scene fresh -- exactly what a
                    // real RESUME does, since nothing persists a robot's death individually.
                    BossCensus.Reset();
                    Object.DestroyImmediate(prevGate.gameObject);
                    Sweep();

                    var freshRobot = new GameObject("MV1129 a30 robot (resumed)");
                    freshRobot.transform.position = boss1.transform.position;
                    freshRobot.AddComponent<CharacterController>();
                    var freshEnemy = freshRobot.AddComponent<RobotEnemy>();
                    InvokeOnEnable(freshEnemy); // runs ResetState(); SetAreaIndex must come after
                    freshEnemy.SetAreaIndex(w.Cfg.dials.areaCount);

                    WorldFinaleGate gate = NewGate();
                    Assert.IsTrue(SaveSystem.RestoreCheckpoint(0), "precondition: the checkpoint must round-trip");
                    SaveSlotData resumed = SaveSystem.Load(0);
                    gate.ResumeFromCheckpoint(resumed.CheckpointFinaleWeaponGranted, resumed.CheckpointFinaleExitOpen);

                    Assert.IsTrue(gate.IsOpen,
                        "MV-1129 case 2: resuming after the orb was already taken must leave the door open " +
                        "-- it is never locked again, including after a RESUME");
                    Assert.IsFalse(freshEnemy.IsAlive,
                        "MV-1183: a resume after the orb is taken must restore an EMPTY final area -- the " +
                        "robot the cold-boot rebuild just placed back into a30 must be destroyed again");

                    Object.DestroyImmediate(freshRobot);
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
