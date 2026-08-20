using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// MV-494: the real frame-time gate — <c>cc-verify.bat</c>'s old "60fps" step only grepped the
    /// build log for two settings strings and measured nothing. This runs INSIDE the shipped standalone
    /// player itself (not the Editor — a different scripting backend and overhead, not what ships),
    /// behind <c>-ccperf</c>, which only <c>cc-verify.bat</c> ever passes: a real player never sees it.
    ///
    /// Same INERT-unless-armed shape as <see cref="PressKitDirector"/>/<see cref="RobotRosterDirector"/>/
    /// <see cref="MaxWorlds.Dev.UiScreensDirector"/>. Once armed it forces every Mower Hutch to start
    /// producing robots from frame one via <see cref="DevTuning.StartingRobots"/> — the shipped default
    /// is 0 (YT-200: a deliberately empty opening), which would make an unattended sample measure an
    /// empty lawn, exactly the false-green the ticket bans — waits for the field to actually populate,
    /// samples a fixed run of real <c>Update</c>-loop frame times, writes a machine-readable report, and
    /// quits itself with a matching exit code so <c>cc-verify.bat</c> can branch on it directly.
    ///
    /// Runs <c>-batchmode -nographics</c>, the same flags every other cc-verify step already uses (see
    /// that file's header) — a live GL context on this shared dev machine is exactly the kind of
    /// external variance (minimised-window render throttling, driver/compositor state) this project's
    /// documented history of flaky/hanging batch invocations (the PlayMode ban above cites three) says
    /// to avoid. That means this gate measures real CPU game-logic frame cost under the shipped
    /// scripting backend — enemy AI, navigation, spawning — not GPU-bound render cost, which stays out
    /// of scope on purpose while the slice is greybox/free-kit-only (CC_AUTONOMY.md). A logic regression
    /// (the ticket's own example: "any change can halve the frame rate") is exactly what this catches.
    /// </summary>
    public sealed class PerfCaptureDirector : MonoBehaviour
    {
        private const string FlagArg = "-ccperf";
        private const string MarkerFile = "ccperf.arm";
        private const string ReportPathArg = "-perfReportPath";
        private const string ThresholdArg = "-perfP95ThresholdMs";
        private const string DefaultReportPath = "Logs/perf-report.txt";

        /// <summary>Measured baseline p95 on `main` at MV-494 was 16.67 ms — the shipped scripting
        /// backend, sampled headless (see this class's own doc comment for why), sits almost exactly on
        /// the 60fps/16.6ms frame-limiter floor for 95% of frames. This is that baseline plus headroom
        /// for run-to-run noise on a shared dev machine, per the ticket's own instruction ("set the gate
        /// slightly above it so it passes today and catches regressions tomorrow" — not tightened to
        /// the 16.6ms floor itself, which would false-fail on ordinary jitter).</summary>
        private const float DefaultP95ThresholdMs = 20f;

        /// <summary>Forces a populated field from frame one instead of YT-200's on-device 0-robot
        /// opening — see this class's own doc comment.</summary>
        private const float StartingRobotsOverride = 6f;

        private const int SampleFrameCount = 300;
        private const float WarmupTimeoutSeconds = 30f;
        private const float SettleSeconds = 2f;
        private const float SamplingTimeoutSeconds = 60f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Armed()) return;
            if (FindFirstObjectByType<PerfCaptureDirector>() != null) return;
            // Live-read by EnemySpawner every frame (see DevTuning.StartingRobots' own doc comment),
            // so setting it here — after the scene's own Awake calls, before any Start() — is already
            // in time for every factory's very first spawn decision.
            DevTuning.StartingRobots = StartingRobotsOverride;
            new GameObject("PerfCaptureDirector").AddComponent<PerfCaptureDirector>();
        }

        public static bool Armed()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, FlagArg, StringComparison.OrdinalIgnoreCase)) return true;
            try { return File.Exists(Path.Combine("Temp", MarkerFile)); }
            catch { return false; }
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            string reportPath = GetArg(ReportPathArg) ?? DefaultReportPath;
            float threshold = TryParseFloatArg(ThresholdArg, DefaultP95ThresholdMs);

            Debug.Log($"[PerfCapture] armed — report -> {reportPath}, p95 threshold {threshold:0.00} ms");

            float warmupDeadline = Time.realtimeSinceStartup + WarmupTimeoutSeconds;
            while (RobotEnemy.ActiveCount == 0)
            {
                if (Time.realtimeSinceStartup > warmupDeadline)
                {
                    Fail(reportPath, $"no robots ever spawned within {WarmupTimeoutSeconds:0}s — cannot sample a populated scene");
                    yield break;
                }
                yield return null;
            }

            // A short settle so more than the first robot out of the door is on the field by the time
            // sampling starts — "a populated area", not "one robot".
            float settleDeadline = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < settleDeadline) yield return null;

            var samplesMs = new List<float>(SampleFrameCount);
            int minLiveEnemies = int.MaxValue;
            float samplingDeadline = Time.realtimeSinceStartup + SamplingTimeoutSeconds;

            while (samplesMs.Count < SampleFrameCount)
            {
                if (Time.realtimeSinceStartup > samplingDeadline)
                {
                    Fail(reportPath, $"only sampled {samplesMs.Count}/{SampleFrameCount} frames within {SamplingTimeoutSeconds:0}s");
                    yield break;
                }
                samplesMs.Add(Time.unscaledDeltaTime * 1000f);
                minLiveEnemies = Mathf.Min(minLiveEnemies, RobotEnemy.ActiveCount);
                yield return null;
            }

            samplesMs.Sort();
            float mean = Mean(samplesMs);
            float median = Percentile(samplesMs, 0.5f);
            float p95 = Percentile(samplesMs, 0.95f);
            float worst = samplesMs[samplesMs.Count - 1];
            bool pass = p95 <= threshold && minLiveEnemies > 0;

            string report =
                $"frames_sampled: {samplesMs.Count}\n" +
                $"mean_ms: {mean.ToString("0.00", CultureInfo.InvariantCulture)}\n" +
                $"median_ms: {median.ToString("0.00", CultureInfo.InvariantCulture)}\n" +
                $"p95_ms: {p95.ToString("0.00", CultureInfo.InvariantCulture)}\n" +
                $"worst_ms: {worst.ToString("0.00", CultureInfo.InvariantCulture)}\n" +
                $"live_enemy_count: {minLiveEnemies}\n" +
                $"threshold_p95_ms: {threshold.ToString("0.00", CultureInfo.InvariantCulture)}\n" +
                $"result: {(pass ? "PASS" : "FAIL")}\n";

            Debug.Log("[PerfCapture] result:\n" + report);
            WriteReport(reportPath, report);

            if (!pass)
            {
                Debug.LogError($"[PerfCapture] FAIL — p95 {p95:0.00} ms vs threshold {threshold:0.00} ms" +
                                (minLiveEnemies <= 0 ? "; live_enemy_count was 0 during sampling" : ""));
            }

            Application.Quit(pass ? 0 : 1);
        }

        private void Fail(string reportPath, string reason)
        {
            string report = $"result: FAIL\nreason: {reason}\n";
            Debug.LogError("[PerfCapture] " + reason);
            WriteReport(reportPath, report);
            Application.Quit(1);
        }

        private static void WriteReport(string path, string contents)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, contents);
            }
            catch (Exception e)
            {
                Debug.LogError("[PerfCapture] failed to write report: " + e);
            }
        }

        private static float Mean(List<float> values)
        {
            if (values.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return sum / values.Count;
        }

        /// <summary>Nearest-rank percentile off an ALREADY-SORTED ascending list.</summary>
        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0) return 0f;
            int index = Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[index];
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private static float TryParseFloatArg(string name, float fallback)
        {
            string raw = GetArg(name);
            if (raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return v;
            return fallback;
        }
    }
}
