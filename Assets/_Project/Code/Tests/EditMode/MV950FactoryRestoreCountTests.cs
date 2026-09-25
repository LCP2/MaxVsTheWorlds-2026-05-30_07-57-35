using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Save;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-950 (the one new test, per CC_AUTONOMY's testing policy): a cold-boot RESUME already
    /// restored <see cref="FactoryCensus.Destroyed"/> correctly (MV-922), but never told the HUD's
    /// <c>ArenaProgress</c> banner or the Result screen's <c>RunStats</c> tally, both of which only
    /// ever advanced off the live <c>HudSignals.FactoryDestroyed</c> kill signal — so both silently
    /// read 0/N after a resume even though gameplay state knew otherwise. Fails to COMPILE on base
    /// commit 2672427: <c>ArenaProgress.RestoreFactoriesDestroyed</c>/<c>HudModel.RestoreFactoriesDestroyed</c>/
    /// <c>RunStats.RestoreFactoriesDestroyed</c>/<c>FactoryCensus.CheckpointRestored</c> do not exist there.
    ///
    /// Tier 2 (resolved values): every assertion reads a live <c>HudModel.Arena.FactoriesDestroyed</c>,
    /// a live <c>RunStats.FactoriesDestroyed</c>, or a live <c>Pickup</c> count — never an authored
    /// constant asserted back at itself.
    /// </summary>
    public sealed class MV950FactoryRestoreCountTests
    {
        private string _dir;
        private MaxWorlds.UI.HudController _hud;
        private PickupDirector _pickupDirector;
        private MaxWorlds.UI.RunTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv950-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            FactoryCensus.Reset();
            Pickup.ResetRegistry();
            RunProgressState.Reset();
            DeathRunState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            // OnDisable is what unsubscribes each from the static HudSignals/FactoryCensus events --
            // AddComponent's OnEnable doesn't reliably fire outside Play mode, but neither does
            // Destroy's OnDisable, so without driving it explicitly first, a destroyed component's
            // stale delegate stays subscribed and blows up the NEXT test that raises the same event.
            if (_hud != null) { InvokeLifecycle(_hud, "OnDisable"); Object.DestroyImmediate(_hud.gameObject); }
            if (_tracker != null) { InvokeLifecycle(_tracker, "OnDisable"); Object.DestroyImmediate(_tracker.gameObject); }
            if (_pickupDirector != null) { InvokeLifecycle(_pickupDirector, "OnDisable"); Object.DestroyImmediate(_pickupDirector.gameObject); }
            foreach (var hutch in Object.FindObjectsByType<MowerHutch>(FindObjectsSortMode.None))
                Object.DestroyImmediate(hutch.gameObject);
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            var es = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es != null) Object.DestroyImmediate(es.gameObject);

            FactoryCensus.Reset();
            Pickup.ResetRegistry();
            RunProgressState.Reset();
            DeathRunState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // OnEnable isn't reliably invoked for AddComponent outside Play mode (same note
        // MV698WeaponCoreFinaleDropTests/MV645HudLeftColumnTests carry) -- drive Awake/OnEnable
        // directly so HudController/RunTracker/PickupDirector actually subscribe the way they do
        // for real.
        private static void InvokeLifecycle(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static MowerHutch MakeHutch(string id)
        {
            var go = new GameObject($"hutch_{id}");
            var hutch = go.AddComponent<MowerHutch>();   // RequireComponent brings EnemySpawner
            hutch.Build();                               // Build() registers it with FactoryCensus
            hutch.SetId(id);
            return hutch;
        }

        [Test]
        public void CheckpointRestore_CatchesUpHudBannerAndRunStatsTally_WithNoReplayedLoot()
        {
            // Three sheds destroyed before the checkpoint is captured.
            MowerHutch shedA = MakeHutch("a2_shed1");
            MowerHutch shedB = MakeHutch("a5_shed1");
            MowerHutch shedC = MakeHutch("a9_shed1");
            shedA.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
            shedB.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
            shedC.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
            Assert.AreEqual(3, FactoryCensus.Destroyed, "three sheds must be down before the checkpoint is captured");

            SaveSystem.CaptureCheckpoint(0, areaIndex: 9);

            // Cold-boot RESUME: the level rebuilds from scratch -- fresh shed instances, a fresh
            // HUD, a fresh RunTracker, a fresh census -- nothing yet knows the checkpoint's history.
            Object.DestroyImmediate(shedA.gameObject);
            Object.DestroyImmediate(shedB.gameObject);
            Object.DestroyImmediate(shedC.gameObject);
            FactoryCensus.Reset();

            _hud = new GameObject("HUD Test").AddComponent<MaxWorlds.UI.HudController>();
            InvokeLifecycle(_hud, "Awake");
            InvokeLifecycle(_hud, "OnEnable");

            _pickupDirector = new GameObject("PickupDirector Test").AddComponent<PickupDirector>();
            InvokeLifecycle(_pickupDirector, "OnEnable");

            _tracker = new GameObject("RunTracker Test").AddComponent<MaxWorlds.UI.RunTracker>();
            InvokeLifecycle(_tracker, "OnEnable");

            // Start() (not Awake) is where a real MowerHutch tells the HUD it exists (MowerHutch.cs),
            // so the level's own build has already made HudModel discover all 3 factories -- exactly
            // the ordering FactoryCensus.ApplyCheckpointDestroyedShedIds's own doc comment requires --
            // before the checkpoint restore below runs.
            InvokeLifecycle(MakeHutch("a2_shed1"), "Start");
            InvokeLifecycle(MakeHutch("a5_shed1"), "Start");
            InvokeLifecycle(MakeHutch("a9_shed1"), "Start");

            int pickupsBeforeRestore = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None).Length;

            bool restored = SaveSystem.RestoreCheckpoint(0);
            Assert.IsTrue(restored, "a captured checkpoint must report as restored");

            var model = (MaxWorlds.UI.HudModel)typeof(MaxWorlds.UI.HudController)
                .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_hud);
            Assert.AreEqual(3, model.Arena.FactoriesDestroyed,
                "the HUD's resolved Arena.FactoriesDestroyed must catch up to the checkpoint's destroyed-shed count, not read 0");

            var stats = (MaxWorlds.UI.RunStats)typeof(MaxWorlds.UI.RunTracker)
                .GetField("_stats", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_tracker);
            Assert.AreEqual(3, stats.FactoriesDestroyed,
                "the Result screen's RunStats tally must catch up to the checkpoint's destroyed-shed count, not read 0");

            int pickupsAfterRestore = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None).Length;
            Assert.AreEqual(pickupsBeforeRestore, pickupsAfterRestore,
                "restoring a checkpoint's already-recorded destruction must never spawn a Device/Supercell/cell pickup");
        }
    }
}
