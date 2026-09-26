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
    /// MV-959 (the one new test, per CC_AUTONOMY's testing policy): Lee (TestFlight 0.9.10, 2026-09-26)
    /// finished World 2 for the first time, killed a21's final boss, and never reached World 3. MV-956
    /// (<c>d59b2a7</c>, merged after 0.9.10) generalised <see cref="WorldFinaleGate"/> and
    /// <see cref="BossVictoryPayoff"/> to open/drop on the final boss area's own <see cref="HudSignals.BossDefeated"/>
    /// for ANY world with a next world, rather than <see cref="HudSignals.RunComplete"/> (which needs
    /// every robot in the world dead) -- but that fix was only proven against World 1's a30. This test
    /// proves the same chain end-to-end on World 2's REAL config (<c>world2_config.json</c>, a21, the
    /// "sludgequeen" boss) through the actual map build: boss dies -> Weapon Core lands at its own death
    /// spot -> the gate opens -> collecting the core and crossing the gate seals Victory -> the save's
    /// WorldIndex advances to World 3 (index 2) -- all while World 2's area 1 ambient population and
    /// area 2's pre-placed dormant garrison are left alive and untouched throughout, proving neither the
    /// orb, the gate, nor Victory require clearing a21 or World 2.
    /// </summary>
    public sealed class MV959World2FinaleChainTests
    {
        private string _dir;
        private GameObject _root;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv959-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 1 });

            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            MaxWorlds.Weapons.PendingMorphingModule.Reset();
            MaxWorlds.Weapons.WeaponSystemState.Reset();
            Time.timeScale = 1f;

            _root = new GameObject("MV-959 Probe Root");
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            Object.DestroyImmediate(_root);

            // Seal() -> ShowResults() builds a real "Result Screen" GameObject (RunTracker's own idiom,
            // untracked by this test's own handles) -- clean it up so it doesn't leak into later tests.
            foreach (var rs in Object.FindObjectsByType<ResultScreen>(FindObjectsSortMode.None))
                Object.DestroyImmediate(rs.gameObject);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            MaxWorlds.Weapons.PendingMorphingModule.Reset();
            MaxWorlds.Weapons.WeaponSystemState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // OnEnable isn't reliably invoked for AddComponent outside Play mode (same note
        // MV625CrossAreaBossDeathTests/Mv915WorldOneFinaleSequenceTests/MV956FinaleDeathPositionTests
        // carry) -- drive it directly so every listener actually subscribes to HudSignals the way it
        // does for real.
        private static void InvokeOnEnable(Component c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(c, null);

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

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

        [Test]
        public void A21FinaleOrb_DropsAndGateOpens_WithoutClearingWorld2_AndAdvancesToWorldThree()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.AreEqual(21, cfg.dials.areaCount, "a21 must be World 2's authored final area");

            // --- the actual map build, not the JSON. ---
            MapBuild built = MapRuntime.Build(map, _root.transform);

            Assert.IsTrue(built.Actors.TryGetValue("sludgequeen", out GameObject bossGo) && bossGo != null,
                "world2_config.json's a21 boss ('sludgequeen') was not built");
            var boss = bossGo.GetComponent<BigBermudaBoss>();
            Assert.IsNotNull(boss, "a21's boss must build as a BigBermudaBoss (MapRuntime.BuildBoss)");

            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg, worldIndex: 1);                     // World 2 is index 1
            areaDirector.Configure(map, System.Array.Empty<CoverPiece>());       // fills area 1, pre-places area 2 dormant
            areaDirector.EnterArea(21);                                         // fills a21 directly

            RobotEnemy[] otherAreaRobots = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                .Where(r => r.AreaIndex != 21).ToArray();
            Assert.IsTrue(otherAreaRobots.Any(r => r.AreaIndex == 1),
                "AC: World 2's area 1 ambient population must still be alive going into a21's boss fight");
            Assert.IsTrue(otherAreaRobots.Any(r => r.AreaIndex == 2 && r.IsDormant),
                "AC: area 2's pre-placed garrison must still be alive (dormant) -- not required dead");

            var tracker = new GameObject("RunTracker Test").AddComponent<RunTracker>();
            InvokeOnEnable(tracker);

            var payoff = new GameObject("BossVictoryPayoff Test").AddComponent<BossVictoryPayoff>();
            var gate = new GameObject("WorldFinaleGate Test").AddComponent<WorldFinaleGate>();
            InvokeOnEnable(payoff);
            InvokeOnEnable(gate);

            BossCensus.Register(boss, "SLUDGEQUEEN", phases: 1, current: 100f, max: 100f, areaIndex: 21);

            Assert.IsFalse(gate.IsOpen, "the wall must be shut before a21's boss dies");
            Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                "no orb may exist before a21's boss dies");

            Vector3 bossDeathPos = bossGo.transform.position;
            InvokeOnDeath(boss);

            Assert.IsTrue(gate.IsOpen, "MV-959: the wall must open the instant a21's boss dies");

            Pickup core = LivePickups().Single(p => p.Kind == PickupKind.WeaponCore);
            float dist = Vector2.Distance(
                new Vector2(core.transform.position.x, core.transform.position.z),
                new Vector2(bossDeathPos.x, bossDeathPos.z));
            Assert.LessOrEqual(dist, 1f,
                "MV-959: the orb must land within 1m (XZ) of a21's boss's own death spot");

            Assert.Greater(areaDirector.ActiveCount, 0,
                "MV-959: World 2's other robots (area 1 ambient, area 2 dormant garrison) must still be " +
                "alive at this point -- proof neither the orb, the gate, nor Victory below depend on a21 " +
                "or World 2 being cleared");

            // --- Victory seals despite those robots still alive: Max collects the core and crosses the
            // now-open finale gate, then the next-world path must advance the save into World 3. ---
            var pickupDirector = PickupDirector.EnsureInstalled();
            InvokeCollect(pickupDirector, core);
            HudSignals.EmitBossPayoffFinished();
            Assert.AreEqual(1, SaveSystem.Load(0).WorldIndex,
                "Victory must not seal before Max actually crosses the open gate");

            HudSignals.EmitFinaleGateCrossed();
            Assert.AreEqual(2, SaveSystem.Load(0).WorldIndex,
                "MV-959: Victory must seal and advance WorldIndex to World 3 (index 2) even though " +
                "World 2's other robots are still alive, and even though HudSignals.RunComplete was " +
                "never fired in this whole test");

            Object.DestroyImmediate(payoff.gameObject);
            Object.DestroyImmediate(gate.gameObject);
            Object.DestroyImmediate(tracker.gameObject);
        }
    }
}
