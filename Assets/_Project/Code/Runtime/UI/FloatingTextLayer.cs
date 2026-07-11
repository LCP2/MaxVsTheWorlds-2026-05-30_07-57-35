using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Renders and pools floating combat text (YT-30) — damage numbers and pickup labels
    /// that rise from a world position and fade. World→screen projection lives here; the
    /// timing/fade curves are the unit-tested <see cref="FloatingTextMotion"/>. Text objects
    /// are pooled so a busy fight doesn't churn GC. Built entirely in code by the HUD.
    /// </summary>
    public sealed class FloatingTextLayer : MonoBehaviour
    {
        private sealed class Item
        {
            public RectTransform Rect;
            public Text Label;
            public Vector3 WorldPos;
            public float Age;
            public float Life;
            public bool Crit;
            public Color Color;
            public Vector2 ScreenBasePx; // recomputed each frame from WorldPos
        }

        private RectTransform _root;
        private Camera _worldCamera;
        private Canvas _canvas;
        private readonly List<Item> _active = new List<Item>(32);
        private readonly Stack<Item> _pool = new Stack<Item>(32);

        public void Init(RectTransform root, Canvas canvas, Camera worldCamera)
        {
            _root = root;
            _canvas = canvas;
            _worldCamera = worldCamera;
        }

        /// <summary>Spawn a rising, fading label at a world position.</summary>
        public void Spawn(Vector3 worldPos, string text, Color color, bool crit, float life, float fontSize)
        {
            Item it = _pool.Count > 0 ? _pool.Pop() : CreateItem();
            it.WorldPos = worldPos;
            it.Age = 0f;
            it.Life = Mathf.Max(0.05f, life);
            it.Crit = crit;
            it.Color = color;
            it.Label.text = text;
            it.Label.color = color;
            it.Label.fontSize = Mathf.RoundToInt(fontSize * (crit ? 2f : 1f)); // spec: crit numbers 2× size
            it.Rect.gameObject.SetActive(true);
            _active.Add(it);
        }

        private Item CreateItem()
        {
            var go = new GameObject("FloatText", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(300f, 60f);
            var label = go.AddComponent<Text>();
            label.font = HudFont.Get();
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
            return new Item { Rect = rect, Label = label };
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var it = _active[i];
                it.Age += dt;
                float p = FloatingTextMotion.Progress(it.Age, it.Life);
                if (p >= 1f)
                {
                    it.Rect.gameObject.SetActive(false);
                    _active.RemoveAt(i);
                    _pool.Push(it);
                    continue;
                }

                if (!TryProject(it.WorldPos, out Vector2 local)) { it.Rect.gameObject.SetActive(false); continue; }
                it.Rect.gameObject.SetActive(true);
                local.y += FloatingTextMotion.RiseAt(p);
                it.Rect.anchoredPosition = local;
                it.Rect.localScale = Vector3.one * FloatingTextMotion.ScaleAt(p, it.Crit);
                var c = it.Color; c.a = FloatingTextMotion.AlphaAt(p);
                it.Label.color = c;
            }
        }

        /// <summary>Project a world point to a local anchored position inside the canvas rect.
        /// Returns false when the point is behind the camera.</summary>
        private bool TryProject(Vector3 worldPos, out Vector2 local)
        {
            local = default;
            Camera cam = _worldCamera != null ? _worldCamera : Camera.main;
            if (cam == null) return false;
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z < 0f) return false; // behind camera
            Camera uiCam = _canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : cam;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, sp, uiCam, out local);
        }
    }
}
