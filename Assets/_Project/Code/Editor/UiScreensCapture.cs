using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Drives the UI-screens capture (MV-421) — the fixed-state screenshot job, distinct from
    /// <see cref="PressKitCapture"/>'s live-gameplay press kit. Same harness shape (opens the
    /// gameplay scene, enters play, waits for the director's done-marker, exits with a pass/fail
    /// code) but arms <c>MaxWorlds.Dev.UiScreensDirector</c> instead, via its own marker file so the
    /// two capture runs never collide.
    ///
    ///   Unity.exe -batchmode -projectPath . -executeMethod MaxWorlds.Editor.UiScreensCapture.CaptureAll
    ///
    /// (NO -nographics: the capture needs a GL context; NO -quit: exits itself once the shots exist.)
    /// </summary>
    public static class UiScreensCapture
    {
        private const string Scene = "Assets/_Project/Scenes/Backyard_Slice.unity";
        private const string ArmFile = "Temp/uiscreens.arm";

        /// <summary>MV-444: <c>EnterPlaymode()</c> triggers a domain reload (this project doesn't set
        /// <c>EnterPlayModeOptions.DisableDomainReload</c> — see <c>EditorSettings.asset</c>), which wipes
        /// every static field and event subscription <see cref="CaptureAll"/> set up beforehand,
        /// including the <see cref="PollHeadless"/> subscription and <see cref="_deadline"/>. With nobody
        /// left polling, the director still captures and writes its done-marker, but the process never
        /// notices and never calls <c>Exit()</c> — it just idles simulating the scene until an external
        /// timeout kills it. Caught live running this exact ticket: a `cc-screens.bat` invocation ran the
        /// capture to completion in well under a minute of play-mode time but the Unity process itself
        /// was still alive ten minutes later. This marker file (distinct from <see cref="ArmFile"/>, which
        /// <see cref="CaptureFromMenu"/> also sets) is what <see cref="ResumeHeadlessPollingAfterReload"/>
        /// checks to know a headless run is in flight and worth re-arming after every reload.</summary>
        private const string HeadlessMarker = "Temp/uiscreens.headless";

        private const double TimeoutSeconds = 120;

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press"));
        private static string DoneFile => Path.Combine(OutDir, "_uiscreens_done.txt");

        [MenuItem("MaxWorlds/Capture UI Screens")]
        public static void CaptureFromMenu()
        {
            Arm();
            OpenScene();
            EditorApplication.EnterPlaymode();
            Debug.Log("[UiScreens] filming — the editor will write PNGs to docs/press/ and stop play mode itself.");
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
                Debug.LogError("[UiScreens] CaptureAll failed to start: " + e);
                EditorApplication.Exit(1);
            }
        }

        private static double _deadline;

        /// <summary>(Re-)subscribes <see cref="PollHeadless"/> with a fresh deadline. Called both from
        /// <see cref="CaptureAll"/> directly and from <see cref="ResumeHeadlessPollingAfterReload"/> after
        /// the domain reload <c>EnterPlaymode()</c> triggers wipes the first subscription — see
        /// <see cref="HeadlessMarker"/>'s doc comment for why that second call is load-bearing, not
        /// defensive.</summary>
        private static void ArmHeadlessPolling()
        {
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update -= PollHeadless;
            EditorApplication.update += PollHeadless;
        }

        /// <summary>Guaranteed to run again after every domain reload, including the one
        /// <c>EnterPlaymode()</c> triggers mid-<see cref="CaptureAll"/> — this is what makes a headless
        /// run's exit actually reliable (see <see cref="HeadlessMarker"/>). A no-op on a normal editor
        /// load/reload where no headless capture is in flight.</summary>
        [InitializeOnLoadMethod]
        private static void ResumeHeadlessPollingAfterReload()
        {
            if (!File.Exists(HeadlessMarker)) return;
            // Guard against a stale marker left by an externally-killed run spuriously waking this
            // polling loop up during an unrelated batch invocation — cc-verify.bat's compile-check and
            // build steps never enter Play Mode, so isPlayingOrWillChangePlaymode is the actual signal
            // that this reload is the one EnterPlaymode() inside CaptureAll() triggered, not just any
            // domain reload. A stale marker with no matching play-mode transition cleans itself up here
            // instead of lingering to confuse the next unrelated Unity invocation.
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
                Debug.Log($"[UiScreens] done ({(ok ? "ok" : "fail")}). Marker:\n{status}");
                // Belt-and-braces (MV-421 fix comment): when this was run locally without an attached
                // display, EditorApplication.Exit() alone did not terminate the process while still in
                // play mode — it kept simulating until an external timeout killed it. Drop out of play
                // mode first; unverified whether this alone fixes it in that environment, but it costs
                // nothing under CI's xvfb-run either way.
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorApplication.Exit(ok ? 0 : 1);
                return;
            }
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                EditorApplication.update -= PollHeadless;
                Disarm();
                Debug.LogError("[UiScreens] timed out waiting for capture to finish.");
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
                    ? "[UiScreens] done — see docs/press/. Marker:\n" + SafeRead(DoneFile)
                    : "[UiScreens] timed out.");
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
