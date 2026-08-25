using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Drives the MV-545 aim-circle diagnostic capture. Same harness shape as
    /// <c>PressKitCapture</c>/<c>UiScreensCapture</c>: opens the gameplay scene, enters play, waits for
    /// <c>MaxWorlds.Dev.AimCircleDiagnosticsDirector</c> to write its done-marker, exits with a
    /// pass/fail code the caller can branch on. One-off diagnostic tool — not wired into cc-verify.bat,
    /// same as PressKitCapture has no dedicated .bat either.
    ///
    ///   Unity.exe -batchmode -projectPath . -executeMethod MaxWorlds.Editor.AimCircleDiagnosticsCapture.CaptureAll
    ///
    /// (NO -nographics: the capture needs a live GL context; NO -quit: the director exits the process
    /// itself once its done-marker exists or it times out.)
    /// </summary>
    public static class AimCircleDiagnosticsCapture
    {
        private const string Scene = "Assets/_Project/Scenes/Backyard_Slice.unity";
        private const string ArmFile = "Temp/aimcirclediag.arm";

        /// <summary>MV-444's fix, reapplied here: <c>EnterPlaymode()</c> triggers a domain reload (this
        /// project doesn't set <c>EnterPlayModeOptions.DisableDomainReload</c>), which wipes every static
        /// field and event subscription <see cref="CaptureAll"/> set up beforehand — including the
        /// <see cref="PollHeadless"/> subscription. Without this marker + <see cref="ResumeHeadlessPollingAfterReload"/>,
        /// the director still runs its capture to completion, but nobody is left listening for its
        /// done-marker and the process never exits — caught live running this exact ticket (MV-545): the
        /// FPS log kept spooling for 7+ minutes after the capture itself had long finished.</summary>
        private const string HeadlessMarker = "Temp/aimcirclediag.headless";

        private const double TimeoutSeconds = 120;

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press"));
        private static string DoneFile => Path.Combine(OutDir, "_aimcirclediag_done.txt");

        [MenuItem("MaxWorlds/Capture Aim Circle Diagnostic (MV-545)")]
        public static void CaptureFromMenu()
        {
            Arm();
            OpenScene();
            EditorApplication.EnterPlaymode();
            Debug.Log("[AimCircleDiag] running — the editor will write to docs/press/ and stop play mode itself.");
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += PollMenu;
        }

        /// <summary>Headless entry point for -executeMethod. Blocks (via the editor update loop) until
        /// the director writes its done-marker, then exits 0; exits 1 on timeout/failure.</summary>
        public static void CaptureAll()
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                if (File.Exists(DoneFile)) File.Delete(DoneFile);
                Arm();
                File.WriteAllText(HeadlessMarker, "1");
                OpenScene();
                ArmHeadlessPolling();
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError("[AimCircleDiag] CaptureAll failed to start: " + e);
                EditorApplication.Exit(1);
            }
        }

        private static double _deadline;

        private static void ArmHeadlessPolling()
        {
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update -= PollHeadless;
            EditorApplication.update += PollHeadless;
        }

        /// <summary>Guaranteed to run again after every domain reload, including the one
        /// <c>EnterPlaymode()</c> triggers mid-<see cref="CaptureAll"/> — see <see cref="HeadlessMarker"/>'s
        /// doc comment. No-op on a normal editor load/reload where no headless capture is in flight.</summary>
        [InitializeOnLoadMethod]
        private static void ResumeHeadlessPollingAfterReload()
        {
            if (!File.Exists(HeadlessMarker)) return;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) { Disarm(); return; }
            ArmHeadlessPolling();
        }

        private static void PollHeadless()
        {
            if (File.Exists(DoneFile))
            {
                string status = SafeRead(DoneFile);
                bool ok = status.StartsWith("ok", StringComparison.OrdinalIgnoreCase);
                EditorApplication.update -= PollHeadless;
                Disarm();
                Debug.Log($"[AimCircleDiag] done ({(ok ? "ok" : "fail")}). Marker:\n{status}");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorApplication.Exit(ok ? 0 : 1);
                return;
            }
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                EditorApplication.update -= PollHeadless;
                Disarm();
                Debug.LogError("[AimCircleDiag] timed out waiting for capture to finish.");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorApplication.Exit(1);
            }
        }

        private static void PollMenu()
        {
            bool done = File.Exists(DoneFile);
            if (done || EditorApplication.timeSinceStartup > _deadline)
            {
                EditorApplication.update -= PollMenu;
                Disarm();
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                Debug.Log(done
                    ? "[AimCircleDiag] done — see docs/press/. Marker:\n" + SafeRead(DoneFile)
                    : "[AimCircleDiag] timed out.");
            }
        }

        private static void OpenScene() => EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        private static void Arm()
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(ArmFile, "1");
        }

        private static void Disarm()
        {
            try { if (File.Exists(ArmFile)) File.Delete(ArmFile); } catch { /* best effort */ }
            try { if (File.Exists(HeadlessMarker)) File.Delete(HeadlessMarker); } catch { /* best effort */ }
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); } catch { return "(unreadable)"; }
        }
    }
}
