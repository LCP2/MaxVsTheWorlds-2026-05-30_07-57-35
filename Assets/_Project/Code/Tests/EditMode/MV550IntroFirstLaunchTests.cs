using System.IO;
using NUnit.Framework;
using MaxWorlds.Intro;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-550: the intro cinematic's derived first-launch gate, <see cref="HomeScreen.ShouldPlayIntroOnFirstLaunch"/>
    /// — true only when every save slot is empty and no capture director is armed. Never persisted:
    /// read straight off <see cref="SaveSystem"/>'s live slot state on every call, per the ticket's
    /// explicit "no <c>SeenIntro</c> flag" rule.
    /// </summary>
    public sealed class MV550IntroFirstLaunchTests
    {
        private string _dir;

        // The three capture directors' marker files (PressKitDirector/UiScreensDirector/
        // PerfCaptureDirector.Armed()) — a relative "Temp/*.arm" file, same mechanism CI uses to arm
        // a headless capture run without a command-line arg.
        private static readonly string[] MarkerFiles =
        {
            Path.Combine("Temp", "presskit.arm"),
            Path.Combine("Temp", "uiscreens.arm"),
            Path.Combine("Temp", "ccperf.arm"),
        };

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-intro-first-launch-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            ClearMarkers();
            IntroCinematic.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            ClearMarkers();
            SaveSystem.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            IntroCinematic.ResetForTests();
        }

        private static void ClearMarkers()
        {
            foreach (var path in MarkerFiles)
                if (File.Exists(path)) File.Delete(path);
        }

        // ------------------------------------------------------------------ AC1: the save-data half

        [Test]
        public void TrueWhenEverySlotIsEmpty()
        {
            Assert.That(HomeScreen.ShouldPlayIntroOnFirstLaunch(), Is.True,
                "an untouched device (every slot empty) is exactly what 'true first launch' means");
        }

        [Test]
        public void FalseWhenAnySingleSlotHasData()
        {
            SaveSystem.Save(1, new SaveSlotData { HasData = true, DisplayName = "DEXTER" });

            Assert.That(HomeScreen.ShouldPlayIntroOnFirstLaunch(), Is.False,
                "one played slot is enough to prove this device is not a true first launch, " +
                "even though slots 0 and 2 are still empty");
        }

        [Test]
        public void ReactsLiveToSaveSystemState_NotACachedLocalBool()
        {
            Assert.That(HomeScreen.ShouldPlayIntroOnFirstLaunch(), Is.True, "starts empty");

            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "MAX" });
            Assert.That(HomeScreen.ShouldPlayIntroOnFirstLaunch(), Is.False,
                "must flip the moment SaveSystem reports data — it is derived, not cached");

            SaveSystem.Delete(0);
            Assert.That(HomeScreen.ShouldPlayIntroOnFirstLaunch(), Is.True,
                "wiping every slot again must read as first-launch again (deliberate — no SeenIntro flag)");
        }

        // ------------------------------------------------------------------ AC2: capture directors win

        [Test]
        public void FalseWhenPressKitIsArmed_EvenWithAllSlotsEmpty()
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Path.Combine("Temp", "presskit.arm"), "");

            Assert.That(HomeScreen.ShouldPlayIntroOnFirstLaunch(), Is.False,
                "a press-kit filming run has nothing to click 'skip' with — the 25s cinematic must never gate it");
        }

        [Test]
        public void FalseWhenUiScreensIsArmed_EvenWithAllSlotsEmpty()
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Path.Combine("Temp", "uiscreens.arm"), "");

            Assert.That(HomeScreen.ShouldPlayIntroOnFirstLaunch(), Is.False,
                "a fixed-state UI capture run would hang behind the cinematic if this gate ignored it");
        }

        [Test]
        public void FalseWhenPerfCaptureIsArmed_EvenWithAllSlotsEmpty()
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Path.Combine("Temp", "ccperf.arm"), "");

            Assert.That(HomeScreen.ShouldPlayIntroOnFirstLaunch(), Is.False,
                "a 25s cinematic in front of a frame-time sample destroys the very measurement it exists to take");
        }

        // ------------------------------------------------------------------ AC4: the returning-player path

        [Test]
        public void ReturningPlayer_TheComposedPlayIntroDecisionIsFalse()
        {
            // The exact boolean HomeScreen.OnPlay computes: IntroCinematic.Enabled is off by default, so
            // once any slot has data this must be false — StartSlot then never calls IntroCinematic.TryPlay,
            // and HomeScreen.Close() marks BootTiming's "controllable" synchronously, in the same frame as
            // PLAY, instead of ~25s later at IntroCinematic's handoff.
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER" });

            bool playIntro = IntroCinematic.Enabled || HomeScreen.ShouldPlayIntroOnFirstLaunch();

            Assert.That(playIntro, Is.False,
                "a returning player (slot 0 already has data) must never trigger the cinematic");
        }

        [Test]
        public void EnabledOverridesTheGateEvenForAReturningPlayer()
        {
            // AC3: IntroCinematic.Enabled stays a manual/test override on top of the derived gate.
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER" });
            IntroCinematic.Enabled = true;

            bool playIntro = IntroCinematic.Enabled || HomeScreen.ShouldPlayIntroOnFirstLaunch();

            Assert.That(playIntro, Is.True,
                "flipping Enabled back on must still force the cinematic, even on a device with saves");
        }
    }
}
