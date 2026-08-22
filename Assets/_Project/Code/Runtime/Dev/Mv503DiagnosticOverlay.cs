using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;

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
        private string _buildStamp;
        private string _cachedPerfLine;
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

        private void ResolvePerfMeterIfNeeded()
        {
            if (_perfMeter != null) return;
            var meter = Bootstrap.ActiveMeter;
            if (meter == null) return;
            _perfMeter = meter;
            _buildStamp = Application.version;
        }

        /// <summary>Pure derivation from an <see cref="FpsMeter"/> — MV-537 AC1: the same meter
        /// Bootstrap ticks every frame, never a second measurement path.</summary>
        public static PerfSnapshot BuildPerfSnapshot(FpsMeter meter, string buildStamp) =>
            meter == null ? default : new PerfSnapshot(meter.Fps, meter.FrameMs, meter.WorstFrameMs, buildStamp ?? "");

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
            if (_instance != null) _instance._visible = !_instance._visible;
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

            if (_perfMeter != null && now - _perfBuiltAt >= PerfRefreshSeconds)
            {
                var perf = BuildPerfSnapshot(_perfMeter, _buildStamp);
                _cachedPerfLine = FormatPerfLine(perf, _perfMeter.SnapshotHistoryOldestFirstMs());
                _perfBuiltAt = now;
            }

            string diagBlock = _lines.Count == 0
                ? "[MV-503] no diagnostic lines captured yet"
                : string.Join("\n", _lines);

            return _cachedPerfLine == null ? diagBlock : _cachedPerfLine + "\n" + diagBlock;
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
