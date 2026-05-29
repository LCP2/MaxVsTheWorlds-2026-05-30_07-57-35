using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// Entry point for the runnable shell (YT-32 §7). Disables VSync and pins the
    /// target frame rate so the slice can hold 60 fps on device. Attach to a single
    /// GameObject in <c>Bootstrap.unity</c>.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        [Tooltip("Frame rate the game requests on startup. 60 for the slice.")]
        [SerializeField] private int targetFrameRate = 60;

        [Tooltip("Draw an on-screen FPS readout — smoke-build verification (YT-32).")]
        [SerializeField] private bool showFps = true;

        private float _smoothedDeltaTime;
        private GUIStyle _fpsStyle;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
        }

        private void Update()
        {
            _smoothedDeltaTime += (Time.unscaledDeltaTime - _smoothedDeltaTime) * 0.1f;
        }

        private void OnGUI()
        {
            if (!showFps)
            {
                return;
            }

            _fpsStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.035f)),
                normal = { textColor = Color.white }
            };

            float fps = _smoothedDeltaTime > 0f ? 1f / _smoothedDeltaTime : 0f;
            GUI.Label(new Rect(12f, 8f, 480f, 60f), $"{fps:0.} fps   (target {targetFrameRate})", _fpsStyle);
        }
    }
}
