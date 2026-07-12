using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Stamps every build with the commit it was built from (YT-62).
    ///
    /// This exists because we lost a whole review cycle to a question nobody could answer: "is the
    /// build you're looking at actually the build I shipped?" QA reported the FPS overlay still
    /// reading 0 on the live link, while the same build's console — measured minutes earlier — was
    /// logging ~57. Both cannot be true of one build, and there was no way to tell which build was
    /// on screen.
    ///
    /// Now there is. The commit SHA goes into <see cref="Application.version"/>, gets drawn in the
    /// corner of the screen and printed to the console at startup. "Which build am I on" stops being
    /// a matter of opinion.
    ///
    /// It also feeds the cache-buster (see WebGlCacheBust), so a browser can't quietly serve a
    /// months-old build from its cache.
    /// </summary>
    public sealed class BuildStamp : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        /// <summary>The version string baked into the build. Prefers the CI commit; falls back to a
        /// local timestamp so a hand-made build is still identifiable.</summary>
        public static string Compose()
        {
            string sha = Environment.GetEnvironmentVariable("GITHUB_SHA");
            string shortSha = string.IsNullOrEmpty(sha) ? "local" : sha.Substring(0, Math.Min(7, sha.Length));
            string stamp = DateTime.UtcNow.ToString("MMdd-HHmm");
            return $"{shortSha}-{stamp}";
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            string version = Compose();
            PlayerSettings.bundleVersion = version;
            Debug.Log($"[BuildStamp] build version: {version}");
        }
    }
}
