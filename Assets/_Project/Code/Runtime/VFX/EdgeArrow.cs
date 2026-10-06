using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1122: a solid red chevron with a white outline at the screen edge, on the line from screen
    /// centre to a world position currently outside the camera's view — the final area's clean-up
    /// marks every counted robot the player can't currently see this way, up to six, nearest first
    /// (<see cref="MaxWorlds.VFX.WorldFinaleGate"/> owns the pool and the selection).
    ///
    /// Built from two tinted copies of the existing <see cref="HudTextures.Arrow"/> triangle (a
    /// slightly larger white one behind, the red one in front) rather than a new outlined texture —
    /// the same "reuse what's already drawn" idiom <see cref="FinaleBanner"/>/<see cref="HudController"/>
    /// already lean on for every other code-driven HUD glyph. Parents onto the live HUD's own scaled
    /// canvas (<see cref="HudController.ActiveCanvas"/>), falling back to a standalone one for a
    /// geometry-independent fixture/test with no HUD built — same fallback <see cref="FinaleBanner.Create"/>
    /// already uses.
    /// </summary>
    public sealed class EdgeArrow : MonoBehaviour
    {
        /// <summary>How far in from the screen edge the arrow sits, as a fraction of the shorter
        /// screen axis — enough that the whole chevron (not just its tip) stays on-screen.</summary>
        private const float EdgeMarginFraction = 0.08f;

        /// <summary>5.3% of screen height (ticket's own number).</summary>
        private const float HeightFraction = 0.053f;

        private static readonly Color FillColor = new Color(1f, 0.231f, 0.188f); // #FF3B30

        private RectTransform _root;

        public bool Visible => gameObject.activeSelf;

        public static EdgeArrow Create(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var root = (RectTransform)go.transform;

            Canvas hudCanvas = HudController.ActiveCanvas;
            Transform parent;
            if (hudCanvas != null)
            {
                parent = hudCanvas.transform;
            }
            else
            {
                // Geometry-independent fallback — no live HUD built (a bare EditMode fixture/test).
                var canvasGo = new GameObject(name + " Canvas", typeof(Canvas), typeof(CanvasScaler));
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 140;
                parent = canvas.transform;
            }

            root.SetParent(parent, worldPositionStays: false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);

            var arrow = go.AddComponent<EdgeArrow>();
            arrow._root = root;

            NewFace(root, "Outline", Color.white, 1.3f);
            NewFace(root, "Fill", FillColor, 1f);

            arrow.gameObject.SetActive(false);
            return arrow;
        }

        private static void NewFace(Transform parent, string name, Color color, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.localScale = new Vector3(scale, scale, 1f);

            var img = go.GetComponent<Image>();
            img.sprite = HudTextures.Arrow(64);
            img.color = color;
            img.raycastTarget = false;
        }

        /// <summary>Resolves this arrow against <paramref name="worldPos"/>/<paramref name="cam"/>:
        /// places it at the screen edge on the line from screen centre to the point, pointing outward,
        /// and returns true. Hides itself and returns false if <paramref name="worldPos"/> actually
        /// falls inside <paramref name="cam"/>'s view right now or <paramref name="cam"/> is null — the
        /// caller (<see cref="MaxWorlds.VFX.WorldFinaleGate"/>) has already screened for off-screen
        /// robots before calling this, but a robot can cross back on-screen between that selection and
        /// this call landing, so this re-checks rather than trust a frame-stale decision.</summary>
        public bool Show(Vector3 worldPos, Camera cam)
        {
            if (cam == null) { Hide(); return false; }

            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            bool behind = vp.z < 0f;
            Vector2 flat = new Vector2(vp.x, vp.y);
            if (behind) flat = new Vector2(1f - flat.x, 1f - flat.y); // mirror -- WorldToViewportPoint inverts XY behind the camera

            bool onScreen = !behind && flat.x >= 0f && flat.x <= 1f && flat.y >= 0f && flat.y <= 1f;
            if (onScreen) { Hide(); return false; }

            Vector2 dir = flat - new Vector2(0.5f, 0.5f);
            if (dir.sqrMagnitude < 1e-8f) dir = Vector2.up;

            float half = 0.5f - EdgeMarginFraction;
            float scaleX = Mathf.Abs(dir.x) > 1e-5f ? half / Mathf.Abs(dir.x) : float.MaxValue;
            float scaleY = Mathf.Abs(dir.y) > 1e-5f ? half / Mathf.Abs(dir.y) : float.MaxValue;
            Vector2 edge = dir * Mathf.Min(scaleX, scaleY);

            Canvas canvas = _root.GetComponentInParent<Canvas>();
            RectTransform canvasRect = canvas != null ? (RectTransform)canvas.transform : null;
            float canvasW = canvasRect != null ? Mathf.Max(1f, canvasRect.rect.width) : Screen.width;
            float canvasH = canvasRect != null ? Mathf.Max(1f, canvasRect.rect.height) : Screen.height;

            if (!gameObject.activeSelf) gameObject.SetActive(true);

            _root.anchoredPosition = new Vector2(edge.x * canvasW, edge.y * canvasH);
            float size = canvasH * HeightFraction;
            _root.sizeDelta = new Vector2(size, size);

            float angleDeg = -Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg; // HudTextures.Arrow points up at 0
            _root.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

            return true;
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
