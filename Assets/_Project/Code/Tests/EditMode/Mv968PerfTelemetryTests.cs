using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-968 AC1 — the one new test this ticket is allowed (Testing policy, MV-465): (a) every
    /// MonoBehaviour that declares its own <c>Update</c>/<c>LateUpdate</c>/<c>FixedUpdate</c>/<c>OnGUI</c>
    /// must carry a <see cref="PerfSectionAttribute"/>, reflected the same "MaxWorlds*, exclude
    /// Tests/Editor" way <c>SceneInstallerTests.TheScan_FindsEverySystemThatSaysItInstallsItself</c>
    /// and <see cref="FrameCost"/>'s own type scan already do; (b) <see cref="PerfTelemetry"/> resolves
    /// windowed per-phase, per-section and worst-frame figures from 3 synthetic, clock-injected frames
    /// — RESOLVED values (Tier 2, MV-465), never a constant that merely happens to match what production
    /// code would produce — with the steady-state frame measured allocation-free.
    /// </summary>
    public sealed class Mv968PerfTelemetryTests
    {
        private sealed class FakeClock : PerfTelemetry.IClock
        {
            public long Ticks;
            public long GetTimestamp() => Ticks;
        }

        [Test]
        public void EveryOwnUpdateMethod_CarriesPerfSection_AndRecorderResolvesWindowedInjectedValues()
        {
            AssertEveryOwnUpdateMethodCarriesPerfSection();
            AssertRecorderResolvesWindowedInjectedValues();
        }

        private static void AssertEveryOwnUpdateMethodCarriesPerfSection()
        {
            const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public |
                                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            string[] ownUpdateMethodNames = { "Update", "LateUpdate", "FixedUpdate", "OnGUI" };

            string[] offenders = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("MaxWorlds", StringComparison.Ordinal)
                            && !a.GetName().Name.Contains("Tests")
                            && !a.GetName().Name.Contains("Editor"))
                .SelectMany(GetLoadableTypes)
                .Where(t => typeof(MonoBehaviour).IsAssignableFrom(t))
                .Where(t => ownUpdateMethodNames.Any(m => t.GetMethod(m, DeclaredInstance) != null))
                .Where(t => t.GetCustomAttributes(typeof(PerfSectionAttribute), true).Length == 0)
                .Select(t => t.FullName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                $"{offenders.Length} MonoBehaviour type(s) declare their own Update/LateUpdate/" +
                $"FixedUpdate/OnGUI with no [PerfSection] attribute: {string.Join(", ", offenders)}");
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
        }

        private static void AssertRecorderResolvesWindowedInjectedValues()
        {
            var clock = new FakeClock();
            PerfTelemetry.UseClockForTest(clock);
            PerfTelemetry.ResetRingForTest();

            const string section = "mv968-test-section";
            double ticksPerMs = Stopwatch.Frequency / 1000.0;

            double[] phaseMs = { 5.0, 7.0, 6.0 };
            double[] sectionMs = { 3.0, 4.0, 2.0 };

            void DriveOneFrame(int i)
            {
                PerfTelemetry.BeginPhase(PerfTelemetry.EnginePhase.Update);
                clock.Ticks += (long)(phaseMs[i] * ticksPerMs);
                PerfTelemetry.EndPhase(PerfTelemetry.EnginePhase.Update);

                PerfTelemetry.BeginSection(section);
                clock.Ticks += (long)(sectionMs[i] * ticksPerMs);
                PerfTelemetry.EndSection(section);

                PerfTelemetry.NotifyFrameEnd();
                clock.Ticks += (long)(1.0 * ticksPerMs);
            }

            // Frame 0 registers `section` for the first time (a Dictionary insert) — driven outside
            // the allocation-measured block below, same "warm up, then measure steady state" idiom
            // AllocationAssert's other callers in this suite already use.
            DriveOneFrame(0);

            AllocationAssert.NoGcMemory(() => DriveOneFrame(1),
                "a steady-state frame (section already registered) must allocate nothing");

            DriveOneFrame(2);

            double expectedPhaseAvg = phaseMs.Average();
            double expectedSectionAvg = sectionMs.Average();
            double expectedWorst = phaseMs.Max();

            Assert.That(PerfTelemetry.WindowedPhaseMs(PerfTelemetry.EnginePhase.Update, 1.0),
                Is.EqualTo(expectedPhaseAvg).Within(0.5),
                "windowed per-phase ms must resolve the average of the 3 injected frames");
            Assert.That(PerfTelemetry.WindowedSectionMs(section, 1.0),
                Is.EqualTo(expectedSectionAvg).Within(0.5),
                "windowed per-section ms must resolve the average of the 3 injected frames");
            Assert.That(PerfTelemetry.WorstFrameMs(5.0),
                Is.EqualTo(expectedWorst).Within(0.5),
                "worst-frame ms must resolve the single worst injected frame's engine-phase total");
        }
    }
}
