using System;
using System.IO;
using System.Linq;
using MaxWorlds.Core;
using MaxWorlds.Editor;
using NUnit.Framework;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-970 AC1 — the one new test this ticket is allowed (Testing policy, MV-465): drives
    /// <see cref="PerfSessionRecorder"/> over 3 simulated seconds with one injected 80 ms frame and two
    /// events (RESOLVED values written to real CSV files — Tier 2, never an authored constant); asserts
    /// the retention rule on an 11th session; and asserts (Tier 2 again — the plist transform's OUTPUT,
    /// not a constant that merely names the two keys) that <see cref="IOSBuild.ApplyTelemetryPlistKeys"/>
    /// sets both file-sharing keys true on a sample Info.plist.
    /// </summary>
    public sealed class Mv970PerfSessionRecorderTests
    {
        [Test]
        public void RecorderWritesRowsSpikesAndEvents_RetentionKeepsTen_AndPlistKeysApplied()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "mv970-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                AssertRecorderWritesRowsSpikesAndEvents(Path.Combine(tempRoot, "session"));
                AssertRetentionDeletesOldestOnEleventhSession(Path.Combine(tempRoot, "retention"));
                AssertPlistTransformSetsBothKeys();
            }
            finally
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        private static void AssertRecorderWritesRowsSpikesAndEvents(string dir)
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var recorder = new PerfSessionRecorder(dir, "testbuild", start);

            // One frame per simulated second (deltaSeconds = RowIntervalSeconds), all cost in initMs so
            // the row's averaged total is exactly the injected value with no other arithmetic in the way.
            // Frame index 1 (80 ms) is the one injected spike (> PerfSessionRecorder.SpikeThresholdMs).
            double[] injectedMs = { 16.0, 80.0, 20.0 };
            for (int i = 0; i < injectedMs.Length; i++)
            {
                var sample = new TelemetryFrameSample(injectedMs[i], 0, 0, 0, 0, 0, 0,
                    1, 2.0, 3.0, 4.0, "mv970-section", 1.5, "a1");
                recorder.RecordFrame(start.AddSeconds(i), PerfSessionRecorder.RowIntervalSeconds, sample);
            }

            recorder.RecordEvent(start.AddSeconds(0.5), "area_enter", "a1");
            recorder.RecordEvent(start.AddSeconds(1.5), "boss_killed", "boss1");
            recorder.Flush();

            string[] sessionLines = File.ReadAllLines(recorder.SessionCsvPath);
            Assert.That(sessionLines.Length, Is.EqualTo(4), "header + 3 rows, one per injected second");
            // ",<value>," (comma-delimited, not a bare substring) so a coincidental digit match inside the
            // ISO timestamp column can't pass this — these are the initMs/totalMs columns' exact value.
            Assert.That(sessionLines[1], Does.Contain(",16,"), "row 1 must resolve the injected 16ms frame");
            Assert.That(sessionLines[2], Does.Contain(",80,"), "row 2 must resolve the injected 80ms spike frame");
            Assert.That(sessionLines[3], Does.Contain(",20,"), "row 3 must resolve the injected 20ms frame");
            Assert.That(recorder.RowsWritten, Is.EqualTo(3));

            string[] spikeLines = File.ReadAllLines(recorder.SpikesCsvPath);
            Assert.That(spikeLines.Length, Is.EqualTo(2),
                "header + exactly one spike row for the single 80ms frame");
            Assert.That(spikeLines[1], Does.Contain(",80,"));

            string[] eventLines = File.ReadAllLines(recorder.EventsCsvPath);
            Assert.That(eventLines.Length, Is.EqualTo(3), "header + the two recorded events, in order");
            Assert.That(eventLines[1], Does.Contain("area_enter"));
            Assert.That(eventLines[2], Does.Contain("boss_killed"));
        }

        private static void AssertRetentionDeletesOldestOnEleventhSession(string dir)
        {
            var baseStart = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
            PerfSessionRecorder oldest = null;

            for (int i = 0; i < PerfSessionRecorder.RetainedSessions + 1; i++)
            {
                var recorder = new PerfSessionRecorder(dir, "b" + i, baseStart.AddSeconds(i));
                if (i == 0) oldest = recorder;
            }

            Assert.That(File.Exists(oldest.SessionCsvPath), Is.False,
                "the oldest session's CSV must be deleted once an 11th session is created");

            string[] remainingSessions = Directory.GetFiles(dir, "session-*.csv")
                .Where(f => !f.EndsWith("-spikes.csv", StringComparison.Ordinal) &&
                            !f.EndsWith("-events.csv", StringComparison.Ordinal))
                .ToArray();
            Assert.That(remainingSessions.Length, Is.EqualTo(PerfSessionRecorder.RetainedSessions),
                $"exactly {PerfSessionRecorder.RetainedSessions} sessions retained after the 11th is created");
        }

        private static void AssertPlistTransformSetsBothKeys()
        {
            const string samplePlist =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                "<plist version=\"1.0\">\n<dict>\n\t<key>CFBundleName</key>\n\t<string>MAX</string>\n</dict>\n</plist>\n";

            string patched = IOSBuild.ApplyTelemetryPlistKeys(samplePlist);

            AssertKeyIsTrue(patched, IOSBuild.FileSharingPlistKey);
            AssertKeyIsTrue(patched, IOSBuild.OpenInPlacePlistKey);

            // Idempotent — a second pass must not duplicate either key.
            string patchedTwice = IOSBuild.ApplyTelemetryPlistKeys(patched);
            Assert.That(patchedTwice, Is.EqualTo(patched), "re-applying to an already-patched plist must be a no-op");
        }

        private static void AssertKeyIsTrue(string plistXml, string key)
        {
            int keyIndex = plistXml.IndexOf($"<key>{key}</key>", StringComparison.Ordinal);
            Assert.That(keyIndex, Is.GreaterThanOrEqualTo(0), $"{key} must be present in the patched plist");

            int valueIndex = plistXml.IndexOf("<true/>", keyIndex, StringComparison.Ordinal);
            int nextKeyIndex = plistXml.IndexOf("<key>", keyIndex + 1, StringComparison.Ordinal);
            Assert.That(valueIndex, Is.GreaterThan(keyIndex), $"{key} must be immediately followed by <true/>");
            if (nextKeyIndex >= 0) Assert.That(valueIndex, Is.LessThan(nextKeyIndex),
                $"<true/> for {key} must come before the next <key>, not some unrelated later one");
        }
    }
}
