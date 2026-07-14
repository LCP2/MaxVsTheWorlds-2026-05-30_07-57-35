using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// The map editor (YT-89): a top-down board for the arena, drag to lay it out, save back to the
    /// map's JSON. Menu: <c>MaxWorlds/Map Editor</c>.
    ///
    /// It draws the DERIVED WALLS, not just the rooms — so dragging a room shows you the wall rebuild
    /// itself, live, and the doorway slide along with its gate. That is the point: the level is rooms
    /// and links, the walls follow, and you can see that they do rather than having to trust it.
    ///
    /// Validation runs on every change and the banner tells you, in words, the first thing that would
    /// stop the map playing. You cannot save a map that would ship broken.
    /// </summary>
    public sealed class MapEditorWindow : EditorWindow
    {
        private const float SidebarWidth = 320f;
        private const float Snap = 0.5f;

        private string _key = MapLibrary.BackyardSlice;
        private MapData _map;
        private bool _dirty;
        private string _invalid;          // null when the map is playable

        private float _zoom = 9f;         // pixels per metre
        private Vector2 _pan;             // pixels
        private bool _panning;

        private object _selected;         // MapZone or MapEntity
        private Vector2 _grabOffset;      // world-space, so a drag does not snap the item to the cursor

        private static readonly Dictionary<ZoneKind, Color> ZoneColors = new Dictionary<ZoneKind, Color>
        {
            { ZoneKind.Entry,    new Color(0.27f, 0.77f, 0.41f, 0.55f) },
            { ZoneKind.Open,     new Color(0.44f, 0.54f, 0.24f, 0.55f) },
            { ZoneKind.Cover,    new Color(0.36f, 0.48f, 0.21f, 0.55f) },
            { ZoneKind.Interior, new Color(0.54f, 0.42f, 0.24f, 0.55f) },
            { ZoneKind.Hazard,   new Color(0.25f, 0.54f, 0.54f, 0.55f) },
            { ZoneKind.Dense,    new Color(0.25f, 0.37f, 0.17f, 0.55f) },
            { ZoneKind.Boss,     new Color(0.42f, 0.29f, 0.54f, 0.55f) },
        };

        private static readonly Dictionary<EntityKind, Color> EntityColors = new Dictionary<EntityKind, Color>
        {
            { EntityKind.PlayerSpawn, new Color(0.27f, 0.77f, 0.41f) },
            { EntityKind.Factory,     new Color(0.90f, 0.33f, 0.24f) },
            { EntityKind.Gate,        new Color(0.91f, 0.73f, 0.29f) },
            { EntityKind.Boss,        new Color(0.72f, 0.42f, 1.00f) },
            { EntityKind.Cover,       new Color(0.35f, 0.68f, 0.45f) },
            { EntityKind.Prop,        new Color(0.55f, 0.55f, 0.58f) },
            { EntityKind.Pickup,      new Color(1.00f, 0.84f, 0.29f) },
        };

        [MenuItem("MaxWorlds/Map Editor (YT-89)")]
        public static void Open()
        {
            var window = GetWindow<MapEditorWindow>("Map Editor");
            window.minSize = new Vector2(900f, 560f);
            window.Reload();
        }

        private void Reload()
        {
            _map = MapLibrary.Load(_key);
            _selected = null;
            _dirty = false;
            Revalidate();
            Frame();
            Repaint();
        }

        private void Revalidate()
        {
            if (_map == null) { _invalid = "the map did not load"; return; }
            _invalid = MapValidation.Validate(_map, out string reason) ? null : reason;
        }

        /// <summary>Fit the whole map on screen.</summary>
        private void Frame()
        {
            if (_map == null || _map.zones.Length == 0) return;

            Rect b = _map.Bounds();
            float canvasW = Mathf.Max(position.width - SidebarWidth, 200f);
            _zoom = Mathf.Clamp(Mathf.Min(canvasW / (b.width + 12f), (position.height - 40f) / (b.height + 12f)), 2f, 40f);
            _pan = Vector2.zero;
        }

        private void OnGUI()
        {
            var canvas = new Rect(0f, 0f, position.width - SidebarWidth, position.height);
            var sidebar = new Rect(canvas.width, 0f, SidebarWidth, position.height);

            DrawCanvas(canvas);
            DrawSidebar(sidebar);
        }

        // ------------------------------------------------------------------ the board

        private Vector2 Origin(Rect canvas) =>
            new Vector2(canvas.center.x + _pan.x, canvas.center.y + _pan.y);

        /// <summary>World XZ → screen. +Z runs UP the board, the way the design board draws it.</summary>
        private Vector2 ToScreen(Rect canvas, float x, float z)
        {
            Vector2 o = Origin(canvas);
            return new Vector2(o.x + x * _zoom, o.y - z * _zoom);
        }

        private Vector2 ToWorld(Rect canvas, Vector2 screen)
        {
            Vector2 o = Origin(canvas);
            return new Vector2((screen.x - o.x) / _zoom, (o.y - screen.y) / _zoom);
        }

        private Rect ScreenRect(Rect canvas, float x, float z, float width, float depth)
        {
            Vector2 tl = ToScreen(canvas, x - width * 0.5f, z + depth * 0.5f);
            return new Rect(tl.x, tl.y, width * _zoom, depth * _zoom);
        }

        private void DrawCanvas(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, new Color(0.10f, 0.11f, 0.13f));
            if (_map == null)
            {
                GUI.Label(new Rect(canvas.x + 16f, canvas.y + 16f, 400f, 20f), $"No map '{_key}'.");
                return;
            }

            GUI.BeginClip(canvas);
            var local = new Rect(0f, 0f, canvas.width, canvas.height);

            foreach (MapZone z in _map.zones)
            {
                Rect r = ScreenRect(local, z.x, z.z, z.width, z.depth);
                EditorGUI.DrawRect(r, ZoneColors.TryGetValue(z.Kind, out Color c) ? c : Color.grey);

                if (ReferenceEquals(z, _selected)) Outline(r, Color.white);

                GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width, 16f),
                    string.IsNullOrEmpty(z.name) ? z.id : z.name, Label(Color.white));
            }

            // The walls the rooms and links imply. This is the map engine's output, drawn live.
            foreach (WallSegment w in MapGeometry.Walls(_map))
            {
                Rect r = ScreenRect(local, w.Center.x, w.Center.z, w.Size.x, w.Size.z);
                EditorGUI.DrawRect(r, new Color(0.86f, 0.86f, 0.90f, 0.85f));
            }

            foreach (MapEntity e in _map.entities)
            {
                Color c = EntityColors.TryGetValue(e.Kind, out Color found) ? found : Color.grey;

                // Cover and props have a real footprint; the rest are markers.
                if (e.Kind == EntityKind.Cover || e.Kind == EntityKind.Prop)
                {
                    Rect r = ScreenRect(local, e.x, e.z, e.width, e.depth);
                    EditorGUI.DrawRect(r, new Color(c.r, c.g, c.b, 0.75f));
                    if (ReferenceEquals(e, _selected)) Outline(r, Color.white);
                }
                else
                {
                    Vector2 p = ToScreen(local, e.x, e.z);
                    var r = new Rect(p.x - 7f, p.y - 7f, 14f, 14f);
                    EditorGUI.DrawRect(r, c);
                    if (ReferenceEquals(e, _selected)) Outline(new Rect(r.x - 2f, r.y - 2f, 18f, 18f), Color.white);

                    GUI.Label(new Rect(p.x + 10f, p.y - 8f, 200f, 16f), e.id, Label(Color.white));
                }
            }

            GUI.EndClip();

            HandleMouse(canvas);
        }

        private static void Outline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2f, r.width, 2f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 2f, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 2f, r.y, 2f, r.height), c);
        }

        private static GUIStyle Label(Color c)
        {
            var s = new GUIStyle(EditorStyles.miniBoldLabel);
            s.normal.textColor = c;
            return s;
        }

        private void HandleMouse(Rect canvas)
        {
            Event e = Event.current;
            if (!canvas.Contains(e.mousePosition)) return;

            var local = new Rect(0f, 0f, canvas.width, canvas.height);
            Vector2 localMouse = e.mousePosition - canvas.position;

            if (e.type == EventType.ScrollWheel)
            {
                _zoom = Mathf.Clamp(_zoom * (1f - e.delta.y * 0.03f), 2f, 40f);
                e.Use(); Repaint(); return;
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _selected = Pick(local, localMouse);
                if (_selected != null)
                {
                    Vector2 world = ToWorld(local, localMouse);
                    _grabOffset = world - At(_selected);
                }
                e.Use(); Repaint(); return;
            }

            if (e.type == EventType.MouseDown && e.button == 2) { _panning = true; e.Use(); return; }
            if (e.type == EventType.MouseUp && e.button == 2) { _panning = false; e.Use(); return; }

            if (e.type == EventType.MouseDrag && _panning)
            {
                _pan += e.delta;
                e.Use(); Repaint(); return;
            }

            if (e.type == EventType.MouseDrag && e.button == 0 && _selected != null)
            {
                Vector2 world = ToWorld(local, localMouse) - _grabOffset;
                MoveTo(_selected, Round(world.x), Round(world.y));
                _dirty = true;
                Revalidate();
                e.Use(); Repaint();
            }
        }

        private static float Round(float v) => Mathf.Round(v / Snap) * Snap;

        /// <summary>Entities first — they sit on top of the rooms, and they are what you usually want
        /// to grab. Then the smallest room under the cursor, so a room nested in a bigger one is
        /// reachable.</summary>
        private object Pick(Rect canvas, Vector2 mouse)
        {
            foreach (MapEntity e in _map.entities)
            {
                bool hit = e.Kind == EntityKind.Cover || e.Kind == EntityKind.Prop
                    ? ScreenRect(canvas, e.x, e.z, e.width, e.depth).Contains(mouse)
                    : new Rect(ToScreen(canvas, e.x, e.z) - new Vector2(8f, 8f), new Vector2(16f, 16f)).Contains(mouse);

                if (hit) return e;
            }

            MapZone best = null;
            foreach (MapZone z in _map.zones)
            {
                if (!ScreenRect(canvas, z.x, z.z, z.width, z.depth).Contains(mouse)) continue;
                if (best == null || z.width * z.depth < best.width * best.depth) best = z;
            }
            return best;
        }

        private static Vector2 At(object item) => item switch
        {
            MapZone z => new Vector2(z.x, z.z),
            MapEntity e => new Vector2(e.x, e.z),
            _ => Vector2.zero,
        };

        private static void MoveTo(object item, float x, float z)
        {
            switch (item)
            {
                case MapZone zone: zone.x = x; zone.z = z; break;
                case MapEntity entity: entity.x = x; entity.z = z; break;
            }
        }

        // ------------------------------------------------------------------ the sidebar

        private void DrawSidebar(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.16f, 0.17f, 0.20f));
            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 10f, area.width - 20f, area.height - 20f));

            EditorGUILayout.LabelField("MAP", EditorStyles.boldLabel);
            DrawMapPicker();

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reload")) Reload();
                if (GUILayout.Button("Frame")) { Frame(); Repaint(); }

                using (new EditorGUI.DisabledScope(_map == null || _invalid != null))
                {
                    if (GUILayout.Button(_dirty ? "Save *" : "Save")) Save();
                }
            }

            EditorGUILayout.Space(8f);
            DrawStatus();

            if (_map == null) { GUILayout.EndArea(); return; }

            EditorGUILayout.Space(8f);
            DrawMapFields();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("SELECTION", EditorStyles.boldLabel);
            DrawSelection();

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Room")) AddZone();
                if (GUILayout.Button("Add Thing")) AddEntity();

                using (new EditorGUI.DisabledScope(_selected == null))
                {
                    if (GUILayout.Button("Delete")) DeleteSelected();
                }
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.HelpBox(
                "Drag a room or a thing to move it. Walls are DERIVED from the rooms and the links " +
                "between them — you never place a wall. Scroll to zoom, middle-drag to pan.",
                MessageType.None);

            GUILayout.EndArea();
        }

        private void DrawMapPicker()
        {
            string[] keys = MapKeys();
            if (keys.Length == 0) { EditorGUILayout.LabelField("No maps in Resources/Maps."); return; }

            int i = Mathf.Max(0, System.Array.IndexOf(keys, _key));
            int picked = EditorGUILayout.Popup(i, keys);
            if (picked != i) { _key = keys[picked]; Reload(); }
        }

        private static string[] MapKeys()
        {
            string dir = Path.GetDirectoryName(MapLibrary.AssetPath("x"));
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return new string[0];

            return Directory.GetFiles(dir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(k => k)
                .ToArray();
        }

        private void DrawStatus()
        {
            if (_map == null)
            {
                EditorGUILayout.HelpBox("No map loaded.", MessageType.Error);
                return;
            }

            if (_invalid == null)
            {
                EditorGUILayout.HelpBox(_dirty ? "Playable — unsaved changes." : "Playable.", MessageType.Info);
                return;
            }

            // The whole reason authoring got faster: the map tells you what is wrong, in words, now —
            // not after a build, a deploy and a playtest.
            EditorGUILayout.HelpBox($"Will not build:\n{_invalid}", MessageType.Error);
        }

        private void DrawMapFields()
        {
            EditorGUI.BeginChangeCheck();

            _map.name = EditorGUILayout.TextField("Name", _map.name);
            _map.wallHeight = EditorGUILayout.FloatField("Wall height", _map.wallHeight);
            _map.wallThickness = EditorGUILayout.FloatField("Wall thickness", _map.wallThickness);

            if (EditorGUI.EndChangeCheck()) { _dirty = true; Revalidate(); }
        }

        private void DrawSelection()
        {
            if (_selected == null)
            {
                EditorGUILayout.LabelField("Nothing selected.", EditorStyles.miniLabel);
                return;
            }

            EditorGUI.BeginChangeCheck();

            if (_selected is MapZone zone)
            {
                zone.id = EditorGUILayout.TextField("Id", zone.id);
                zone.name = EditorGUILayout.TextField("Name", zone.name);
                zone.type = EnumPopup("Type", zone.type, System.Enum.GetNames(typeof(ZoneKind)));
                zone.x = EditorGUILayout.FloatField("X", zone.x);
                zone.z = EditorGUILayout.FloatField("Z", zone.z);
                zone.width = EditorGUILayout.FloatField("Width", zone.width);
                zone.depth = EditorGUILayout.FloatField("Depth", zone.depth);

                EditorGUILayout.Space(4f);
                DrawLinksFor(zone);
            }
            else if (_selected is MapEntity e)
            {
                e.id = EditorGUILayout.TextField("Id", e.id);
                e.kind = EnumPopup("Kind", e.kind, System.Enum.GetNames(typeof(EntityKind)));
                e.x = EditorGUILayout.FloatField("X", e.x);
                e.z = EditorGUILayout.FloatField("Z", e.z);

                if (e.Kind == EntityKind.Cover || e.Kind == EntityKind.Prop || e.Kind == EntityKind.Gate)
                {
                    e.width = EditorGUILayout.FloatField("Width", e.width);
                    e.height = EditorGUILayout.FloatField("Height", e.height);
                    e.depth = EditorGUILayout.FloatField("Depth", e.depth);
                }

                if (e.Kind == EntityKind.Cover)
                {
                    e.shape = EnumPopup("Shape", e.shape, System.Enum.GetNames(typeof(CoverShape)));
                    e.dressing = EnumPopup("Dressing", e.dressing, System.Enum.GetNames(typeof(CoverDressing)));
                }

                if (e.Kind == EntityKind.Gate)
                {
                    e.opensOn = EditorGUILayout.TextField("Opens on (factory id)", e.opensOn);
                    EditorGUILayout.LabelField(" ", $"Seals {MapRuntime.SealWidth(_map, e):0.#} m",
                        EditorStyles.miniLabel);
                }
            }

            if (EditorGUI.EndChangeCheck()) { _dirty = true; Revalidate(); }
        }

        /// <summary>The links a room has, and a way to add one. A link is what makes a doorway — there
        /// is no other way to get from one room to the next.</summary>
        private void DrawLinksFor(MapZone zone)
        {
            EditorGUILayout.LabelField("Ways out", EditorStyles.miniBoldLabel);

            var links = _map.links.ToList();
            for (int i = links.Count - 1; i >= 0; i--)
            {
                MapLink link = links[i];
                if (link.from != zone.id && link.to != zone.id) continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    string other = link.from == zone.id ? link.to : link.from;
                    EditorGUILayout.LabelField($"→ {other}", GUILayout.Width(90f));

                    EditorGUILayout.LabelField("door", GUILayout.Width(30f));
                    link.doorway = EditorGUILayout.FloatField(link.doorway, GUILayout.Width(40f));

                    if (GUILayout.Button("x", GUILayout.Width(20f)))
                    {
                        links.RemoveAt(i);
                        _map.links = links.ToArray();
                        _dirty = true; Revalidate();
                        return;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                string[] others = _map.zones.Where(z => z.id != zone.id).Select(z => z.id).ToArray();
                if (others.Length == 0) return;

                if (GUILayout.Button("Link to…", GUILayout.Width(70f)))
                {
                    var menu = new GenericMenu();
                    foreach (string other in others)
                    {
                        string target = other;
                        menu.AddItem(new GUIContent(target), false, () =>
                        {
                            links.Add(new MapLink { from = zone.id, to = target, doorway = 0f });
                            _map.links = links.ToArray();
                            _dirty = true; Revalidate(); Repaint();
                        });
                    }
                    menu.ShowAsContext();
                }
            }
        }

        private static string EnumPopup(string label, string current, string[] names)
        {
            int i = Mathf.Max(0, System.Array.FindIndex(names,
                n => string.Equals(n, current, System.StringComparison.OrdinalIgnoreCase)));
            return names[EditorGUILayout.Popup(label, i, names)].ToLowerInvariant();
        }

        // ------------------------------------------------------------------ editing

        private void AddZone()
        {
            var zones = _map.zones.ToList();
            zones.Add(new MapZone
            {
                id = Unique("room", zones.Select(z => z.id)),
                name = "New Room",
                type = "open",
                x = 0f, z = 0f, width = 20f, depth = 20f,
            });
            _map.zones = zones.ToArray();
            _selected = zones[zones.Count - 1];
            _dirty = true; Revalidate(); Repaint();
        }

        private void AddEntity()
        {
            var entities = _map.entities.ToList();
            entities.Add(new MapEntity
            {
                id = Unique("cover", entities.Select(e => e.id)),
                kind = "cover",
                x = 0f, z = 0f,
                width = 3f, height = 2f, depth = 3f,
            });
            _map.entities = entities.ToArray();
            _selected = entities[entities.Count - 1];
            _dirty = true; Revalidate(); Repaint();
        }

        private void DeleteSelected()
        {
            if (_selected is MapZone zone)
            {
                _map.zones = _map.zones.Where(z => !ReferenceEquals(z, zone)).ToArray();
                // A link to a room that is gone is a link to nowhere.
                _map.links = _map.links.Where(l => l.from != zone.id && l.to != zone.id).ToArray();
            }
            else if (_selected is MapEntity entity)
            {
                _map.entities = _map.entities.Where(e => !ReferenceEquals(e, entity)).ToArray();
            }

            _selected = null;
            _dirty = true; Revalidate(); Repaint();
        }

        private static string Unique(string stem, IEnumerable<string> taken)
        {
            var used = new HashSet<string>(taken);
            for (int i = 1; ; i++)
            {
                string candidate = $"{stem}_{i}";
                if (used.Add(candidate)) return candidate;
            }
        }

        private void Save()
        {
            if (_map == null) return;

            if (!MapValidation.Validate(_map, out string reason))
            {
                // Belt and braces — the Save button is already disabled. A map that cannot play must
                // not be able to reach a build through this window.
                EditorUtility.DisplayDialog("Map will not build", reason, "Fix it");
                return;
            }

            MapLibrary.Save(_key, _map);
            _dirty = false;
            Debug.Log($"[MapEditor] saved '{_key}' → {MapLibrary.AssetPath(_key)}");
        }
    }
}
