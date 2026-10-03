using System.Collections.Generic;
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
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1078 (the one new test, per CC_AUTONOMY's testing policy): the finale's reversed rule, end to
    /// end, against World 1's real map. Fails on base commit 8f71734: <c>WorldFinaleGate.OnBossDefeated</c>
    /// opens the exit directly off <see cref="HudSignals.BossDefeated"/>, so <c>IsOpen</c> is already TRUE
    /// the instant the final boss dies -- before the Weapon Core is even collected and with a30's other
    /// garrison robots still alive -- the opposite of every assertion below.
    /// </summary>
    public sealed class MV1078FinaleWeaponAndCleanupTests
    {
        private string _dir;
        private GameObject _root;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1078-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            Time.timeScale = 1f;

            _root = new GameObject("MV-1078 Probe Root");
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

            // MV-1079: Beat A/Beat B's own scratch VFX/UI (GroundRing flash/ring/burst, the travelling
            // SentinelBolt, the centre FinaleBanner) are never torn down by OnDisable here -- same
            // "OnDisable isn't reliably invoked for AddComponent outside Play mode" note as OnEnable
            // above -- so sweep them by hand, same idiom as the ResultScreen cleanup just above.
            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(ring.gameObject);
            foreach (var bolt in Object.FindObjectsByType<SentinelBolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(bolt.gameObject);
            foreach (var banner in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // OnEnable isn't reliably invoked for AddComponent outside Play mode (same note
        // MV956FinaleDeathPositionTests/MV959World2FinaleChainTests carry) -- drive it directly so every
        // listener actually subscribes to HudSignals the way it does for real.
        private static void InvokeOnEnable(Component c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(c, null);

        private static void InvokeUpdate(Component c) =>
            c.GetType().GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
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

        private static void Kill(RobotEnemy r) =>
            r.TakeDamage(new DamageInfo(999999f, r.transform.position, Vector3.forward, Team.Player));

        [Test]
        public void World1Finale_OpensOnlyAfterCoreCollectedAndFinalAreaRobotsAllDead()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapBuild built = MapRuntime.Build(map, _root.transform);

            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);
            areaDirector.Configure(map, System.Array.Empty<CoverPiece>());   // fills area 1
            areaDirector.EnterArea(30);                                     // fills a30 directly

            RobotEnemy[] a30Enemies = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                .Where(r => r.AreaIndex == 30).ToArray();
            Assert.Greater(a30Enemies.Length, 3,
                "a30 must build more than the 3 robots this test leaves alive, or the setup below is wrong");

            // Kill every a30 robot but 3, so the final boss's own death lands with exactly 3 living
            // robots left in the final area -- AC1's own precondition.
            for (int i = 3; i < a30Enemies.Length; i++) Kill(a30Enemies[i]);
            RobotEnemy[] remaining = a30Enemies.Take(3).ToArray();
            Assert.IsTrue(remaining.All(r => r.IsAlive),
                "setup: exactly 3 of a30's robots must still be alive before the boss dies");

            var pickupDirector = PickupDirector.EnsureInstalled();
            var payoff = new GameObject("BossVictoryPayoff Test").AddComponent<BossVictoryPayoff>();
            var gate = new GameObject("WorldFinaleGate Test").AddComponent<WorldFinaleGate>();
            InvokeOnEnable(payoff);
            InvokeOnEnable(gate);

            Assert.IsTrue(built.Actors.TryGetValue("a30_boss1", out GameObject boss1Go) && boss1Go != null,
                "world1_config.json's a30_boss1 was not built");
            Assert.IsTrue(built.Actors.TryGetValue("a30_boss2", out GameObject boss2Go) && boss2Go != null,
                "world1_config.json's a30_boss2 was not built");
            var boss1 = boss1Go.GetComponent<BigBermudaBoss>();
            var boss2 = boss2Go.GetComponent<BigBermudaBoss>();
            BossCensus.Register(boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
            BossCensus.Register(boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);

            InvokeOnDeath(boss1);
            InvokeOnDeath(boss2);   // a30's last boss -- on base this alone opens the gate

            Assert.IsFalse(gate.IsOpen,
                "MV-1078: boss death alone -- even a30's last -- must no longer open the exit; it now " +
                "waits on the Core being collected AND a30's own robots being dead");

            Pickup core = LivePickups().Single(p => p.Kind == PickupKind.WeaponCore);
            InvokeCollect(pickupDirector, core);

            // MV-1079: collecting the Core now starts Beat A (WEAPON TAKEN) instead of applying the
            // morph and the clean-up wake synchronously -- the morph itself lands at the beat's own
            // authored 0.5s instant.
            gate.TickWeaponBeat(0.5f);
            Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary,
                "MV-1079: the beat applies the next world's weapon at its own 0.5s instant");
            Assert.AreEqual(0, areaDirector.ActiveWorldIndex,
                "MV-1078: the weapon morph applies while the PLAYED world is still World 1 -- the morph " +
                "itself is not what advances it");
            Assert.IsFalse(gate.IsOpen,
                "MV-1078: the Core is collected but a30's 3 remaining robots are still alive -- the exit " +
                "must stay shut");

            // MV-1079: the rest of Beat A (2.5s total) -- clean-up (waking a30's own remaining robots)
            // only begins once it finishes.
            gate.TickWeaponBeat(2.0f);

            Kill(remaining[0]);
            InvokeUpdate(gate);
            Kill(remaining[1]);
            InvokeUpdate(gate);
            Assert.IsFalse(gate.IsOpen,
                "MV-1078: two of a30's three remaining robots are dead, one is still alive -- the exit " +
                "must stay shut");

            Kill(remaining[2]);
            // MV-1079: the last living robot dying starts Beat B (EXIT OPEN, 3.0s) instead of opening the
            // door synchronously -- InvokeUpdate drives Update() with whatever stray Time.unscaledDeltaTime
            // the editor happens to carry between calls (often large), which is enough on its own to run
            // the whole beat to completion here; the explicit tick below is what makes that deterministic
            // regardless of what the editor's own clock did.
            InvokeUpdate(gate);
            gate.TickExitBeat(1.0f);
            Assert.IsTrue(gate.IsOpen,
                "MV-1079: with the Core collected and a30's own robots now dead, Beat B must have reached " +
                "its door-open instant and the door must be open");

            Object.DestroyImmediate(payoff.gameObject);
            Object.DestroyImmediate(gate.gameObject);
        }
    }
}
