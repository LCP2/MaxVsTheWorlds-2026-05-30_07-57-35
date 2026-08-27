using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Drives the MV-585 AC6 capture — one 852x393 (iPhone landscape) screenshot of the Force Field
    /// HUD button with the bubble raised, for Lee's eyes-on judgment. Same harness shape as
    /// <c>WaterGroundTrailCapture</c>, including the headless-survives-domain-reload marker.
    ///
    ///   Unity.exe -batchmode -projectPath . -executeMethod MaxWorlds.Editor.MV585ForceFieldCapture.CaptureAll
    ///
    /// (NO -nographics: the capture needs a GL context; NO -quit: it exits itself once the shot exists.)
    /// </summary>
    public static class MV585ForceFieldCapture
    {
        private const string Scene = "Assets/_Project/Scenes/Backyard_Slice.unity";
        private const string ArmFile = "Temp/mv585.arm";
        private const string HeadlessMarker = "Temp/mv585.headless";
        private const double TimeoutSeconds = 90;

        private static string OutDir => @"C:\Dev\MaxVsTheWorlds-Images";
        private static string DoneFile => Path.Combine(OutDir, "_mv585_done.txt");

        [MenuItem("MaxWorlds/Capture MV-585 Force Field Label Shot")]
        public static void CaptureFromMenu()
        {
            Arm();
            OpenScene();
            EditorApplication.EnterPlaymode();
            Debug.Log("[MV585ForceFieldCapture] filming — the editor will write the PNG to " +
                      OutDir + " and stop play mode itself.");
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
                Debug.LogError("[MV585ForceFieldCapture] CaptureAll failed to start: " + e);
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
                Debug.Log($"[MV585ForceFieldCapture] done ({(ok ? "ok" : "fail")}). Marker:\n{status}");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorApplication.Exit(ok ? 0 : 1);
                return;
            }
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                EditorApplication.update -= PollHeadless;
                Disarm();
                Debug.LogError("[MV585ForceFieldCapture] timed out waiting for capture to finish.");
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
                    ? "[MV585ForceFieldCapture] done — see " + OutDir + ". Marker:\n" + SafeRead(DoneFile)
                    : "[MV585ForceFieldCapture] timed out.");
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
