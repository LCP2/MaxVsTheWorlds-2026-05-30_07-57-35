using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Drives the press-kit capture (YT-97) and the ui-screens job (MV-421). The screenshots themselves
    /// are rendered in play mode by <c>MaxWorlds.Dev.PressKitDirector</c> (it has to be — the whole game
    /// is built at runtime); this is only the harness that opens the gameplay scene, enters play, waits
    /// for the director to finish, and — in the headless path — exits the editor with a pass/fail code
    /// the caller can branch on.
    ///
    /// Press-kit entry points:
    ///   * Menu "MaxWorlds/Capture Press Kit" — for a human in the editor.
    ///   * <see cref="CaptureAll"/> via <c>-executeMethod</c> — the automated run:
    ///     <c>Unity.exe -batchmode -projectPath . -executeMethod MaxWorlds.Editor.PressKitCapture.CaptureAll</c>
    ///     (NO -nographics: the capture needs a GPU; NO -quit: we exit ourselves once the shots exist).
    ///
    /// ui-screens entry point (MV-421):
    ///   * <see cref="CaptureUiScreens"/> via <c>-executeMethod</c>:
    ///     <c>Unity.exe -batchmode -projectPath . -executeMethod MaxWorlds.Editor.PressKitCapture.CaptureUiScreens</c>
    ///     Same NO -nographics / NO -quit rules — see <c>MaxWorlds.Dev.PressKitDirector.RunUiScreens</c> for
    ///     what actually gets captured and named.
    ///
    /// Each director run is armed by its own marker file (survives the play-mode domain reload a static
    /// flag would not) and reports completion by writing <c>&lt;outDir&gt;/_done.txt</c>.
    /// </summary>
    public static class PressKitCapture
    {
        private const string Scene = "Assets/_Project/Scenes/Backyard_Slice.unity";
        private const string ArmFile = "Temp/presskit.arm";
        private const string UiScreensArmFile = "Temp/uiscreens.arm";
        private const double TimeoutSeconds = 300;

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "press"));
        private static string DoneFile => Path.Combine(OutDir, "_done.txt");

        private static string UiScreensOutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "ui-screens"));
        private static string UiScreensDoneFile => Path.Combine(UiScreensOutDir, "_done.txt");

        [MenuItem("MaxWorlds/Capture Press Kit")]
        public static void CaptureFromMenu()
        {
            Arm();
            OpenScene();
            EditorApplication.EnterPlaymode();
            Debug.Log("[PressKit] filming — the editor will write PNGs to docs/press/ and stop play mode itself.");
            _menuMode = true;
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += PollMenu;
        }

        /// <summary>Headless entry point for -executeMethod. Blocks (via the editor update loop) until the
        /// director writes its done-marker, then exits 0; exits 1 on timeout/failure.</summary>
        public static void CaptureAll()
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                if (File.Exists(DoneFile)) File.Delete(DoneFile);
                Arm();
                OpenScene();
                _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
                EditorApplication.update += PollHeadless;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError("[PressKit] CaptureAll failed to start: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Headless entry point for -executeMethod, MV-421's ui-screens job. Same blocking
        /// pattern as <see cref="CaptureAll"/> — opens the gameplay scene, arms the director for
        /// <c>RunUiScreens</c> specifically (not the gameplay press-kit), waits for its done-marker, exits
        /// 0/1 accordingly.</summary>
        public static void CaptureUiScreens()
        {
            try
            {
                Directory.CreateDirectory(UiScreensOutDir);
                if (File.Exists(UiScreensDoneFile)) File.Delete(UiScreensDoneFile);
                ArmUiScreens();
                OpenScene();
                _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
                EditorApplication.update += PollUiScreensHeadless;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError("[UiScreens] CaptureUiScreens failed to start: " + e);
                EditorApplication.Exit(1);
            }
        }

        private static double _deadline;
        private static bool _menuMode;

        private static void PollUiScreensHeadless()
        {
            if (File.Exists(UiScreensDoneFile))
            {
                string status = SafeRead(UiScreensDoneFile);
                bool ok = status.StartsWith("ok", StringComparison.OrdinalIgnoreCase);
                EditorApplication.update -= PollUiScreensHeadless;
                DisarmUiScreens();
                Debug.Log($"[UiScreens] done ({(ok ? "ok" : "fail")}). Marker:\n{status}");
                EditorApplication.Exit(ok ? 0 : 1);
                return;
            }
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                EditorApplication.update -= PollUiScreensHeadless;
                DisarmUiScreens();
                Debug.LogError("[UiScreens] timed out waiting for capture to finish.");
                EditorApplication.Exit(1);
            }
        }

        private static void PollHeadless()
        {
            if (File.Exists(DoneFile))
            {
                string status = SafeRead(DoneFile);
                bool ok = status.StartsWith("ok", StringComparison.OrdinalIgnoreCase);
                EditorApplication.update -= PollHeadless;
                Disarm();
                Debug.Log($"[PressKit] done ({(ok ? "ok" : "fail")}). Marker:\n{status}");
                EditorApplication.Exit(ok ? 0 : 1);
                return;
            }
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                EditorApplication.update -= PollHeadless;
                Disarm();
                Debug.LogError("[PressKit] timed out waiting for capture to finish.");
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
                    ? "[PressKit] done — see docs/press/. Marker:\n" + SafeRead(DoneFile)
                    : "[PressKit] timed out.");
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
        }

        private static void ArmUiScreens()
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(UiScreensArmFile, "1");
        }

        private static void DisarmUiScreens()
        {
            try { if (File.Exists(UiScreensArmFile)) File.Delete(UiScreensArmFile); } catch { /* best effort */ }
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); } catch { return "(unreadable)"; }
        }
    }
}
