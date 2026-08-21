using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Drives the missile-trail capture (MV-508) — one 1920x1080 screenshot of a
    /// <c>MaxWorlds.Dev.MissileTrailCaptureDirector</c>-staged missile in flight, for AC6's eyes-on
    /// evidence. Same harness shape as <see cref="RobotRosterCapture"/>, including its MV-444 fix (a
    /// headless-survives-domain-reload marker, since <c>EnterPlaymode()</c> always triggers one — see
    /// that class's own doc comment for the history), and its own distinct arm/marker files so it
    /// doesn't collide with the other capture directors on the same run.
    ///
    ///   Unity.exe -batchmode -projectPath . -executeMethod MaxWorlds.Editor.MissileTrailCapture.CaptureAll
    ///
    /// (NO -nographics: the capture needs a GL context; NO -quit: it exits itself once the shot exists.)
    /// </summary>
    public static class MissileTrailCapture
    {
        private const string Scene = "Assets/_Project/Scenes/Backyard_Slice.unity";
        private const string ArmFile = "Temp/missiletrail.arm";
        private const string HeadlessMarker = "Temp/missiletrail.headless";
        private const double TimeoutSeconds = 90;

        private static string OutDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press", "missile-trail"));
        private static string DoneFile => Path.Combine(OutDir, "_missiletrail_done.txt");

        [MenuItem("MaxWorlds/Capture Missile Trail")]
        public static void CaptureFromMenu()
        {
            Arm();
            OpenScene();
            EditorApplication.EnterPlaymode();
            Debug.Log("[MissileTrailCapture] filming — the editor will write the PNG to docs/press/missile-trail/ and stop play mode itself.");
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
                Debug.LogError("[MissileTrailCapture] CaptureAll failed to start: " + e);
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
                Debug.Log($"[MissileTrailCapture] done ({(ok ? "ok" : "fail")}). Marker:\n{status}");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorApplication.Exit(ok ? 0 : 1);
                return;
            }
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                EditorApplication.update -= PollHeadless;
                Disarm();
                Debug.LogError("[MissileTrailCapture] timed out waiting for capture to finish.");
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
                    ? "[MissileTrailCapture] done — see docs/press/missile-trail/. Marker:\n" + SafeRead(DoneFile)
                    : "[MissileTrailCapture] timed out.");
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
