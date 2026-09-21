using System;
using System.Diagnostics;
using Unity.Profiling;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-876 built this instrument; MV-881 fixes what it actually measured. <see cref="FormatLine"/>
    /// used to take an external "windowTotalMs" and report <c>other = windowTotalMs - sum(buckets)</c>.
    /// That parameter was always the wall-clock length of the display's own 0.25s refresh window, not
    /// any frame's real busy time — so <c>other</c> came out ~237-282 ms on every single reading Lee
    /// took, regardless of load. It was a tautology, not a measurement.
    ///
    /// The fix removes the external parameter entirely (so nothing outside this class can hand it the
    /// wrong quantity again) and tracks each rendered frame's own elapsed time internally, on the same
    /// clock the six buckets already use: <see cref="MarkFrameRendered"/> is still called once per
    /// rendered frame, but now every call after the first closes the PREVIOUS frame and folds its real
    /// elapsed wall time into <see cref="s_frameTimeSumMs"/>. <see cref="FormatLine"/> reports
    /// <c>other = frameTimeSum - bucketSum</c> — both sides now real per-frame time accumulated since
    /// the last <see cref="Reset"/>, directly comparable, never a fixed window length.
    ///
    /// Six call sites each still charge their own wall-clock cost into a <see cref="Bucket"/>; whatever
    /// is left over — <c>other</c> in <see cref="FormatLine"/> — is the single most informative figure
    /// this ticket can produce, because it says the cost is NOT in any system instrumented here.
    ///
    /// Static and shared rather than an injected instance: every wrapped call site (RobotEnemy,
    /// Replicator, SludgeFlowDirector, GroundAnchorVfx, HudController, CombatVfx) is a different,
    /// independently self-installed MonoBehaviour with no shared reference to hand a per-instance
    /// accumulator to — the same reason <see cref="Bootstrap.PopulationLineProvider"/> is static.
    ///
    /// Buckets accumulate across every rendered frame since the last <see cref="Reset"/>, and are only
    /// read and reset at the readout's own refresh cadence — never once per single frame — so the
    /// accumulator itself allocates nothing, and the "ms" figures and <c>other</c> stay directly
    /// comparable against that same window's own real accumulated frame time.
    /// </summary>
    public static class FrameCost
    {
        public enum Bucket { Robot, Repl, Sludge, Anchor, Hud, Vfx }

        private const int BucketCount = 6;

        /// <summary>The raw tick source, isolated behind an interface so a test can inject exact,
        /// hand-picked deltas instead of wall time — same seam <see cref="IFrameTimingSource"/> already
        /// uses for <see cref="FrameTimingProbe"/>. This one clock now backs BOTH the six buckets and
        /// each frame's own elapsed time, so a test can drive the whole residual computation with no
        /// dependency on <c>Unity.Profiling.ProfilerRecorder</c>, which cannot be faked in EditMode.</summary>
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

        private static readonly long[] s_beginTicks = new long[BucketCount];
        private static readonly double[] s_bucketMs = new double[BucketCount];
        private static int s_fixedUpdateCount;
        private static int s_frameCount;

        private static long s_lastFrameTicks;
        private static bool s_hasLastFrameTicks;
        private static double s_frameTimeSumMs;

        private static int s_robotAwakeThisFrame;
        private static int s_robotAwakeLast;

        /// <summary>First statement of a wrapped method. <paramref name="isAwake"/> only matters for
        /// <see cref="Bucket.Robot"/> (MV-881 comment: report the awake count next to the robot bucket,
        /// so the per-awake cost is readable directly instead of inferred against a different line);
        /// every other bucket ignores it.</summary>
        public static void Begin(Bucket bucket, bool isAwake = false)
        {
            s_beginTicks[(int)bucket] = s_clock.GetTimestamp();
            if (bucket == Bucket.Robot && isAwake) s_robotAwakeThisFrame++;
        }

        /// <summary>Last statement of a wrapped method.</summary>
        public static void End(Bucket bucket)
        {
            long elapsedTicks = s_clock.GetTimestamp() - s_beginTicks[(int)bucket];
            s_bucketMs[(int)bucket] += elapsedTicks * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Call once per rendered frame — Bootstrap's own per-frame marker, not one of the six
        /// attributed systems. Every call after the first closes the frame opened by the PREVIOUS call:
        /// the elapsed wall time between the two, on the same clock the buckets use, is that frame's own
        /// real cost, folded into <see cref="s_frameTimeSumMs"/> — never a fixed display-refresh window
        /// length (that was the MV-876 bug).</summary>
        public static void MarkFrameRendered()
        {
            long now = s_clock.GetTimestamp();

            if (s_hasLastFrameTicks)
            {
                s_frameTimeSumMs += (now - s_lastFrameTicks) * 1000.0 / Stopwatch.Frequency;
                s_frameCount++;
                s_robotAwakeLast = s_robotAwakeThisFrame;
                s_robotAwakeThisFrame = 0;
            }

            s_lastFrameTicks = now;
            s_hasLastFrameTicks = true;
        }

        /// <summary>Call from a FixedUpdate. Fixed-timestep catch-up (several physics steps behind one
        /// rendered frame) is the other hypothesis this ticket exists to rule in or out.</summary>
        public static void NotifyFixedUpdate() => s_fixedUpdateCount++;

        /// <summary>Formats the accumulated buckets against this same window's own real accumulated
        /// frame time (see class comment) — no external parameter, so nothing outside this class can
        /// hand it the wrong quantity. AC3 requires every number on the readout to be per rendered
        /// frame, not a window total, so every bucket and the residual are divided by
        /// <see cref="s_frameCount"/> here — the window still accumulates raw sums (so a single-frame
        /// window, exactly what <c>Mv881FrameCostResidualTests</c> drives, divides by 1 and is
        /// unaffected), only the DISPLAYED figure changes from a multi-frame sum to a per-frame average.
        /// At most two lines: the six buckets (plus the awake count and the residual) on the first, the
        /// <c>ProfilerRecorder</c> figures MV-881 adds on the second — those are already per-frame
        /// (<see cref="ProfilerRecorder.LastValue"/> is always the most recently completed frame's own
        /// reading, never a sum), so they need no division. Allocates; call only at the readout's own
        /// refresh cadence, never per frame (see class comment).</summary>
        public static string FormatLine()
        {
            int frames = Math.Max(1, s_frameCount);

            double sum = 0.0;
            for (int i = 0; i < BucketCount; i++) sum += s_bucketMs[i];
            double residualPerFrame = (s_frameTimeSumMs - sum) / frames;
            double fixedPerFrame = s_fixedUpdateCount / (double)frames;

            string bucketLine =
                $"ms robot {s_bucketMs[(int)Bucket.Robot] / frames:0.0} (awake {s_robotAwakeLast}) " +
                $"repl {s_bucketMs[(int)Bucket.Repl] / frames:0.0} sludge {s_bucketMs[(int)Bucket.Sludge] / frames:0.0} " +
                $"anch {s_bucketMs[(int)Bucket.Anchor] / frames:0.0} hud {s_bucketMs[(int)Bucket.Hud] / frames:0.0} " +
                $"vfx {s_bucketMs[(int)Bucket.Vfx] / frames:0.0} other {residualPerFrame:0.0}  fixed {fixedPerFrame:0.0}/frame";

            return bucketLine + "\n" + FormatProfilerLine();
        }

        /// <summary>Clears every bucket, the fixed-update/frame counters and the frame-time accumulator
        /// — the top of the next accumulation window.</summary>
        public static void Reset()
        {
            for (int i = 0; i < BucketCount; i++) s_bucketMs[i] = 0.0;
            s_fixedUpdateCount = 0;
            s_frameCount = 0;
            s_frameTimeSumMs = 0.0;
            s_hasLastFrameTicks = false;
            s_robotAwakeThisFrame = 0;
            s_robotAwakeLast = 0;

            EnsureRecordersStarted();
        }

        // ---------------------------------------------------------------------------------------------
        // MV-881: Unity.Profiling.ProfilerRecorder figures — supplementary to the residual above, never
        // part of its sum. Camera.Render / Batches / SetPass are the priority (see the ticket): if they
        // rise and fall with what is MOVING rather than with what exists, that is the answer. Started
        // once for the process lifetime (creating/disposing a recorder every frame would itself cost a
        // frame, the one thing this instrument must never do) and never disposed — FrameCost is a
        // static, process-lifetime accumulator, the same reasoning the class comment gives for buckets.
        // ---------------------------------------------------------------------------------------------

        private static bool s_recordersStarted;
        private static ProfilerRecorder s_playerLoopRecorder;
        private static ProfilerRecorder s_cameraRenderRecorder;
        private static ProfilerRecorder s_physicsRecorder;
        private static ProfilerRecorder s_scriptUpdateRecorder;
        private static ProfilerRecorder s_gcAllocRecorder;
        private static ProfilerRecorder s_batchesRecorder;
        private static ProfilerRecorder s_setPassRecorder;
        private static ProfilerRecorder s_trianglesRecorder;

        /// <summary>Idempotent; safe to call every <see cref="Reset"/>. A marker name Unity doesn't
        /// recognise on this platform/build (the render-stat counters need a development build — see
        /// the ticket) leaves that recorder's default, invalid state rather than throwing, and
        /// <see cref="FormatProfilerLine"/> already reports "-" for an invalid recorder rather than a
        /// false zero — the same "don't print a confident reading from an instrument that hasn't
        /// measured anything" rule <see cref="FrameTimingProbe.HasReading"/> documents.</summary>
        private static void EnsureRecordersStarted()
        {
            if (s_recordersStarted) return;
            s_recordersStarted = true;

            try
            {
                s_playerLoopRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PlayerLoop");
                s_cameraRenderRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Camera.Render");
                s_physicsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate");
                s_scriptUpdateRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Update.ScriptRunBehaviourUpdate");
                s_gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc");
                s_batchesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                s_setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                s_trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            }
            catch (Exception e)
            {
                // Diagnostic-only instrumentation must never take the game down with it.
                UnityEngine.Debug.LogWarning($"[FrameCost] ProfilerRecorder setup failed: {e.Message}");
            }
        }

        private static string FormatProfilerLine() =>
            $"pl {Ms(s_playerLoopRecorder)} cam {Ms(s_cameraRenderRecorder)} " +
            $"phys {Ms(s_physicsRecorder)} scr {Ms(s_scriptUpdateRecorder)} " +
            $"gc {Kb(s_gcAllocRecorder)}kb batch {Count(s_batchesRecorder)} " +
            $"setp {Count(s_setPassRecorder)} tri {Count(s_trianglesRecorder)}";

        private static string Ms(in ProfilerRecorder rec) =>
            rec.Valid ? (rec.LastValue / 1e6).ToString("0.0") : "-";

        private static string Kb(in ProfilerRecorder rec) =>
            rec.Valid ? (rec.LastValue / 1024.0).ToString("0.0") : "-";

        private static string Count(in ProfilerRecorder rec) =>
            rec.Valid ? rec.LastValue.ToString() : "-";
    }
}
