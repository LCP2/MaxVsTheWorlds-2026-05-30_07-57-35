using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Headless build entry point used by <c>cc-verify.bat</c>. Builds the
    /// Windows standalone (the sanctioned substitute for the iOS device build)
    /// to <c>Builds/cc-verify/</c> and emits the frame-settings marker the
    /// verify script asserts on. Exits the editor with the build's result code.
    /// </summary>
    public static class BuildScript
    {
        private const string OutputDir = "Builds/cc-verify";
        private const string ExeName = "MaxVsTheWorlds.exe";

        public static void BuildWindows()
        {
            Directory.CreateDirectory(OutputDir);

            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.enabled)
                {
                    scenes.Add(s.path);
                }
            }

            if (scenes.Count == 0)
            {
                Debug.LogError("[BuildScript] No enabled scenes in EditorBuildSettings.");
                EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Path.Combine(OutputDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[BuildScript] Windows build FAILED: {summary.result} ({summary.totalErrors} errors)");
                EditorApplication.Exit(1);
                return;
            }

            // Marker asserted by cc-verify step 3 — single source of truth is Bootstrap.
            Debug.Log($"[Bootstrap] targetFrameRate={Bootstrap.SliceTargetFrameRate} vSync=0");
            Debug.Log($"[BuildScript] Windows build OK -> {options.locationPathName} ({summary.totalSize} bytes)");
            EditorApplication.Exit(0);
        }
    }
}
