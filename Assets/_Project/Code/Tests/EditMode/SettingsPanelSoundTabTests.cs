using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Audio;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1009 — the Settings panel's new SOUND tab: one ON/OFF pill per SfxDirector-registered cue plus
    /// Music, a muted cue never starts a voice (checked before the rate limit — same resolved-value
    /// instrumentation <c>ProcSfxTests</c> already reads), and the mute survives a simulated relaunch
    /// through the same <see cref="DevTuning.Save"/>/<see cref="DevTuning.LoadSaved"/> path every other
    /// knob uses.
    ///
    /// Fail-first: there is no SOUND tab on the base commit (MV-1008's HEAD, fdd7942) — TabNames only
    /// has ENEMIES/ECONOMY/WEAPONS/ARENA/FEEL, so "Page SOUND" never resolves.
    /// </summary>
    public sealed class SettingsPanelSoundTabTests
    {
        private GameObject _panelGo;
        private GameObject _directorGo;

        [TearDown]
        public void TearDown()
        {
            if (_panelGo != null) Object.DestroyImmediate(_panelGo);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            foreach (var d in Object.FindObjectsByType<SfxDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);

            DevMode.Reset();
            DevTuning.Reset();
            DevTuning.ClearSaved();
        }

        [Test]
        public void SoundTabTogglesResolveGateMutedCuesAndPersistAcrossReload()
        {
            DevTuning.Reset();
            DevTuning.ClearSaved();

            // --- 1. Every registered cue plus Music resolves its own toggle, all ON. ---
            _panelGo = new GameObject("SettingsPanel(Test)");
            var panel = _panelGo.AddComponent<SettingsPanel>();
            panel.BuildForTests();

            var canvas = _panelGo.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas, "Settings Canvas was not built.");

            var soundPage = canvas.transform.Find("Safe Area/Panel/Page SOUND");
            Assert.IsNotNull(soundPage, "Page SOUND was not resolved under the panel.");

            var onColor = new Color(0.30f, 0.85f, 0.35f);

            var musicRow = soundPage.Find("Music");
            Assert.IsNotNull(musicRow, "No Music toggle row resolved on the SOUND tab.");
            var musicBg = musicRow.Find("BG")?.GetComponent<Image>();
            Assert.IsNotNull(musicBg, "Music toggle row has no BG pill.");
            Assert.AreEqual(onColor, musicBg.color, "Music toggle must start ON (green).");

            foreach (var cue in SfxCueLibrary.AllCues)
            {
                string label = SfxCueLibrary.DisplayNames[cue];
                var row = soundPage.Find(label);
                Assert.IsNotNull(row, $"No toggle row resolved for cue '{cue}' (label '{label}').");
                var bg = row.Find("BG")?.GetComponent<Image>();
                Assert.IsNotNull(bg, $"Toggle row '{label}' has no BG pill.");
                Assert.AreEqual(onColor, bg.color, $"'{label}' should start ON (green).");
            }

            // --- 2. Flip "Robot destroyed" OFF; a muted cue must never start a voice. ---
            _directorGo = new GameObject("SfxDirector(Test)");
            var director = _directorGo.AddComponent<SfxDirector>();

            string robotLabel = SfxCueLibrary.DisplayNames[SfxCueLibrary.Cue.EnemyKilled];
            var robotButton = soundPage.Find(robotLabel).Find("BG").GetComponent<Button>();
            robotButton.onClick.Invoke();   // ON -> OFF

            for (int i = 0; i < 5; i++) HudSignals.EmitEnemyKilled(Vector3.zero);
            Assert.AreEqual(0, director.VoiceStartsThisWindow(SfxCueLibrary.Cue.EnemyKilled),
                "A muted cue must never start a voice.");

            // --- 3. Save, reset in-memory state, auto-load: still OFF. ---
            DevTuning.Save();
            DevTuning.Reset();
            DevTuning.LoadSaved();

            Assert.IsTrue(SfxDirector.IsCueMuted(SfxCueLibrary.Cue.EnemyKilled),
                "Robot destroyed must still be OFF after a simulated relaunch.");
        }
    }
}
