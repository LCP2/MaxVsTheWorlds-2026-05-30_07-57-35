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
using MaxWorlds.Save;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1245 (the one new test, per CC_AUTONOMY's testing policy): with the Helmsman dead and 5
    /// robots alive in area 30, taking the Helm Key leaves 0 robots alive in area 30 and the exit gate
    /// resolved open in the same call (AC1's first clause, same shared step as every other world's
    /// finale — <see cref="WorldFinaleGate.OnHelmKeyCollected"/> reuses <c>DestroyRestAndOpenDoor</c>),
    /// and the Observation Deck's own floor reaches both its doors with no gap &gt; 1 cm (AC1's second
    /// clause) — measured against the REAL, built <see cref="ObservationDeckSequence"/> geometry, not
    /// the formula that built it.
    ///
    /// Loads World 5 through the real world-load path (<c>WorldLibrary.Load</c> -&gt;
    /// <c>WorldMapLoader.TryLoad</c> -&gt; <c>WorldTransitions.ApplyExitDoorway</c> -&gt;
    /// <c>MapRuntime.Build</c>), same idiom as <c>MV1013LastWorldFinaleTests</c>.
    ///
    /// Fails on base commit (pre-MV-1245) two different ways: <c>WorldCatalog</c>'s Mothership row
    /// still carries MV-1225's placeholder <c>ExitWall = Wall.E</c> (not the real north viewport), and
    /// <c>PickupDirector.Collect</c>'s own HelmKey case only ever shows a toast — it never calls
    /// <c>WorldFinaleGate.OnHelmKeyCollected</c> (that method doesn't exist yet), so the gate never
    /// opens and area 30's other robots are never destroyed.
    /// </summary>
    public sealed class MV1245MothershipFinaleTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1245-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = WorldCatalog.IndexOf(WorldIds.Mothership) });

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            Time.timeScale = 1f;

            foreach (var stray in Object.FindObjectsByType<ObservationDeckSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var stray in Object.FindObjectsByType<ObservationDeckSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var banner in Object.FindObjectsByType<MaxWorlds.UI.FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);

            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void InvokeOnEnable(Object component) =>
            component.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void InvokeOnDeath(HelmsmanBoss boss) =>
            typeof(HelmsmanBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
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
            InvokeOnEnable(e); // joins RobotEnemy.Active and runs ResetState() -- must run before SetAreaIndex
            e.SetAreaIndex(areaIndex);
            return e;
        }

        [Test]
        public void HelmKeyCollected_DestroysArea30AndOpensDoor_AndObservationDeckFloorReachesBothDoors()
        {
            int worldIndex = WorldCatalog.IndexOf(WorldIds.Mothership);
            GameObject root = null, pathGo = null, playerGo = null, gateGo = null;
            RobotEnemy[] otherRobots = null;

            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
                Assert.IsNotNull(cfg, "World 5 failed to load — see the error log above.");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                WorldTransitions.ApplyExitDoorway(map, cfg, worldIndex);
                Assert.IsTrue(map.exitDoorway.HasValue, "the Mothership must author a real finale doorway");
                Assert.AreEqual(Wall.N, map.exitDoorway.Value.Wall,
                    "MV-1245: the Bridge's own exit is the NORTH viewport, not MV-1225's placeholder east wall");

                root = new GameObject("MV1245 Root");
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms();

                Assert.IsNotNull(built.ExitGate, "area 30 must build a real exit AreaGate for the viewport");
                InvokeAwake(built.ExitGate);

                Assert.IsTrue(built.Actors.TryGetValue("helmsman", out GameObject bossGo) && bossGo != null,
                    "World 5's a30 boss ('helmsman') was not built");
                var boss = bossGo.GetComponent<HelmsmanBoss>();
                Assert.IsNotNull(boss, "World 5's final boss must build as a HelmsmanBoss");

                pathGo = new GameObject("MV1245 BackyardPath");
                var path = pathGo.AddComponent<BackyardPath>();
                SetPrivateField(path, "_cfg", cfg);
                SetPrivateField(path, "_map", map);
                SetPrivateField(path, "_build", built);

                var areaDirector = pathGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg, worldIndex);

                var pickupDirector = root.AddComponent<PickupDirector>();

                gateGo = new GameObject("WorldFinaleGate Test");
                var gate = gateGo.AddComponent<WorldFinaleGate>();
                InvokeAwake(gate);
                InvokeOnEnable(gate);

                otherRobots = new[]
                {
                    NewAliveRobot("a30_robot1", 30),
                    NewAliveRobot("a30_robot2", 30),
                    NewAliveRobot("a30_robot3", 30),
                    NewAliveRobot("a30_robot4", 30),
                    NewAliveRobot("a30_robot5", 30),
                };

                BossCensus.Register(boss, "HELMSMAN", phases: 2, current: 100f, max: 100f, areaIndex: cfg.dials.areaCount);

                playerGo = new GameObject("MV1245 Max") { tag = "Player" };
                playerGo.AddComponent<CharacterController>();
                playerGo.AddComponent<PlayerController>();
                playerGo.transform.position = bossGo.transform.position;

                Assert.IsFalse(gate.IsOpen, "the viewport must stay shut before the Helmsman dies");

                // --- The Helmsman falls -- its own OnDeath drops the Helm Key directly (no
                // BossVictoryPayoff/deferred-spawn involvement, unlike the Weapon-Core bosses). ---
                InvokeOnDeath(boss);

                Pickup[] keys = LivePickups().Where(p => p.Kind == PickupKind.HelmKey).ToArray();
                Assert.AreEqual(1, keys.Length, "the Helmsman's death must drop exactly one Helm Key");

                Assert.IsFalse(gate.IsOpen, "the Helmsman dying alone must not open the viewport");
                foreach (RobotEnemy r in otherRobots)
                    Assert.IsTrue(r.IsAlive, "area 30's other robots must still be alive while the gate is shut");

                // --- Max takes the Helm Key. AC1: in the SAME call, every robot still alive in area 30
                // is destroyed and the viewport opens -- no clean-up phase. ---
                InvokeCollect(pickupDirector, keys[0]);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide -- the deck's fresh colliders need this before .bounds reads real values

                Assert.IsTrue(gate.IsOpen, "taking the Helm Key must open the viewport immediately");
                Assert.IsTrue(built.ExitGate.IsOpen,
                    "the SAME real map gate MapRuntime built must be the one that opened");
                foreach (RobotEnemy r in otherRobots)
                    Assert.IsFalse(r.IsAlive, "every robot still alive in area 30 must be destroyed the instant the Helm Key is taken");

                // --- AC1's second clause: the Observation Deck's own floor reaches both its doors. ---
                var deck = Object.FindFirstObjectByType<ObservationDeckSequence>();
                Assert.IsNotNull(deck, "taking the Helm Key must build the Observation Deck beyond the viewport");

                Collider floorCol = deck.Floor.GetComponent<Collider>();
                Collider glassCol = deck.FarGlass.GetComponent<Collider>();
                Assert.IsNotNull(floorCol, "the deck floor must carry real, resolved collider geometry");
                Assert.IsNotNull(glassCol, "the deck's far glass must carry real, resolved collider geometry");

                float nearGap = Mathf.Abs(floorCol.bounds.min.z - map.exitDoorway.Value.Coord);
                float farGap = Mathf.Abs(floorCol.bounds.max.z - glassCol.bounds.min.z);
                Assert.LessOrEqual(nearGap, 0.01f,
                    $"the floor's near edge must sit flush against the viewport doorway (measured gap {nearGap:0.####} m)");
                Assert.LessOrEqual(farGap, 0.01f,
                    $"the floor's far edge must sit flush against the far glass (measured gap {farGap:0.####} m)");
                Assert.GreaterOrEqual(floorCol.bounds.size.z, ObservationDeckSequence.DeckLength - 0.01f,
                    "the floor must actually span the ticket's own 30 m length");
            }
            finally
            {
                if (otherRobots != null)
                    foreach (RobotEnemy r in otherRobots)
                        if (r != null) Object.DestroyImmediate(r.gameObject);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (gateGo != null) Object.DestroyImmediate(gateGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
                if (root != null) Object.DestroyImmediate(root);
                BossCensus.Reset();
            }
        }
    }
}
