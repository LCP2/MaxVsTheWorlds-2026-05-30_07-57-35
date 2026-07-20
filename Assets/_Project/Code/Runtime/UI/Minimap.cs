using UnityEngine;
using UnityEngine.UI;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The always-on minimap (YT-73): where Max is, right now, without opening anything.
    ///
    /// It is the SAME renderer as the full-screen map (<see cref="MapPanel"/>), just smaller and
    /// see-through — which is what the ticket asks for ("build the map data model once, render it at
    /// two scales"). So the two can never disagree with each other, or with the level: all of it
    /// comes from BackyardPathLayout.
    ///
    /// Top-left. The bottom corners are the twin sticks and the top-right is the ability column, so
    /// this is the only quiet corner — and it's the far side of the screen from where the fight
    /// usually is, since Max advances up-field.
    ///
    /// Tapping it opens the full map (YT-123): a player looking at the minimap is already asking the
    /// question the full map answers, so the minimap IS the control — there is no separate MAP
    /// button any more. The full map's own overlay closes on a tap, so tapping again dismisses it.
    /// </summary>
    public sealed class Minimap : MonoBehaviour
    {
        /// <summary>Small enough to ignore, big enough to read at a glance. The arena is long and
        /// narrow, so the panel is too — a minimap that isn't the shape of the place is a lie.</summary>
        public const float PanelWidth = 150f;
        public const float PanelHeight = 280f;

        /// <summary>See-through: the fight has to stay readable underneath it.</summary>
        public const float Opacity = 0.62f;

        /// <summary>Markers are scaled UP relative to the panel — at this size a dot drawn to scale
        /// would be a pixel, and the whole point is to spot yourself instantly.</summary>
        public const float MarkerScale = 1.7f;

        public MapPanel Map { get; private set; }

        public void Build(RectTransform root, MapScreen fullMap, Vector2 topLeftOffset)
        {
            var rt = (RectTransform)transform;
            rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeftOffset;
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            // A dim backing so the plan reads over grass and explosions alike, without being a slab.
            var bg = gameObject.AddComponent<Image>();
            bg.sprite = HudTextures.RoundedBox(24, 0.22f);
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.04f, 0.06f, 0.05f, 0.45f);

            var mapGo = new GameObject("Minimap Plan", typeof(RectTransform));
            Map = mapGo.AddComponent<MapPanel>();
            Map.Build(rt, new Vector2(PanelWidth - 10f, PanelHeight - 10f), Opacity, MarkerScale);

            // A player staring at the minimap is already asking what the full map answers.
            if (fullMap != null)
            {
                var btn = gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(fullMap.Open);
            }
        }
    }
}
