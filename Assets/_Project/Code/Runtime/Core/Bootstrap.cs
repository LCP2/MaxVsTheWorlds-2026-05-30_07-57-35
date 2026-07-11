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

        [Tooltip("Draw an on-screen FPS readout — smoke-build verification (YT-32).")]
        [SerializeField] private bool showFps = true;

        [Tooltip("Also print the frame rate to the log every couple of seconds. This is how the " +
                 "WebGL build's real frame rate can be read from a browser console (YT-62).")]
        [SerializeField] private bool logFps = true;

        private readonly FpsMeter _meter = new FpsMeter(0.5f);
        private float _lastLogAt;
        private GUIStyle _fpsStyle;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;

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
            if (!_meter.Tick(Time.realtimeSinceStartup)) return;
            if (!logFps) return;

            float now = Time.realtimeSinceStartup;
            if (now - _lastLogAt < 2f) return;
            _lastLogAt = now;

            Debug.Log($"[FPS] {_meter.Fps:0.0} fps");
        }

        private void OnGUI()
        {
            if (!showFps) return;

            _fpsStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.035f)),
                normal = { textColor = Color.white }
            };

            // "measuring…" rather than "0" until the first window closes. A confident "0 fps" on a
            // plainly-animating game is what sent QA hunting for a stall that wasn't there.
            string text = _meter.HasReading
                ? $"{_meter.Fps:0.} fps   (target {targetFrameRate})"
                : $"measuring…   (target {targetFrameRate})";

            GUI.Label(new Rect(12f, 8f, 480f, 60f), text, _fpsStyle);
        }
    }
}
