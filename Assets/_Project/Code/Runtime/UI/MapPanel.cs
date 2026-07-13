using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Factories;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Draws <see cref="ArenaMap"/> into a UI rect (YT-72 / YT-73). ONE renderer, used at two
    /// scales: the always-on minimap in the corner and the full-screen map. Both show the same
    /// rooms and the same live markers, so they can never disagree with each other or with the
    /// level — all three come from <see cref="BackyardPathLayout"/>.
    ///
    /// The floor plan is drawn from data rather than captured with a second camera: a render-texture
    /// minimap costs a whole extra render pass every frame, and this arena is a handful of
    /// rectangles. Rooms are built once; only the markers move.
    /// </summary>
    public sealed class MapPanel : MonoBehaviour
    {
        private static readonly Color RoomFill = new Color(0.42f, 0.58f, 0.35f, 0.55f);  // grass
        private static readonly Color RoomEdge = new Color(0.10f, 0.12f, 0.10f, 0.85f);
        private static readonly Color PlayerDot = new Color(0.95f, 0.35f, 0.28f);        // Max's red
        private static readonly Color FactoryDot = new Color(0.95f, 0.55f, 0.15f);       // hazard orange
        private static readonly Color BossDot = new Color(0.85f, 0.12f, 0.12f);
        private static readonly Color GateShut = new Color(0.55f, 0.42f, 0.30f);
        private static readonly Color GateOpen = new Color(0.35f, 0.95f, 0.55f);
        private static readonly Color Dead = new Color(0.35f, 0.35f, 0.35f, 0.6f);

        private BackyardPathLayout _layout;
        private Rect _bounds;
        private RectTransform _plan;      // the aspect-correct area the arena is drawn inside
        private float _markerScale = 1f;

        private RectTransform _player, _factory, _gate, _boss;
        private Image _factoryImg, _gateImg, _bossImg;

        private Transform _playerT;
        private MowerHutch _hutch;
        private SubZoneGate _gateObj;
        private BigBermudaBoss _bossObj;

        /// <summary>Where Max is, 0..1 across the map. Exposed so a test can assert the dot tracks
        /// him without reading pixels.</summary>
        public Vector2 PlayerNormalized { get; private set; }

        /// <summary>The room Max is standing in, or -1. This is what makes it a map rather than a
        /// picture: it can tell you where you are.</summary>
        public int PlayerRoom { get; private set; } = -1;

        /// <summary>
        /// Build the plan inside <paramref name="parent"/>. <paramref name="markerScale"/> keeps
        /// dots legible on the tiny minimap without making them balloon on the full map.
        /// </summary>
        public void Build(RectTransform parent, Vector2 size, float opacity, float markerScale)
        {
            _markerScale = markerScale;

            var path = FindFirstObjectByType<BackyardPath>();
            _layout = path != null ? path.Layout : BackyardPathLayout.Default;
            _bounds = ArenaMap.Bounds(_layout);

            var rt = (RectTransform)transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;

            // Never squash the arena to fill the box — a map with the wrong proportions makes
            // distances read wrong, which is the one thing a map must not do.
            Vector2 plan = ArenaMap.FitPreservingAspect(size, ArenaMap.AspectRatio(_bounds));
            _plan = NewRect("Plan", rt);
            _plan.anchorMin = _plan.anchorMax = _plan.pivot = new Vector2(0.5f, 0.5f);
            _plan.sizeDelta = plan;

            foreach (var room in ArenaMap.Rooms(_layout)) BuildRoom(room, opacity);

            _gate = BuildMarker("Gate", GateShut, 0.055f, out _gateImg);
            _factory = BuildMarker("Factory", FactoryDot, 0.075f, out _factoryImg);
            _boss = BuildMarker("Boss", BossDot, 0.09f, out _bossImg);
            _player = BuildMarker("Max", PlayerDot, 0.07f, out _);

            AcquireTargets();
            Refresh();
        }

        private void BuildRoom(MapRoom room, float opacity)
        {
            Rect n = ArenaMap.NormalizeRect(room.Xz, _bounds);

            var rt = NewRect(room.Name, _plan);
            rt.anchorMin = new Vector2(n.xMin, n.yMin);
            rt.anchorMax = new Vector2(n.xMax, n.yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var edge = rt.gameObject.AddComponent<Image>();
            edge.sprite = HudTextures.RoundedBox(24, 0.12f);
            edge.type = Image.Type.Sliced;
            edge.color = new Color(RoomEdge.r, RoomEdge.g, RoomEdge.b, RoomEdge.a * opacity);
            edge.raycastTarget = false;

            var fill = NewRect("Fill", rt);
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(2f, 2f); fill.offsetMax = new Vector2(-2f, -2f);
            var img = fill.gameObject.AddComponent<Image>();
            img.sprite = HudTextures.RoundedBox(24, 0.12f);
            img.type = Image.Type.Sliced;
            img.color = new Color(RoomFill.r, RoomFill.g, RoomFill.b, RoomFill.a * opacity);
            img.raycastTarget = false;
        }

        private RectTransform BuildMarker(string name, Color color, float sizeFraction, out Image img)
        {
            var rt = NewRect(name, _plan);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float px = Mathf.Max(6f, _plan.sizeDelta.y * sizeFraction * _markerScale);
            rt.sizeDelta = new Vector2(px, px);

            img = rt.gameObject.AddComponent<Image>();
            img.sprite = HudTextures.Disc();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        private void AcquireTargets()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            _playerT = p != null ? p.transform : null;
            _hutch = FindFirstObjectByType<MowerHutch>();
            _gateObj = FindFirstObjectByType<SubZoneGate>();
            _bossObj = FindFirstObjectByType<BigBermudaBoss>();
        }

        private void LateUpdate() => Refresh();

        /// <summary>Re-plot the live markers. Cheap — four transforms.</summary>
        public void Refresh()
        {
            if (_plan == null) return;
            if (_playerT == null) AcquireTargets();

            if (_playerT != null)
            {
                Vector2 xz = Flatten(_playerT.position);
                PlayerNormalized = ArenaMap.Normalize(xz, _bounds);
                PlayerRoom = ArenaMap.RoomAt(xz, ArenaMap.Rooms(_layout));
                Place(_player, PlayerNormalized);
            }

            if (_hutch != null)
            {
                Place(_factory, ArenaMap.Normalize(Flatten(_hutch.transform.position), _bounds));
                // Greyed rather than removed: "the thing you came to kill, already dead" is exactly
                // what the player wants the map to tell them.
                if (_factoryImg != null) _factoryImg.color = _hutch.IsAlive ? FactoryDot : Dead;
            }

            if (_gateObj != null)
            {
                Place(_gate, ArenaMap.Normalize(Flatten(_gateObj.transform.position), _bounds));
                if (_gateImg != null)
                {
                    var col = _gateObj.GetComponent<Collider>();
                    bool open = col == null || !col.enabled;   // the gate opens by disabling itself
                    _gateImg.color = open ? GateOpen : GateShut;
                }
            }

            if (_bossObj != null && _boss != null)
            {
                _boss.gameObject.SetActive(true);
                Place(_boss, ArenaMap.Normalize(Flatten(_bossObj.transform.position), _bounds));
            }
            else if (_boss != null)
            {
                _boss.gameObject.SetActive(false);
            }
        }

        private void Place(RectTransform marker, Vector2 normalized)
        {
            if (marker == null) return;
            marker.anchorMin = marker.anchorMax = normalized;
            marker.anchoredPosition = Vector2.zero;
        }

        private static Vector2 Flatten(Vector3 world) => new Vector2(world.x, world.z);

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }
    }
}
