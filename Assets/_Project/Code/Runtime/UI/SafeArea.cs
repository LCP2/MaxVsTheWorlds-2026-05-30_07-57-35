using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Insets a full-screen child RectTransform to <see cref="Screen.safeArea"/> so nothing
    /// parented under it lands beneath an iPhone notch / Dynamic Island / rounded corner /
    /// home indicator (YT-98). The HUD builds its edge-anchored controls (status strip,
    /// joysticks, ability slots, minimap) under a rect carrying this component; full-screen
    /// overlays (biome tint, floating combat text, the big map) stay outside it and keep
    /// covering the whole display.
    ///
    /// Code-driven per <c>docs/CODE_DRIVEN_SCENES.md</c> — no inspector wiring. On desktop /
    /// in CI the safe area equals the full screen, so the anchors resolve to (0,0)-(1,1) and
    /// layout is unchanged; the inset only appears on hardware that reports a notch.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        /// <summary>Test seam: when set, used instead of <see cref="Screen.safeArea"/> so a
        /// PlayMode test can simulate a notch without a device. Null on real builds.</summary>
        public static Rect? SimulatedSafeArea;

        /// <summary>Test seam: screen size to pair with <see cref="SimulatedSafeArea"/>.</summary>
        public static Vector2? SimulatedScreenSize;

        private RectTransform _rect;
        private Rect _lastSafeArea = new Rect(0, 0, 0, 0);
        private Vector2Int _lastScreen = Vector2Int.zero;

        private void Awake() => _rect = (RectTransform)transform;

        private void OnEnable() => Apply();

        private void Update()
        {
            // Cheap guard: only re-anchor when the safe area or resolution actually changes
            // (device rotation, split view). Runs every frame but touches nothing 99% of the time.
            Rect safe = CurrentSafeArea(out Vector2 screen);
            var screenInt = new Vector2Int(Mathf.RoundToInt(screen.x), Mathf.RoundToInt(screen.y));
            if (safe == _lastSafeArea && screenInt == _lastScreen) return;
            Apply();
        }

        private void Apply()
        {
            if (_rect == null) _rect = (RectTransform)transform;

            Rect safe = CurrentSafeArea(out Vector2 screen);
            ComputeAnchors(safe, screen.x, screen.y, out Vector2 min, out Vector2 max);

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;

            _lastSafeArea = safe;
            _lastScreen = new Vector2Int(Mathf.RoundToInt(screen.x), Mathf.RoundToInt(screen.y));
        }

        private static Rect CurrentSafeArea(out Vector2 screen)
        {
            if (SimulatedSafeArea.HasValue && SimulatedScreenSize.HasValue)
            {
                screen = SimulatedScreenSize.Value;
                return SimulatedSafeArea.Value;
            }
            screen = new Vector2(Screen.width, Screen.height);
            return Screen.safeArea;
        }

        /// <summary>
        /// Converts a pixel-space safe area into anchor fractions inside a full-screen parent.
        /// Pure and deterministic so it can be asserted in EditMode. Degenerate inputs
        /// (zero-size screen or safe area) fall back to a full-screen (0,0)-(1,1) rect rather
        /// than collapsing the HUD to a point.
        /// </summary>
        public static void ComputeAnchors(Rect safeArea, float screenW, float screenH,
            out Vector2 anchorMin, out Vector2 anchorMax)
        {
            if (screenW <= 0f || screenH <= 0f || safeArea.width <= 0f || safeArea.height <= 0f)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
                return;
            }

            anchorMin = new Vector2(safeArea.xMin / screenW, safeArea.yMin / screenH);
            anchorMax = new Vector2(safeArea.xMax / screenW, safeArea.yMax / screenH);

            // Clamp against rounding / over-reported insets so anchors never leave [0,1] or invert.
            anchorMin.x = Mathf.Clamp01(anchorMin.x);
            anchorMin.y = Mathf.Clamp01(anchorMin.y);
            anchorMax.x = Mathf.Clamp01(anchorMax.x);
            anchorMax.y = Mathf.Clamp01(anchorMax.y);
            if (anchorMax.x <= anchorMin.x) { anchorMin.x = 0f; anchorMax.x = 1f; }
            if (anchorMax.y <= anchorMin.y) { anchorMin.y = 0f; anchorMax.y = 1f; }
        }
    }
}
