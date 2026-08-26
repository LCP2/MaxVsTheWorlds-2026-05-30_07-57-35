using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.UI
{
    /// <summary>
    /// The full-screen, pinch-zoomable map (MV-563), replacing <see cref="HudController"/>'s old
    /// always-on minimap widget outright — there is no fog of war here, every area is visible from the
    /// first frame it opens. Same self-installing/Open()/Close()/pause-on-open idiom as
    /// <see cref="WeaponsScreen"/>: a dedicated root object, built lazily, toggled by one screen-root
    /// GameObject.
    ///
    /// Geometry is never hand-placed: every area rectangle and the player marker are projected straight
    /// off the live <see cref="MaxWorlds.Arena.MapData"/> through <see cref="MinimapModel"/>'s rotated
    /// projections, rebuilt fresh on every <see cref="Open"/> — so a config edit (an added area, a moved
    /// shed) shows up with no code change, and there is nothing here to keep in sync by hand.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapScreen : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<MapScreen>() != null) return;
            new GameObject("MapScreen").AddComponent<MapScreen>();
        }

        private const float RefW = 1920f, RefH = 1080f;

        // Every colour and dimension for the map itself lives in MapScreenDesign (MV-567) — this block
        // keeps only the screen chrome that design artefact does not cover (close button, legend panel).
        private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.09f, 0.9f);
        private static readonly Color TextColor = Color.white;
        private static readonly Color CloseButtonColor = new Color(0.85f, 0.20f, 0.20f);
        private static readonly Color LegendPanelColor = new Color(0.05f, 0.06f, 0.08f, 0.88f);

        /// <summary>Zoom multiplier growth per world-unit of pinch-distance change is irrelevant here —
        /// zoom tracks the pinch distance RATIO directly (see <see cref="ApplyPinch"/>), not a rate.</summary>
        private const float MinPinchDistance = 8f; // guards a divide-by-~0 on a near-zero-length pinch

        private Canvas _canvas;
        private RectTransform _screenRoot;
        private RectTransform _safeRoot;
        private RectTransform _viewport;
        private RectTransform _content;
        private RectTransform _playerMarker;
        private MapDragSurface _dragSurface;

        private BackyardPath _backyardPath;
        private PlayerController _player;

        private bool _open;
        private float _prevTimeScale = 1f;
        private bool _contentBuilt;

        private MapData _map;
        private Rect _worldBounds;
        private Vector2 _contentSize;   // rotated content footprint, world metres treated 1:1 as local units
        private float _fitScale;
        private float _maxZoomMultiplier;
        private float _zoom = 1f;       // multiplier on top of _fitScale; 1 = the opening fit state
        private Vector2 _pan;           // content anchoredPosition offset from viewport centre, px

        private int _shownCurrentArea = -1;
        private readonly List<Image> _areaImages = new List<Image>();
        private readonly List<int> _areaIndexByImage = new List<int>();
        private readonly List<bool> _areaIsBoss = new List<bool>();
        private readonly List<bool> _areaIsShed = new List<bool>();
        private readonly List<Image> _areaBorderImages = new List<Image>();

        private readonly Dictionary<int, Vector2> _lastPointers = new Dictionary<int, Vector2>();

        /// <summary>Is the map currently up (and the game paused)? Test-only, same idiom as
        /// <see cref="WeaponsScreen.IsOpen"/>.</summary>
        public bool IsOpen => _open;

        private void OnDestroy()
        {
            if (_open) Time.timeScale = _prevTimeScale;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        /// <summary>Opens the full map, pausing the game exactly as <see cref="WeaponsScreen.Open"/>
        /// does. Ignored if already open. Rebuilds the area geometry from the live map every time (cheap
        /// — a few dozen rectangles) rather than caching it once, so the map can never go stale against
        /// a map that changed since the last time it was open.</summary>
        public void Open()
        {
            if (_open) return;
            if (_canvas == null) Build();

            _open = true;
            _prevTimeScale = TimeScaleCapture.ClampForCapture(Time.timeScale);
            Time.timeScale = 0f;

            // Activate before measuring: RebuildContent/ResetView read _viewport.rect to fit the world
            // to screen, and the safe area's own inset (SafeArea.Apply) only ever runs from OnEnable/
            // Update, so the viewport must actually be enabled first for that rect to be real.
            _screenRoot.gameObject.SetActive(true);
            RebuildContent();
            ResetView();
        }

        /// <summary>Re-fits the opening view against an EXPLICIT viewport size instead of the live
        /// (possibly stale) <c>_viewport.rect</c> — for a caller that already knows the exact pixel
        /// target it's about to render at, same reason <c>WeaponsScreen.ApplyBoardScale</c> exists
        /// (MV-444): <see cref="UiScreensDirector"/>'s capture flips this canvas onto its own camera
        /// AFTER <see cref="Open"/> has already run <see cref="RebuildContent"/> against whatever ambient
        /// resolution batchmode happened to have live at that moment, not the shot's real (w, h).</summary>
        public void RefitOpeningView(float viewportWidth, float viewportHeight)
        {
            if (!_contentBuilt) return;
            Vector2 viewportSize = new Vector2(Mathf.Max(1f, viewportWidth), Mathf.Max(1f, viewportHeight));
            _fitScale = MapPanZoomModel.FitScale(viewportSize, _contentSize) * (1f - MapScreenDesign.FitMargin);
            ResetView();
        }

        /// <summary>Closes the map and resumes at whatever speed it paused from.</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            Time.timeScale = _prevTimeScale;
            _screenRoot.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_open) return;

            HandleGesture();
            UpdateCurrentArea();
            UpdatePlayerMarker();
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            EnsureEventSystem();

            _backyardPath = FindFirstObjectByType<BackyardPath>();
            _player = FindFirstObjectByType<PlayerController>();

            var go = new GameObject("Map Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 205; // under THE RIG (210) — the two never open at once, order is just a tiebreak

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            _screenRoot = NewRect("Screen Root", _canvas.transform);
            Stretch(_screenRoot);

            var backdrop = AddImage(_screenRoot, HudTextures.Solid(), MapScreenDesign.Background, "Backdrop");
            Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = false;

            _safeRoot = NewRect("Safe Area", _screenRoot);
            Stretch(_safeRoot);
            _safeRoot.gameObject.AddComponent<SafeArea>();

            _viewport = NewRect("Map Viewport", _safeRoot);
            Stretch(_viewport);

            var dragGo = new GameObject("Drag Surface", typeof(RectTransform), typeof(Image));
            dragGo.transform.SetParent(_viewport, false);
            var dragRect = (RectTransform)dragGo.transform;
            Stretch(dragRect);
            var dragImage = dragGo.GetComponent<Image>();
            dragImage.color = Color.clear;
            dragImage.raycastTarget = true;
            _dragSurface = dragGo.AddComponent<MapDragSurface>();

            _content = NewRect("Map Content", _viewport);
            Anchor(_content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

            BuildCloseButton();
            BuildLegend();

            _screenRoot.gameObject.SetActive(false);
        }

        private void BuildCloseButton()
        {
            const float w = 104f, h = 56f;
            var root = NewRect("Close Button", _safeRoot);
            Anchor(root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            root.sizeDelta = new Vector2(w, h);
            root.anchoredPosition = new Vector2(-24f, -24f);

            var bg = AddImage(root, HudTextures.RoundedBox(32, 0.5f), CloseButtonColor, "BG");
            Stretch(bg.rectTransform); bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Close);

            var label = AddText(root, 24f, TextColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            label.text = "× CLOSE"; // U+00D7: HudFont's LegacyRuntime.ttf has no coverage for a dingbat X
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
        }

        /// <summary>Fixed to the bottom-left of the safe area (AC 8) — a sibling of the pannable
        /// <see cref="_content"/>, not a child of it, so it never pans or zooms with the map. Built once;
        /// unlike the area geometry it names nothing live (a symbol's meaning cannot change map to
        /// map), so there is nothing here that needs rebuilding on every <see cref="Open"/>.</summary>
        private void BuildLegend()
        {
            const float rowH = 22f, iconSize = 16f, padX = 12f, padTop = 10f, padBottom = 8f, width = 216f;

            (string label, Sprite sprite, Color color, Sprite outlineSprite, Color outlineColor)[] rows =
            {
                ("Area", HudTextures.RoundedBox(16, 0.18f), MapScreenDesign.AreaFill, HudTextures.RoundedBoxOutline(16, 0.18f, MapScreenDesign.AreaBorderWidth), MapScreenDesign.AreaBorder),
                ("Shed area", HudTextures.RoundedBox(16, 0.18f), MapScreenDesign.ShedAreaFill, HudTextures.RoundedBoxOutline(16, 0.18f, MapScreenDesign.AreaBorderWidth), MapScreenDesign.ShedAreaBorder),
                ("Boss arena", HudTextures.RoundedBox(16, 0.18f), MapScreenDesign.BossAreaFill, HudTextures.RoundedBoxOutline(16, 0.18f, MapScreenDesign.AreaBorderWidth), MapScreenDesign.BossAreaBorder),
                ("Cover", HudTextures.RoundedBox(12, 0.2f), MapScreenDesign.Cover, null, default),
                ("Static shed", HudTextures.Disc(24), MapScreenDesign.ShedStatic, HudTextures.Ring(24, 2f), MapScreenDesign.ShedOutline),
                ("Mobile shed", HudTextures.Disc(24), MapScreenDesign.ShedMobile, HudTextures.Ring(24, 2f), MapScreenDesign.ShedOutline),
                ("Gate", HudTextures.RoundedBox(8, 0.4f), MapScreenDesign.Gate, null, default),
                ("Boss gate", HudTextures.RoundedBox(8, 0.4f), MapScreenDesign.BossGate, null, default),
                ("Boss", HudTextures.Disc(24), MapScreenDesign.Boss, HudTextures.Ring(24, 2f), MapScreenDesign.BossOutline),
                ("You", HudTextures.Disc(24), MapScreenDesign.Player, null, default),
            };

            float height = padTop + padBottom + rowH * rows.Length;

            var root = NewRect("Legend", _safeRoot);
            Anchor(root, Vector2.zero, Vector2.zero, Vector2.zero);
            root.sizeDelta = new Vector2(width, height);
            root.anchoredPosition = new Vector2(16f, 16f);

            var bg = AddImage(root, HudTextures.RoundedBox(32, 0.15f), LegendPanelColor, "BG");
            Stretch(bg.rectTransform);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;

            for (int i = 0; i < rows.Length; i++)
            {
                // Row 0 at the TOP of the panel, growing downward.
                float rowTop = -(padTop + rowH * i);

                var icon = AddImage(root, rows[i].sprite, rows[i].color, "Icon");
                Anchor(icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
                icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
                icon.rectTransform.anchoredPosition = new Vector2(padX, rowTop - rowH * 0.5f + iconSize * 0.5f);
                icon.raycastTarget = false;

                if (rows[i].outlineSprite != null)
                {
                    var outline = AddImage(icon.rectTransform, rows[i].outlineSprite, rows[i].outlineColor, "Outline");
                    Stretch(outline.rectTransform);
                    outline.raycastTarget = false;
                }

                var text = AddText(root, 13f, TextColor, TextAnchor.MiddleLeft);
                Anchor(text.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
                text.rectTransform.offsetMin = new Vector2(padX + iconSize + 8f, rowTop - rowH);
                text.rectTransform.offsetMax = new Vector2(-padX, rowTop);
                text.text = rows[i].label;
                text.raycastTarget = false;
            }
        }

        // ------------------------------------------------------------------ content (geometry)

        /// <summary>Tears down and rebuilds every area rectangle + the player marker off the live map —
        /// called once per <see cref="Open"/>. A map with no "area&lt;N&gt;" zones (not yet loaded, or a
        /// non-gated test scene) leaves the content empty rather than throwing.</summary>
        private void RebuildContent()
        {
            foreach (Transform child in _content) Destroy(child.gameObject);
            _areaImages.Clear();
            _areaIndexByImage.Clear();
            _areaIsBoss.Clear();
            _areaIsShed.Clear();
            _areaBorderImages.Clear();
            _playerMarker = null;
            _shownCurrentArea = -1;

            _map = _backyardPath != null ? _backyardPath.Map : null;
            int areaCount = MinimapModel.CountAreas(_map);
            _contentBuilt = areaCount > 0;
            if (!_contentBuilt)
            {
                _content.sizeDelta = Vector2.zero;
                return;
            }

            _worldBounds = MinimapModel.AreaBounds(_map);
            // Rotated: content width tracks the world's Z-extent (the long run), height tracks its
            // X-extent (MinimapModel.RotatedNormalizedZoneRect's own axis swap) — metres treated 1:1 as
            // local units; only the aspect ratio between them matters, everything else is scale-to-fit.
            _contentSize = new Vector2(Mathf.Max(1f, _worldBounds.height), Mathf.Max(1f, _worldBounds.width));
            _content.sizeDelta = _contentSize;

            float totalAreaWidth = 0f; // world-X extent per zone — becomes the rotated on-screen HEIGHT
            int zoneCount = 0;
            // Queued rather than drawn inline (MV-567 item 7): labels must land above cover/gates/sheds/
            // bosses in the final draw order, not just above their own room's fill+border.
            var roomLabels = new List<(RectTransform room, Vector2 roomSize, int areaIndex, string name, bool isBoss)>();

            foreach (MapZone zone in _map.zones)
            {
                if (zone == null) continue;
                int areaIndex = AreaAccumulationDirector.AreaIndexOf(zone.id);
                if (areaIndex <= 0 || areaIndex > areaCount) continue;

                Rect rot = MinimapModel.RotatedNormalizedZoneRect(_worldBounds, zone);
                bool isBoss = MinimapModel.IsBossZone(zone);
                bool isShed = !isBoss && MinimapModel.ZoneHasShed(_map, zone);
                (Color fill, Color border) = RoleColors(isBoss, isShed);

                var room = AddImage(_content, HudTextures.RoundedBox(16, 0.12f), fill, $"Area {areaIndex}");
                Anchor(room.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero);
                room.rectTransform.anchoredPosition = new Vector2(rot.x * _contentSize.x, rot.y * _contentSize.y);
                Vector2 roomSize = new Vector2(
                    Mathf.Max(2f, rot.width * _contentSize.x),
                    Mathf.Max(2f, rot.height * _contentSize.y));
                room.rectTransform.sizeDelta = roomSize;
                room.type = Image.Type.Sliced;
                room.raycastTarget = false;
                _areaImages.Add(room);
                _areaIndexByImage.Add(areaIndex);
                _areaIsBoss.Add(isBoss);
                _areaIsShed.Add(isShed);

                var outline = AddImage(room.rectTransform, HudTextures.RoundedBoxOutline(16, 0.12f, MapScreenDesign.AreaBorderWidth), border, "Border");
                Stretch(outline.rectTransform);
                outline.type = Image.Type.Sliced;
                outline.raycastTarget = false;
                _areaBorderImages.Add(outline);

                roomLabels.Add((room.rectTransform, roomSize, areaIndex, zone.name, isBoss));

                totalAreaWidth += zone.width;
                zoneCount++;
            }

            // Cover (MV-567 draw order item 4) — projected through the exact same world->content point
            // every other marker here uses (ContentPoint); an area rectangle is spatial context only,
            // never a parent an entity's marker needs to belong to.
            foreach (MapEntity entity in _map.entities)
                if (entity != null && entity.Kind == EntityKind.Cover) AddCoverMarker(entity);

            // Gates (item 5): every doorway a link cuts, drawn on the exact hole MapGeometry.Doorway
            // solves — not a per-entity loop, since an open (gate-less) doorway is still a way through
            // the wall the map must show.
            if (_map.links != null)
            {
                foreach (MapLink link in _map.links)
                {
                    if (link == null) continue;
                    if (!MapGeometry.Doorway(_map, link, out bool runsAlongX, out float coord, out Span hole)) continue;

                    bool isBossGate = MinimapModel.IsBossGate(_map, link);
                    MinimapModel.DoorwayEndpoints(runsAlongX, coord, hole, out Vector2 worldA, out Vector2 worldB);
                    AddGateMarker(worldA, worldB, isBossGate);
                }
            }

            // Sheds (item 6), then bosses (item 7) — two passes over the same flat list rather than one
            // switch, so a shed can never end up drawn after a boss just because it happened to be
            // authored later in the map file.
            foreach (MapEntity entity in _map.entities)
                if (entity != null && entity.Kind == EntityKind.Factory && entity.Dressing == CoverDressing.Shed)
                    AddShedMarker(entity);
            foreach (MapEntity entity in _map.entities)
                if (entity != null && entity.Kind == EntityKind.Boss) AddBossMarker(entity);

            // Area index + name labels (item 8) — anchored to each room's own bottom-left corner (item
            // 3): the top-left is where cover clusters, and drawn last so a label is never buried under
            // a shed or boss glow that happens to sit near it.
            foreach ((RectTransform room, Vector2 roomSize, int areaIndex, string name, bool isBoss) in roomLabels)
            {
                Color labelColor = isBoss ? MapScreenDesign.BossLabelText : MapScreenDesign.LabelText;
                // Bounded to the room's OWN width (MV-567 fidelity fix, not in the original seven
                // changes): with 30 areas packed edge to edge, AddText's shared Overflow setting let a
                // long area name bleed sideways straight into whichever neighbour sat alongside it —
                // that cross-room bleed, not the (correctly tiny, ~11 px) font height, is what actually
                // made the map illegible against the reference.
                float labelWidth = Mathf.Max(4f, roomSize.x - MapScreenDesign.LabelInset * 2f);

                var indexLabel = AddWorldMetreLabel(room, MapScreenDesign.IndexLabelHeight,
                    new Vector2(labelWidth, MapScreenDesign.IndexLabelHeight * 1.3f),
                    new Vector2(MapScreenDesign.LabelInset, MapScreenDesign.LabelInset),
                    labelColor, TextAnchor.LowerLeft);
                indexLabel.text = areaIndex.ToString();
                indexLabel.fontStyle = FontStyle.Bold;
                indexLabel.raycastTarget = false;

                if (!string.IsNullOrEmpty(name))
                {
                    var nameLabel = AddWorldMetreLabel(room, MapScreenDesign.NameLabelHeight,
                        new Vector2(labelWidth, MapScreenDesign.NameLabelHeight * 1.3f),
                        new Vector2(MapScreenDesign.LabelInset, MapScreenDesign.LabelInset + MapScreenDesign.IndexLabelHeight * 1.2f),
                        labelColor, TextAnchor.LowerLeft);
                    nameLabel.horizontalOverflow = HorizontalWrapMode.Wrap;   // wraps within the room, never bleeds sideways
                    nameLabel.text = name;
                    nameLabel.raycastTarget = false;
                }
            }

            float typicalAreaWorldHeight = zoneCount > 0 ? totalAreaWidth / zoneCount : 1f;
            Vector2 viewportSize = ViewportSize();
            _fitScale = MapPanZoomModel.FitScale(viewportSize, _contentSize) * (1f - MapScreenDesign.FitMargin);
            _maxZoomMultiplier = MapPanZoomModel.MaxZoomMultiplier(viewportSize.y, _fitScale, typicalAreaWorldHeight);

            // Player dot (item 9) — legend (item 10) is screen-space chrome built once in Build(), never
            // part of this pannable content.
            float playerDiameter = MapScreenDesign.PlayerRadius * 2f;
            _playerMarker = NewRect("Player Marker", _content);
            Anchor(_playerMarker, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            _playerMarker.sizeDelta = new Vector2(playerDiameter, playerDiameter);

            var glow = AddImage(_playerMarker, HudTextures.Disc(32),
                new Color(MapScreenDesign.Player.r, MapScreenDesign.Player.g, MapScreenDesign.Player.b, 0.45f), "Glow");
            Stretch(glow.rectTransform);
            glow.raycastTarget = false;

            var dot = AddImage(_playerMarker, HudTextures.Disc(24), MapScreenDesign.Player, "Dot");
            Center(dot.rectTransform, MapScreenDesign.PlayerRadius);
            dot.raycastTarget = false;
        }

        /// <summary>Fill+border for an area's role (MV-566 AC 2) — boss beats shed beats ordinary, so a
        /// boss arena that happens to also hold a shed still reads red, not amber.</summary>
        private static (Color fill, Color border) RoleColors(bool isBoss, bool isShed)
        {
            if (isBoss) return (MapScreenDesign.BossAreaFill, MapScreenDesign.BossAreaBorder);
            if (isShed) return (MapScreenDesign.ShedAreaFill, MapScreenDesign.ShedAreaBorder);
            return (MapScreenDesign.AreaFill, MapScreenDesign.AreaBorder);
        }

        /// <summary>A world XZ point projected onto <see cref="_content"/> exactly the way the player
        /// marker and every area rectangle already are (<see cref="MinimapModel.RotatedNormalizedPosition"/>
        /// times <see cref="_contentSize"/>) — the one projection every marker in this file shares, so a
        /// cover blob, a gate and the player dot can never disagree about where the same ground point
        /// sits on screen.</summary>
        private Vector2 ContentPoint(float worldX, float worldZ)
        {
            Vector2 n = MinimapModel.RotatedNormalizedPosition(_worldBounds, worldX, worldZ);
            return new Vector2(n.x * _contentSize.x, n.y * _contentSize.y);
        }

        private void AddCoverMarker(MapEntity entity)
        {
            var marker = AddImage(_content, HudTextures.RoundedBox(12, 0.2f), MapScreenDesign.Cover, "Cover");
            Anchor(marker.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            marker.rectTransform.anchoredPosition = ContentPoint(entity.x, entity.z);
            // Rotated the same way a room's own footprint is: world depth (Z) becomes on-screen width,
            // world width (X) becomes on-screen height.
            marker.rectTransform.sizeDelta = new Vector2(Mathf.Max(1.2f, entity.depth), Mathf.Max(1.2f, entity.width));
            marker.type = Image.Type.Sliced;
            marker.raycastTarget = false;
        }

        /// <summary>A mobile shed (MV-548/MV-562) reads distinctly from a static one (MV-567 item 4) —
        /// same size and outline, different fill, resolved straight off <see cref="MapEntity.mobile"/>
        /// rather than re-reading the world config the map already has this flat off of.</summary>
        private void AddShedMarker(MapEntity entity)
        {
            Color fill = entity.mobile ? MapScreenDesign.ShedMobile : MapScreenDesign.ShedStatic;
            var marker = AddImage(_content, HudTextures.Disc(24), fill, "Shed");
            Anchor(marker.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            marker.rectTransform.anchoredPosition = ContentPoint(entity.x, entity.z);
            marker.rectTransform.sizeDelta = new Vector2(MapScreenDesign.ShedSize, MapScreenDesign.ShedSize);
            marker.raycastTarget = false;

            var outline = AddImage(marker.rectTransform, HudTextures.Ring(24, 2f), MapScreenDesign.ShedOutline, "Outline");
            Stretch(outline.rectTransform);
            outline.raycastTarget = false;
        }

        /// <summary>A boss "dominates its arena" (AC 2/6) — a soft halo behind a bright disc, both sized
        /// well past the boss's own authored footprint (which can be as small as 3.5 m in a 30-46 m
        /// room) so it still reads as the room's centrepiece at full zoom-out.</summary>
        private void AddBossMarker(MapEntity entity)
        {
            Vector2 pos = ContentPoint(entity.x, entity.z);
            float size = MapScreenDesign.BossRadius * 2f;

            var glow = AddImage(_content, HudTextures.Glow(32),
                new Color(MapScreenDesign.Boss.r, MapScreenDesign.Boss.g, MapScreenDesign.Boss.b, 0.4f), "Boss Glow");
            Anchor(glow.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            glow.rectTransform.anchoredPosition = pos;
            glow.rectTransform.sizeDelta = new Vector2(size * 1.8f, size * 1.8f);
            glow.raycastTarget = false;

            var marker = AddImage(_content, HudTextures.Disc(24), MapScreenDesign.Boss, "Boss");
            Anchor(marker.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            marker.rectTransform.anchoredPosition = pos;
            marker.rectTransform.sizeDelta = new Vector2(size, size);
            marker.raycastTarget = false;

            var outline = AddImage(marker.rectTransform, HudTextures.Ring(24, 2f), MapScreenDesign.BossOutline, "Outline");
            Stretch(outline.rectTransform);
            outline.raycastTarget = false;
        }

        /// <summary>A gate as a short bar spanning its doorway's actual hole (MV-566 item 4: "using
        /// MapGeometry.Doorway's hole") — both endpoints projected exactly like every other marker here,
        /// so the bar sits on the real wall line and at the real width regardless of which world axis
        /// the doorway runs along.</summary>
        private void AddGateMarker(Vector2 worldA, Vector2 worldB, bool isBossGate)
        {
            Vector2 a = ContentPoint(worldA.x, worldA.y);
            Vector2 b = ContentPoint(worldB.x, worldB.y);
            Vector2 mid = (a + b) * 0.5f;
            float length = Mathf.Max(1.5f, Vector2.Distance(a, b));
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            float thickness = isBossGate ? MapScreenDesign.BossGateThickness : MapScreenDesign.GateThickness;

            var marker = AddImage(_content, HudTextures.RoundedBox(8, 0.4f),
                isBossGate ? MapScreenDesign.BossGate : MapScreenDesign.Gate, "Gate");
            Anchor(marker.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            marker.rectTransform.anchoredPosition = mid;
            marker.rectTransform.sizeDelta = new Vector2(length, thickness);
            marker.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
            marker.raycastTarget = false;
        }

        /// <summary>Resets zoom to the opening "fit the whole world" state and pan to centred — the
        /// state every <see cref="Open"/> starts from (AC: "starts zoomed to fit the whole world").</summary>
        private void ResetView()
        {
            _zoom = 1f;
            _pan = Vector2.zero;
            _lastPointers.Clear();
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            if (_content == null) return;
            float scale = _fitScale * _zoom;
            _content.localScale = new Vector3(scale, scale, 1f);
            _content.anchoredPosition = _pan;
        }

        private Vector2 ViewportSize()
        {
            if (_viewport == null) return new Vector2(RefW, RefH);
            Rect r = _viewport.rect;
            return new Vector2(Mathf.Max(1f, r.width), Mathf.Max(1f, r.height));
        }

        private void UpdateCurrentArea()
        {
            if (!_contentBuilt) return;

            int currentArea = 1;
            if (_backyardPath != null && _backyardPath.AreaDirector != null)
                currentArea = _backyardPath.AreaDirector.CurrentArea;

            if (currentArea == _shownCurrentArea) return;
            _shownCurrentArea = currentArea;

            // Border-only override (MapScreenDesign.CurrentAreaBorder's own doc comment): the fill
            // never changes, so a current BOSS arena still reads red, not the highlight cyan.
            for (int i = 0; i < _areaImages.Count; i++)
            {
                bool isCurrent = _areaIndexByImage[i] == currentArea;
                _areaBorderImages[i].color = isCurrent
                    ? MapScreenDesign.CurrentAreaBorder
                    : RoleColors(_areaIsBoss[i], _areaIsShed[i]).border;
            }
        }

        private void UpdatePlayerMarker()
        {
            if (_playerMarker == null || _player == null) return;

            Vector3 pos = _player.transform.position;
            Vector2 rot = MinimapModel.RotatedNormalizedPosition(_worldBounds, pos.x, pos.z);
            _playerMarker.anchoredPosition = new Vector2(rot.x * _contentSize.x, rot.y * _contentSize.y);
        }

        // ------------------------------------------------------------------ pinch/pan gesture

        /// <summary>Reads the drag surface's live pointer set every frame and turns it into a pan (one
        /// finger) or a pinch-zoom-and-pan (two fingers), clamped through <see cref="MapPanZoomModel"/>.
        /// A frame where the active pointer COUNT changes (a finger just went down or up) only updates
        /// the remembered baseline — applying no delta that frame — so neither gesture ever jumps on the
        /// transition in or out of it.</summary>
        private void HandleGesture()
        {
            if (_dragSurface == null || _content == null) return;
            IReadOnlyDictionary<int, Vector2> pointers = _dragSurface.Pointers;

            if (pointers.Count == 2 && _lastPointers.Count == 2 && SameKeys(pointers, _lastPointers))
            {
                ApplyPinch(pointers);
            }
            else if (pointers.Count == 1 && _lastPointers.Count == 1 && SameKeys(pointers, _lastPointers))
            {
                ApplyPan(pointers);
            }

            _lastPointers.Clear();
            foreach (var kv in pointers) _lastPointers[kv.Key] = kv.Value;
        }

        private static bool SameKeys(IReadOnlyDictionary<int, Vector2> a, Dictionary<int, Vector2> b)
        {
            foreach (var kv in a) if (!b.ContainsKey(kv.Key)) return false;
            return true;
        }

        private void ApplyPan(IReadOnlyDictionary<int, Vector2> pointers)
        {
            Vector2 current = First(pointers);
            Vector2 previous = First(_lastPointers);
            _pan = MapPanZoomModel.ClampPan(_pan + (current - previous), ViewportSize(), _contentSize, _fitScale * _zoom);
            ApplyTransform();
        }

        private void ApplyPinch(IReadOnlyDictionary<int, Vector2> pointers)
        {
            var e = pointers.GetEnumerator();
            e.MoveNext(); Vector2 a = e.Current.Value;
            e.MoveNext(); Vector2 b = e.Current.Value;

            var prevE = _lastPointers.GetEnumerator();
            prevE.MoveNext(); Vector2 pa = prevE.Current.Value;
            prevE.MoveNext(); Vector2 pb = prevE.Current.Value;

            Vector2 midpoint = (a + b) * 0.5f;
            Vector2 prevMidpoint = (pa + pb) * 0.5f;
            float distance = Mathf.Max(MinPinchDistance, Vector2.Distance(a, b));
            float prevDistance = Mathf.Max(MinPinchDistance, Vector2.Distance(pa, pb));

            Vector2 viewportCentre = ViewportSize() * 0.5f;
            Vector2 pivot = midpoint - viewportCentre; // pan/pivot space is centred on the viewport

            float oldZoomFull = _fitScale * _zoom;
            float newZoomMultiplier = MapPanZoomModel.ClampZoom(_zoom * (distance / prevDistance), _maxZoomMultiplier);
            float newZoomFull = _fitScale * newZoomMultiplier;

            _pan = MapPanZoomModel.ZoomAboutPoint(_pan, oldZoomFull, newZoomFull, pivot);
            // Two-finger pan: the midpoint itself moving (beyond the pinch spread) drags the map too.
            _pan += midpoint - prevMidpoint;
            _zoom = newZoomMultiplier;
            _pan = MapPanZoomModel.ClampPan(_pan, ViewportSize(), _contentSize, _fitScale * _zoom);
            ApplyTransform();
        }

        private static Vector2 First(IReadOnlyDictionary<int, Vector2> pointers)
        {
            foreach (var kv in pointers) return kv.Value;
            return Vector2.zero;
        }

        // ------------------------------------------------------------------ small UI helpers
        // (same duplicated-per-screen idiom as WeaponsScreen/HudController's own private helpers)

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image AddImage(Transform parent, Sprite sprite, Color color, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static Text AddText(Transform parent, float size, Color color, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            // Floored at 4 (MV-567): this legacy dynamic font renders wildly oversized — many times its
            // own container — below roughly fontSize 4 in this project's setup (confirmed empirically:
            // NameLabelHeight's 2.3 rounds to 2 and blew the area-name labels up past their own room;
            // IndexLabelHeight's 3.4 rounds to 3 and happened to render fine, right at the edge of
            // whatever internal threshold this is). Every caller of this helper wants legible text, never
            // a value in that broken range, so the floor belongs here once, not repeated per call site.
            t.fontSize = Mathf.Max(4, Mathf.RoundToInt(size));
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>A map-content label sized directly in world metres (MV-567 fidelity fix, not in the
        /// original seven changes): asked for at its true tiny nominal size, this dynamic font
        /// rasterizes each glyph at only a few pixels, and <see cref="_content"/>'s own zoom then
        /// magnifies that low-res glyph into an illegible blur — the same defect the constant-size
        /// design was meant to fix, just moved from "wrong size" to "wrong resolution". Asking for a
        /// much bigger nominal <see cref="Text.fontSize"/>, then compensating with the label's own
        /// <see cref="Transform.localScale"/>, keeps the on-screen footprint identical (world metres in,
        /// world metres out — <paramref name="worldSizeDelta"/> and <paramref name="worldAnchoredPosition"/>
        /// are both still expressed in the room's own world-metre space) while the glyph itself
        /// rasterizes crisp.</summary>
        private const float TextCrispnessBoost = 12f;

        private static Text AddWorldMetreLabel(Transform parent, float worldHeight, Vector2 worldSizeDelta,
            Vector2 worldAnchoredPosition, Color color, TextAnchor align)
        {
            Text t = AddText(parent, worldHeight * TextCrispnessBoost, color, align);
            Anchor(t.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero);
            t.rectTransform.localScale = new Vector3(1f / TextCrispnessBoost, 1f / TextCrispnessBoost, 1f);
            t.rectTransform.sizeDelta = worldSizeDelta * TextCrispnessBoost;
            t.rectTransform.anchoredPosition = worldAnchoredPosition;
            return t;
        }

        private static void Anchor(RectTransform r, Vector2 min, Vector2 max, Vector2 pivot)
        {
            r.anchorMin = min; r.anchorMax = max; r.pivot = pivot;
        }

        private static void Stretch(RectTransform r, float padding = 0f)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(-padding, -padding);
            r.offsetMax = new Vector2(padding, padding);
        }

        private static void Center(RectTransform r, float size)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(size, size);
            r.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>Tracks every currently-active pointer's screen position by pointer id (MV-563) — the
    /// EventSystem/InputSystemUIInputModule already routes simultaneous touches as distinct pointer ids
    /// through these same handler interfaces (the move/aim joysticks rely on the identical fact to work
    /// together), so a plain dictionary keyed by <see cref="PointerEventData.pointerId"/> is enough to
    /// tell <see cref="MapScreen"/> how many fingers are down and where, without it caring about the
    /// underlying input device at all.</summary>
    internal sealed class MapDragSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private readonly Dictionary<int, Vector2> _pointers = new Dictionary<int, Vector2>();

        public IReadOnlyDictionary<int, Vector2> Pointers => _pointers;

        public void OnPointerDown(PointerEventData eventData) => _pointers[eventData.pointerId] = eventData.position;
        public void OnDrag(PointerEventData eventData) => _pointers[eventData.pointerId] = eventData.position;
        public void OnPointerUp(PointerEventData eventData) => _pointers.Remove(eventData.pointerId);

        private void OnDisable() => _pointers.Clear();
    }
}
