using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-951 — a cold-boot RESUME only restored THE RIG/wallet/deaths/flood/destroyed-factories
    /// (MV-524/776/922/950); five other things Max had already achieved were not carried across a
    /// process restart, all read in code at 8c54e36: (1) <c>AreaAccumulationDirector.Configure</c>
    /// unconditionally fills area 1 (and pre-places area 2's dormant garrison) before the Home screen
    /// even knows which checkpoint, if any, is about to RESUME, leaving those areas populated behind a
    /// player who lands deeper in; (2) <c>ResumeCheckpoint</c>/<c>Continue</c> land
    /// <c>AreaAccumulationDirector.CurrentArea</c> one area BEHIND the checkpoint (standing at the gate
    /// looking in), so a pause/focus capture taken before that gate breaks again regressed the save by
    /// one area; (3) <c>AbilityCreditBank</c>/<c>UpgradeState</c> get wiped by
    /// <c>HomeScreen.OnResume</c>'s own transient-state reset with nothing restoring them afterward; (4)
    /// <c>SaveSlotData.WeaponCorePending</c> was only ever written false; (5) the Invasion Level's
    /// escalation clock (<c>DifficultyDirector</c>) is zeroed by every cold-boot scene build with
    /// nothing to restore it.
    ///
    /// Builds the real shipped World 1 config (same idiom as <c>Mv909ResumeAreaGateLatchTests</c>'s
    /// World 2 build) and drives the real entry points — <c>AreaAccumulationDirector.Configure</c>,
    /// <c>SaveSystem.CaptureCheckpoint</c>/<c>RestoreCheckpoint</c>, <c>WorldRunner.ResumeCheckpoint</c>
    /// — rather than a hand-rolled shortcut, since the bug is entirely in how those real entry points
    /// interact across a simulated process restart.
    ///
    /// Reads live/resolved state throughout (<c>RobotEnemy.IsAlive</c>/<c>AreaIndex</c> off a fresh
    /// <c>FindObjectsByType</c> scan — <c>RobotEnemy.Active</c> itself is only populated by
    /// <c>OnEnable</c>, which Unity does not invoke for a plain MonoBehaviour outside Play mode, the
    /// same reason <c>AreaAccumulationDirector.RestoreArea</c>'s own doc names — <c>AbilityCreditBank.Banked</c>,
    /// <c>UpgradeState.IsInstalled</c>, <c>DifficultyDirector.Elapsed</c>), never a <c>SaveSlotData</c>
    /// field asserted back at itself, except where the assertion IS the write path under test (the
    /// pause-capture regression guard, (b) below, which is inherently about what gets persisted).
    /// </summary>
    public sealed class MV951ResumeRestoresWholeRunTests
    {
        private string _dir;
        private GameObject _root;
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv951-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;

            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            PickupWallet.Reset();
            DeathRunState.Reset();
            AbilityCreditBank.Reset();
            UpgradeState.Reset();
            PendingMorphingModule.Reset();
            DifficultyDirector.Reset();
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_root != null) Object.DestroyImmediate(_root);

            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            PickupWallet.Reset();
            DeathRunState.Reset();
            AbilityCreditBank.Reset();
            UpgradeState.Reset();
            PendingMorphingModule.Reset();
            DifficultyDirector.Reset();
            DevTuning.Reset();
            SaveSystem.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static void InvokeCapturePauseCheckpoint(WorldRunner runner) =>
            typeof(WorldRunner).GetMethod("CapturePauseCheckpoint", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(runner, null);

        private static List<RobotEnemy> LiveRobots()
        {
            var list = new List<RobotEnemy>();
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                if (r != null && r.IsAlive) list.Add(r);
            return list;
        }

        [Test]
        public void ColdBootResume_RestoresAreasCreditsPartsAndEscalation_AndNeverRegressesTheCheckpoint()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            // ---- arrange: a checkpoint already sitting at area 5 (areas 1-4 cleared), carrying a
            // ---- banked ability credit, an installed part, and a live escalation clock ----------------
            SaveSystem.ActiveSlot = 0;

            AbilityCreditBank.Bank();
            UpgradeState.Install(PartKind.BeamNozzle);
            DifficultyDirector.Tick(37f);
            float capturedElapsed = DifficultyDirector.Elapsed;

            SaveSystem.CaptureCheckpoint(0, areaIndex: 5);

            // The process now looks exactly like a cold boot: every system just captured wipes, same as
            // HomeScreen.OnResume's own transient-state reset — a pass that merely reads stale statics
            // rather than doing the restore work would pass this test anyway without this step.
            AbilityCreditBank.Reset();
            UpgradeState.Reset();
            PendingMorphingModule.Reset();
            DifficultyDirector.Reset();

            // ---- act: the actual cold-boot build (BackyardPath.Awake's own order) -------------------
            _root = new GameObject("MV951 Host");
            MapBuild built = MapRuntime.Build(map, _root.transform);

            var areaGo = new GameObject("Area Accumulation");
            areaGo.transform.SetParent(_root.transform);
            var areaDirector = areaGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);
            areaDirector.Configure(map, built.Cover);   // the bug's own reproduction: fills area1, pre-places area2's garrison

            var runnerGo = new GameObject("WorldRunner Test Root");
            runnerGo.transform.SetParent(_root.transform);
            var runner = runnerGo.AddComponent<WorldRunner>();
            runner.Configure(cfg, map, built, areaDirector);

            MapZone area1 = map.Zone("area1");
            Assert.IsNotNull(area1, "setup failure: World 1 must have an area1 zone to place the cold-boot player in");
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = area1.Center;

            // ---- the real RESUME entry point (HomeScreen.OnResume -> SaveSystem.RestoreCheckpoint --
            // ---- -> WorldRunner.ResumeCheckpoint) ----------------------------------------------------
            bool restored = SaveSystem.RestoreCheckpoint(0);
            Assert.IsTrue(restored, "precondition: the checkpoint captured above must round-trip");

            runner.ResumeCheckpoint(SaveSystem.Load(0).CheckpointAreaIndex);

            // ---- (a): no robot left behind in an area before the checkpoint --------------------------
            foreach (RobotEnemy r in LiveRobots())
                Assert.That(r.AreaIndex, Is.GreaterThanOrEqualTo(5),
                    $"MV-951: '{r.name}' is still alive in area {r.AreaIndex}, before the checkpoint's " +
                    "area 5 — Configure() fills area1 (and pre-places area2's garrison) on every cold " +
                    "boot, regardless of which area a resume is about to land in");

            // ---- (b): a pause-capture before any gate break must never regress the checkpoint --------
            InvokeCapturePauseCheckpoint(runner);
            Assert.That(SaveSystem.Load(0).CheckpointAreaIndex, Is.EqualTo(5),
                "MV-951: ResumeCheckpoint lands CurrentArea one area back (standing at the gate into the " +
                "checkpoint area) — a capture taken before that gate breaks again must not persist that " +
                "lower area over the checkpoint it was restored from");

            // ---- (c): the banked credit and installed part survive the resume -------------------------
            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(1),
                "MV-951: a banked ability credit must survive a cold-boot RESUME");
            Assert.IsTrue(UpgradeState.IsInstalled(PartKind.BeamNozzle),
                "MV-951: an installed part must survive a cold-boot RESUME");

            // ---- (d): the escalation clock resumes from where it was captured, not the fresh build's
            // ---- own reset to zero ---------------------------------------------------------------------
            Assert.That(DifficultyDirector.Elapsed, Is.EqualTo(capturedElapsed).Within(0.001f),
                "MV-951: the escalation clock must restore to its captured value, not the cold-boot reset");
        }

        /// <summary>Item 4 on its own, not covered by the test above: <c>SaveSlotData.WeaponCorePending</c>
        /// was only ever written false, so a cold-boot RESUME after the World 1 finale's Weapon Core is
        /// banked but before THE RIG is opened silently lost the pending morph. No scene/world build
        /// needed — <c>PendingMorphingModule</c>/<c>SaveSystem</c> are the whole contract.</summary>
        [Test]
        public void ColdBootRestore_RestoresAPendingWeaponCoreMorph()
        {
            SaveSystem.ActiveSlot = 0;

            PendingMorphingModule.SetWeaponCore();
            Assert.IsTrue(PendingMorphingModule.WeaponCorePending,
                "precondition: the World 1 finale's Weapon Core is banked");

            SaveSystem.CaptureCheckpoint(0, areaIndex: 5);

            // Cold boot: HomeScreen.OnResume's own transient-state wipe.
            PendingMorphingModule.Reset();
            Assert.IsFalse(PendingMorphingModule.WeaponCorePending, "precondition: the wipe actually cleared it");

            Assert.IsTrue(SaveSystem.RestoreCheckpoint(0));

            Assert.IsTrue(PendingMorphingModule.WeaponCorePending,
                "MV-951: a banked Weapon Core pending morph must survive a cold-boot RESUME");
        }
    }
}
