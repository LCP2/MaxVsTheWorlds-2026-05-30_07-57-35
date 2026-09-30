using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1032 (the one new test, per CC_AUTONOMY's testing policy): a <c>CaptureDirector</c> preset's
    /// own <c>BeforeSceneLoad</c> seeds <see cref="SaveSystem.ActiveSlot"/> = 0 directly (dodging
    /// HomeScreen's pick-a-slot modal, which would otherwise freeze <see cref="Time.timeScale"/> before
    /// any capture could run) — which used to land on <c>HomeScreen.Start()</c>'s "a slot is already
    /// live" branch and trust whatever slot 0 already held on disk verbatim, instead of the clean
    /// bypass (<c>StartSlot</c>) the other three capture-style directors already route through. A slot
    /// carrying a genuine mid-run checkpoint is exactly what must never be resumed into an unattended
    /// capture — the same shape of hang that stalled two presets on MV-1018 for over 10 minutes each.
    ///
    /// Fails on base commit fe199052: <c>CaptureDirector.Armed()</c> doesn't exist yet
    /// (<c>ArmedPreset</c>/<c>IsArmed</c> are private), so <c>HomeScreen.Start()</c> has no way to know
    /// a capture is armed and takes the untouched-checkpoint branch regardless.
    /// </summary>
    public sealed class MV1032CaptureNeverResumesLiveRunTests
    {
        private string _dir;
        private const string ArmFile = "Temp/healthbarcluster.arm";
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1032-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;

            // A slot 0 carrying a genuine mid-run checkpoint — exactly what a capture must never resume
            // into. Mirrors what a CaptureDirector preset's own BeforeSceneLoad does: set ActiveSlot = 0
            // directly, without going through HomeScreen.StartSlot's clean wipe.
            SaveSystem.Save(0, new SaveSlotData
            {
                HasData = true,
                DisplayName = "TEST",
                HasRunInProgress = true,
                CheckpointAreaIndex = 7,
            });
            SaveSystem.ActiveSlot = 0;

            // Arm a real preset the same way an actual headless/menu capture run does — CapturePresets
            // .All["healthbarcluster"].ArmFile, the file-marker half of CaptureDirector.IsArmed.
            Directory.CreateDirectory("Temp");
            File.WriteAllText(ArmFile, "1");

            _go = new GameObject("HomeScreen Probe");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (File.Exists(ArmFile)) File.Delete(ArmFile);
            SaveSystem.ResetForTests();
            UpgradeState.Reset();
            HydroBurst.Reset();
            PickupWallet.Reset();
            WeaponSystemState.Reset();
            AbilityCreditBank.Reset();
            PendingMorphingModule.Reset();
            DeathRunState.Reset();
            RunProgressState.Reset();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void ArmedCapture_TakesTheCleanBypass_InsteadOfResumingSlot0sLiveRun()
        {
            var screen = _go.AddComponent<HomeScreen>();

            typeof(HomeScreen)
                .GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(screen, null);

            Assert.That(screen.IsOpen, Is.False,
                "an armed capture must never show the pick-a-slot modal");
            Assert.That(SaveSystem.Load(0).HasRunInProgress, Is.False,
                "the bypass path (StartSlot -> WipeForFreshRun -> ClearCheckpoint) must wipe slot 0's " +
                "stale run — the old 'ActiveSlot already live' branch left it untouched, which is " +
                "exactly a capture resuming a live run instead of a controlled one");
        }
    }
}
