using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// MV-505: makes the MV-503/MV-504 <c>[MV-503]</c> diagnostic lines (<c>PlayerController</c>,
    /// <c>MapRuntime</c>) readable on the device the movement bug actually reproduces on — a phone has
    /// no browser console. A pure log consumer: it only listens to
    /// <see cref="Application.logMessageReceived"/> for lines that already start with the
    /// <c>[MV-503]</c> prefix, so nothing about the diagnostics themselves changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Mv503DiagnosticOverlay : MonoBehaviour
    {
        private const string Prefix = "[MV-503]";
        private const int Capacity = 8;

        private static Mv503DiagnosticOverlay _instance;

        private readonly List<string> _lines = new List<string>(Capacity);
        private bool _visible;
        private GUIStyle _textStyle;

        public IReadOnlyList<string> Lines => _lines;
        public bool Visible => _visible;

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
        public string BuildOverlayText()
        {
            if (!_visible) return null;
            return _lines.Count == 0
                ? "[MV-503] no diagnostic lines captured yet"
                : string.Join("\n", _lines);
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
