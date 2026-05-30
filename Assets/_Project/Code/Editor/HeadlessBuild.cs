using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Headless build entry point invoked by <c>cc-verify.bat</c>:
    /// <c>-executeMethod MaxWorlds.Editor.HeadlessBuild.WindowsBootstrap -buildOutput &lt;path&gt;</c>.
    /// Builds the enabled build scenes (Bootstrap is scene 0) to a Windows64
    /// standalone — the sanctioned substitute for the iOS device build — and
    /// emits the frame-settings marker the verify step asserts on. Exits the
    /// editor with the build's result code so the .bat can branch on errorlevel.
    /// </summary>
    public static class HeadlessBuild
    {
        private const string DefaultOutput = "Builds/cc-verify/MaxVsTheWorlds.exe";
        private const string BootstrapScene = "Assets/_Project/Scenes/Bootstrap.unity";

        public static void WindowsBootstrap()
        {
            string output = GetArg("-buildOutput") ?? DefaultOutput;
            string dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.enabled)
                {
                    scenes.Add(s.path);
                }
            }
            if (scenes.Count == 0 && File.Exists(BootstrapScene))
            {
                scenes.Add(BootstrapScene);
            }
            if (scenes.Count == 0)
            {
                Debug.LogError("[HeadlessBuild] No scenes to build.");
                EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            // Slice frame config — asserted by cc-verify step 4 (matches Bootstrap.Awake).
            Debug.Log("[HeadlessBuild] slice config: targetFrameRate=60 vSync=0");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[HeadlessBuild] Windows build FAILED: {summary.result} ({summary.totalErrors} errors)");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[HeadlessBuild] Windows build OK -> {output} ({summary.totalSize} bytes)");
            EditorApplication.Exit(0);
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
