using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-968: the single release-build-safe, windowed replacement for <see cref="FrameCost"/>'s
    /// since-boot buckets (MV-968's own ticket text names the exact bug: <c>FrameCost.Reset()</c> is
    /// never called from a path that runs on <c>IPhonePlayer</c>, so every device reading before this
    /// ticket was an average diluted by however long the app had been running).
    ///
    /// Two independent measurements, both timed on the same <see cref="IClock"/> seam (Stopwatch ticks,
    /// so this works identically in a release player with no Development Build / Profiler dependency):
    ///
    /// 1. <b>Engine phases</b> — <see cref="BeginPhase"/>/<see cref="EndPhase"/> around each of the
    ///    seven top-level <c>UnityEngine.LowLevel.PlayerLoop</c> phases, installed once by
    ///    <see cref="InstallPlayerLoopHooks"/> (production; a test drives these two methods directly
    ///    with no dependency on the real player loop).
    /// 2. <b>Named sections</b> — <see cref="BeginSection"/>/<see cref="EndSection"/>, keyed by the
    ///    same string a <see cref="PerfSectionAttribute"/> names a system with.
    ///
    /// Both close out into one shared ring buffer per rendered frame (<see cref="NotifyFrameEnd"/>),
    /// fixed-capacity and preallocated — nothing here allocates once <see cref="NotifyFrameEnd"/> has
    /// run once (its first call touches every backing array, which are all sized at class load), which
    /// is what lets a test wrap a steady-state frame in <c>AllocationAssert.NoGcMemory</c>.
    ///
    /// "Windowed, never since-boot" (ticket item 3): every read-side query takes an explicit
    /// <c>windowSeconds</c> and only ever sums/maxes over ring entries younger than that — a rolling 1s
    /// window for the per-phase/per-section averages, and the single worst frame in the last 5s,
    /// exactly the two figures the ticket's overlay item asks for. The ring is time-ordered (oldest to
    /// newest is a contiguous walk backwards from the write head), so every query can stop at the first
    /// entry older than the window instead of scanning the whole ring.
    /// </summary>
    public static class PerfTelemetry
    {
        /// <summary>The seven top-level <c>PlayerLoop</c> phases the ticket names, in the order Unity
        /// runs them every frame.</summary>
        public enum EnginePhase
        {
            Initialization, EarlyUpdate, FixedUpdate, PreUpdate, Update, PreLateUpdate, PostLateUpdate
        }

        private const int PhaseCount = 7;

        /// <summary>Ticket item 2: "the section names group into &lt;=24 systems". A section past this
        /// cap is silently dropped by <see cref="SectionIndex"/> rather than throwing or growing — a
        /// diagnostic instrument must never take the game down because a 25th system registered.</summary>
        private const int MaxSections = 24;

        /// <summary>512 rendered frames covers a 5s worst-frame window down to ~102fps before the ring
        /// wraps early — comfortably above this project's 60fps target — without sizing the ring off a
        /// runtime frame-rate reading, which a fixed-capacity, allocate-once buffer can't do anyway.</summary>
        private const int RingCapacity = 512;

        // -----------------------------------------------------------------------------------------
        // Clock seam — identical idiom to FrameCost.IClock: production uses Stopwatch.GetTimestamp(),
        // a test injects exact hand-picked deltas with no dependency on wall time.
        // -----------------------------------------------------------------------------------------

        public interface IClock
        {
            long GetTimestamp();
        }

        private sealed class StopwatchClock : IClock
        {
            public long GetTimestamp() => Stopwatch.GetTimestamp();
        }

        private static IClock s_clock = new StopwatchClock();

        /// <summary>Test seam — production code never calls this.</summary>
        public static void UseClockForTest(IClock clock) => s_clock = clock ?? new StopwatchClock();

        // -----------------------------------------------------------------------------------------
        // Section registry — a name is assigned a stable index the first time it's seen. That first
        // registration allocates (a Dictionary entry); every Begin/End call after is a lookup against
        // an existing key, which does not allocate.
        // -----------------------------------------------------------------------------------------

        private static readonly Dictionary<string, int> s_sectionIndices = new Dictionary<string, int>(MaxSections);
        private static readonly string[] s_sectionNames = new string[MaxSections];
        private static int s_sectionCount;

        private static int SectionIndex(string section)
        {
            if (s_sectionIndices.TryGetValue(section, out int idx)) return idx;
            if (s_sectionCount >= MaxSections) return -1;
            idx = s_sectionCount++;
            s_sectionIndices[section] = idx;
            s_sectionNames[idx] = section;
            return idx;
        }

        // -----------------------------------------------------------------------------------------
        // Current-frame accumulation — cleared by NotifyFrameEnd, never reallocated.
        // -----------------------------------------------------------------------------------------

        private static readonly long[] s_sectionBeginTicks = new long[MaxSections];
        private static readonly double[] s_sectionMsThisFrame = new double[MaxSections];
        private static readonly long[] s_phaseBeginTicks = new long[PhaseCount];
        private static readonly double[] s_phaseMsThisFrame = new double[PhaseCount];
        private static int s_fixedStepsThisFrame;

        public static void BeginSection(string section)
        {
            int idx = SectionIndex(section);
            if (idx < 0) return;
            s_sectionBeginTicks[idx] = s_clock.GetTimestamp();
        }

        public static void EndSection(string section)
        {
            int idx = SectionIndex(section);
            if (idx < 0) return;
            long elapsedTicks = s_clock.GetTimestamp() - s_sectionBeginTicks[idx];
            s_sectionMsThisFrame[idx] += elapsedTicks * 1000.0 / Stopwatch.Frequency;
        }

        public static void BeginPhase(EnginePhase phase) =>
            s_phaseBeginTicks[(int)phase] = s_clock.GetTimestamp();

        public static void EndPhase(EnginePhase phase)
        {
            long elapsedTicks = s_clock.GetTimestamp() - s_phaseBeginTicks[(int)phase];
            s_phaseMsThisFrame[(int)phase] += elapsedTicks * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Call once from inside the FixedUpdate phase's own subsystem list — see
        /// <see cref="InstallPlayerLoopHooks"/> — once per fixed-timestep catch-up step, so
        /// <see cref="NotifyFrameEnd"/> can record exactly how many ran before this rendered frame
        /// (ticket item 1: "record fixed steps per frame").</summary>
        public static void NotifyFixedStep() => s_fixedStepsThisFrame++;

        // -----------------------------------------------------------------------------------------
        // Ring buffer — one slot per rendered frame. Flat arrays (not double[,]/List<>) so nothing here
        // allocates past class load.
        // -----------------------------------------------------------------------------------------

        private static readonly long[] s_frameTicks = new long[RingCapacity];
        private static readonly double[] s_frameSectionMs = new double[RingCapacity * MaxSections];
        private static readonly double[] s_framePhaseMs = new double[RingCapacity * PhaseCount];
        private static readonly double[] s_frameTotalMs = new double[RingCapacity];
        private static readonly int[] s_frameFixedSteps = new int[RingCapacity];
        private static int s_ringHead;
        private static int s_ringCount;

        /// <summary>Latest-frame pass-through figures the ticket asks to "record" but not window —
        /// <c>Time.unscaledDeltaTime</c> and the <see cref="FrameTimingProbe"/> cpu/gpu samples, both
        /// already per-frame values with no averaging benefit from this ring.</summary>
        public static float LatestUnscaledDeltaTimeMs { get; private set; }
        public static float LatestCpuMainThreadMs { get; private set; }
        public static float LatestCpuRenderThreadMs { get; private set; }
        public static float LatestGpuMs { get; private set; }
        public static bool LatestFrameTimingHasReading { get; private set; }

        /// <summary>Call once per rendered frame, from the same PostLateUpdate hook that closes the
        /// engine-phase measurement (see <see cref="InstallPlayerLoopHooks"/>). Folds this frame's
        /// section/phase accumulation into the ring, then clears the accumulators for the next frame.</summary>
        public static void NotifyFrameEnd(float unscaledDeltaTimeSeconds = 0f, FrameTimingProbe timingProbe = null)
        {
            long now = s_clock.GetTimestamp();
            int slot = s_ringHead;

            int sectionBase = slot * MaxSections;
            for (int i = 0; i < MaxSections; i++)
            {
                s_frameSectionMs[sectionBase + i] = s_sectionMsThisFrame[i];
                s_sectionMsThisFrame[i] = 0.0;
            }

            // The seven top-level phases are non-overlapping by construction (together they ARE the
            // whole player-loop pass for this frame), so their sum is the frame's real wall-clock cost.
            // Sections are a breakdown WITHIN Update/LateUpdate/FixedUpdate/OnGUI (all already inside
            // one of these phases) — summing sections into the same total would double-count exactly
            // the time the owning phase already charged.
            double phaseTotal = 0.0;
            int phaseBase = slot * PhaseCount;
            for (int i = 0; i < PhaseCount; i++)
            {
                double ms = s_phaseMsThisFrame[i];
                s_framePhaseMs[phaseBase + i] = ms;
                phaseTotal += ms;
                s_phaseMsThisFrame[i] = 0.0;
            }

            s_frameTotalMs[slot] = phaseTotal;
            s_frameTicks[slot] = now;
            s_frameFixedSteps[slot] = s_fixedStepsThisFrame;
            s_fixedStepsThisFrame = 0;

            s_ringHead = (s_ringHead + 1) % RingCapacity;
            if (s_ringCount < RingCapacity) s_ringCount++;

            LatestUnscaledDeltaTimeMs = unscaledDeltaTimeSeconds * 1000f;
            if (timingProbe != null && timingProbe.HasReading)
            {
                LatestFrameTimingHasReading = true;
                LatestCpuMainThreadMs = timingProbe.CpuMainThreadFrameTimeMs;
                LatestCpuRenderThreadMs = timingProbe.CpuRenderThreadFrameTimeMs;
                LatestGpuMs = timingProbe.GpuFrameTimeMs;
            }
            else
            {
                LatestFrameTimingHasReading = false;
            }
        }

        private static double TicksToSeconds(long ticks) => ticks / (double)Stopwatch.Frequency;

        private static long LatestTicks()
        {
            int lastSlot = (s_ringHead - 1 + RingCapacity) % RingCapacity;
            return s_frameTicks[lastSlot];
        }

        /// <summary>Average per-frame ms for <paramref name="section"/> over the last
        /// <paramref name="windowSeconds"/> of rendered frames (never since-boot — ticket item 3).
        /// Zero for an unregistered/never-used section name, the same "instrument reports nothing
        /// rather than a false zero from a different quantity" convention <see cref="FrameCost"/>
        /// already uses for an invalid <c>ProfilerRecorder</c>.</summary>
        public static double WindowedSectionMs(string section, double windowSeconds)
        {
            if (!s_sectionIndices.TryGetValue(section, out int idx)) return 0.0;
            return WindowedMs(idx, MaxSections, s_frameSectionMs, windowSeconds);
        }

        public static double WindowedPhaseMs(EnginePhase phase, double windowSeconds) =>
            WindowedMs((int)phase, PhaseCount, s_framePhaseMs, windowSeconds);

        private static double WindowedMs(int index, int stride, double[] ring, double windowSeconds)
        {
            if (s_ringCount == 0) return 0.0;
            long nowTicks = LatestTicks();

            double sum = 0.0;
            int frames = 0;
            for (int n = 0; n < s_ringCount; n++)
            {
                int slot = (s_ringHead - 1 - n + RingCapacity) % RingCapacity;
                if (TicksToSeconds(nowTicks - s_frameTicks[slot]) > windowSeconds) break;
                sum += ring[slot * stride + index];
                frames++;
            }
            return frames > 0 ? sum / frames : 0.0;
        }

        /// <summary>The single worst rendered frame's total (all seven engine phases summed — see
        /// <see cref="NotifyFrameEnd"/> for why phases, not sections, are the non-overlapping total) in
        /// the last <paramref name="windowSeconds"/> — ticket item 3's other windowed figure,
        /// deliberately a max rather than an average, since an average would hide exactly the one bad
        /// frame this exists to surface.</summary>
        public static double WorstFrameMs(double windowSeconds)
        {
            if (s_ringCount == 0) return 0.0;
            long nowTicks = LatestTicks();

            double worst = 0.0;
            for (int n = 0; n < s_ringCount; n++)
            {
                int slot = (s_ringHead - 1 - n + RingCapacity) % RingCapacity;
                if (TicksToSeconds(nowTicks - s_frameTicks[slot]) > windowSeconds) break;
                if (s_frameTotalMs[slot] > worst) worst = s_frameTotalMs[slot];
            }
            return worst;
        }

        /// <summary>Average fixed-timestep steps per rendered frame over the window — the MV-957
        /// <c>FrameCost.s_fixedSpanSumMs</c> estimate's replacement, now an exact count rather than an
        /// elapsed-span guess (see <see cref="NotifyFixedStep"/>).</summary>
        public static double WindowedFixedStepsPerFrame(double windowSeconds)
        {
            if (s_ringCount == 0) return 0.0;
            long nowTicks = LatestTicks();

            double sum = 0.0;
            int frames = 0;
            for (int n = 0; n < s_ringCount; n++)
            {
                int slot = (s_ringHead - 1 - n + RingCapacity) % RingCapacity;
                if (TicksToSeconds(nowTicks - s_frameTicks[slot]) > windowSeconds) break;
                sum += s_frameFixedSteps[slot];
                frames++;
            }
            return frames > 0 ? sum / frames : 0.0;
        }

        /// <summary>The top <paramref name="count"/> sections by windowed ms, highest first — the
        /// overlay's "top 8 sections" (ticket item 7). Writes into caller-supplied buffers rather than
        /// returning a new collection, so the overlay's own 0.25s-cadence formatting call is the only
        /// place this allocates, never a per-frame path.</summary>
        public static int TopSections(double windowSeconds, string[] namesOut, double[] msOut)
        {
            int n = Math.Min(namesOut.Length, msOut.Length);
            int found = 0;
            // MaxSections is small (<=24); an insertion pass into the caller's fixed buffers needs no
            // separate allocation for a sort.
            for (int i = 0; i < s_sectionCount; i++)
            {
                double ms = WindowedMs(i, MaxSections, s_frameSectionMs, windowSeconds);
                int insertAt = found;
                while (insertAt > 0 && msOut[insertAt - 1] < ms) insertAt--;
                if (insertAt >= n) continue;
                int last = Math.Min(found, n - 1);
                for (int j = last; j > insertAt; j--)
                {
                    msOut[j] = msOut[j - 1];
                    namesOut[j] = namesOut[j - 1];
                }
                msOut[insertAt] = ms;
                namesOut[insertAt] = s_sectionNames[i];
                if (found < n) found++;
            }
            return found;
        }

        /// <summary>Ticket item 7 — the "?" overlay's windowed phase breakdown (all seven engine
        /// phases) plus the top 8 sections by windowed ms. Allocates (string formatting, and the two
        /// fixed 8-entry buffers below); called only at the overlay's own 0.25s refresh cadence, same
        /// "never per frame" rule <see cref="FrameCost.FormatLine"/> already follows.</summary>
        public static string FormatOverlayLine()
        {
            var sb = new StringBuilder("[MV-968] phases ");
            sb.Append("init ").Append(WindowedPhaseMs(EnginePhase.Initialization, 1.0).ToString("0.0"))
              .Append(" early ").Append(WindowedPhaseMs(EnginePhase.EarlyUpdate, 1.0).ToString("0.0"))
              .Append(" fixed ").Append(WindowedPhaseMs(EnginePhase.FixedUpdate, 1.0).ToString("0.0"))
              .Append(" preupd ").Append(WindowedPhaseMs(EnginePhase.PreUpdate, 1.0).ToString("0.0"))
              .Append(" upd ").Append(WindowedPhaseMs(EnginePhase.Update, 1.0).ToString("0.0"))
              .Append(" prelate ").Append(WindowedPhaseMs(EnginePhase.PreLateUpdate, 1.0).ToString("0.0"))
              .Append(" postlate ").Append(WindowedPhaseMs(EnginePhase.PostLateUpdate, 1.0).ToString("0.0"))
              .Append("  worst5s ").Append(WorstFrameMs(5.0).ToString("0.0"))
              .Append("  fixedSteps ").Append(WindowedFixedStepsPerFrame(1.0).ToString("0.0")).Append("/frame");

            var names = new string[8];
            var ms = new double[8];
            int found = TopSections(1.0, names, ms);
            sb.Append("  top:");
            for (int i = 0; i < found; i++)
                sb.Append(' ').Append(names[i]).Append(' ').Append(ms[i].ToString("0.0"));

            if (LatestFrameTimingHasReading)
            {
                sb.Append("  cpuMain ").Append(LatestCpuMainThreadMs.ToString("0.0"))
                  .Append(" cpuRender ").Append(LatestCpuRenderThreadMs.ToString("0.0"))
                  .Append(" gpu ").Append(LatestGpuMs.ToString("0.0"));
            }

            return sb.ToString();
        }

        // -----------------------------------------------------------------------------------------
        // Production PlayerLoop wiring — ticket item 1. Not exercised by an EditMode test (PlayMode/a
        // real player loop is the only thing that ever calls SetPlayerLoop meaningfully; see
        // CC_AUTONOMY.md's standing ban on authoring or running a PlayMode test for this project) —
        // Begin/EndPhase and NotifyFrameEnd above are what a test drives directly instead.
        // -----------------------------------------------------------------------------------------

        private static bool s_playerLoopInstalled;

        /// <summary>Idempotent; call once (Bootstrap.Awake). Inserts a Begin/End marker pair around
        /// each of the seven top-level phases by wrapping that phase's own <c>subSystemList</c> — never
        /// touches what's already inside it, so nothing downstream reorders relative to any other
        /// system.</summary>
        public static void InstallPlayerLoopHooks()
        {
            if (s_playerLoopInstalled) return;
            s_playerLoopInstalled = true;

            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            InsertMarkers(ref loop, typeof(UnityEngine.PlayerLoop.Initialization), EnginePhase.Initialization);
            InsertMarkers(ref loop, typeof(UnityEngine.PlayerLoop.EarlyUpdate), EnginePhase.EarlyUpdate);
            InsertFixedUpdateMarkers(ref loop);
            InsertMarkers(ref loop, typeof(UnityEngine.PlayerLoop.PreUpdate), EnginePhase.PreUpdate);
            InsertMarkers(ref loop, typeof(UnityEngine.PlayerLoop.Update), EnginePhase.Update);
            InsertMarkers(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate), EnginePhase.PreLateUpdate);
            InsertPostLateUpdateMarkers(ref loop);
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
        }

        /// <summary>Test-only teardown so a domain reload isn't required between EditMode runs that
        /// exercise this. Production never calls this — see <see cref="InstallPlayerLoopHooks"/>'s own
        /// idempotency guard for why the real game only ever installs once.</summary>
        public static void UninstallPlayerLoopHooksForTest()
        {
            if (!s_playerLoopInstalled) return;
            s_playerLoopInstalled = false;
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(UnityEngine.LowLevel.PlayerLoop.GetDefaultPlayerLoop());
        }

        private static void InsertMarkers(ref UnityEngine.LowLevel.PlayerLoopSystem root, Type phaseType, EnginePhase phase)
        {
            var subSystems = root.subSystemList;
            for (int i = 0; i < subSystems.Length; i++)
            {
                if (subSystems[i].type != phaseType) continue;
                var phaseSystem = subSystems[i];
                phaseSystem.subSystemList = WithMarkers(phaseSystem.subSystemList, phase);
                subSystems[i] = phaseSystem;
                return;
            }
        }

        private static UnityEngine.LowLevel.PlayerLoopSystem[] WithMarkers(
            UnityEngine.LowLevel.PlayerLoopSystem[] inner, EnginePhase phase)
        {
            inner ??= Array.Empty<UnityEngine.LowLevel.PlayerLoopSystem>();
            var withMarkers = new UnityEngine.LowLevel.PlayerLoopSystem[inner.Length + 2];
            withMarkers[0] = new UnityEngine.LowLevel.PlayerLoopSystem
            {
                type = typeof(PerfTelemetry),
                updateDelegate = () => BeginPhase(phase)
            };
            Array.Copy(inner, 0, withMarkers, 1, inner.Length);
            withMarkers[withMarkers.Length - 1] = new UnityEngine.LowLevel.PlayerLoopSystem
            {
                type = typeof(PerfTelemetry),
                updateDelegate = () => EndPhase(phase)
            };
            return withMarkers;
        }

        /// <summary>FixedUpdate's own <c>subSystemList</c> runs 0+ times per rendered frame (Unity's
        /// catch-up loop) — <see cref="NotifyFixedStep"/> is inserted as an extra entry INSIDE that
        /// list, so it fires once per catch-up iteration, on top of the same start/end phase markers
        /// every other phase gets.</summary>
        private static void InsertFixedUpdateMarkers(ref UnityEngine.LowLevel.PlayerLoopSystem root)
        {
            var subSystems = root.subSystemList;
            for (int i = 0; i < subSystems.Length; i++)
            {
                if (subSystems[i].type != typeof(UnityEngine.PlayerLoop.FixedUpdate)) continue;
                var phaseSystem = subSystems[i];
                var inner = phaseSystem.subSystemList ?? Array.Empty<UnityEngine.LowLevel.PlayerLoopSystem>();
                var withMarkers = new UnityEngine.LowLevel.PlayerLoopSystem[inner.Length + 3];
                withMarkers[0] = new UnityEngine.LowLevel.PlayerLoopSystem
                {
                    type = typeof(PerfTelemetry),
                    updateDelegate = () => BeginPhase(EnginePhase.FixedUpdate)
                };
                withMarkers[1] = new UnityEngine.LowLevel.PlayerLoopSystem
                {
                    type = typeof(PerfTelemetry),
                    updateDelegate = NotifyFixedStep
                };
                Array.Copy(inner, 0, withMarkers, 2, inner.Length);
                withMarkers[withMarkers.Length - 1] = new UnityEngine.LowLevel.PlayerLoopSystem
                {
                    type = typeof(PerfTelemetry),
                    updateDelegate = () => EndPhase(EnginePhase.FixedUpdate)
                };
                phaseSystem.subSystemList = withMarkers;
                subSystems[i] = phaseSystem;
                return;
            }
        }

        /// <summary>PostLateUpdate is the last of the seven phases, so its own End marker is also where
        /// a whole rendered frame closes — <see cref="NotifyFrameEnd"/> runs immediately after,
        /// reading <c>Time.unscaledDeltaTime</c> and <see cref="Bootstrap.ActiveTimingProbe"/> directly
        /// since Core has no other per-frame call site left to hand them in from.</summary>
        private static void InsertPostLateUpdateMarkers(ref UnityEngine.LowLevel.PlayerLoopSystem root)
        {
            var subSystems = root.subSystemList;
            for (int i = 0; i < subSystems.Length; i++)
            {
                if (subSystems[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate)) continue;
                var phaseSystem = subSystems[i];
                var inner = phaseSystem.subSystemList ?? Array.Empty<UnityEngine.LowLevel.PlayerLoopSystem>();
                var withMarkers = new UnityEngine.LowLevel.PlayerLoopSystem[inner.Length + 3];
                withMarkers[0] = new UnityEngine.LowLevel.PlayerLoopSystem
                {
                    type = typeof(PerfTelemetry),
                    updateDelegate = () => BeginPhase(EnginePhase.PostLateUpdate)
                };
                Array.Copy(inner, 0, withMarkers, 1, inner.Length);
                withMarkers[withMarkers.Length - 2] = new UnityEngine.LowLevel.PlayerLoopSystem
                {
                    type = typeof(PerfTelemetry),
                    updateDelegate = () => EndPhase(EnginePhase.PostLateUpdate)
                };
                withMarkers[withMarkers.Length - 1] = new UnityEngine.LowLevel.PlayerLoopSystem
                {
                    type = typeof(PerfTelemetry),
                    updateDelegate = () => NotifyFrameEnd(UnityEngine.Time.unscaledDeltaTime, Bootstrap.ActiveTimingProbe)
                };
                phaseSystem.subSystemList = withMarkers;
                subSystems[i] = phaseSystem;
                return;
            }
        }

        /// <summary>Test seam: clears every accumulator and the ring, but deliberately NOT the section
        /// registry (a name's index is stable for the process/test-assembly lifetime, same reasoning
        /// <see cref="FrameCost"/>'s own reflection-cached type list gives for never resetting).</summary>
        public static void ResetRingForTest()
        {
            Array.Clear(s_sectionBeginTicks, 0, s_sectionBeginTicks.Length);
            Array.Clear(s_sectionMsThisFrame, 0, s_sectionMsThisFrame.Length);
            Array.Clear(s_phaseBeginTicks, 0, s_phaseBeginTicks.Length);
            Array.Clear(s_phaseMsThisFrame, 0, s_phaseMsThisFrame.Length);
            s_fixedStepsThisFrame = 0;
            Array.Clear(s_frameTicks, 0, s_frameTicks.Length);
            Array.Clear(s_frameSectionMs, 0, s_frameSectionMs.Length);
            Array.Clear(s_framePhaseMs, 0, s_framePhaseMs.Length);
            Array.Clear(s_frameTotalMs, 0, s_frameTotalMs.Length);
            Array.Clear(s_frameFixedSteps, 0, s_frameFixedSteps.Length);
            s_ringHead = 0;
            s_ringCount = 0;
            LatestUnscaledDeltaTimeMs = 0f;
            LatestCpuMainThreadMs = 0f;
            LatestCpuRenderThreadMs = 0f;
            LatestGpuMs = 0f;
            LatestFrameTimingHasReading = false;
        }
    }
}
