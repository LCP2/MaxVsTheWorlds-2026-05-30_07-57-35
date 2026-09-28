using System;
using System.IO;
using MaxWorlds.Core;
using NUnit.Framework;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-993's one allowed new test (Testing policy, MV-465): asserts the reservedMB/monoMB columns
    /// this ticket adds to <see cref="PerfSessionRecorder"/>'s session CSV are RESOLVED values actually
    /// written to the file (Tier 2), not merely a header naming them. Must fail on the pre-fix commit
    /// (58c1dee) — <see cref="TelemetryFrameSample"/> had no ReservedMB/MonoMB members and
    /// <see cref="PerfSessionRecorder.RecordInstantRow"/> did not exist, so this test would not even
    /// compile against that commit.
    /// </summary>
    public sealed class MV993PerfMemoryColumnsTests
    {
        [Test]
        public void SessionCsv_CarriesReservedAndMonoMB_AsResolvedValues_ForBothWindowedAndInstantRows()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mv993-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var start = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);
                var recorder = new PerfSessionRecorder(dir, "testbuild", start);

                // A windowed row (the once-a-second cadence) carries the reading from its one sample.
                var windowedSample = new TelemetryFrameSample(1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
                    "", 0, "a20", reservedMB: 512.3, monoMB: 88.7);
                recorder.RecordFrame(start, PerfSessionRecorder.RowIntervalSeconds, windowedSample);

                // The area-entry/low-memory forced row (MV-993 item 1/3) writes immediately, bypassing
                // the once-a-second window, with its own independent reading.
                var instantSample = new TelemetryFrameSample(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    "", 0, "area21", reservedMB: 640.1, monoMB: 95.4);
                recorder.RecordInstantRow(start.AddSeconds(0.1), instantSample);
                recorder.Flush();

                string[] lines = File.ReadAllLines(recorder.SessionCsvPath);
                Assert.That(lines[0], Does.Contain("reservedMB,monoMB"),
                    "the header must name both new columns, in order");
                Assert.That(lines.Length, Is.EqualTo(3), "header + the windowed row + the instant row");
                Assert.That(lines[1], Does.Contain(",512.3,88.7"),
                    "the windowed row must resolve the injected reservedMB/monoMB values, not merely have a header naming them");
                Assert.That(lines[2], Does.Contain(",640.1,95.4"),
                    "the instant row must resolve its OWN independent reading, written ahead of the next scheduled window");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
