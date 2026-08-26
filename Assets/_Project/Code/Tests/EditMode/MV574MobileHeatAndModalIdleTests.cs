using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-574 — iOS ran hot and drained battery. Two independent changes, one regression surface each:
    /// <list type="bullet">
    /// <item>Change 1 drops MSAA on the Mobile quality level/URP asset (heat), verified for the URP-asset
    /// side and the SSAO/PC-untouched guards in <see cref="MsaaRenderSettingsTests"/>; this file covers
    /// the <see cref="QualitySettings"/> level side (resolved, not the source line — AC1/AC2) and that
    /// the gameplay camera's FXAA + post-processing (the AA pass Mobile keeps once MSAA drops) are
    /// untouched (AC3).</item>
    /// <item>Change 2 idles <see cref="Application.targetFrameRate"/> to 30 behind any open modal —
    /// genuinely new, leak-prone logic (<see cref="ModalFrameRateGate"/>), proven here against the
    /// MV-506 failure shape: two overlapping modals must not let the first Close() restore full rate
    /// early, and a modal torn down without a clean Close() must still restore it, not latch idle
    /// forever (AC4/AC5).</item>
    /// </list>
    /// </summary>
    public sealed class MV574MobileHeatAndModalIdleTests
    {
        [Test]
        public void QualityLevels_ResolveExpectedAntiAliasing_MobileOffPcUntouched()
        {
            int original = QualitySettings.GetQualityLevel();
            try
            {
                int mobile = System.Array.IndexOf(QualitySettings.names, "Mobile");
                int pc = System.Array.IndexOf(QualitySettings.names, "PC");
                Assert.GreaterOrEqual(mobile, 0, "no Mobile quality level");
                Assert.GreaterOrEqual(pc, 0, "no PC quality level");

                QualitySettings.SetQualityLevel(mobile, applyExpensiveChanges: false);
                Assert.AreEqual(0, QualitySettings.antiAliasing,
                    "Mobile quality level must resolve antiAliasing to 0 — MSAA 4x at native res + HDR " +
                    "is the largest bandwidth item on a tile-based mobile GPU and the leading cause of " +
                    "the reported iOS heat/battery drain.");

                QualitySettings.SetQualityLevel(pc, applyExpensiveChanges: false);
                Assert.AreEqual(4, QualitySettings.antiAliasing,
                    "PC quality level must keep antiAliasing at 4x — this ticket is mobile-only.");
            }
            finally
            {
                QualitySettings.SetQualityLevel(original, applyExpensiveChanges: false);
            }
        }

        [Test]
        public void GameplayCamera_KeepsFxaaAndPostProcessing_Unaffected()
        {
            // MV-417's problem too: whatever scene the Editor had open (e.g. Backyard_Slice.unity) can
            // still carry a real, active MainCamera-tagged object in an EditMode run, so Camera.main
            // would otherwise resolve to that ambient camera instead of this test's own.
            Camera[] suppressed = CameraTestUtil.SuppressAmbientMainCameras();
            var camGo = new GameObject("MV574 Main Camera Probe", typeof(Camera));
            camGo.tag = "MainCamera";
            var lightingGo = new GameObject("MV574 Lighting Probe");
            var lighting = lightingGo.AddComponent<BackyardLighting>();
            try
            {
                typeof(BackyardLighting)
                    .GetMethod("EnablePostProcessingOnCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(lighting, null);

                var data = camGo.GetComponent<Camera>().GetUniversalAdditionalCameraData();
                Assert.AreEqual(AntialiasingMode.FastApproximateAntialiasing, data.antialiasing,
                    "FXAA must stay exactly as BackyardLighting sets it — it is the AA pass Mobile keeps " +
                    "once MSAA drops, and it is nearly free since post-processing already forces an " +
                    "intermediate render target.");
                Assert.IsTrue(data.renderPostProcessing,
                    "post-processing must stay on — required for the tonemap/bloom/grade stack to appear at all.");
            }
            finally
            {
                Object.DestroyImmediate(lightingGo);
                Object.DestroyImmediate(camGo);
                CameraTestUtil.RestoreAmbientMainCameras(suppressed);
            }
        }

        [Test]
        public void ModalFrameRateGate_IdlesOnOpen_RestoresOnClose_HandlesOverlapAndDestroyWhileOpen()
        {
            int originalRate = Application.targetFrameRate;
            float originalScale = Time.timeScale;
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            MaxWorlds.Weapons.PendingMorphingModule.Reset();
            // Isolation from the rest of the batch run: many EditMode tests elsewhere open one of these
            // screens without going through its own Close()/OnDestroy path, which would otherwise leave
            // this static count elevated by the time this test runs (production always balances it —
            // see ModalFrameRateGate.ResetForTests's own doc).
            ModalFrameRateGate.ResetForTests();
            try
            {
                Application.targetFrameRate = 60;

                // Single modal: opening idles to 30.
                var weaponsGo = new GameObject("MV574 WeaponsScreen Probe");
                var weapons = weaponsGo.AddComponent<WeaponsScreen>();
                weapons.Open();
                Assert.AreEqual(30, Application.targetFrameRate,
                    "opening a modal must idle the frame rate to 30");

                // Overlap (MV-383's precedent: a draft-pick layers on top of an already-open
                // WeaponsScreen) — the reference count must not let the first Close() below restore
                // full rate while the second modal is still up.
                var upgradeGo = new GameObject("MV574 UpgradeScreen Probe");
                var upgrade = upgradeGo.AddComponent<UpgradeScreen>();
                upgrade.OpenStatus();
                Assert.AreEqual(30, Application.targetFrameRate,
                    "a second modal opening on top of an open one must still read 30");

                weapons.Close();
                Assert.AreEqual(30, Application.targetFrameRate,
                    "closing one of two open modals must not restore full rate while the other is " +
                    "still open — this is the MV-506 failure shape applied to frame rate instead of timeScale");

                upgrade.Continue();
                Assert.AreEqual(60, Application.targetFrameRate,
                    "closing the last open modal must restore 60");

                Object.DestroyImmediate(weaponsGo);
                Object.DestroyImmediate(upgradeGo);

                // Destroyed while open (the MV-506 failure shape itself): a modal torn down without a
                // clean Close() — a scene swap — must still restore full rate, not latch the idle rate.
                // Unity does not reliably dispatch OnDestroy synchronously for a component that was
                // created and torn down within the same EditMode test tick without ever running Start()
                // (MV506TimeScaleCaptureGuardTests hits the same limit — it never destroys an open
                // screen directly either), so this invokes the screen's own OnDestroy safety net
                // directly, exactly as Unity would call it, rather than relying on DestroyImmediate's
                // callback timing.
                var destroyGo = new GameObject("MV574 Destroy-While-Open Probe");
                var destroyScreen = destroyGo.AddComponent<WeaponsScreen>();
                destroyScreen.Open();
                Assert.AreEqual(30, Application.targetFrameRate, "sanity: reopening idles again");
                // OnDestroy also Destroy()s its canvas, which only ever runs in a real Play session —
                // under the edit-mode test runner that call logs an error that isn't the thing under
                // test here, same idiom MV506TimeScaleCaptureGuardTests already uses for HomeScreen.
                LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));
                typeof(WeaponsScreen).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(destroyScreen, null);
                Assert.AreEqual(60, Application.targetFrameRate,
                    "a modal torn down via its OnDestroy safety net while still open must still restore " +
                    "60, not latch the idle rate forever");
                Object.DestroyImmediate(destroyGo);
            }
            finally
            {
                ModalFrameRateGate.ResetForTests();
                Application.targetFrameRate = originalRate;
                Time.timeScale = originalScale;
                WeaponSystemState.Reset();
                PickupWallet.Reset();
                MaxWorlds.Weapons.PendingMorphingModule.Reset();
            }
        }
    }
}
