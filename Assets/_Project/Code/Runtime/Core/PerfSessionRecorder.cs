using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-970 ("2/2"): writes <see cref="PerfTelemetry"/>'s per-frame figures to disk so Lee can pull a
    /// real play session off a TestFlight build with no dev tools attached — MV-968 built the recorder,
    /// this ticket is the file it writes to.
    ///
    /// One instance is one play session. Three CSVs share a session-stamped base name in the caller's
    /// telemetry directory: the main file (one averaged row per second), a spikes file (one row per
    /// frame over <see cref="SpikeThresholdMs"/>, unaveraged — the exact frame that was slow), and an
    /// events file (free-form timestamped lines — <see cref="RecordEvent"/>). Writes buffer in memory
    /// and only hit disk on <see cref="Flush"/> — the ticket's "every 5s and on pause/quit" cadence is
    /// the caller's job (see <c>Bootstrap</c>), not this class's, so a test can flush deterministically
    /// with no wall-clock timer involved.
    /// </summary>
    public readonly struct TelemetryFrameSample
    {
        public readonly double InitMs, EarlyUpdateMs, FixedUpdateMs, PreUpdateMs, UpdateMs, PreLateUpdateMs, PostLateUpdateMs;
        public readonly double TotalMs;
        public readonly double FixedSteps;
        public readonly double CpuMainMs, CpuRenderMs, GpuMs;
        public readonly string TopSectionName;
        public readonly double TopSectionMs;
        public readonly string Context;

        public TelemetryFrameSample(double initMs, double earlyUpdateMs, double fixedUpdateMs, double preUpdateMs,
            double updateMs, double preLateUpdateMs, double postLateUpdateMs, double fixedSteps,
            double cpuMainMs, double cpuRenderMs, double gpuMs, string topSectionName, double topSectionMs,
            string context)
        {
            InitMs = initMs;
            EarlyUpdateMs = earlyUpdateMs;
            FixedUpdateMs = fixedUpdateMs;
            PreUpdateMs = preUpdateMs;
            UpdateMs = updateMs;
            PreLateUpdateMs = preLateUpdateMs;
            PostLateUpdateMs = postLateUpdateMs;
            // Same "the seven phases ARE the whole player-loop pass, so their sum is the frame's real
            // wall-clock cost" reasoning as PerfTelemetry.NotifyFrameEnd.
            TotalMs = initMs + earlyUpdateMs + fixedUpdateMs + preUpdateMs + updateMs + preLateUpdateMs + postLateUpdateMs;
            FixedSteps = fixedSteps;
            CpuMainMs = cpuMainMs;
            CpuRenderMs = cpuRenderMs;
            GpuMs = gpuMs;
            TopSectionName = topSectionName;
            TopSectionMs = topSectionMs;
            Context = context;
        }
    }

    public sealed class PerfSessionRecorder
    {
        /// <summary>Ticket item 2: "any frame over 50 ms" goes to the spikes file.</summary>
        public const double SpikeThresholdMs = 50.0;

        /// <summary>Ticket item 1: "one row per second".</summary>
        public const double RowIntervalSeconds = 1.0;

        /// <summary>Ticket item 4: "keep the last 10 sessions, delete older".</summary>
        public const int RetainedSessions = 10;

        /// <summary>Ticket item 4: "cap each session at 20 MB". Applies per-file (session/spikes); once
        /// a file is at or over the cap, further rows for THAT file are dropped rather than growing it
        /// unbounded — the rows already on disk are what matters, not a hard truncation mid-write.</summary>
        public const long MaxFileBytes = 20L * 1024 * 1024;

        private const string SessionHeader =
            "timestampUtc,initMs,earlyUpdateMs,fixedUpdateMs,preUpdateMs,updateMs,preLateUpdateMs,postLateUpdateMs," +
            "totalMs,fixedSteps,cpuMainMs,cpuRenderMs,gpuMs,topSectionName,topSectionMs,context";

        private const string EventHeader = "timestampUtc,eventName,context";

        private const string SpikeSuffix = "-spikes.csv";
        private const string EventSuffix = "-events.csv";

        public string SessionFileName { get; }
        public string SessionCsvPath { get; }
        public string SpikesCsvPath { get; }
        public string EventsCsvPath { get; }

        /// <summary>Rows written to the main session file — the "?" overlay's own readout (ticket item 6).</summary>
        public int RowsWritten { get; private set; }

        private readonly StringBuilder _pendingSession = new StringBuilder();
        private readonly StringBuilder _pendingSpikes = new StringBuilder();
        private readonly StringBuilder _pendingEvents = new StringBuilder();

        private double _secondsAccumulated;
        private int _sampleCount;
        private double _sumInit, _sumEarly, _sumFixed, _sumPre, _sumUpdate, _sumPreLate, _sumPostLate;
        private double _sumFixedSteps, _sumCpuMain, _sumCpuRender, _sumGpu;
        private string _lastTopSectionName = "";
        private double _lastTopSectionMs;
        private string _lastContext = "";

        private bool _sessionCapped;
        private bool _spikesCapped;

        public PerfSessionRecorder(string telemetryDir, string buildStamp, DateTime startUtc)
        {
            if (string.IsNullOrEmpty(telemetryDir)) throw new ArgumentException("telemetryDir required", nameof(telemetryDir));

            Directory.CreateDirectory(telemetryDir);
            ApplyRetention(telemetryDir);
            WriteReadmeOnce(telemetryDir);

            SessionFileName = $"session-{startUtc:yyyyMMdd-HHmmss}-{buildStamp}";
            SessionCsvPath = Path.Combine(telemetryDir, SessionFileName + ".csv");
            SpikesCsvPath = Path.Combine(telemetryDir, SessionFileName + SpikeSuffix);
            EventsCsvPath = Path.Combine(telemetryDir, SessionFileName + EventSuffix);

            File.WriteAllText(SessionCsvPath, SessionHeader + "\n");
            File.WriteAllText(SpikesCsvPath, SessionHeader + "\n");
            File.WriteAllText(EventsCsvPath, EventHeader + "\n");
        }

        /// <summary>Call once per rendered frame. <paramref name="deltaSeconds"/> drives the once-a-second
        /// row cadence explicitly (never a wall clock read internally) so a test can simulate exact
        /// seconds with no timing flake.</summary>
        public void RecordFrame(DateTime utcNow, double deltaSeconds, in TelemetryFrameSample sample)
        {
            _sampleCount++;
            _sumInit += sample.InitMs;
            _sumEarly += sample.EarlyUpdateMs;
            _sumFixed += sample.FixedUpdateMs;
            _sumPre += sample.PreUpdateMs;
            _sumUpdate += sample.UpdateMs;
            _sumPreLate += sample.PreLateUpdateMs;
            _sumPostLate += sample.PostLateUpdateMs;
            _sumFixedSteps += sample.FixedSteps;
            _sumCpuMain += sample.CpuMainMs;
            _sumCpuRender += sample.CpuRenderMs;
            _sumGpu += sample.GpuMs;
            if (!string.IsNullOrEmpty(sample.TopSectionName))
            {
                _lastTopSectionName = sample.TopSectionName;
                _lastTopSectionMs = sample.TopSectionMs;
            }
            if (!string.IsNullOrEmpty(sample.Context)) _lastContext = sample.Context;

            if (sample.TotalMs > SpikeThresholdMs) AppendSpikeRow(utcNow, sample);

            _secondsAccumulated += deltaSeconds;
            if (_secondsAccumulated >= RowIntervalSeconds && _sampleCount > 0)
            {
                AppendSessionRow(utcNow);
                ResetAccumulators();
            }
        }

        public void RecordEvent(DateTime utcNow, string eventName, string context)
        {
            _pendingEvents.Append(utcNow.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(eventName)).Append(',').Append(EscapeCsv(context)).Append('\n');
        }

        /// <summary>Writes every buffered row to disk. Caller decides the cadence (Bootstrap: every 5s
        /// and on pause/quit — ticket item 4); this class has no timer of its own.</summary>
        public void Flush()
        {
            if (_pendingSession.Length > 0)
            {
                AppendCapped(SessionCsvPath, _pendingSession, ref _sessionCapped);
                _pendingSession.Clear();
            }
            if (_pendingSpikes.Length > 0)
            {
                AppendCapped(SpikesCsvPath, _pendingSpikes, ref _spikesCapped);
                _pendingSpikes.Clear();
            }
            if (_pendingEvents.Length > 0)
            {
                File.AppendAllText(EventsCsvPath, _pendingEvents.ToString());
                _pendingEvents.Clear();
            }
        }

        private void AppendSessionRow(DateTime utcNow)
        {
            RowsWritten++;
            double n = _sampleCount;
            _pendingSession.Append(FormatRow(utcNow, _sumInit / n, _sumEarly / n, _sumFixed / n, _sumPre / n,
                _sumUpdate / n, _sumPreLate / n, _sumPostLate / n, _sumFixedSteps / n, _sumCpuMain / n,
                _sumCpuRender / n, _sumGpu / n, _lastTopSectionName, _lastTopSectionMs, _lastContext)).Append('\n');
        }

        private void AppendSpikeRow(DateTime utcNow, in TelemetryFrameSample sample)
        {
            _pendingSpikes.Append(FormatRow(utcNow, sample.InitMs, sample.EarlyUpdateMs, sample.FixedUpdateMs,
                sample.PreUpdateMs, sample.UpdateMs, sample.PreLateUpdateMs, sample.PostLateUpdateMs,
                sample.FixedSteps, sample.CpuMainMs, sample.CpuRenderMs, sample.GpuMs,
                sample.TopSectionName ?? "", sample.TopSectionMs, sample.Context ?? "")).Append('\n');
        }

        private static string FormatRow(DateTime utcNow, double initMs, double earlyMs, double fixedMs, double preMs,
            double updateMs, double preLateMs, double postLateMs, double fixedSteps, double cpuMainMs,
            double cpuRenderMs, double gpuMs, string topSectionName, double topSectionMs, string context)
        {
            double totalMs = initMs + earlyMs + fixedMs + preMs + updateMs + preLateMs + postLateMs;
            return string.Join(",",
                utcNow.ToString("O", CultureInfo.InvariantCulture),
                Num(initMs), Num(earlyMs), Num(fixedMs), Num(preMs), Num(updateMs), Num(preLateMs), Num(postLateMs),
                Num(totalMs), Num(fixedSteps), Num(cpuMainMs), Num(cpuRenderMs), Num(gpuMs),
                EscapeCsv(topSectionName), Num(topSectionMs), EscapeCsv(context));
        }

        private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // A comma inside a name/context would silently misalign columns downstream (Lee opens these in
        // Files/Numbers, not a CSV-aware tool) — semicolon is the one separator the ticket's own free-form
        // context strings (area ids, gate ids) never contain.
        private static string EscapeCsv(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(",", ";").Replace("\n", " ");

        private void ResetAccumulators()
        {
            _secondsAccumulated = 0.0;
            _sampleCount = 0;
            _sumInit = _sumEarly = _sumFixed = _sumPre = _sumUpdate = _sumPreLate = _sumPostLate = 0.0;
            _sumFixedSteps = _sumCpuMain = _sumCpuRender = _sumGpu = 0.0;
        }

        private static void AppendCapped(string path, StringBuilder pending, ref bool capped)
        {
            if (capped) return;
            var info = new FileInfo(path);
            if (info.Exists && info.Length >= MaxFileBytes)
            {
                capped = true;
                return;
            }
            File.AppendAllText(path, pending.ToString());
        }

        /// <summary>Ticket item 4: "keep the last 10 sessions, delete older" — run BEFORE this session's
        /// own files are created, so creating an 11th session's files leaves exactly
        /// <see cref="RetainedSessions"/> on disk afterward. Session base names sort chronologically as
        /// plain strings (the embedded <c>yyyyMMdd-HHmmss</c> stamp is fixed-width), so no parse is needed.</summary>
        private static void ApplyRetention(string dir)
        {
            var baseNames = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string f in Directory.GetFiles(dir, "session-*.csv"))
                baseNames.Add(StripKnownSuffix(Path.GetFileName(f)));

            int excess = baseNames.Count - (RetainedSessions - 1);
            if (excess <= 0) return;

            foreach (string baseName in baseNames)
            {
                if (excess <= 0) break;
                TryDelete(Path.Combine(dir, baseName + ".csv"));
                TryDelete(Path.Combine(dir, baseName + SpikeSuffix));
                TryDelete(Path.Combine(dir, baseName + EventSuffix));
                excess--;
            }
        }

        private static string StripKnownSuffix(string fileName)
        {
            if (fileName.EndsWith(SpikeSuffix, StringComparison.Ordinal))
                return fileName.Substring(0, fileName.Length - SpikeSuffix.Length);
            if (fileName.EndsWith(EventSuffix, StringComparison.Ordinal))
                return fileName.Substring(0, fileName.Length - EventSuffix.Length);
            return fileName.Substring(0, fileName.Length - ".csv".Length);
        }

        private static void TryDelete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static void WriteReadmeOnce(string dir)
        {
            string path = Path.Combine(dir, "README.txt");
            if (File.Exists(path)) return;

            string readme =
                "MAX vs THE WORLDS -- perf telemetry (MV-970)\n\n" +
                "Every play session writes three CSVs here, all sharing one session-stamped name:\n" +
                "  session-<yyyyMMdd-HHmmss>-<build>.csv          one row per second\n" +
                "  session-<yyyyMMdd-HHmmss>-<build>-spikes.csv   one row per frame over " +
                SpikeThresholdMs.ToString("0", CultureInfo.InvariantCulture) + " ms\n" +
                "  session-<yyyyMMdd-HHmmss>-<build>-events.csv   timestamped app/gameplay events\n\n" +
                "Session/spikes columns:\n" + SessionHeader + "\n\n" +
                "Events columns:\n" + EventHeader + "\n\n" +
                "timestampUtc is ISO-8601 (round-trip \"O\" format). Every *Ms figure is milliseconds;\n" +
                "cpu/gpu columns read 0 wherever no FrameTimingManager reading is available (e.g. off-iOS).\n" +
                "Only the last " + RetainedSessions.ToString(CultureInfo.InvariantCulture) + " sessions are kept -- older ones are deleted automatically.\n" +
                "Each CSV stops appending past " + (MaxFileBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) +
                " MB; the rows already written stay.\n";

            File.WriteAllText(path, readme);
        }
    }
}
