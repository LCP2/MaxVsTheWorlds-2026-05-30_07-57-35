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
        /// The marketing version (CFBundleShortVersionString) — tracks the release milestone, bumped
        /// by hand as the game hits each one (YT-135: 0.1.0 → 0.2.0, the weapon epic). The build
        /// NUMBER auto-increments separately (see <see cref="ComposeIosBuildNumber"/>); this string is
        /// the deliberate human decision about which milestone the build represents. It's a compiled
        /// constant, NOT a build-time edit of a tracked file — the thing that dirtied the tree and
        /// broke the iOS build in YT-117/YT-119.
        /// </summary>
        public const string MilestoneVersion = "0.2.0";

        /// <summary>
        /// The iOS marketing version (CFBundleShortVersionString) — always the hand-set
        /// <see cref="MilestoneVersion"/> (YT-139).
        ///
        /// It IGNORES GameCI's computed <paramref name="gameCiVersion"/> on purpose. That was the bug
        /// in YT-135: GameCI's default Semantic versioning auto-bumps a version like "0.0.152" from the
        /// commit count, and "0.0.152" is a perfectly legal iOS version string — so the old
        /// "use GameCI's if it's valid, else the milestone" rule ALWAYS took GameCI's auto-bump and the
        /// milestone pin never once applied. The version is supposed to track the milestone we choose,
        /// not the commit count, so the constant wins outright. The build NUMBER
        /// (<see cref="ComposeIosBuildNumber"/>) still auto-increments — that's the per-upload counter.
        ///
        /// Still a compiled constant, so nothing edits a tracked file at build time (YT-117).
        /// </summary>
        public static string ComposeIosVersion(string gameCiVersion) => MilestoneVersion;

        /// <summary>
        /// The iOS CFBundleVersion: a UTC <c>yyMMddHHmm</c> stamp.
        ///
        /// YT-117 proposed sourcing this from GITHUB_RUN_NUMBER instead. Don't — two reasons, both
        /// load-bearing:
        ///
        /// 1. GameCI runs Unity inside a Docker container and does NOT forward GITHUB_RUN_NUMBER
        ///    into it. The env-var branch that used to be here never once executed in CI; every
        ///    build has come from the timestamp fallback. Silent dead code that looked like the
        ///    primary path is what made this bug hard to read.
        /// 2. Apple requires CFBundleVersion to strictly increase within a marketing-version train.
        ///    Build 2607191034 is already uploaded, so a run number (11, 12, ...) is *lower* and
        ///    Apple would reject it. The timestamp is already past that value and only grows.
        ///
        /// The format is also bounded on purpose: it must stay numeric, under 18 characters, and
        /// below 2^32 (App Store Connect rejects larger integers). yyMMddHHmm satisfies all three
        /// until the year 2042; a seconds-resolution stamp would overflow 2^32 and be rejected.
        /// </summary>
        public static string ComposeIosBuildNumber(DateTime utcNow) => utcNow.ToString("yyMMddHHmm");

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
                string build = ComposeIosBuildNumber(DateTime.UtcNow);
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
