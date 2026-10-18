using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Audio;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1237 — the Mothership's own procedural music row and SFX cues, same pipeline as every other
    /// world (<see cref="ProcMusic"/>/<see cref="ProcSfx"/>, no outside tools). One EditMode test per
    /// the project's testing policy:
    ///
    /// 1. <see cref="ProcMusic.RenderLoop"/> for <see cref="MusicWorld.Mothership"/> is deterministic —
    ///    two renders are byte-identical — and differs from the Reef's own rendered buffer (Tier 2: a
    ///    resolved render output, never an authored constant).
    /// 2. Every one of this ticket's 14 new <see cref="SfxCueLibrary.Cue"/> ids resolves a real toggle
    ///    row on the Settings panel's SOUND tab, built the same generic way every existing cue's row is
    ///    (sourced off <see cref="SfxCueLibrary.AllCues"/>/<see cref="SfxCueLibrary.DisplayNames"/>) —
    ///    not just a dictionary-key check, and (per the project's Rule 3 — no presence-only asserts) the
    ///    row's BG pill must resolve to the same "ON" green every other cue's row starts at, same
    ///    resolved-property check as <c>SettingsPanelSoundTabTests</c>'s own loop over every cue.
    ///
    /// Fail-first: neither <see cref="MusicWorld.Mothership"/> nor any of the 14 new
    /// <see cref="SfxCueLibrary.Cue"/> members existed on the base commit (5cf68d4) — this file does
    /// not compile without them. Confirmed with <c>cc-test.bat MV1237MothershipAudioTests</c> against
    /// that commit plus this test file alone:
    ///
    /// <c>error CS0117: 'MusicWorld' does not contain a definition for 'Mothership'</c> (and thirteen
    /// more CS0117s, one per missing <see cref="SfxCueLibrary.Cue"/> member) — a compile failure, the
    /// same valid "fails" shape <c>MV1137GeneratedMusicTests</c> and <c>MV1008ProcMusicTests</c> both
    /// document for a type/member that doesn't exist yet on base.
    /// </summary>
    public sealed class MV1237MothershipAudioTests
    {
        private static readonly SfxCueLibrary.Cue[] NewCues =
        {
            SfxCueLibrary.Cue.LightMissileLaunch,
            SfxCueLibrary.Cue.LightMissileHit,
            SfxCueLibrary.Cue.OrbitalsHit,
            SfxCueLibrary.Cue.CradleBuildTick,
            SfxCueLibrary.Cue.CradleStepOut,
            SfxCueLibrary.Cue.CradleDestroyed,
            SfxCueLibrary.Cue.SkitterHop,
            SfxCueLibrary.Cue.BreakerChainSnap,
            SfxCueLibrary.Cue.BreakerPunch,
            SfxCueLibrary.Cue.BreakerRam,
            SfxCueLibrary.Cue.TractorBeam,
            SfxCueLibrary.Cue.VentFire,
            SfxCueLibrary.Cue.HelmsmanLensSweep,
            SfxCueLibrary.Cue.ViewportRising,
        };

        private GameObject _panelGo;

        [TearDown]
        public void TearDown()
        {
            if (_panelGo != null) Object.DestroyImmediate(_panelGo);
        }

        [Test]
        public void MothershipMusicIsDeterministicAndDistinct_AndEveryNewSfxIdHasASettingsRow()
        {
            // --- (1) ProcMusic: deterministic, and distinct from the Reef it used to reuse ---
            float[] renderA = ProcMusic.RenderLoop(MusicWorld.Mothership);
            float[] renderB = ProcMusic.RenderLoop(MusicWorld.Mothership);
            CollectionAssert.AreEqual(renderA, renderB,
                "ProcMusic.RenderLoop(Mothership) must render byte-identical buffers on repeat calls.");

            float[] reef = ProcMusic.RenderLoop(MusicWorld.Reef);
            CollectionAssert.AreNotEqual(renderA, reef,
                "The Mothership's buffer must differ from the Reef's — it must not still be reusing it.");

            // --- (2) every new cue gets a real row on the SOUND tab, same generic build as every
            // other registered cue (SettingsPanelSoundTabTests proves the existing cues the same way) ---
            _panelGo = new GameObject("SettingsPanel(Test)");
            var panel = _panelGo.AddComponent<SettingsPanel>();
            panel.BuildForTests();

            var canvas = _panelGo.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas, "Settings Canvas was not built.");
            var soundPage = canvas.transform.Find("Safe Area/Panel/Page SOUND");
            Assert.IsNotNull(soundPage, "Page SOUND was not resolved under the panel.");

            var onColor = new Color(0.30f, 0.85f, 0.35f);

            foreach (var cue in NewCues)
            {
                string label = SfxCueLibrary.DisplayNames[cue];
                var row = soundPage.Find(label);
                Assert.IsNotNull(row, $"No toggle row resolved for new cue '{cue}' (label '{label}').");
                var bg = row.Find("BG")?.GetComponent<Image>();
                Assert.IsNotNull(bg, $"Toggle row '{label}' has no BG pill.");
                Assert.AreEqual(onColor, bg.color, $"'{label}' should start ON (green).");
            }
        }
    }
}
