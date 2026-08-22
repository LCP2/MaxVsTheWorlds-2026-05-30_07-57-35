using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-537 — puts frame-rate and worst-frame numbers on the MV-503/MV-505 on-device diagnostic
    /// overlay, reusing Bootstrap's existing <see cref="FpsMeter"/> rather than a second measurement
    /// path. AC1 (resolved fps/worst-frame reported when open), AC2 (zero cost while hidden), AC4
    /// (build stamp appears), and AC5 (the MV-503 diagnostic lines still appear alongside it) are the
    /// tests here. AC3 (worst-frame tracks a spike and decays) lives in
    /// <see cref="FpsMeterTests"/> — it's a property of the meter itself, not the overlay. AC6
    /// (cc-verify) and AC7 (human TestFlight check) aren't EditMode-testable.
    /// </summary>
    public sealed class Mv537PerfOverlayTests
    {
        private GameObject _go;
        private Mv503DiagnosticOverlay _overlay;

        // Neither Awake nor OnEnable is reliably invoked for AddComponent outside Play mode — same
        // empirical finding Mv505DiagnosticOverlayTests/MV503StuckDiagnosticTests already carry.
        private static void InvokeLifecycle(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("MV-537 Perf Overlay Probe");
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

        [Test]
        public void OpenOverlay_ReportsFpsAndWorstFrame_ResolvedFromTheSameFpsMeterData()
        {
            var meter = SteadySixtyFpsMeter();

            // The resolved value, not an authored constant: a real Tick() stream drives this to ~60,
            // and the snapshot must carry that same number, not a second, independent guess.
            var snapshot = Mv503DiagnosticOverlay.BuildPerfSnapshot(meter, "abc1234-test");
            Assert.That(snapshot.Fps, Is.EqualTo(60f).Within(2f));
            Assert.That(snapshot.Fps, Is.EqualTo(meter.Fps));
            Assert.That(snapshot.FrameMs, Is.EqualTo(meter.FrameMs));
            Assert.That(snapshot.WorstFrameMs, Is.EqualTo(meter.WorstFrameMs));
            Assert.That(snapshot.WorstFrameMs, Is.GreaterThan(0f));

            // And it must actually reach the drawn text once the overlay is open.
            _overlay.SetPerfSource(meter, "abc1234-test");
            Mv503DiagnosticOverlay.ToggleVisible();
            string text = _overlay.BuildOverlayText(0f);

            Assert.That(text, Does.Contain($"{meter.Fps:0.0} fps"),
                "the resolved fps figure must reach the overlay text, not just the snapshot struct");
            Assert.That(text, Does.Contain($"worst {meter.WorstFrameMs:0.0} ms"),
                "the resolved worst-frame figure must reach the overlay text");
        }

        [Test]
        public void WhileHidden_AllocatesNothing_AndBuildsNoStrings()
        {
            var meter = SteadySixtyFpsMeter();
            _overlay.SetPerfSource(meter, "abc1234-test");
            Assert.IsFalse(_overlay.Visible, "precondition: overlay starts hidden");

            // Warm up JIT/static caches once outside the assertion — a cold first call can allocate
            // for reasons unrelated to the code under test, which AllocatingGCMemory would otherwise
            // misreport as a violation.
            _overlay.BuildOverlayText(0f);

            // Fully qualified rather than "using UnityEngine.TestTools.Constraints;" — that namespace
            // also declares an "Is" that collides with NUnit.Framework.Is used everywhere else in
            // this file (CS0104).
            //
            // Must be a void statement lambda (TestDelegate), not a value-returning one: a
            // Func<string> lambda makes NUnit dereference it to its return value up front (for
            // building a failure message), and AllocatingGCMemoryConstraint.ApplyTo(object) then
            // throws ArgumentNullException on that dereferenced null instead of ever measuring GC —
            // it needs the raw delegate handed back to re-invoke internally, not a resolved value.
            Assert.That(() => { _overlay.BuildOverlayText(0f); },
                UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(Is.Not));
            Assert.IsNull(_overlay.BuildOverlayText(0f), "a hidden overlay must still build nothing at all");
        }

        [Test]
        public void OpenOverlay_ShowsTheBuildStamp_SoAPhotoIsSelfIdentifying()
        {
            var meter = SteadySixtyFpsMeter();
            _overlay.SetPerfSource(meter, "zz9987-buildstamp");
            Mv503DiagnosticOverlay.ToggleVisible();

            string text = _overlay.BuildOverlayText(0f);
            Assert.That(text, Does.Contain("zz9987-buildstamp"));
        }

        /// <summary>Regression guard (AC5) — MV-537 must add the perf report alongside the
        /// MV-503/MV-505 movement diagnostics, not displace them.</summary>
        [Test]
        public void PerfLine_CoexistsWithTheMv503DiagnosticLines()
        {
            Debug.Log("[MV-503] handoff: cc.enabled=True isGrounded=True pos=(0.0, 0.0, 0.0)");

            var meter = SteadySixtyFpsMeter();
            _overlay.SetPerfSource(meter, "regression-guard");
            Mv503DiagnosticOverlay.ToggleVisible();

            string text = _overlay.BuildOverlayText(0f);
            Assert.That(text, Does.Contain("[MV-503] handoff:"),
                "MV-537 must not displace the MV-503/MV-505 movement diagnostics");
            Assert.That(text, Does.Contain("fps"),
                "and must add the perf report alongside them");
        }
    }
}
