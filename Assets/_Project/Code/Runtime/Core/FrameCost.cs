using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
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
        /// <summary>MV-888: <see cref="Debug"/> added alongside the original six. Bootstrap.OnGUI
        /// drew seven lines of IMGUI with no bucket wrapping it at all, so whatever it cost landed
        /// silently in <c>other</c> — this bucket is how that gets its own line instead.
        ///
        /// MV-940: <see cref="Gate"/> (MapRuntime's own per-frame gate self-heal check, MV-925/937 —
        /// the ticket's own lead that shipped in 0.9.9 without fixing the World 2 walkway fps collapse)
        /// and <see cref="Sentinel"/> (a live sentinel's per-frame Update) are the two systems Lee's
        /// iPhone overlay could not previously attribute at all — everything before this bucket existed
        /// landed silently in <c>other</c>, same reasoning as <see cref="Debug"/> above.</summary>
        public enum Bucket { Robot, Repl, Sludge, Anchor, Hud, Vfx, Debug, Gate, Sentinel }

        private const int BucketCount = 9;

        /// <summary>MV-940: a robot's per-frame cost broken down by WHICH part of its tick spent it —
        /// nested inside the single <see cref="Bucket.Robot"/> charge <see cref="RobotEnemy"/>'s own
        /// Update already wraps its whole tick in, so these are deliberately NOT part of
        /// <see cref="s_bucketMs"/>/<see cref="BucketCount"/> and never enter <see cref="FormatLine"/>'s
        /// sum-vs-frame-time residual: adding them there would double-count the same milliseconds the
        /// outer Robot bucket already charges. Purely an additional breakdown line for reading where
        /// inside "robot" the time actually goes.
        ///
        /// MV-966: dropped the old <c>Behind</c> case — it existed only to separately account the
        /// MV-870 Dormant-far throttle's reduced ticks, which this ticket removed outright in favour of
        /// parking (see <see cref="RobotEnemy.Tick"/>'s own doc comment); there is no reduced-tick case
        /// left to separately account.</summary>
        public enum RobotSubPhase { Dormant, AwakeAiRoute, Separation, Movement }

        private const int RobotSubPhaseCount = 4;
        private static readonly long[] s_subBeginTicks = new long[RobotSubPhaseCount];
        private static readonly double[] s_subMs = new double[RobotSubPhaseCount];

        /// <summary>Nested inside an outer <see cref="Begin(Bucket, bool)"/>/<see cref="End"/> pair for
        /// <see cref="Bucket.Robot"/> — see <see cref="RobotSubPhase"/>'s own doc for why this never
        /// touches <see cref="s_bucketMs"/>.</summary>
        public static void BeginRobotSub(RobotSubPhase phase) =>
            s_subBeginTicks[(int)phase] = s_clock.GetTimestamp();

        public static void EndRobotSub(RobotSubPhase phase)
        {
            long elapsedTicks = s_clock.GetTimestamp() - s_subBeginTicks[(int)phase];
            s_subMs[(int)phase] += elapsedTicks * 1000.0 / Stopwatch.Frequency;
        }

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

        /// <summary>MV-957: the gap between the first and last <see cref="NotifyFixedUpdate"/> call
        /// folded into one rendered frame — every fixed-timestep catch-up step that frame ran, timed on
        /// the same <see cref="IClock"/> seam as everything else here, instead of merely counted the way
        /// <see cref="s_fixedUpdateCount"/> already is. This is a release-build-safe stand-in for the
        /// <c>ProfilerRecorder</c> physics marker (see <see cref="NotAvailable"/>'s "n/a(dev)" below),
        /// approximated from the one centralized <c>FixedUpdate</c> Bootstrap already calls it from,
        /// rather than two new components pinned to Script Execution Order extremes — adding project
        /// settings entries for that risks reordering some other system's physics-dependent callback,
        /// which this measurement-only ticket must not do.</summary>
        private static long s_fixedUpdateFrameFirstTicks;
        private static long s_fixedUpdateFrameLastTicks;
        private static bool s_hasFixedUpdateThisFrame;
        private static double s_fixedSpanSumMs;
        private static int s_fixedSpanSampleCount;

        /// <summary>MV-957: <see cref="GC.CollectionCount"/> read once every <see cref="GcSampleWindowSeconds"/>
        /// — a real release-build-safe cadence of its own, deliberately independent of the 0.25s readout
        /// refresh <see cref="Reset"/> runs on, so it survives across many <see cref="Reset"/> calls
        /// rather than being cleared before it can accumulate anything meaningful.</summary>
        private const double GcSampleWindowSeconds = 5.0;
        private static long s_gcWindowStartTicks;
        private static bool s_hasGcWindowStart;
        private static int s_gcCollectionsAtWindowStart;
        private static int s_gcCollectionsLastWindow;

        /// <summary>MV-957: the game's own <c>MonoBehaviour</c> subclasses that declare their own
        /// <c>Update</c> — reflection-scanned once (same assembly filter as
        /// <see cref="MaxWorlds.Core.SceneInstallers.Discover"/>) and cached for the process lifetime,
        /// since the set can only change on a domain reload and a per-refresh reflection scan across
        /// every loaded assembly would cost far more than the census walk it feeds.</summary>
        private const int TopMonoBehaviourTypesShown = 8;
        private static Type[] s_ownUpdateTypes;

        // ---------------------------------------------------------------------------------------------
        // MV-886: the render bucket. URP raises RenderPipelineManager.beginFrameRendering/
        // endFrameRendering as ordinary C# events in a release player — no Profiler, no Development
        // Build, no deploy.yml change. Timed on the same IClock seam as the six buckets above (never
        // Time.realtimeSinceStartup, which a test cannot fake), so a test can drive it directly without
        // URP ever actually rendering a frame. Per-camera timing is deliberately not added: this
        // project's camera is a single fixed top-down rig (CLAUDE.md — no free-look, no split-screen),
        // so "per-camera, if more than one camera renders" never applies here.
        // ---------------------------------------------------------------------------------------------

        private static bool s_renderEventsSubscribed;
        private static long s_renderFrameBeginTicks;
        private static double s_renderFrameTimeSumMs;
        private static int s_renderFrameSampleCount;

        /// <summary>Idempotent; call once (Bootstrap.Awake). Subscribes for the process lifetime, same
        /// "never per frame" reasoning as <see cref="EnsureRecordersStarted"/>'s ProfilerRecorders —
        /// creating/disposing a subscription every frame would itself cost a frame.</summary>
        public static void SubscribeRenderEvents()
        {
            if (s_renderEventsSubscribed) return;
            s_renderEventsSubscribed = true;
            UnityEngine.Rendering.RenderPipelineManager.beginFrameRendering += OnBeginFrameRendering;
            UnityEngine.Rendering.RenderPipelineManager.endFrameRendering += OnEndFrameRendering;
        }

        /// <summary>Idempotent; call once (Bootstrap.OnDestroy) so a domain reload / scene teardown
        /// never leaves a stale delegate registered against the render pipeline.</summary>
        public static void UnsubscribeRenderEvents()
        {
            if (!s_renderEventsSubscribed) return;
            s_renderEventsSubscribed = false;
            UnityEngine.Rendering.RenderPipelineManager.beginFrameRendering -= OnBeginFrameRendering;
            UnityEngine.Rendering.RenderPipelineManager.endFrameRendering -= OnEndFrameRendering;
        }

        private static void OnBeginFrameRendering(UnityEngine.Rendering.ScriptableRenderContext ctx, UnityEngine.Camera[] cameras) =>
            BeginRenderFrame();

        private static void OnEndFrameRendering(UnityEngine.Rendering.ScriptableRenderContext ctx, UnityEngine.Camera[] cameras) =>
            EndRenderFrame();

        /// <summary>AC1's test seam: production code reaches this only via <see cref="OnBeginFrameRendering"/>
        /// above; a test calls it directly, on the same injected <see cref="IClock"/> the six buckets
        /// use, with no dependency on URP ever actually rendering a frame in EditMode.</summary>
        public static void BeginRenderFrame() => s_renderFrameBeginTicks = s_clock.GetTimestamp();

        /// <summary>See <see cref="BeginRenderFrame"/>. Averages over its own sample count, not
        /// <see cref="s_frameCount"/> — the same self-contained reasoning
        /// <see cref="s_renderedFrameCount"/>'s doc comment gives for <c>fixedPerFrame</c>, so this
        /// figure can never inherit an off-by-one from a different counter that happens to be tracking a
        /// different event.</summary>
        public static void EndRenderFrame()
        {
            long elapsedTicks = s_clock.GetTimestamp() - s_renderFrameBeginTicks;
            s_renderFrameTimeSumMs += elapsedTicks * 1000.0 / Stopwatch.Frequency;
            s_renderFrameSampleCount++;
        }

        // ---------------------------------------------------------------------------------------------
        // MV-886: the one-off renderer census. Computed once when an area finishes building (see
        // MapRuntime.MapStaticBatchRoot.Start) and cached here — never recomputed per frame (AC3).
        // FrameCost never walks a hierarchy itself (this is Core; Arena/Rendering types are not visible
        // here) — the caller does the counting and hands over the already-formatted totals, the same
        // "Core can't see Gameplay" split Bootstrap.WorldProbeLineProvider already uses.
        // ---------------------------------------------------------------------------------------------

        private static string s_areaCensusLine;

        /// <summary>MV-887: <paramref name="enabled"/> is how many of <paramref name="mapGeometry"/> +
        /// <paramref name="replicators"/> + <paramref name="robots"/> + <paramref name="sludgeDressing"/>
        /// currently have <c>Renderer.enabled</c> true — the area-renderer gate's own effect made visible
        /// in the same one screenshot the total already was, rather than a second, separate readout.
        ///
        /// MV-925: <paramref name="gateZoneId"/> is the zone id the gate is CURRENTLY applied to — called
        /// again every time the gate re-applies (not just once at build time, as before this ticket), so
        /// this line can never go stale the way it did when the render gate silently latched on an old
        /// area while this reading kept reporting whatever it read at Start().</summary>
        public static void RecordAreaRendererCensus(int mapGeometry, int replicators, int robots, int sludgeDressing, int opaque, int transparent, int enabled, string gateZoneId)
        {
            int total = mapGeometry + replicators + robots + sludgeDressing;
            s_areaCensusLine =
                $"census gate {gateZoneId} renderers {total} enabled {enabled} (opaque {opaque} transparent {transparent})  " +
                $"map {mapGeometry} repl {replicators} robots {robots} sludge {sludgeDressing}";
        }

        /// <summary>Null until the first area finishes building — <see cref="Bootstrap.RebuildReadout"/>
        /// already skips a null/empty line, so nothing draws before then.</summary>
        public static string AreaCensusLine() => s_areaCensusLine;

        /// <summary>MV-885: every rendered frame counted unconditionally, including the first call
        /// after <see cref="Reset"/>. <see cref="s_frameCount"/> deliberately excludes that first call
        /// (it has no previous timestamp to diff, so it can't measure that frame's own elapsed time —
        /// see <see cref="MarkFrameRendered"/>), but <see cref="s_fixedUpdateCount"/> has no such gap:
        /// it accumulates every <see cref="NotifyFixedUpdate"/> call from the moment of Reset, including
        /// whatever ran before that first mark. Dividing the fixed-update total by <see cref="s_frameCount"/>
        /// therefore attributed N frames' worth of catch-up to N-1 frames — a reading above the real
        /// per-frame cap even when no single frame ever exceeded it (proven by
        /// <c>Mv885FixedPerFrameClampTests</c>, which failed with "fixed 7.5/frame" against a resolved
        /// cap of 5 before this field existed). This counts every rendered frame, matching what
        /// <see cref="s_fixedUpdateCount"/> actually spans.</summary>
        private static int s_renderedFrameCount;

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

            s_renderedFrameCount++;

            // MV-957: the FixedUpdate calls folded in since the PREVIOUS MarkFrameRendered all belong to
            // the frame this call is closing (Unity runs every FixedUpdate for a frame before that
            // frame's own Update) — so this is exactly the right place to close out that frame's span.
            if (s_hasFixedUpdateThisFrame)
            {
                long spanTicks = s_fixedUpdateFrameLastTicks - s_fixedUpdateFrameFirstTicks;
                s_fixedSpanSumMs += spanTicks * 1000.0 / Stopwatch.Frequency;
                s_fixedSpanSampleCount++;
                s_hasFixedUpdateThisFrame = false;
            }

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
        /// <summary>MV-957: also times the fixed-update span itself (see <see cref="s_fixedSpanSumMs"/>'s
        /// own doc for what that span means), on top of the pre-existing per-frame count.</summary>
        public static void NotifyFixedUpdate()
        {
            s_fixedUpdateCount++;

            long now = s_clock.GetTimestamp();
            if (!s_hasFixedUpdateThisFrame)
            {
                s_fixedUpdateFrameFirstTicks = now;
                s_hasFixedUpdateThisFrame = true;
            }
            s_fixedUpdateFrameLastTicks = now;
        }

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

            // MV-885: this one divides by every rendered frame (see s_renderedFrameCount doc comment),
            // NOT by `frames` — frames undercounts by exactly the still-open frame that hasn't been
            // closed by a subsequent MarkFrameRendered call yet, which inflated this specific figure
            // past the real per-frame cap while every other line stayed correct.
            double fixedPerFrame = s_fixedUpdateCount / (double)Math.Max(1, s_renderedFrameCount);

            // MV-886: same self-contained-denominator reasoning as fixedPerFrame just above — divides
            // by its own sample count, never `frames`.
            double renderPerFrame = s_renderFrameSampleCount > 0 ? s_renderFrameTimeSumMs / s_renderFrameSampleCount : 0.0;

            string bucketLine =
                $"ms robot {s_bucketMs[(int)Bucket.Robot] / frames:0.0} (awake {s_robotAwakeLast}) " +
                $"repl {s_bucketMs[(int)Bucket.Repl] / frames:0.0} sludge {s_bucketMs[(int)Bucket.Sludge] / frames:0.0} " +
                $"anch {s_bucketMs[(int)Bucket.Anchor] / frames:0.0} hud {s_bucketMs[(int)Bucket.Hud] / frames:0.0} " +
                $"vfx {s_bucketMs[(int)Bucket.Vfx] / frames:0.0} dbg {s_bucketMs[(int)Bucket.Debug] / frames:0.0} " +
                $"gate {s_bucketMs[(int)Bucket.Gate] / frames:0.0} sent {s_bucketMs[(int)Bucket.Sentinel] / frames:0.0} " +
                $"other {residualPerFrame:0.0} render {renderPerFrame:0.0}  " +
                $"fixed {fixedPerFrame:0.0}/frame";

            return bucketLine + "\n" + FormatProfilerLine() + "\n" + FormatRobotSubPhaseLine(frames) +
                   "\n" + FormatReleaseSafeTimingLine() + "\n" + FormatSystemCensusLine();
        }

        /// <summary>MV-940: the robot-bucket breakdown — see <see cref="RobotSubPhase"/>'s own doc for
        /// why this is a separate, non-summed line rather than more entries in <see cref="bucketLine"/>
        /// above.</summary>
        private static string FormatRobotSubPhaseLine(int frames) =>
            $"robot/ dormant {s_subMs[(int)RobotSubPhase.Dormant] / frames:0.0} " +
            $"awake {s_subMs[(int)RobotSubPhase.AwakeAiRoute] / frames:0.0} " +
            $"sep {s_subMs[(int)RobotSubPhase.Separation] / frames:0.0} " +
            $"move {s_subMs[(int)RobotSubPhase.Movement] / frames:0.0}";

        /// <summary>Clears every bucket, the fixed-update/frame counters and the frame-time accumulator
        /// — the top of the next accumulation window.</summary>
        public static void Reset()
        {
            for (int i = 0; i < BucketCount; i++) s_bucketMs[i] = 0.0;
            s_fixedUpdateCount = 0;
            s_frameCount = 0;
            s_renderedFrameCount = 0;
            s_frameTimeSumMs = 0.0;
            s_hasLastFrameTicks = false;
            s_robotAwakeThisFrame = 0;
            s_robotAwakeLast = 0;
            s_renderFrameTimeSumMs = 0.0;
            s_renderFrameSampleCount = 0;

            for (int i = 0; i < RobotSubPhaseCount; i++) s_subMs[i] = 0.0;

            // MV-957: the fixed-update span accumulator resets with everything else above (it's read
            // per-refresh-window, same as the buckets). The GC 5s sample window and the reflection-cached
            // own-Update type list deliberately do NOT reset here — see their own fields' doc comments.
            s_fixedSpanSumMs = 0.0;
            s_fixedSpanSampleCount = 0;

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

        /// <summary>Idempotent; safe to call every <see cref="Reset"/>. MV-885: <c>phys</c> and
        /// <c>scr</c> used dot-separated names ("FixedUpdate.PhysicsFixedUpdate",
        /// "Update.ScriptRunBehaviourUpdate") that do not exist in this engine build at all — the real
        /// PlayerLoop-hierarchy marker names use a slash ("FixedUpdate/PhysicsFixedUpdate",
        /// "Update/ScriptRunBehaviourUpdate"), confirmed by grepping both the nondevelopment and
        /// development Windows-player <c>UnityPlayer.dll</c> shipped with this project's own
        /// 6000.4.9f1 Editor install: the slash form is present in BOTH, the dot form in neither. That
        /// was a naming bug, not a build-type limitation, and is fixed below. <c>cam</c>
        /// ("Camera.Render"), <c>gc</c> ("GC.Alloc") and <c>batch</c> ("Batches Count") were checked the
        /// same way and are genuinely absent from the nondevelopment player binary while present in the
        /// development one — those three require a Development Build and cannot be fixed by a name
        /// change; see the Jira comment for the full per-counter breakdown (AC3). A marker name Unity
        /// doesn't recognise on this platform/build leaves that recorder's default, invalid state rather
        /// than throwing, and <see cref="FormatProfilerLine"/> reports "n/a" for an invalid recorder
        /// rather than a false zero — the same "don't print a confident reading from an instrument that
        /// hasn't measured anything" rule <see cref="FrameTimingProbe.HasReading"/> documents.</summary>
        private static void EnsureRecordersStarted()
        {
            if (s_recordersStarted) return;
            s_recordersStarted = true;

            try
            {
                s_playerLoopRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PlayerLoop");
                s_cameraRenderRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Camera.Render");
                s_physicsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "FixedUpdate/PhysicsFixedUpdate");
                s_scriptUpdateRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Update/ScriptRunBehaviourUpdate");
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
            $"gc {Kb(s_gcAllocRecorder)} batch {Count(s_batchesRecorder)} " +
            $"setp {Count(s_setPassRecorder)} tri {Count(s_trianglesRecorder)}";

        /// <summary>MV-885 point 2: distinguishable from a real zero reading, plus a one-word reason.
        /// <see cref="ProfilerRecorder"/> exposes no "why" of its own, so the reason is the one thing we
        /// CAN determine at runtime: <see cref="UnityEngine.Debug.isDebugBuild"/> reflects whether this
        /// is a Development Build, where every marker this class starts is known to exist (see
        /// <see cref="EnsureRecordersStarted"/>). A recorder still invalid there means the name itself is
        /// wrong; a recorder invalid outside one means it needs a Development Build to sample at all.</summary>
        private static string NotAvailable() => UnityEngine.Debug.isDebugBuild ? "n/a(name)" : "n/a(dev)";

        private static string Ms(in ProfilerRecorder rec) =>
            rec.Valid ? (rec.LastValue / 1e6).ToString("0.0") : NotAvailable();

        private static string Kb(in ProfilerRecorder rec) =>
            rec.Valid ? (rec.LastValue / 1024.0).ToString("0.0") + "kb" : NotAvailable();

        private static string Count(in ProfilerRecorder rec) =>
            rec.Valid ? rec.LastValue.ToString() : NotAvailable();

        // ---------------------------------------------------------------------------------------------
        // MV-957: release-build-safe instrumentation for what ProfilerRecorder cannot see there — phys/
        // scr/gc/batch above report "n/a(dev)" outside a Development Build, and release builds are what
        // Lee actually plays. Everything below uses only APIs that work in a release player: this same
        // IClock seam for timing, GC.CollectionCount/GC.GetTotalMemory for memory, and FindObjectsByType
        // for the census. The census walk allocates (it returns arrays), so — same rule as FormatLine
        // itself and AreaCensusLine above — it must only run here, at the readout's own refresh cadence,
        // never per frame; MV527AllocationGuardTests' source-shape scan would flag it if it ever moved
        // into an Update/LateUpdate/FixedUpdate body.
        // ---------------------------------------------------------------------------------------------

        /// <summary>The physics-span estimate (see <see cref="s_fixedSpanSumMs"/>'s doc) plus the two
        /// GC figures ProfilerRecorder's dev-only markers can't substitute for in a release build.</summary>
        private static string FormatReleaseSafeTimingLine()
        {
            double fixedSpanPerFrame = s_fixedSpanSampleCount > 0 ? s_fixedSpanSumMs / s_fixedSpanSampleCount : 0.0;

            SampleGcIfDue();

            double heapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            return $"phys~ {fixedSpanPerFrame:0.0}/frame  gc0/5s {s_gcCollectionsLastWindow} heap {heapMb:0.0}mb";
        }

        /// <summary>No-ops until <see cref="GcSampleWindowSeconds"/> has actually elapsed since the last
        /// sample, so the reported delta is always over a real ~5s window rather than the much shorter,
        /// noisier 0.25s readout cadence <see cref="FormatReleaseSafeTimingLine"/> is otherwise called
        /// on.</summary>
        private static void SampleGcIfDue()
        {
            long now = s_clock.GetTimestamp();
            if (!s_hasGcWindowStart)
            {
                s_gcWindowStartTicks = now;
                s_gcCollectionsAtWindowStart = GC.CollectionCount(0);
                s_hasGcWindowStart = true;
                return;
            }

            double elapsedSeconds = (now - s_gcWindowStartTicks) / (double)Stopwatch.Frequency;
            if (elapsedSeconds < GcSampleWindowSeconds) return;

            int current = GC.CollectionCount(0);
            s_gcCollectionsLastWindow = current - s_gcCollectionsAtWindowStart;
            s_gcWindowStartTicks = now;
            s_gcCollectionsAtWindowStart = current;
        }

        /// <summary>Item 1's census: active/enabled counts of the component types MV-957 lists, plus the
        /// top <see cref="TopMonoBehaviourTypesShown"/> of our own MonoBehaviour types (by live instance
        /// count) that declare their own Update. Computed live on every call, never cached — unlike
        /// <see cref="AreaCensusLine"/> above, these are plain <c>UnityEngine</c> types this Core assembly
        /// can already see directly, so there's no "caller does the counting" split to preserve, and the
        /// ticket's own wording is "refreshed at the readout's own cadence", not "computed once".</summary>
        private static string FormatSystemCensusLine()
        {
            int ccCount = 0;
            foreach (var cc in UnityEngine.Object.FindObjectsByType<UnityEngine.CharacterController>(UnityEngine.FindObjectsSortMode.None))
                if (cc.enabled) ccCount++;

            int colliderCount = 0, triggerColliderCount = 0;
            foreach (var col in UnityEngine.Object.FindObjectsByType<UnityEngine.Collider>(UnityEngine.FindObjectsSortMode.None))
            {
                // CharacterController is itself a Collider subclass — counted separately above, not
                // double-counted into the generic collider tally here.
                if (!col.enabled || col is UnityEngine.CharacterController) continue;
                if (col.isTrigger) triggerColliderCount++; else colliderCount++;
            }

            int rbCount = UnityEngine.Object.FindObjectsByType<UnityEngine.Rigidbody>(UnityEngine.FindObjectsSortMode.None).Length;

            int animCount = 0;
            foreach (var anim in UnityEngine.Object.FindObjectsByType<UnityEngine.Animator>(UnityEngine.FindObjectsSortMode.None))
                if (anim.enabled) animCount++;

            int psPlayingCount = 0;
            foreach (var ps in UnityEngine.Object.FindObjectsByType<UnityEngine.ParticleSystem>(UnityEngine.FindObjectsSortMode.None))
                if (ps.isPlaying) psPlayingCount++;

            int lightPoint = 0, lightSpot = 0, lightDir = 0, lightArea = 0;
            foreach (var light in UnityEngine.Object.FindObjectsByType<UnityEngine.Light>(UnityEngine.FindObjectsSortMode.None))
            {
                if (!light.enabled) continue;
                switch (light.type)
                {
                    case UnityEngine.LightType.Point: lightPoint++; break;
                    case UnityEngine.LightType.Spot: lightSpot++; break;
                    case UnityEngine.LightType.Directional: lightDir++; break;
                    default: lightArea++; break;
                }
            }

            int rendererCount = 0;
            foreach (var rend in UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>(UnityEngine.FindObjectsSortMode.None))
                if (rend.enabled) rendererCount++;

            int skinnedCount = 0;
            foreach (var skinned in UnityEngine.Object.FindObjectsByType<UnityEngine.SkinnedMeshRenderer>(UnityEngine.FindObjectsSortMode.None))
                if (skinned.enabled) skinnedCount++;

            int trailLineCount = 0;
            foreach (var trail in UnityEngine.Object.FindObjectsByType<UnityEngine.TrailRenderer>(UnityEngine.FindObjectsSortMode.None))
                if (trail.enabled) trailLineCount++;
            foreach (var line in UnityEngine.Object.FindObjectsByType<UnityEngine.LineRenderer>(UnityEngine.FindObjectsSortMode.None))
                if (line.enabled) trailLineCount++;

            return $"census cc {ccCount} coll {colliderCount}/{triggerColliderCount} rb {rbCount} " +
                   $"anim {animCount} ps {psPlayingCount} " +
                   $"light point {lightPoint} spot {lightSpot} dir {lightDir} area {lightArea} " +
                   $"rend {rendererCount} skinned {skinnedCount} trail/line {trailLineCount}  " +
                   FormatTopMonoBehaviourTypes();
        }

        /// <summary>Classifies every live <c>MonoBehaviour</c> instance against <see cref="OwnUpdateTypes"/>
        /// and tallies by exact runtime type (a subclass with no Update of its own is not counted against
        /// its base type here — it declares no Update, so it isn't in the set being classified against).</summary>
        private static string FormatTopMonoBehaviourTypes()
        {
            Type[] ownTypes = OwnUpdateTypes();
            if (ownTypes.Length == 0) return "top:";

            var counts = new Dictionary<Type, int>(ownTypes.Length);
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>(UnityEngine.FindObjectsSortMode.None))
            {
                Type t = behaviour.GetType();
                if (Array.IndexOf(ownTypes, t) < 0) continue;
                counts.TryGetValue(t, out int existing);
                counts[t] = existing + 1;
            }

            var entries = new List<KeyValuePair<Type, int>>(counts);
            entries.Sort((a, b) => b.Value.CompareTo(a.Value));

            var sb = new StringBuilder("top:");
            int shown = Math.Min(TopMonoBehaviourTypesShown, entries.Count);
            for (int i = 0; i < shown; i++)
                sb.Append(' ').Append(entries[i].Key.Name).Append(' ').Append(entries[i].Value);
            return sb.ToString();
        }

        /// <summary>Reflection-scans the game's own runtime assemblies once for MonoBehaviour subclasses
        /// that declare their own Update — same assembly filter as
        /// <see cref="MaxWorlds.Core.SceneInstallers.Discover"/> (name starts with "MaxWorlds", excludes
        /// "Tests"/"Editor"). Cached forever in <see cref="s_ownUpdateTypes"/>: the type set can only
        /// change on a domain reload, and re-running this scan every refresh would cost far more than the
        /// FindObjectsByType walk it's feeding.</summary>
        private static Type[] OwnUpdateTypes()
        {
            if (s_ownUpdateTypes != null) return s_ownUpdateTypes;

            var found = new List<Type>(64);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name;
                if (!name.StartsWith("MaxWorlds", StringComparison.Ordinal)) continue;
                if (name.Contains("Tests") || name.Contains("Editor")) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }

                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public |
                                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                foreach (var type in types)
                {
                    if (!typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type)) continue;
                    if (type.GetMethod("Update", Flags) == null) continue;
                    found.Add(type);
                }
            }

            s_ownUpdateTypes = found.ToArray();
            return s_ownUpdateTypes;
        }
    }
}
