using System;
using System.IO;
using MaxWorlds.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// The single reusable headless/menu driver (MV-592) for every preset in
    /// <see cref="CapturePresets"/> — supersedes the per-ticket Editor entry points that used to sit
    /// alongside each bespoke director. Same harness shape every one of them shared: arm the preset's
    /// marker/flag, open its scene, enter play mode, poll for its done-marker (surviving the domain
    /// reload <c>EnterPlaymode()</c> always triggers — the MV-444 fix), then exit 0/1.
    ///
    ///   Unity.exe -batchmode -projectPath . -executeMethod MaxWorlds.Editor.CaptureEntryPoint.CaptureHealthBarCluster
    ///   (and CaptureMissileTrail / CaptureWaterGroundTrail / CaptureMv585ForceField)
    ///
    /// (NO -nographics: the capture needs a GL context; NO -quit: it exits itself once the shot(s) exist.)
    ///
    /// A visual ticket that needs a new capture adds a preset to <see cref="CapturePresets"/> and, if it
    /// needs headless/menu invocation, one thin wrapper method here — not a new director/entry-point
    /// pair. See CC_AUTONOMY.md's guardrail on new files under Runtime/Dev or Editor.
    /// </summary>
    public static class CaptureEntryPoint
    {
        [MenuItem("MaxWorlds/Capture/Health Bar Cluster")]
        public static void MenuHealthBarCluster() => RunFromMenu(CapturePresets.All["healthbarcluster"]);
        public static void CaptureHealthBarCluster() => Run(CapturePresets.All["healthbarcluster"]);

        [MenuItem("MaxWorlds/Capture/Missile Trail (MV-508)")]
        public static void MenuMissileTrail() => RunFromMenu(CapturePresets.All["missiletrail"]);
        public static void CaptureMissileTrail() => Run(CapturePresets.All["missiletrail"]);

        [MenuItem("MaxWorlds/Capture/Water Ground Trail (MV-555)")]
        public static void MenuWaterGroundTrail() => RunFromMenu(CapturePresets.All["watergroundtrail"]);
        public static void CaptureWaterGroundTrail() => Run(CapturePresets.All["watergroundtrail"]);

        [MenuItem("MaxWorlds/Capture/Force Field Label (MV-585)")]
        public static void MenuMv585ForceField() => RunFromMenu(CapturePresets.All["mv585forcefield"]);
        public static void CaptureMv585ForceField() => Run(CapturePresets.All["mv585forcefield"]);

        [MenuItem("MaxWorlds/Capture/HUD Reshuffle 1920x1080 (MV-606)")]
        public static void MenuMv6061920() => RunFromMenu(CapturePresets.All["mv6061920"]);
        public static void CaptureMv6061920() => Run(CapturePresets.All["mv6061920"]);

        [MenuItem("MaxWorlds/Capture/HUD Reshuffle Phone (MV-606)")]
        public static void MenuMv606Phone() => RunFromMenu(CapturePresets.All["mv606phone"]);
        public static void CaptureMv606Phone() => Run(CapturePresets.All["mv606phone"]);

        [MenuItem("MaxWorlds/Capture/Water Reach (MV-617)")]
        public static void MenuMv617WaterReach() => RunFromMenu(CapturePresets.All["mv617waterreach"]);
        public static void CaptureMv617WaterReach() => Run(CapturePresets.All["mv617waterreach"]);

        private static string PrimaryOutDir(CapturePreset preset) => preset.OutputDirs[0];
        private static string DoneFile(CapturePreset preset) => Path.Combine(PrimaryOutDir(preset), preset.DoneFileName);

        public static void RunFromMenu(CapturePreset preset)
        {
            Arm(preset);
            OpenScene(preset);
            EditorApplication.EnterPlaymode();
            Debug.Log($"{preset.LogTag} filming — the editor will write to {PrimaryOutDir(preset)} and stop play mode itself.");
            _active = preset;
            _deadline = EditorApplication.timeSinceStartup + preset.TimeoutSeconds;
            EditorApplication.update += PollMenu;
        }

        /// <summary>Headless entry point for -executeMethod. Blocks (via the editor update loop) until
        /// the director writes its done-marker, then exits 0; exits 1 on timeout/failure.</summary>
        public static void Run(CapturePreset preset)
        {
            try
            {
                Directory.CreateDirectory(PrimaryOutDir(preset));
                string doneFile = DoneFile(preset);
                if (File.Exists(doneFile)) File.Delete(doneFile);
                Arm(preset);
                Directory.CreateDirectory("Temp");
                File.WriteAllText(preset.HeadlessMarker, "1");
                OpenScene(preset);
                ArmHeadlessPolling(preset);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError($"{preset.LogTag} Run failed to start: " + e);
                EditorApplication.Exit(1);
            }
        }

        private static CapturePreset _active;
        private static double _deadline;

        private static void ArmHeadlessPolling(CapturePreset preset)
        {
            _active = preset;
            _deadline = EditorApplication.timeSinceStartup + preset.TimeoutSeconds;
            EditorApplication.update -= PollHeadless;
            EditorApplication.update += PollHeadless;
        }

        /// <summary>EnterPlaymode() always triggers a domain reload, which wipes _active/_deadline —
        /// find whichever preset's headless marker survived on disk and resume polling for it.</summary>
        [InitializeOnLoadMethod]
        private static void ResumeHeadlessPollingAfterReload()
        {
            foreach (var preset in CapturePresets.All.Values)
            {
                if (!File.Exists(preset.HeadlessMarker)) continue;
                if (!EditorApplication.isPlayingOrWillChangePlaymode) { Disarm(preset); continue; }
                ArmHeadlessPolling(preset);
                return;
            }
        }

        private static void PollHeadless()
        {
            var preset = _active;
            if (preset == null) { EditorApplication.update -= PollHeadless; return; }
            string doneFile = DoneFile(preset);
            if (File.Exists(doneFile))
            {
                string status = SafeRead(doneFile);
                bool ok = status.StartsWith("ok", StringComparison.OrdinalIgnoreCase);
                EditorApplication.update -= PollHeadless;
                Disarm(preset);
                Debug.Log($"{preset.LogTag} done ({(ok ? "ok" : "fail")}). Marker:\n{status}");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorApplication.Exit(ok ? 0 : 1);
                return;
            }
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                EditorApplication.update -= PollHeadless;
                Disarm(preset);
                Debug.LogError($"{preset.LogTag} timed out waiting for capture to finish.");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorApplication.Exit(1);
            }
        }

        private static void PollMenu()
        {
            var preset = _active;
            if (preset == null) { EditorApplication.update -= PollMenu; return; }
            bool done = File.Exists(DoneFile(preset));
            if (done || EditorApplication.timeSinceStartup > _deadline)
            {
                EditorApplication.update -= PollMenu;
                Disarm(preset);
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                Debug.Log(done
                    ? $"{preset.LogTag} done — see {PrimaryOutDir(preset)}. Marker:\n" + SafeRead(DoneFile(preset))
                    : $"{preset.LogTag} timed out.");
            }
        }

        private static void OpenScene(CapturePreset preset) => EditorSceneManager.OpenScene(preset.Scene, OpenSceneMode.Single);

        private static void Arm(CapturePreset preset)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(preset.ArmFile, "1");
        }

        private static void Disarm(CapturePreset preset)
        {
            try { if (File.Exists(preset.ArmFile)) File.Delete(preset.ArmFile); } catch { /* best effort */ }
            try { if (File.Exists(preset.HeadlessMarker)) File.Delete(preset.HeadlessMarker); } catch { /* best effort */ }
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); } catch { return "(unreadable)"; }
        }
    }
}
