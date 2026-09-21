using System;
using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// Entry point for the runnable shell (YT-32 §7). Sets the frame pacing and draws the on-screen
    /// FPS readout. Attach to a single GameObject in <c>Bootstrap.unity</c>.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        [Tooltip("Frame rate the game requests on startup. 60 for the slice. Not applied on WebGL " +
                 "— see Awake.")]
        [SerializeField] private int targetFrameRate = 60;

        [Tooltip("Draw an on-screen FPS readout — smoke-build verification (YT-32). Auto-hidden on " +
                 "the iOS player build regardless of this flag (MV-342); this just lets a dev/QA " +
                 "build turn it off too.")]
        [SerializeField] private bool showFps = true;

        [Tooltip("Also print the frame rate to the log every couple of seconds. This is how the " +
                 "WebGL build's real frame rate can be read from a browser console (YT-62).")]
        [SerializeField] private bool logFps = true;

        private readonly FpsMeter _meter = new FpsMeter(0.5f);
        private readonly FrameTimingProbe _timingProbe = new FrameTimingProbe();
        private float _lastLogAt;
        private GUIStyle _fpsStyle;

        /// <summary>The single FpsMeter instance Bootstrap ticks every frame — exposed so other
        /// diagnostics (MV-503/MV-505's overlay, MV-537's perf figures) read the same measurement
        /// rather than sampling a second, possibly-disagreeing one.</summary>
        public static FpsMeter ActiveMeter { get; private set; }

        /// <summary>The single FrameTimingProbe instance Bootstrap ticks every frame (MV-663) — same
        /// one-tick-site reasoning as <see cref="ActiveMeter"/>, so the overlay never samples a second,
        /// possibly-disagreeing measurement path.</summary>
        public static FrameTimingProbe ActiveTimingProbe { get; private set; }

        /// <summary>MV-766: the world/palette/look diagnostic line, resolved and formatted by
        /// <c>MaxWorlds.Arena.BackyardPath.BuildWorldProbeLine</c>. Wired in as a plain
        /// <see cref="Func{TResult}"/> rather than a direct call, because this (Core) assembly must
        /// not reference the Rendering/Arena types the probe actually reads — the same reason
        /// <see cref="ActiveMeter"/>/<see cref="ActiveTimingProbe"/> flow the other way round, as a
        /// value Gameplay pulls out rather than a type Core reaches into.</summary>
        public static Func<string> WorldProbeLineProvider;

        /// <summary>MV-869: the robot-population / Replicator-state diagnostic line, resolved and
        /// formatted by <c>MaxWorlds.Enemies.PopulationReadout.BuildLine</c>. Same "Core can't see
        /// Gameplay" wiring as <see cref="WorldProbeLineProvider"/>. Rebuilt on <see cref="PopulationLineRefreshSeconds"/>
        /// (see <see cref="OnGUI"/>), never once per <c>OnGUI</c> call — the provider walks the live
        /// robot registry, and this readout must not itself cost a frame.</summary>
        public static Func<string> PopulationLineProvider;

        /// <summary>MV-869: how often <see cref="PopulationLineProvider"/> is re-invoked — a glance-rate
        /// readout, not a per-frame one, matching <c>Mv503DiagnosticOverlay.PerfRefreshSeconds</c>'s own
        /// cached-line cadence.</summary>
        private const float PopulationLineRefreshSeconds = 0.25f;

        private string _cachedPopulationLine;
        private float _populationLineBuiltAt = float.NegativeInfinity;

        /// <summary>MV-876: the script-time attribution line — see <see cref="FrameCost"/>. Same
        /// refresh-window reasoning as <see cref="PopulationLineRefreshSeconds"/>: the accumulator
        /// itself costs ticks only every frame, and only this cadence allocates a string from it.</summary>
        private const float FrameCostRefreshSeconds = 0.25f;

        private string _cachedFrameCostLine;
        private float _frameCostWindowStartAt = float.NegativeInfinity;

        private void Awake()
        {
            // First line in the log, so a browser console immediately answers "which build is this?"
            Debug.Log($"[Build] {Application.version}  ({Application.platform})");

            // YT-216's cold-launch reference point — everything downstream (Home shown, controllable)
            // diffs against this to verify time-to-fun on device from the log alone.
            BootTiming.Mark("bootstrap-awake");

            ActiveMeter = _meter;
            ActiveTimingProbe = _timingProbe;

            // MV-876: a clean accumulation window from this boot, not whatever a previous scene load
            // left behind (the static accumulator survives domain reloads across scene changes).
            FrameCost.Reset();
            _frameCostWindowStartAt = Time.realtimeSinceStartup;

            QualitySettings.vSyncCount = 0;

            // MV-883: a floor-guard, not a fix. Without this, a slow rendered frame makes Unity run
            // the fixed-timestep physics step repeatedly to catch up (measured at 8.5 steps/frame at
            // 5.3 fps) — more physics work, which makes the next frame slower, a positive feedback
            // loop that amplifies the frame-rate collapse rather than just riding it out. Clamping the
            // catch-up window to 0.1s caps that at 5 steps. Below ~10 fps the game now deliberately
            // runs in slow motion (simulated time falls behind wall time) instead of spending ever
            // more of the frame catching up — not a regression, since play is already unplayable
            // there. Does not touch Time.fixedDeltaTime (the 50 Hz tick rate is unchanged — MV-883 AC3).
            Time.maximumDeltaTime = 0.1f;

#if UNITY_WEBGL && !UNITY_EDITOR
            // On WebGL the browser owns the frame loop — Unity drives itself from
            // requestAnimationFrame. Pinning Application.targetFrameRate makes Unity run its own
            // timer instead, which starves rAF (a page-side rAF probe simply times out, which is
            // exactly what QA hit) and gives a WORSE cadence, not a better one. -1 hands pacing back
            // to the browser, which on a 60 Hz display means 60.
            //
            // This is the one place the "targetFrameRate = 60" rule is deliberately not applied, and
            // only on WebGL. Every other platform still pins it.
            Application.targetFrameRate = -1;
#else
            Application.targetFrameRate = targetFrameRate;
#endif
        }

        private void Update()
        {
            _timingProbe.Tick();
            FrameCost.MarkFrameRendered();

            if (!_meter.Tick(Time.realtimeSinceStartup)) return;
            if (!logFps) return;

            float now = Time.realtimeSinceStartup;
            if (now - _lastLogAt < 2f) return;
            _lastLogAt = now;

            // Frame time as well as rate: at a genuinely bad frame rate the millisecond figure is
            // what tells you whether you're looking at a stall or a throttle.
            Debug.Log($"[FPS] {_meter.Fps:0.0} fps  ({_meter.FrameMs:0.0} ms/frame)");
        }

        /// <summary>MV-876: counts fixed-timestep steps against rendered frames — the other hypothesis
        /// a frame rate pinned across every load reduction (render scale, shadow distance, population)
        /// points at, alongside a fixed per-frame cost. See <see cref="FrameCost"/>. MV-885 confirmed the
        /// MV-883 clamp (<see cref="Time.maximumDeltaTime"/> above) is applied correctly — the "fixed
        /// 6.5/frame" reading that looked like it exceeded the clamp was <see cref="FrameCost"/>
        /// undercounting its own denominator, not this call under-clamping.</summary>
        private void FixedUpdate() => FrameCost.NotifyFixedUpdate();

        private void OnDestroy()
        {
            if (ActiveMeter == _meter) ActiveMeter = null;
            if (ActiveTimingProbe == _timingProbe) ActiveTimingProbe = null;
        }

        /// <summary>Real players only ever see the iOS TestFlight/App Store build — the WebGL Pages
        /// link, the cc-verify Windows standalone and the Editor are all dev/QA surfaces, which is
        /// exactly where this smoke-verification readout (YT-32/YT-62) belongs (MV-342). Hiding it on
        /// the iOS player build, and nowhere else, needs no CI change: Unity already stamps
        /// UNITY_IOS/Application.platform per target, so there is nothing to inject mid-build (the
        /// last attempt at that, YT-120, dirtied the git tree and tripped the version guard).</summary>
        public static bool ShouldShowDebugOverlay(bool showFpsFlag, RuntimePlatform platform, bool isEditor)
        {
            if (!showFpsFlag) return false;
            if (isEditor) return true;
            return platform != RuntimePlatform.IPhonePlayer;
        }

        private void OnGUI()
        {
            if (!ShouldShowDebugOverlay(showFps, Application.platform, Application.isEditor)) return;

            _fpsStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.035f)),
                normal = { textColor = Color.white }
            };

            // Two rules here, both learned the hard way:
            //
            //  * Never render a bare "0". "measuring…" until the first window closes, and one
            //    decimal below 10 fps — so a genuinely bad 0.4 fps reads as "0.4 fps", not as a
            //    broken counter. The old readout could not tell those two apart, and we spent a
            //    review cycle not knowing which one we were looking at.
            //
            //  * Always show WHICH BUILD this is. "Is the fix even deployed?" cost us a whole
            //    round trip; a browser can serve a cached build with no sign that it has.
            string fps = !_meter.HasReading ? "measuring…"
                       : _meter.Fps < 10f ? $"{_meter.Fps:0.0} fps"
                       : $"{_meter.Fps:0} fps";

            // MV-881 Requirement A: every row below is drawn by DrawWrappedLine, which advances y by
            // that row's own measured height (including wrapped rows) instead of a fixed offset — see
            // its doc comment. labelWidth also caps rows at the visible screen so wrapping (and the
            // legibility that depends on it) is measured against what a phone can actually show, not a
            // fixed 900px that could run off a narrower device.
            float labelWidth = Mathf.Min(900f, Screen.width - 24f);
            float y = 8f;

            DrawWrappedLine($"{fps}   (target {targetFrameRate})   build {Application.version}", ref y, labelWidth);

            // MV-766: a second line, under the same "smoke-verification, not player UI" condition
            // as the stamp above — what actually resolved, read from the live objects, never
            // recomputed from the world index.
            string probeLine = WorldProbeLineProvider?.Invoke();
            if (string.IsNullOrEmpty(probeLine)) return;

            DrawWrappedLine(probeLine, ref y, labelWidth);

            // MV-869: a third line, under the same condition as the two above — how many actors are
            // alive and whether World 2's Replicator chain is actually live. Rebuilt at most every
            // PopulationLineRefreshSeconds, never once per OnGUI call, so reading it never costs a
            // frame the way an unbounded per-frame walk of the robot registry would.
            float now = Time.realtimeSinceStartup;
            if (PopulationLineProvider != null && now - _populationLineBuiltAt >= PopulationLineRefreshSeconds)
            {
                _cachedPopulationLine = PopulationLineProvider.Invoke();
                _populationLineBuiltAt = now;
            }

            DrawWrappedLine(_cachedPopulationLine, ref y, labelWidth);

            // MV-876: a fourth/fifth line (MV-881 made the readout two lines — see FrameCost.FormatLine),
            // under the same condition as the ones above — the script-time attribution readout that
            // replaces guessing at World 2's pinned 11 fps with measurement. Rebuilt at most every
            // FrameCostRefreshSeconds, never once per OnGUI call, matching the population line's own
            // "must not itself cost a frame" reasoning (FrameCost.FormatLine allocates;
            // FrameCost.Begin/End/MarkFrameRendered/NotifyFixedUpdate never do).
            if (now - _frameCostWindowStartAt >= FrameCostRefreshSeconds)
            {
                _cachedFrameCostLine = FrameCost.FormatLine();
                FrameCost.Reset();
                _frameCostWindowStartAt = now;
            }

            DrawWrappedLine(_cachedFrameCostLine, ref y, labelWidth);
        }

        /// <summary>MV-881 Requirement A: a ticket comment caught the population line and the frame-cost
        /// line physically overlapping — GUI.Label wraps text that doesn't fit <paramref name="width"/>,
        /// but the readout's rows were spaced at a fixed <c>fontSize * 1.2</c> regardless, so a wrapped
        /// row bled into the next label's position and cost a digit (misread as "robot 10.6" instead of
        /// "robot 100.6"). Each line now gets a rect sized to its OWN measured height — including
        /// embedded "\n"s, which is how a two-line <see cref="FrameCost.FormatLine"/> return draws as
        /// two rows from one label — and <paramref name="y"/> only advances by that much, so no line can
        /// ever draw on top of another at any font size or aspect.</summary>
        private void DrawWrappedLine(string text, ref float y, float width)
        {
            if (string.IsNullOrEmpty(text)) return;

            float height = _fpsStyle.CalcHeight(new GUIContent(text), width);
            GUI.Label(new Rect(12f, y, width, height), text, _fpsStyle);
            y += height;
        }
    }
}
