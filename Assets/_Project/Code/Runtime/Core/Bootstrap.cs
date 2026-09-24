using System;
using System.Text;
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
        /// Gameplay" wiring as <see cref="WorldProbeLineProvider"/>. Rebuilt on
        /// <see cref="ReadoutRefreshSeconds"/>'s cadence (see <see cref="RebuildReadout"/>), never once
        /// per <c>OnGUI</c> call — the provider walks the live robot registry, and this readout must not
        /// itself cost a frame.</summary>
        public static Func<string> PopulationLineProvider;

        /// <summary>MV-933: the readout's own build cadence — at most 4 times/second, cached between
        /// rebuilds. Before this ticket the fps line and <see cref="WorldProbeLineProvider"/> were
        /// rebuilt on EVERY <c>OnGUI</c> invocation (Unity calls it at least twice per rendered frame —
        /// Layout then Repaint — and once per queued input event besides), so a provider that walks the
        /// live scene (<c>BackyardPath.BuildWorldProbeLine</c>'s <c>FindFirstObjectByType</c>/
        /// <c>GameObject.Find</c> calls) paid that cost dozens of times a second regardless of frame
        /// rate — worse the slower the game already ran, the exact runaway MV-933 measured (104.9 ms/
        /// frame in World 2 a10). The population and frame-cost lines already had their own 0.25s
        /// refresh timers before this ticket; one clock now gates every line, extending that same
        /// discipline to the two lines (fps, world-probe) that never had it.</summary>
        private const float ReadoutRefreshSeconds = 0.25f;

        private float _readoutBuiltAt = float.NegativeInfinity;

        /// <summary>The compact ("FPS only") readout — first line alone: fps, target, build stamp.
        /// Rebuilt only on <see cref="ReadoutRefreshSeconds"/>'s cadence, never per <c>OnGUI</c> call.</summary>
        private GUIContent _compactContent;
        private float _compactHeight;

        /// <summary>The full readout — every line, joined with "\n" into ONE <see cref="GUIContent"/> so
        /// <see cref="DrawOverlay"/> makes exactly one <c>GUI.Label</c> call per frame instead of one per
        /// line (MV-933 fix items 2/3: a single retained label, no per-frame IMGUI layout or string
        /// concatenation). <see cref="GUIStyle.CalcHeight"/> — the other per-line cost the old code paid
        /// every frame via <c>DrawWrappedLine</c> — is also only computed at rebuild time.</summary>
        private GUIContent _fullContent;
        private float _fullHeight;

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

            // MV-886: subscribed once for the process lifetime — see FrameCost's own doc comment.
            FrameCost.SubscribeRenderEvents();

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
            FrameCost.UnsubscribeRenderEvents();
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

        /// <summary>MV-888: `Event.current`, not the new Input System — this file's assembly
        /// (MaxWorlds.Core; see its .asmdef) references nothing, and this ticket's diff is scoped to
        /// FrameCost.cs/Bootstrap.cs/one test file, so adding a package reference is off the table.
        /// IMGUI's event queue is populated independently of the Active Input Handling project setting
        /// (this project runs New-only — see CLAUDE.md), so a KeyDown/MouseDown check here works with
        /// no dependency on either input path. F1 is free (grepped every existing binding under
        /// Runtime/: Ctrl+Shift+D, F2-F4, [, ], ;, ' all belong to DevModeController). The tap zone is a
        /// thin strip dead-centre along the top edge: every screen corner already hosts a live HudController
        /// control (utility icons + HOME top-left, weapons button + module badge top-right, joysticks
        /// bottom/left/right), and this ticket's diff can't touch that file to carve out a dedicated
        /// icon — top-centre is the one strip nothing there already listens on. Neither branch draws or
        /// allocates: only a Rect.Contains against Event.current's own struct fields, so it costs
        /// nothing extra whether the overlay is visible or not.
        ///
        /// MV-931: flips <see cref="PerfOverlaySettings.CurrentMode"/> directly, the same value the
        /// Settings panel's "Performance stats" switch reads and writes, so this path and that one
        /// can never disagree — and the flip persists immediately, same as a switch tap. MV-933:
        /// three states now exist, so both the key and the tap zone cycle Off -> FPS only -> Full ->
        /// Off rather than a plain negation.</summary>
        private void PollOverlayToggle()
        {
            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1)
            {
                PerfOverlaySettings.CurrentMode = NextMode(PerfOverlaySettings.CurrentMode);
                return;
            }

            if (e.type == EventType.MouseDown)
            {
                float w = Mathf.Min(200f, Screen.width * 0.3f);
                var hotZone = new Rect(Screen.width * 0.5f - w * 0.5f, 0f, w, 48f);
                if (hotZone.Contains(e.mousePosition)) PerfOverlaySettings.CurrentMode = NextMode(PerfOverlaySettings.CurrentMode);
            }
        }

        private static PerfOverlaySettings.Mode NextMode(PerfOverlaySettings.Mode mode) =>
            (PerfOverlaySettings.Mode)(((int)mode + 1) % 3);

        private void OnGUI()
        {
            PollOverlayToggle();
            PerfOverlaySettings.Mode mode = PerfOverlaySettings.CurrentMode;
            if (mode == PerfOverlaySettings.Mode.Off) return;
            if (!ShouldShowDebugOverlay(showFps, Application.platform, Application.isEditor)) return;

            FrameCost.Begin(FrameCost.Bucket.Debug);
            try
            {
                DrawOverlay(mode);
            }
            finally
            {
                FrameCost.End(FrameCost.Bucket.Debug);
            }
        }

        /// <summary>MV-933: whether the cached readout content is stale and must be rebuilt — a pure,
        /// static predicate (same "extract for testability" idiom as <see cref="ShouldShowDebugOverlay"/>)
        /// so an EditMode test can drive the throttle with a simulated clock, with no MonoBehaviour, no
        /// OnGUI, no Unity object lifecycle involved.</summary>
        public static bool ShouldRebuildReadout(float now, float lastBuiltAt, float refreshSeconds) =>
            now - lastBuiltAt >= refreshSeconds;

        /// <summary>Tracks which mode the cached content was last built for, so flipping the switch
        /// (a rare, user-driven event, not a per-frame one) shows the right content immediately instead
        /// of waiting out the rest of the current <see cref="ReadoutRefreshSeconds"/> window.</summary>
        private PerfOverlaySettings.Mode? _readoutBuiltForMode;

        /// <summary>MV-888's OnGUI body, now wrapped by the caller in FrameCost's Debug bucket so its
        /// own IMGUI cost gets a line instead of landing silently in `other`. MV-933: the expensive part
        /// — building the strings (including <see cref="WorldProbeLineProvider"/>'s scene-walking Find
        /// calls) and measuring their height — now happens only inside <see cref="RebuildReadout"/>, on
        /// <see cref="ReadoutRefreshSeconds"/>'s cadence; every other call here just draws whichever
        /// <see cref="GUIContent"/> is already cached, one <c>GUI.Label</c> call, no layout work.</summary>
        private void DrawOverlay(PerfOverlaySettings.Mode mode)
        {
            float now = Time.realtimeSinceStartup;
            if (_readoutBuiltForMode != mode || ShouldRebuildReadout(now, _readoutBuiltAt, ReadoutRefreshSeconds))
            {
                RebuildReadout(mode);
                _readoutBuiltAt = now;
                _readoutBuiltForMode = mode;
            }

            bool compact = mode == PerfOverlaySettings.Mode.FpsOnly;
            GUIContent content = compact ? _compactContent : _fullContent;
            float height = compact ? _compactHeight : _fullHeight;
            if (content == null) return;

            float labelWidth = Mathf.Min(900f, Screen.width - 24f);
            GUI.Label(new Rect(12f, 8f, labelWidth, height), content, _fpsStyle);
        }

        /// <summary>MV-933: builds the readout content at most every <see cref="ReadoutRefreshSeconds"/>
        /// — never per <c>OnGUI</c> call (fix items 1-3). The compact ("FPS only") first line is always
        /// built, since either mode can draw it; the full line set (fix item 5's "Full" state) — and the
        /// <see cref="WorldProbeLineProvider"/>/<see cref="PopulationLineProvider"/>/<see cref="FrameCost"/>
        /// work behind it — is skipped entirely in FPS-only mode (fix item 4: compact mode must not pay
        /// for lines it never shows).</summary>
        private void RebuildReadout(PerfOverlaySettings.Mode mode)
        {
            _fpsStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.035f)),
                normal = { textColor = Color.white }
            };

            float labelWidth = Mathf.Min(900f, Screen.width - 24f);

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

            string firstLine = $"{fps}   (target {targetFrameRate})   build {Application.version}";

            _compactContent = new GUIContent(firstLine);
            _compactHeight = _fpsStyle.CalcHeight(_compactContent, labelWidth);

            if (mode != PerfOverlaySettings.Mode.Full) return;

            var sb = new StringBuilder(firstLine);

            // MV-766: the world/palette/look diagnostic — what actually resolved, read from the live
            // objects, never recomputed from the world index. This walk (FindFirstObjectByType /
            // GameObject.Find) is the one MV-933 traced as the readout's real cost in a heavy scene —
            // it now runs at most 4 times/second instead of on every OnGUI call.
            string probeLine = WorldProbeLineProvider?.Invoke();
            if (!string.IsNullOrEmpty(probeLine)) sb.Append('\n').Append(probeLine);

            // MV-869: how many actors are alive and whether World 2's Replicator chain is actually
            // live.
            string populationLine = PopulationLineProvider?.Invoke();
            if (!string.IsNullOrEmpty(populationLine)) sb.Append('\n').Append(populationLine);

            // MV-876/MV-881: the script-time attribution readout. FormatLine/Reset are paired so the
            // window FrameCost reports on is exactly the one between this rebuild and the last.
            string frameCostLine = FrameCost.FormatLine();
            FrameCost.Reset();
            if (!string.IsNullOrEmpty(frameCostLine)) sb.Append('\n').Append(frameCostLine);

            // MV-886: the one-off renderer census — already formatted and cached by MapRuntime the
            // moment an area finishes building, so reading it here costs nothing beyond appending the
            // string that's already there. Null until the first area has built.
            string censusLine = FrameCost.AreaCensusLine();
            if (!string.IsNullOrEmpty(censusLine)) sb.Append('\n').Append(censusLine);

            _fullContent = new GUIContent(sb.ToString());
            _fullHeight = _fpsStyle.CalcHeight(_fullContent, labelWidth);
        }
    }
}
