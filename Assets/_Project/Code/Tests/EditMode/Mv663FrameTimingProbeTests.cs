using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-663 — the "?" overlay's ms/frame line is derived from fps, so a phone holding 60fps at 3ms
    /// of GPU work and one holding 60fps at 15ms print the exact same line; neither the ticket's
    /// thermal question nor MV-662's device pass can be answered from it. This proves the new,
    /// separately-sourced cpu/gpu line actually reaches the drawn overlay text with the resolved
    /// figures a known raw sample produces, and that "nothing captured" formats as the literal
    /// "timing n/a" rather than a confident, silent zero — the same failure <see cref="FpsMeter"/>'s
    /// own doc comment warns against.
    /// </summary>
    public sealed class Mv663FrameTimingProbeTests
    {
        private GameObject _go;
        private Mv503DiagnosticOverlay _overlay;

        // Neither Awake nor OnEnable is reliably invoked for AddComponent outside Play mode — same
        // empirical finding Mv505DiagnosticOverlayTests/Mv537PerfOverlayTests already carry.
        private static void InvokeLifecycle(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("MV-663 Frame Timing Probe Overlay Probe");
            _overlay = _go.AddComponent<Mv503DiagnosticOverlay>();
            InvokeLifecycle(_overlay, "Awake");
            InvokeLifecycle(_overlay, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            InvokeLifecycle(_overlay, "OnDisable");
            Object.DestroyImmediate(_go);
        }

        private static FpsMeter SteadySixtyFpsMeter()
        {
            var meter = new FpsMeter(0.5f);
            float t = 0f;
            meter.Tick(t);
            for (int i = 0; i < 60; i++)
            {
                t += 1f / 60f;
                meter.Tick(t);
            }
            return meter;
        }

        /// <summary>A fake <see cref="IFrameTimingSource"/> with hand-picked values — the "small
        /// source" MV-663 asks for, the same idea FpsMeter.Tick(now) uses an injected clock for.</summary>
        private sealed class FakeFrameTimingSource : IFrameTimingSource
        {
            public uint Count;
            public FrameTiming Sample;

            public uint CaptureLatest(FrameTiming[] buffer)
            {
                if (Count > 0) buffer[0] = Sample;
                return Count;
            }
        }

        [Test]
        public void KnownTimings_ReachTheFormattedOverlayLine_AndNoReadingPrintsNaNotZero()
        {
            var meter = SteadySixtyFpsMeter();
            _overlay.SetPerfSource(meter, "mv663-test");

            // (a) A known raw sample must resolve to the correct ms figures and HasReading true — and
            // that must reach the drawn text, not just the probe's own fields.
            var knownSource = new FakeFrameTimingSource
            {
                Count = 1,
                Sample = new FrameTiming
                {
                    cpuFrameTime = 12.5,
                    cpuMainThreadFrameTime = 8.25,
                    cpuRenderThreadFrameTime = 4.0,
                    gpuFrameTime = 15.75,
                }
            };
            var probeWithReading = new FrameTimingProbe(knownSource);
            probeWithReading.Tick();

            Assert.IsTrue(probeWithReading.HasReading, "a captured sample must be reported as a reading");
            Assert.That(probeWithReading.CpuFrameTimeMs, Is.EqualTo(12.5f).Within(0.01f));
            Assert.That(probeWithReading.CpuMainThreadFrameTimeMs, Is.EqualTo(8.25f).Within(0.01f));
            Assert.That(probeWithReading.CpuRenderThreadFrameTimeMs, Is.EqualTo(4.0f).Within(0.01f));
            Assert.That(probeWithReading.GpuFrameTimeMs, Is.EqualTo(15.75f).Within(0.01f));

            _overlay.SetTimingSource(probeWithReading);
            Mv503DiagnosticOverlay.ToggleVisible();
            string readingText = _overlay.BuildOverlayText(0f);

            Assert.That(readingText, Does.Contain("cpu 12.5 ms (main 8.3 / render 4.0)  gpu 15.8 ms"),
                "the resolved cpu/gpu figures must reach the drawn overlay text, not just the probe's fields");

            // (b) Nothing captured must format as the literal "timing n/a" — never a zero, which would
            // read as a confident (and wrong) measurement.
            var emptySource = new FakeFrameTimingSource { Count = 0 };
            var probeWithNoReading = new FrameTimingProbe(emptySource);
            probeWithNoReading.Tick();

            Assert.IsFalse(probeWithNoReading.HasReading, "an empty capture must not be reported as a reading");

            _overlay.SetTimingSource(probeWithNoReading);
            // Past PerfRefreshSeconds so the cached line is forced to rebuild against the new probe.
            string naText = _overlay.BuildOverlayText(1f);

            Assert.That(naText, Does.Contain("timing n/a"),
                "no reading must draw the literal 'timing n/a' line");
            Assert.That(naText, Does.Not.Contain("cpu 0.0 ms"),
                "no reading must never be drawn as a confident zero");
        }
    }
}
