using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// MV-505: makes the MV-503/MV-504 <c>[MV-503]</c> diagnostic lines (<c>PlayerController</c>,
    /// <c>MapRuntime</c>) readable on the device the movement bug actually reproduces on — a phone has
    /// no browser console. A pure log consumer: it only listens to
    /// <see cref="Application.logMessageReceived"/> for lines that already start with the
    /// <c>[MV-503]</c> prefix, so nothing about the diagnostics themselves changes.
    ///
    /// MV-537 extends it with live performance figures (fps, frame time, worst-frame-in-5s, a short
    /// history, the build stamp) — the same sanctioned "hidden by default, present on TestFlight"
    /// surface, so a photo of it is self-identifying and doesn't need Xcode/Console.app to read.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("overlay")]
    public sealed class Mv503DiagnosticOverlay : MonoBehaviour
    {
        private const string Prefix = "[MV-503]";
        private const int Capacity = 8;

        /// <summary>Perf lines rebuild at most this often while open — a glance-rate readout, not a
        /// per-frame one, so reading it never distorts the measurement it's showing (MV-537).</summary>
        private const float PerfRefreshSeconds = 0.25f;

        private static Mv503DiagnosticOverlay _instance;

        private readonly List<string> _lines = new List<string>(Capacity);
        private bool _visible;
        private GUIStyle _textStyle;

        private FpsMeter _perfMeter;
        private FrameTimingProbe _timingProbe;
        private string _buildStamp;
        private string _cachedPerfLine;
        private string _cachedTimingLine;
        private string _cachedFrameRateLine;
        private string _cachedPopulationLine;
        private string _cachedFrameCostLine;
        private string _cachedPerfTelemetryLine;
        private string _cachedFallsLine;
        private string _cachedSessionRecorderLine;
        private float _perfBuiltAt = float.NegativeInfinity;

        public IReadOnlyList<string> Lines => _lines;
        public bool Visible => _visible;

        /// <summary>Resolved perf figures for one instant — MV-537 AC1. A plain data carrier so a test
        /// can assert the numbers directly instead of parsing the drawn text.</summary>
        public readonly struct PerfSnapshot
        {
            public readonly float Fps;
            public readonly float FrameMs;
            public readonly float WorstFrameMs;
            public readonly string BuildStamp;

            public PerfSnapshot(float fps, float frameMs, float worstFrameMs, string buildStamp)
            {
                Fps = fps;
                FrameMs = frameMs;
                WorstFrameMs = worstFrameMs;
                BuildStamp = buildStamp;
            }
        }

        /// <summary>Wires the perf figures to a specific meter/build-stamp pair. Tests call this
        /// directly with a hand-driven <see cref="FpsMeter"/>; the live game leaves it unset and
        /// <see cref="ResolvePerfMeterIfNeeded"/> pulls <see cref="Bootstrap.ActiveMeter"/> lazily on
        /// first open instead — Bootstrap's own Awake runs before this overlay's
        /// [RuntimeInitializeOnLoadMethod] installs it, so wiring at Awake time the other way round
        /// would read a not-yet-installed overlay.</summary>
        public void SetPerfSource(FpsMeter meter, string buildStamp)
        {
            _perfMeter = meter;
            _buildStamp = buildStamp;
        }

        /// <summary>MV-663 — same idea as <see cref="SetPerfSource"/>: tests wire a hand-driven
        /// <see cref="FrameTimingProbe"/> directly; the live game leaves it unset and
        /// <see cref="ResolveTimingProbeIfNeeded"/> pulls <see cref="Bootstrap.ActiveTimingProbe"/>
        /// lazily on first open.</summary>
        public void SetTimingSource(FrameTimingProbe probe)
        {
            _timingProbe = probe;
        }

        private void ResolvePerfMeterIfNeeded()
        {
            if (_perfMeter != null) return;
            var meter = Bootstrap.ActiveMeter;
            if (meter == null) return;
            _perfMeter = meter;
            _buildStamp = Application.version;
        }

        private void ResolveTimingProbeIfNeeded()
        {
            if (_timingProbe != null) return;
            var probe = Bootstrap.ActiveTimingProbe;
            if (probe == null) return;
            _timingProbe = probe;
        }

        /// <summary>Pure derivation from an <see cref="FpsMeter"/> — MV-537 AC1: the same meter
        /// Bootstrap ticks every frame, never a second measurement path.</summary>
        public static PerfSnapshot BuildPerfSnapshot(FpsMeter meter, string buildStamp) =>
            meter == null ? default : new PerfSnapshot(meter.Fps, meter.FrameMs, meter.WorstFrameMs, buildStamp ?? "");

        /// <summary>Resolved measured CPU/GPU frame cost for one instant — MV-663. A plain data
        /// carrier so a test can assert the numbers directly instead of parsing the drawn text.
        /// <see cref="HasReading"/> false (the struct's default) is what a probe with nothing captured
        /// yet resolves to — a legitimate, displayed state, never a silent zero.</summary>
        public readonly struct TimingSnapshot
        {
            public readonly bool HasReading;
            public readonly float CpuFrameTimeMs;
            public readonly float CpuMainThreadFrameTimeMs;
            public readonly float CpuRenderThreadFrameTimeMs;
            public readonly float GpuFrameTimeMs;

            public TimingSnapshot(bool hasReading, float cpuFrameTimeMs, float cpuMainThreadFrameTimeMs,
                float cpuRenderThreadFrameTimeMs, float gpuFrameTimeMs)
            {
                HasReading = hasReading;
                CpuFrameTimeMs = cpuFrameTimeMs;
                CpuMainThreadFrameTimeMs = cpuMainThreadFrameTimeMs;
                CpuRenderThreadFrameTimeMs = cpuRenderThreadFrameTimeMs;
                GpuFrameTimeMs = gpuFrameTimeMs;
            }
        }

        /// <summary>Pure derivation from a <see cref="FrameTimingProbe"/> — MV-663: the same probe
        /// Bootstrap ticks every frame, never a second measurement path. A null probe or one with
        /// nothing captured both resolve to <c>default</c> (<see cref="TimingSnapshot.HasReading"/>
        /// false), so the overlay always has a legitimate "no reading yet" state to draw.</summary>
        public static TimingSnapshot BuildTimingSnapshot(FrameTimingProbe probe) =>
            probe != null && probe.HasReading
                ? new TimingSnapshot(true, probe.CpuFrameTimeMs, probe.CpuMainThreadFrameTimeMs,
                    probe.CpuRenderThreadFrameTimeMs, probe.GpuFrameTimeMs)
                : default;

        private static string FormatPerfLine(PerfSnapshot perf, float[] historyMs)
        {
            var history = new System.Text.StringBuilder();
            if (historyMs != null)
            {
                for (int i = 0; i < historyMs.Length; i++)
                {
                    if (i > 0) history.Append('/');
                    history.Append(historyMs[i].ToString("0"));
                }
            }

            return $"[MV-537] {perf.Fps:0.0} fps  ({perf.FrameMs:0.0} ms/frame)  worst {perf.WorstFrameMs:0.0} ms/5s" +
                   $"  hist {history} ms  build {perf.BuildStamp}";
        }

        /// <summary>MV-663 — the fps line above is derived, not measured, and can't tell an
        /// idle-capped frame from a GPU-saturated one. "timing n/a" (never a zero) is what
        /// <see cref="TimingSnapshot.HasReading"/> false formats to.</summary>
        private static string FormatTimingLine(TimingSnapshot t) =>
            t.HasReading
                ? $"cpu {t.CpuFrameTimeMs:0.0} ms (main {t.CpuMainThreadFrameTimeMs:0.0} / render {t.CpuRenderThreadFrameTimeMs:0.0})  gpu {t.GpuFrameTimeMs:0.0} ms"
                : "timing n/a";

        /// <summary>Resolved frame-rate/thermal figures for one instant — MV-910. Settles whether a
        /// steady 30fps reading is our own code (an unbalanced <see cref="ModalFrameRateGate"/>
        /// Enter/Exit leaking the idle rate upward) or an external iOS ceiling (this reads 60 while
        /// measured fps stays low). A plain data carrier so a test can assert the numbers directly
        /// instead of parsing the drawn text.</summary>
        public readonly struct FrameRateSnapshot
        {
            public readonly int ResolvedTargetFrameRate;
            public readonly int ModalGateOpenCount;
            public readonly bool ThermalHasReading;
            public readonly string ThermalStateName;
            public readonly bool IsLowPowerModeEnabled;
            public readonly string ThermalTierTag;

            public FrameRateSnapshot(int resolvedTargetFrameRate, int modalGateOpenCount, bool thermalHasReading,
                string thermalStateName, bool isLowPowerModeEnabled, string thermalTierTag)
            {
                ResolvedTargetFrameRate = resolvedTargetFrameRate;
                ModalGateOpenCount = modalGateOpenCount;
                ThermalHasReading = thermalHasReading;
                ThermalStateName = thermalStateName;
                IsLowPowerModeEnabled = isLowPowerModeEnabled;
                ThermalTierTag = thermalTierTag;
            }
        }

        /// <summary>Reads back the RESOLVED <see cref="Application.targetFrameRate"/> — never the
        /// authored constant (MV-910 AC1) — alongside <see cref="ModalFrameRateGate.OpenCount"/>, the
        /// iOS-only thermal reading, and (MV-958) the tier <see cref="ThermalQualityGovernor.Active"/>
        /// has actually applied in response to it — Nominal off-iOS, where no runner ever installs one.</summary>
        public static FrameRateSnapshot BuildFrameRateSnapshot() =>
            new FrameRateSnapshot(
                Application.targetFrameRate,
                ModalFrameRateGate.OpenCount,
                IosDeviceStateProbe.HasReading,
                IosDeviceStateProbe.ThermalStateName,
                IosDeviceStateProbe.IsLowPowerModeEnabled,
                ThermalQualityGovernor.TierTag(ThermalQualityGovernor.Active?.AppliedTier ?? ThermalTier.Nominal));

        /// <summary>MV-910 — "thermal n/a" (never a false reading) is what
        /// <see cref="FrameRateSnapshot.ThermalHasReading"/> false formats to off-iOS, same convention
        /// as <see cref="FormatTimingLine"/> above. MV-958 adds the governor's own tier right after the
        /// state name, e.g. "thermal serious tier S".</summary>
        private static string FormatFrameRateLine(FrameRateSnapshot s) =>
            $"[MV-910] target {s.ResolvedTargetFrameRate} fps  modalGate {s.ModalGateOpenCount}  " +
            (s.ThermalHasReading
                ? $"thermal {s.ThermalStateName} tier {s.ThermalTierTag} lowPower={s.IsLowPowerModeEnabled}"
                : "thermal n/a");

        /// <summary>MV-955: the FALLS section — every <see cref="FallEventLog"/> entry recorded since
        /// the process started, oldest first. Same 0.25s cache cadence as every other line here; a fall
        /// itself is rare, so rebuilding this only costs anything on the same glance-rate schedule the
        /// perf lines already pay.</summary>
        private static string FormatFallsLine()
        {
            if (FallEventLog.Events.Count == 0) return "[MV-955] FALLS: none recorded";

            var sb = new System.Text.StringBuilder("[MV-955] FALLS:");
            foreach (FallEventRecord e in FallEventLog.Events)
                sb.Append("\n  ").Append(FallEventLog.FormatLine(e));
            return sb.ToString();
        }

        /// <summary>MV-970 item 6: "current session file name and rows written" — the overlay's own
        /// readout of the recorder writing everything else on this overlay to disk. Reads
        /// <see cref="Bootstrap.ActiveSessionRecorder"/> rather than owning a reference, same "resolve
        /// lazily off the static" idiom as <see cref="ResolvePerfMeterIfNeeded"/>.</summary>
        private static string FormatSessionRecorderLine()
        {
            PerfSessionRecorder recorder = Bootstrap.ActiveSessionRecorder;
            return recorder == null
                ? "[MV-970] telemetry: not recording"
                : $"[MV-970] telemetry: {recorder.SessionFileName}.csv  rows {recorder.RowsWritten}";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<Mv503DiagnosticOverlay>() != null) return;
            new GameObject("Mv503DiagnosticOverlay").AddComponent<Mv503DiagnosticOverlay>();
        }

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnEnable() => Application.logMessageReceived += HandleLog;
        private void OnDisable() => Application.logMessageReceived -= HandleLog;

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (condition == null || !condition.StartsWith(Prefix, StringComparison.Ordinal)) return;
            if (_lines.Count >= Capacity) _lines.RemoveAt(0);
            _lines.Add(condition);
        }

        /// <summary>Wired to the HUD's existing "?" utility icon
        /// (<c>MaxWorlds.UI.HudController.BuildUtilityIcons</c>) rather than a new input path — Help
        /// has no other behaviour yet, and this is the control already sitting next to the FPS/build
        /// readout in the top-left that a thumb can reach.</summary>
        public static void ToggleVisible()
        {
            if (_instance == null) return;
            _instance._visible = !_instance._visible;
            // MV-970: one of the ticket's own named events -- "overlay open/close".
            Bootstrap.ActiveSessionRecorder?.RecordEvent(
                System.DateTime.UtcNow, _instance._visible ? "overlay_open" : "overlay_close", "");
        }

        /// <summary>The text OnGUI would draw — null while hidden, so no line joining/formatting work
        /// happens at all until a tap makes the overlay visible.</summary>
        public string BuildOverlayText() => BuildOverlayText(Time.realtimeSinceStartup);

        /// <summary>Same as the no-arg overload, with the clock injected — MV-537 tests drive this
        /// with a fixed <paramref name="now"/> instead of the real one, same idiom as
        /// <see cref="FpsMeter.Tick"/>.</summary>
        public string BuildOverlayText(float now)
        {
            if (!_visible) return null;

            ResolvePerfMeterIfNeeded();
            ResolveTimingProbeIfNeeded();

            if (_perfMeter != null && now - _perfBuiltAt >= PerfRefreshSeconds)
            {
                var perf = BuildPerfSnapshot(_perfMeter, _buildStamp);
                _cachedPerfLine = FormatPerfLine(perf, _perfMeter.SnapshotHistoryOldestFirstMs());
                _cachedTimingLine = FormatTimingLine(BuildTimingSnapshot(_timingProbe));
                // MV-910: same cadence, same cache — settles "our own gate idled it" vs "iOS capped it
                // externally" right next to the timing line it was too coarse to answer.
                _cachedFrameRateLine = FormatFrameRateLine(BuildFrameRateSnapshot());
                // MV-869: same cadence, same cache — the population/Replicator line under MV-663's
                // timing line, never rebuilt more often than the perf figures already are.
                _cachedPopulationLine = PopulationReadout.BuildLine();
                // MV-940: the per-system ms buckets (robot/repl/sludge/anch/hud/vfx/dbg/gate/sent, the
                // robot sub-phase breakdown, and the dev-build-only physics/GC ProfilerRecorder line)
                // used to exist only behind Bootstrap's own F1/top-strip "Full" overlay — never visible
                // on this one, the overlay Lee actually reads off TestFlight. Same 0.25s cache as every
                // other line here (MV-933: rebuilt at most 4x/s).
                _cachedFrameCostLine = FrameCost.FormatLine();
                // MV-968: same cadence, same cache — the windowed engine-phase breakdown and top
                // sections PerfTelemetry resolves, replacing the since-boot FrameCost averages this
                // ticket's own investigation found diluted on device (see PerfTelemetry's class doc).
                _cachedPerfTelemetryLine = PerfTelemetry.FormatOverlayLine();
                // MV-955: same cadence, same cache -- the FALLS section, right after the frame-cost
                // line every other debug readout already sits under.
                _cachedFallsLine = FormatFallsLine();
                // MV-970: same cadence, same cache -- the session recorder's own file name/row count,
                // right after the FALLS section.
                _cachedSessionRecorderLine = FormatSessionRecorderLine();
                _perfBuiltAt = now;
            }

            string diagBlock = _lines.Count == 0
                ? "[MV-503] no diagnostic lines captured yet"
                : string.Join("\n", _lines);

            string perfBlock = _cachedPerfLine == null
                ? null
                : _cachedPerfLine + "\n" + _cachedTimingLine + "\n" + _cachedFrameRateLine + "\n" +
                  _cachedPopulationLine + "\n" + _cachedFrameCostLine + "\n" + _cachedPerfTelemetryLine +
                  "\n" + _cachedFallsLine + "\n" + _cachedSessionRecorderLine;
            return perfBlock == null ? diagBlock : perfBlock + "\n" + diagBlock;
        }

        private void OnGUI()
        {
            string text = BuildOverlayText();
            if (text == null) return;

            // Sized off Screen.height, same idiom Bootstrap's FPS readout and DevModeController's
            // panel already use — legible on a 852x393 phone viewport, not just a desktop window.
            _textStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.03f)),
                wordWrap = true,
                normal = { textColor = Color.white }
            };

            float w = Mathf.Min(Screen.width - 24f, 760f);
            float h = Mathf.Min(Screen.height - 24f, Screen.height * 0.55f);
            var rect = new Rect(12f, Screen.height * 0.12f, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.88f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f), text, _textStyle);
        }
    }
}
