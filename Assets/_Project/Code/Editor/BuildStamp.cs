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

        /// <summary>
        /// iOS rejects a bundle version that isn't purely digits-and-dots (CFBundleShortVersionString:
        /// "must consist only of '.'s and numbers, begin and end with a number, ≤18 chars"), so the
        /// SHA/timestamp stamp <see cref="Compose"/> makes is illegal there. Use the numeric version
        /// GameCI already computes (the <c>VERSION</c> env, e.g. "0.0.110") when it's valid; otherwise
        /// fall back to a stable marketing version for a hand-made iOS build.
        /// </summary>
        public static string ComposeIosVersion(string gameCiVersion) =>
            IsValidIosVersion(gameCiVersion) ? gameCiVersion : "0.1.0";

        /// <summary>True if <paramref name="v"/> is a legal iOS CFBundleShortVersionString.</summary>
        public static bool IsValidIosVersion(string v)
        {
            if (string.IsNullOrEmpty(v) || v.Length > 18) return false;
            if (!char.IsDigit(v[0]) || !char.IsDigit(v[v.Length - 1])) return false;
            bool lastWasDot = false;
            foreach (char c in v)
            {
                if (c == '.')
                {
                    if (lastWasDot) return false; // no empty components ("1..0")
                    lastWasDot = true;
                }
                else if (char.IsDigit(c)) { lastWasDot = false; }
                else return false; // letters, dashes, etc. are illegal
            }
            return true;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.iOS)
            {
                // Numeric marketing version (SHA stamp is illegal on iOS — see ComposeIosVersion),
                // plus a unique build number so each TestFlight upload is distinct.
                string version = ComposeIosVersion(Environment.GetEnvironmentVariable("VERSION"));
                string run = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
                string build = string.IsNullOrEmpty(run) ? DateTime.UtcNow.ToString("yyMMddHHmm") : run;
                PlayerSettings.bundleVersion = version;
                PlayerSettings.iOS.buildNumber = build;
                Debug.Log($"[BuildStamp] iOS version {version} build {build} (commit {Compose()})");
                return;
            }

            string stamp = Compose();
            PlayerSettings.bundleVersion = stamp;
            Debug.Log($"[BuildStamp] build version: {stamp}");
        }
    }
}
