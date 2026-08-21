using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Drives the MV-507 MSAA comparison capture via <c>MaxWorlds.Dev.MsaaComparisonDirector</c>. Same
    /// harness shape as <see cref="RobotRosterCapture"/> — a headless-survives-domain-reload marker,
    /// since <c>EnterPlaymode()</c> always triggers one — with its own arm/marker files so it never
    /// collides with the roster or press-kit captures.
    ///
    ///   Unity.exe -batchmode -projectPath . -executeMethod MaxWorlds.Editor.MsaaComparisonCapture.Capture
    ///
    /// Run once with the URP asset's MSAA off and once with it on; the director names each PNG by the
    /// resolved <c>msaaSampleCount</c> it actually rendered with, so re-running never silently overwrites
    /// the other state's evidence.
    /// </summary>
    public static class MsaaComparisonCapture
    {
        private const string Scene = "Assets/_Project/Scenes/Backyard_Slice.unity";
        private const string ArmFile = "Temp/msaacompare.arm";
        private const string HeadlessMarker = "Temp/msaacompare.headless";
        private const double TimeoutSeconds = 60;

        private static string OutDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "msaa-comparison"));
        private static string DoneFile => Path.Combine(OutDir, "_msaacompare_done.txt");

        [MenuItem("MaxWorlds/Capture MSAA Comparison (MV-507)")]
        public static void CaptureFromMenu()
        {
            Arm();
            OpenScene();
            EditorApplication.EnterPlaymode();
            Debug.Log("[MsaaComparison] filming — the editor will write a PNG to docs/press/msaa-comparison/ and stop play mode itself.");
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += PollMenu;
        }

        /// <summary>Headless entry point for -executeMethod. Blocks (via the editor update loop) until
        /// the director writes its done-marker, then exits 0; exits 1 on timeout/failure.</summary>
        public static void Capture()
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
                Debug.LogError("[MsaaComparison] Capture failed to start: " + e);
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
                Debug.Log($"[MsaaComparison] done ({(ok ? "ok" : "fail")}). Marker:\n{status}");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorApplication.Exit(ok ? 0 : 1);
                return;
            }
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                EditorApplication.update -= PollHeadless;
                Disarm();
                Debug.LogError("[MsaaComparison] timed out waiting for capture to finish.");
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
                    ? "[MsaaComparison] done — see docs/press/msaa-comparison/. Marker:\n" + SafeRead(DoneFile)
                    : "[MsaaComparison] timed out.");
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
