using System.IO;
using NUnit.Framework;
using MaxWorlds.Intro;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-826's per-slot intro gate, <see cref="HomeScreen.ShouldPlayIntroForSlot"/> — true whenever
    /// PLAY is about to start a run on a slot holding no data (a never-used slot, or one just RESET)
    /// and no capture director is armed. Supersedes MV-550's whole-device
    /// <c>ShouldPlayIntroOnFirstLaunch</c>, whose global "every slot empty" semantics never allowed a
    /// single RESET to replay the film. Never persisted: read straight off <see cref="SaveSystem"/>'s
    /// live slot state on every call, per MV-550's original "no <c>SeenIntro</c> flag" rule, which
    /// still holds under the new per-slot shape.
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

        // ------------------------------------------------------------------ MV-826 AC2: per-slot, not whole-device

        // On base 302e10e, the gate in use (HomeScreen.ShouldPlayIntroOnFirstLaunch()) returns false
        // here — slot 0 having data fails the "every slot empty" check even though slot 1 itself is
        // untouched. This is the one new test (Rule 1) proving MV-826's fix: the gate must be asked
        // per-slot, so a played slot 0 can never block an empty (or just-RESET) slot 1's film.
        [Test]
        public void GateIsPerSlot_OccupiedSlotDoesNotBlockAnEmptySlot()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER" });

            Assert.That(HomeScreen.ShouldPlayIntroForSlot(1), Is.True,
                "slot 1 has no data — its own gate must be true regardless of slot 0's state");
            Assert.That(HomeScreen.ShouldPlayIntroForSlot(0), Is.False,
                "slot 0 already has data — PLAY on it must not replay the film");
        }

        [Test]
        public void ReactsLiveToSaveSystemState_NotACachedLocalBool()
        {
            Assert.That(HomeScreen.ShouldPlayIntroForSlot(0), Is.True, "starts empty");

            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "MAX" });
            Assert.That(HomeScreen.ShouldPlayIntroForSlot(0), Is.False,
                "must flip the moment SaveSystem reports data for this slot — it is derived, not cached");

            SaveSystem.Delete(0);
            Assert.That(HomeScreen.ShouldPlayIntroForSlot(0), Is.True,
                "a RESET slot must read as intro-eligible again (Lee's decision — no SeenIntro flag)");
        }

        // ------------------------------------------------------------------ AC2 (MV-550, retained): capture directors win

        [Test]
        public void FalseWhenPressKitIsArmed_EvenOnAnEmptySlot()
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Path.Combine("Temp", "presskit.arm"), "");

            Assert.That(HomeScreen.ShouldPlayIntroForSlot(0), Is.False,
                "a press-kit filming run has nothing to click 'skip' with — the film must never gate it");
        }

        [Test]
        public void FalseWhenUiScreensIsArmed_EvenOnAnEmptySlot()
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Path.Combine("Temp", "uiscreens.arm"), "");

            Assert.That(HomeScreen.ShouldPlayIntroForSlot(0), Is.False,
                "a fixed-state UI capture run would hang behind the film if this gate ignored it");
        }

        [Test]
        public void FalseWhenPerfCaptureIsArmed_EvenOnAnEmptySlot()
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Path.Combine("Temp", "ccperf.arm"), "");

            Assert.That(HomeScreen.ShouldPlayIntroForSlot(0), Is.False,
                "a film in front of a frame-time sample destroys the very measurement it exists to take");
        }

        // ------------------------------------------------------------------ AC3 (MV-550, retained): the returning-player path

        [Test]
        public void ReturningPlayer_TheComposedPlayIntroDecisionIsFalse()
        {
            // The exact boolean HomeScreen.OnPlay computes: IntroCinematic.Enabled is off by default, so
            // a slot that already has data must be false — StartSlot then never calls
            // IntroCinematic.TryPlay, and HomeScreen.Close() marks BootTiming's "controllable"
            // synchronously, in the same frame as PLAY, instead of ~15s later at the film's handoff.
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER" });

            bool playIntro = IntroCinematic.Enabled || HomeScreen.ShouldPlayIntroForSlot(0);

            Assert.That(playIntro, Is.False,
                "a returning player (slot 0 already has data) must never trigger the film");
        }

        [Test]
        public void EnabledOverridesTheGateEvenForAReturningPlayer()
        {
            // IntroCinematic.Enabled stays a manual/test override on top of the derived gate.
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER" });
            IntroCinematic.Enabled = true;

            bool playIntro = IntroCinematic.Enabled || HomeScreen.ShouldPlayIntroForSlot(0);

            Assert.That(playIntro, Is.True,
                "flipping Enabled back on must still force the film, even on a slot with saves");
        }
    }
}
