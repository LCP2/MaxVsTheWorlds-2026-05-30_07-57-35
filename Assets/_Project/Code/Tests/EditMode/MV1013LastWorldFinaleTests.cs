using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1013 (the one new test, per CC_AUTONOMY's testing policy): World 3 (the last world) gets the
    /// same finale as every other world — its own final boss (Anchorhead, a30) dying drops a Weapon Core,
    /// with no requirement that any OTHER robot in a30 (or the world) also be dead. Loads World 3 through
    /// the real world-load path (the same <c>WorldLibrary.Load</c> -&gt; <c>WorldMapLoader.TryLoad</c> -&gt;
    /// <c>WorldTransitions.ApplyExitDoorway</c> -&gt; <c>MapRuntime.Build</c> pipeline <c>BackyardPath.Awake</c>
    /// runs, <see cref="MV997WorldExitDoorTests"/>'s own idiom), not a synthetic single-area config.
    ///
    /// MV-1078 updated this test's own exit-door assertions: the real door opens once the Core is
    /// collected AND a30's other robots are dead too, not on the boss's death alone — see that ticket for
    /// the full ruling. The Weapon-Core-on-death-alone assertions are untouched.
    ///
    /// Fails on base commit 3cfa5fb two different ways: <c>BossVictoryPayoff.MaybeDropWeaponCore</c> and
    /// <c>WorldFinaleGate.Install</c> both bail out via their own <c>HasNextWorld()</c> guard, which
    /// reads false for World 3 (the last entry in <c>WorldLibrary</c>) — no Weapon Core, no finale gate
    /// at all — and <c>WorldTransitions.ApplyExitDoorway</c> no-ops for World 3 (no
    /// <see cref="WorldTransitionEntry"/> row past World 2), so <c>MapRuntime.Build</c> never even builds
    /// an exit <see cref="AreaGate"/> to open.
    /// </summary>
    public sealed class MV1013LastWorldFinaleTests
    {
        private string _dir;
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1013-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 2 });

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            Time.timeScale = 1f;

            _root = new GameObject("MV-1013 Probe Root");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);

            // Seal() -> ShowResults() builds a real "Result Screen" GameObject (RunTracker's own idiom,
            // untracked by this test's own handles) -- clean it up so it doesn't leak into later tests.
            foreach (var rs in Object.FindObjectsByType<ResultScreen>(FindObjectsSortMode.None))
                Object.DestroyImmediate(rs.gameObject);

            // MV-1079: Beat B's own scratch VFX/UI (GroundRing burst, the travelling SentinelBolt, the
            // centre FinaleBanner) are never torn down by OnDisable here -- same "OnDisable isn't
            // reliably invoked for AddComponent outside Play mode" note as the ResultScreen cleanup above.
            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(ring.gameObject);
            foreach (var bolt in Object.FindObjectsByType<SentinelBolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(bolt.gameObject);
            foreach (var banner in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;   // Seal() -> ResultScreen.Show() freezes the game; restore it
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void InvokeOnEnable(Object component) =>
            component.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void InvokeUpdate(Object component) =>
            component.GetType().GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static Pickup[] LivePickups() =>
            Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None);

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }

        private static RobotEnemy NewAliveRobot(string name, int areaIndex)
        {
            var go = new GameObject(name);
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // MV-1078: OnEnable (not just Awake) isn't reliably invoked for a plain AddComponent outside
            // Play mode either -- without this, the robot never joins RobotEnemy.Active, so
            // AreaAccumulationDirector.ActiveCountForArea silently reads 0 for it and the finale gate
            // opens as if it were already dead. InvokeOnEnable also runs ResetState() itself, so the
            // standalone call this replaced is gone -- SetAreaIndex must run AFTER it or OnEnable's own
            // ResetState() would clobber it back to unset.
            InvokeOnEnable(e);
            e.SetAreaIndex(areaIndex);
            return e;
        }

        [Test]
        public void World3FinalBossDeath_DropsCoreAndOpensDoor_WithoutRequiringA30sOtherRobotsDead()
        {
            const int worldIndex = 2; // World 3 -- the last entry in WorldLibrary
            GameObject root = null, pathGo = null, playerGo = null, payoffGo = null, trackerGo = null, gateGo = null;
            RobotEnemy[] otherRobots = null;

            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
                Assert.IsNotNull(cfg, "World 3 failed to load — see the error log above.");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                // MV-1013: stamps map.exitDoorway off WorldTransitions' finale-only table -- a no-op on
                // base commit, since ApplyExitDoorway only ever resolved a WorldTransitionEntry row, and
                // World 3 (the last world) has none.
                WorldTransitions.ApplyExitDoorway(map, cfg, worldIndex);

                root = new GameObject("MV1013 Root");
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.IsNotNull(built.ExitGate, "World 3 must build a real exit AreaGate for its own finale door");
                InvokeAwake(built.ExitGate); // AddComponent never fires Awake in this EditMode harness

                Assert.IsTrue(built.Actors.TryGetValue("anchorhead", out GameObject bossGo) && bossGo != null,
                    "World 3's a30 boss ('anchorhead') was not built");
                var boss = bossGo.GetComponent<BigBermudaBoss>();
                Assert.IsNotNull(boss, "World 3's final boss must build as a BigBermudaBoss");

                pathGo = new GameObject("MV1013 BackyardPath");
                var path = pathGo.AddComponent<BackyardPath>();
                // BackyardPath.Awake is never invoked here (would try to reload/rebuild the whole world
                // itself) -- wire the fields it would have set directly, same idiom MV997WorldExitDoorTests
                // already uses for this exact component.
                SetPrivateField(path, "_cfg", cfg);
                SetPrivateField(path, "_map", map);
                SetPrivateField(path, "_build", built);

                var areaDirector = pathGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg, worldIndex);

                var pickupDirector = root.AddComponent<PickupDirector>();

                var payoff = new GameObject("BossVictoryPayoff Test").AddComponent<BossVictoryPayoff>();
                InvokeOnEnable(payoff);
                payoffGo = payoff.gameObject;

                var tracker = new GameObject("RunTracker Test").AddComponent<RunTracker>();
                InvokeOnEnable(tracker);
                trackerGo = tracker.gameObject;

                gateGo = new GameObject("WorldFinaleGate Test");
                var gate = gateGo.AddComponent<WorldFinaleGate>();
                InvokeAwake(gate);
                InvokeOnEnable(gate);

                // >= 3 other robots alive in a30 -- the exact condition the old HudSignals.RunComplete
                // gate (every robot in the final area dead) used to require before the world could end.
                otherRobots = new[]
                {
                    NewAliveRobot("a30_robot1", 30),
                    NewAliveRobot("a30_robot2", 30),
                    NewAliveRobot("a30_robot3", 30),
                };

                BossCensus.Register(boss, "ANCHORHEAD", phases: 1, current: 100f, max: 100f, areaIndex: cfg.dials.areaCount);

                Vector3 bossDeathPos = Vector3.zero;
                HudSignals.BossKilled += pos => bossDeathPos = pos;

                Assert.IsFalse(gate.IsOpen, "the finale door must stay shut before Anchorhead dies");

                // --- Anchorhead falls via the normal damage path -- the world's only boss in a30. ---
                InvokeOnDeath(boss);

                // AC1 (same frame as the death): a Weapon Core within 1 m of the boss's own death spot,
                // and the finale door open -- neither may depend on a30's other robots being dead.
                Pickup[] cores = LivePickups().Where(p => p.Kind == PickupKind.WeaponCore).ToArray();
                Assert.AreEqual(1, cores.Length, "Anchorhead's death must drop exactly one Weapon Core");
                float coreDist = Vector2.Distance(
                    new Vector2(cores[0].transform.position.x, cores[0].transform.position.z),
                    new Vector2(bossDeathPos.x, bossDeathPos.z));
                Assert.LessOrEqual(coreDist, 1f,
                    "the Weapon Core must land within 1 m (XZ) of Anchorhead's own death position");

                // MV-1078: the boss dying -- even World 3's own last one -- no longer opens the finale
                // door by itself; it opens once the Core is collected AND a30's other robots are dead.
                Assert.IsFalse(gate.IsOpen,
                    "MV-1078: boss death alone must no longer open the finale door");
                foreach (RobotEnemy r in otherRobots)
                    Assert.IsTrue(r.IsAlive, "a30's other robots must still be alive while the gate is shut");

                // --- Max collects the core. The weapon morph applies immediately; a30's other robots
                // are still alive, so the door stays shut. ---
                InvokeCollect(pickupDirector, cores[0]);
                Assert.IsFalse(gate.IsOpen,
                    "MV-1078: collecting the Core alone must not open the door -- a30's other robots are " +
                    "still alive");

                // --- a30's other robots die. Only now does the door open. ---
                foreach (RobotEnemy r in otherRobots)
                {
                    r.TakeDamage(new DamageInfo(999999f, r.transform.position, Vector3.forward, Team.Player));
                    InvokeUpdate(gate);
                }

                // MV-1079: the last robot dying starts Beat B (EXIT OPEN, 3.0s) -- WorldFinaleGate.Open()
                // itself now lands 1.0s into that beat, not synchronously off the kill.
                gate.TickExitBeat(1.0f);

                Assert.IsTrue(gate.IsOpen,
                    "MV-1078: with the Core collected and a30's other robots now dead, the door must open");
                Assert.IsTrue(built.ExitGate.IsOpen,
                    "the SAME real map gate MapRuntime built must be the one that opened -- no corridor to build");

                HudSignals.EmitBossPayoffFinished();

                Assert.IsNull(Object.FindFirstObjectByType<ResultScreen>(),
                    "Victory must not seal before Max actually crosses the open door");

                playerGo = new GameObject("MV1013 Max") { tag = "Player" };
                ExitDoorway doorway = map.exitDoorway.Value;
                Assert.AreEqual(Wall.E, doorway.Wall, "World 3's own exit convention: every world's door runs east");
                // Standing well short of the door -- WorldFinaleGate's own crossing check must not fire yet.
                playerGo.transform.position = new Vector3(doorway.Coord - 5f, 0f, doorway.Hole.Mid);
                InvokeUpdate(gate);
                Assert.IsNull(Object.FindFirstObjectByType<ResultScreen>(),
                    "standing short of the open door must not raise FinaleGateCrossed / seal Victory");

                // Now walk him through it.
                playerGo.transform.position = new Vector3(doorway.Coord + 1f, 0f, doorway.Hole.Mid);
                InvokeUpdate(gate);

                var resultScreen = Object.FindFirstObjectByType<ResultScreen>();
                Assert.IsNotNull(resultScreen,
                    "MV-1013: crossing the open door must raise Victory and show the Result screen, " +
                    "exactly as every other world's finale does");

                foreach (RobotEnemy r in otherRobots)
                    Assert.IsFalse(r.IsAlive,
                        "MV-1078: a30's other robots were killed off to let the finale door open -- they " +
                        "must still be dead by the time Victory seals");
            }
            finally
            {
                if (otherRobots != null)
                    foreach (RobotEnemy r in otherRobots)
                        if (r != null) Object.DestroyImmediate(r.gameObject);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (gateGo != null) Object.DestroyImmediate(gateGo);
                if (trackerGo != null) Object.DestroyImmediate(trackerGo);
                if (payoffGo != null) Object.DestroyImmediate(payoffGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
                if (root != null) Object.DestroyImmediate(root);
                BossCensus.Reset();
            }
        }
    }
}
