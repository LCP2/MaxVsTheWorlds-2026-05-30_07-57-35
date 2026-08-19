using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Procedurally-generated sprites for the YT-30 HUD — bars, discs, the concentric
    /// "tech-ring" joystick base, direction arrow, crosshair, and rounded icon boxes.
    /// Everything is drawn in code (no committed art), per the greybox / code-driven-scene
    /// rule, so a fresh clone builds an identical HUD headlessly. Sprites are cached by key.
    /// </summary>
    public static class HudTextures
    {
        private static readonly Dictionary<string, Sprite> s_cache = new Dictionary<string, Sprite>();

        /// <summary>1×1 white sprite. Tint via Image.color — used for every bar/panel fill.</summary>
        public static Sprite Solid()
        {
            if (s_cache.TryGetValue("solid", out var s)) return s;
            var tex = NewTex(4, 4);
            Fill(tex, Color.white);
            return Cache("solid", tex, 100f);
        }

        /// <summary>Filled anti-aliased disc (radial-fill overlays, joystick knob).</summary>
        public static Sprite Disc(int size = 128)
        {
            string key = $"disc{size}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - r + 0.5f) * (x - r + 0.5f) + (y - r + 0.5f) * (y - r + 0.5f));
                float a = Mathf.Clamp01(r - d);           // 1px feathered edge
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        /// <summary>Soft radial halo — alpha 1 at the centre falling smoothly to 0 at the edge
        /// (quadratic ease-out), unlike <see cref="Disc"/>'s hard feathered-edge fill. THE RIG board's
        /// owned/draftable node glow (MV-433) reuses this one shared texture at whatever size/tint a
        /// node's state calls for, rather than baking a texture per node. Tint (and the halo's overall
        /// peak alpha, e.g. 0.28) via Image.color.</summary>
        public static Sprite Glow(int size = 128)
        {
            string key = $"glow{size}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.Clamp01(d / r);
                float a = 1f - t * t;
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        /// <summary>A cog/gear disc with notched teeth (MV-262: the Weapons screen's spinning
        /// "parts available" symbol) — deliberately asymmetric so a rotation actually reads as
        /// motion, unlike the symmetric <see cref="Disc"/>. Tint via Image.color.</summary>
        public static Sprite Gear(int size = 64, int teeth = 8)
        {
            string key = $"gear{size}_{teeth}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            float cx = size * 0.5f, cy = size * 0.5f;
            float rOuter = size * 0.5f - 1f;
            float rToothBase = rOuter * 0.72f;
            float rHole = rOuter * 0.32f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx + 0.5f, dy = y - cy + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);                               // -pi..pi
                float sector = (ang + Mathf.PI) / (2f * Mathf.PI) * teeth;     // 0..teeth
                float frac = sector - Mathf.Floor(sector);                    // 0..1 within its wedge
                bool inTooth = frac > 0.22f && frac < 0.78f;                   // tooth fills the wedge's middle
                float rMax = inTooth ? rOuter : rToothBase;
                float a = (d <= rMax && d >= rHole) ? 1f : 0f;
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        /// <summary>Concentric glowing rings — the joystick base. Tint via Image.color.</summary>
        public static Sprite TechRings(int size = 160, int rings = 3)
        {
            string key = $"rings{size}_{rings}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            float cx = size * 0.5f, cy = size * 0.5f, outer = size * 0.5f - 1f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - cx + 0.5f) * (x - cx + 0.5f) + (y - cy + 0.5f) * (y - cy + 0.5f));
                float a = 0f;
                // A faint filled core plus `rings` bright concentric bands.
                if (d <= outer) a = 0.12f;
                for (int i = 1; i <= rings; i++)
                {
                    float rr = outer * i / rings;
                    float band = 1f - Mathf.Clamp01(Mathf.Abs(d - rr) / 2.2f); // ~2px bands
                    a = Mathf.Max(a, band);
                }
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        /// <summary>Upward-pointing triangle (movement direction overlay).</summary>
        public static Sprite Arrow(int size = 64)
        {
            if (s_cache.TryGetValue("arrow", out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float ny = (float)y / size;                 // 0 bottom -> 1 top
                float halfWidth = (1f - ny) * 0.5f * size;  // widens toward the base
                float cx = size * 0.5f;
                bool inside = ny > 0.15f && Mathf.Abs(x - cx) <= halfWidth;
                px[y * size + x] = new Color(1, 1, 1, inside ? 1f : 0f);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache("arrow", tex, 100f);
        }

        /// <summary>Thin crosshair glyph (aim-joystick centre).</summary>
        public static Sprite Crosshair(int size = 96)
        {
            if (s_cache.TryGetValue("cross", out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float c = size * 0.5f, thick = Mathf.Max(1.5f, size * 0.04f), gap = size * 0.14f, len = size * 0.42f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - c + 0.5f), dy = Mathf.Abs(y - c + 0.5f);
                bool horiz = dy <= thick && dx >= gap && dx <= len;
                bool vert = dx <= thick && dy >= gap && dy <= len;
                px[y * size + x] = new Color(1, 1, 1, (horiz || vert) ? 1f : 0f);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache("cross", tex, 100f);
        }

        /// <summary>Rounded-corner box (icon buttons, ability slots, name card). 9-sliced.</summary>
        public static Sprite RoundedBox(int size = 64, float cornerFraction = 0.28f)
        {
            string key = $"rbox{size}_{Mathf.RoundToInt(cornerFraction * 100)}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float radius = size * cornerFraction;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float a = RoundedAlpha(x, y, size, size, radius);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            float b = radius; // 9-slice border so scaling keeps corners crisp
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }

        /// <summary>Filled regular polygon (MV-423: THE RIG board's hex/diamond nodes). Vertex <c>i</c>
        /// sits at angle <c>(360/sides)*i + rotationDeg</c> in this y-down pixel space, so
        /// <c>rotationDeg=-90</c> puts a vertex straight up ("pointy-top" per the design data file).
        /// Circumradius is always <c>height/2</c> — callers size <paramref name="width"/> to the
        /// shape's own bounding box (a pointy-top hex: <c>r*sqrt(3) x 2r</c>; a diamond/square:
        /// <c>2r x 2r</c>) so the sprite never distorts when stretched onto a node's RectTransform.</summary>
        public static Sprite Polygon(int sides, float rotationDeg, int width, int height)
        {
            string key = $"poly{sides}_{rotationDeg}_{width}_{height}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(width, height);
            var px = new Color32[width * height];
            float cx = width * 0.5f, cy = height * 0.5f, r = height * 0.5f;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float a = PolygonAlpha(x + 0.5f - cx, y + 0.5f - cy, sides, rotationDeg, r, 0f, false, 0);
                px[y * width + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        /// <summary>Stroked (outline-only) regular polygon — same shape/orientation rules as
        /// <see cref="Polygon"/>. <paramref name="dashed"/> breaks the perimeter into
        /// <paramref name="dashCount"/> equal on/off arcs (the "capability, draftable" node state's
        /// dashed hex).</summary>
        public static Sprite PolygonOutline(int sides, float rotationDeg, int width, int height,
            float strokeWidth, bool dashed = false, int dashCount = 18)
        {
            string key = $"polyO{sides}_{rotationDeg}_{width}_{height}_{strokeWidth}_{dashed}_{dashCount}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(width, height);
            var px = new Color32[width * height];
            float cx = width * 0.5f, cy = height * 0.5f, r = height * 0.5f;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float a = PolygonAlpha(x + 0.5f - cx, y + 0.5f - cy, sides, rotationDeg, r, strokeWidth, dashed, dashCount);
                px[y * width + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        /// <summary>Signed distance (px) from (px,py) to a w×h rounded-rect boundary of corner
        /// <paramref name="radius"/> — negative inside, positive outside; the standard rounded-box SDF.
        /// Backs both <see cref="RoundedBoxOutline"/> and <see cref="BezierStroke"/>'s neighbours below.</summary>
        private static float RoundedBoxSdf(float px, float py, float w, float h, float radius)
        {
            float qx = Mathf.Abs(px - w * 0.5f) - (w * 0.5f - radius);
            float qy = Mathf.Abs(py - h * 0.5f) - (h * 0.5f - radius);
            float ax = Mathf.Max(qx, 0f), ay = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>Stroked (outline-only) rounded rect — THE RIG board's level-pill / currency-chip
        /// border (MV-443). Same <c>size</c>/<paramref name="cornerFraction"/> convention as
        /// <see cref="RoundedBox"/>; 9-sliced so it scales onto any rect without distorting the corner
        /// radius or stroke width.</summary>
        public static Sprite RoundedBoxOutline(int size, float cornerFraction, float strokeWidth)
        {
            string key = $"rboxO{size}_{Mathf.RoundToInt(cornerFraction * 100)}_{strokeWidth}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float radius = size * cornerFraction;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundedBoxSdf(x + 0.5f, y + 0.5f, size, size, radius);
                float a = Mathf.Clamp01(strokeWidth * 0.5f - Mathf.Abs(d) + 0.5f);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            float b = radius;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }

        /// <summary>Anti-aliased cubic-bezier stroke (MV-443: THE RIG board's parent/child connector
        /// lines) — points are already expressed in the returned texture's own local, top-left-origin
        /// pixel space (callers translate board-space coordinates by the curve's own bounding-box
        /// minimum before calling). Reuses the same stroke-to-segment distance technique
        /// <see cref="VectorIcon"/>'s path stroking already uses, just flattening a single cubic instead
        /// of a whole SVG path.</summary>
        public static Sprite BezierStroke(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float strokeWidth, int width, int height)
        {
            string key = $"bez_{p0}_{p1}_{p2}_{p3}_{strokeWidth}_{width}_{height}";
            if (s_cache.TryGetValue(key, out var s)) return s;

            var pts = FlattenCubic(p0, p1, p2, p3, 24);
            pts.Insert(0, p0);

            var tex = NewTex(width, height);
            var px = new Color32[width * height];
            float half = strokeWidth * 0.5f;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var pt = new Vector2(x + 0.5f, y + 0.5f);
                float a = 0f;
                for (int i = 0; i < pts.Count - 1; i++)
                    a = Mathf.Max(a, Mathf.Clamp01(half - DistanceToSegment(pt, pts[i], pts[i + 1]) + 0.5f));
                px[y * width + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        /// <summary>Stroked circle — the capability node's outer dashed ring
        /// (<c>geometry.capOuterRingOffset</c>) and any other plain ring need. True circular distance
        /// (not a polygon approximation), so it stays round regardless of <paramref name="dashCount"/>.</summary>
        public static Sprite Ring(int size, float strokeWidth, bool dashed = false, int dashCount = 24)
        {
            string key = $"ring{size}_{strokeWidth}_{dashed}_{dashCount}";
            if (s_cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float c = size * 0.5f, r = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float lx = x + 0.5f - c, ly = y + 0.5f - c;
                float d = Mathf.Sqrt(lx * lx + ly * ly);
                float a = Mathf.Clamp01(strokeWidth * 0.5f - Mathf.Abs(d - r) + 0.5f);
                if (dashed && a > 0f)
                {
                    float t = Mathf.Repeat(Mathf.Atan2(ly, lx), 2f * Mathf.PI) / (2f * Mathf.PI);
                    if (((int)(t * dashCount)) % 2 != 0) a = 0f;
                }
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        /// <summary>Coverage (0..1) of a regular <paramref name="sides"/>-gon with circumradius
        /// <paramref name="r"/> at local point (<paramref name="lx"/>, <paramref name="ly"/>), centred on
        /// the origin. <paramref name="strokeWidth"/> &lt;= 0 fills the whole shape; otherwise only the
        /// band within half that width of the boundary is covered (an outline). The nearest-edge
        /// distance formula: for the sector straddling <c>phi</c>, the edge sits at
        /// <c>apothem / cos(a)</c> where <c>a</c> is <c>phi</c>'s offset from that sector's edge
        /// midpoint — standard regular-polygon SDF, exact at edges, a mitred-corner approximation at
        /// vertices (invisible at the stroke widths THE RIG's nodes use, 2-4px against a 40-72px
        /// radius).</summary>
        private static float PolygonAlpha(float lx, float ly, int sides, float rotationDeg, float r,
            float strokeWidth, bool dashed, int dashCount)
        {
            float segment = 2f * Mathf.PI / sides;
            float rot = rotationDeg * Mathf.Deg2Rad;
            float apothem = r * Mathf.Cos(segment * 0.5f);
            float phi = Mathf.Atan2(ly, lx);
            float a = Mathf.Repeat(phi - rot - segment * 0.5f, segment) - segment * 0.5f;
            float edge = apothem / Mathf.Cos(a);
            float d = Mathf.Sqrt(lx * lx + ly * ly);

            float alpha = strokeWidth <= 0f
                ? Mathf.Clamp01(edge - d + 0.5f)
                : Mathf.Clamp01(strokeWidth * 0.5f - Mathf.Abs(d - edge) + 0.5f);

            if (dashed && alpha > 0f)
            {
                float t = Mathf.Repeat(phi - rot, 2f * Mathf.PI) / (2f * Mathf.PI);
                if (((int)(t * dashCount)) % 2 != 0) alpha = 0f;
            }
            return alpha;
        }

        /// <summary>Renders one of THE RIG's procedural icons (MV-423, <c>rig_board.json</c>'s
        /// <c>icons</c> block) — a tiny SVG fragment ("&lt;path d=... /&gt;"/"&lt;circle .../&gt;" tags,
        /// <c>#ICON#</c> as the colour placeholder) drawn in a 44x44 box centred on the origin. Ported
        /// as geometry rather than substituted with a font glyph — <c>HudFont</c> (<c>LegacyRuntime.ttf</c>)
        /// has no coverage for these symbols and would render tofu. Supports the path commands the data
        /// file actually authors — absolute <c>M/L/C/A/Z</c> — plus <c>circle</c>. White/alpha like
        /// every other sprite here; tint via Image.color.</summary>
        public static Sprite VectorIcon(string svgFragment, int size)
        {
            string key = $"vicon{size}_{svgFragment.GetHashCode()}";
            if (s_cache.TryGetValue(key, out var s)) return s;

            List<IconOp> ops = ParseIconOps(svgFragment);
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            float scale = size / 44f;
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float lx = (x + 0.5f - c) / scale, ly = (y + 0.5f - c) / scale;
                float alpha = 0f;
                foreach (var op in ops) alpha = Mathf.Max(alpha, op.CoverageAt(lx, ly));
                px[y * size + x] = new Color(1, 1, 1, alpha);
            }
            tex.SetPixels32(px); tex.Apply();
            return Cache(key, tex, 100f);
        }

        // --- vector icon parsing/rasterising ---

        private static readonly Regex TagRegex = new Regex("<(path|circle)\\s+([^>]*)/>", RegexOptions.Compiled);
        private static readonly Regex AttrRegex = new Regex("([\\w-]+)=\"([^\"]*)\"", RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new Regex(@"-?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        private abstract class IconOp { public abstract float CoverageAt(float x, float y); }

        /// <summary>A parsed <c>&lt;path&gt;</c> tag — one or more flattened polylines (Bezier/arc
        /// segments already subdivided into line segments), stroked and/or filled.</summary>
        private sealed class IconPathOp : IconOp
        {
            public List<List<Vector2>> Subpaths;
            public bool Fill, Stroke;
            public float StrokeWidth;

            public override float CoverageAt(float x, float y)
            {
                float a = 0f;
                var p = new Vector2(x, y);
                if (Fill)
                    foreach (var sp in Subpaths)
                        if (PointInPolygon(p, sp)) { a = 1f; break; }
                if (Stroke)
                {
                    float half = StrokeWidth * 0.5f;
                    foreach (var sp in Subpaths)
                        for (int i = 0; i < sp.Count - 1; i++)
                            a = Mathf.Max(a, Mathf.Clamp01(half - DistanceToSegment(p, sp[i], sp[i + 1]) + 0.5f));
                }
                return a;
            }
        }

        private sealed class IconCircleOp : IconOp
        {
            public Vector2 Center;
            public float Radius, StrokeWidth;
            public bool Fill, Stroke;

            public override float CoverageAt(float x, float y)
            {
                float d = Vector2.Distance(new Vector2(x, y), Center);
                float a = 0f;
                if (Fill) a = Mathf.Max(a, Mathf.Clamp01(Radius - d + 0.5f));
                if (Stroke) a = Mathf.Max(a, Mathf.Clamp01(StrokeWidth * 0.5f - Mathf.Abs(d - Radius) + 0.5f));
                return a;
            }
        }

        private static List<IconOp> ParseIconOps(string svg)
        {
            var ops = new List<IconOp>();
            foreach (Match tagMatch in TagRegex.Matches(svg))
            {
                string tag = tagMatch.Groups[1].Value;
                var attrs = new Dictionary<string, string>();
                foreach (Match am in AttrRegex.Matches(tagMatch.Groups[2].Value))
                    attrs[am.Groups[1].Value] = am.Groups[2].Value;

                bool fillOn = attrs.TryGetValue("fill", out var fillV) && fillV == "#ICON#";
                bool strokeOn = attrs.TryGetValue("stroke", out var strokeV) && strokeV == "#ICON#";
                float strokeWidth = attrs.TryGetValue("stroke-width", out var swV) &&
                    float.TryParse(swV, NumberStyles.Float, CultureInfo.InvariantCulture, out var sw) ? sw : 2f;
                if (!fillOn && !strokeOn) continue;

                if (tag == "circle")
                {
                    ops.Add(new IconCircleOp
                    {
                        Center = new Vector2(ParseAttrFloat(attrs, "cx"), ParseAttrFloat(attrs, "cy")),
                        Radius = ParseAttrFloat(attrs, "r"),
                        StrokeWidth = strokeWidth,
                        Fill = fillOn,
                        Stroke = strokeOn
                    });
                }
                else if (attrs.TryGetValue("d", out var d))
                {
                    ops.Add(new IconPathOp
                    {
                        Subpaths = ParsePathD(d),
                        Fill = fillOn,
                        Stroke = strokeOn,
                        StrokeWidth = strokeWidth
                    });
                }
            }
            return ops;
        }

        private static float ParseAttrFloat(Dictionary<string, string> attrs, string name) =>
            attrs.TryGetValue(name, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

        /// <summary>Flattens an SVG path's absolute <c>M/L/C/A/Z</c> commands into polylines. Extra
        /// coordinate pairs after a single command letter repeat it (standard SVG shorthand — the data
        /// file's star/spread/lob icons all rely on this for their multi-point <c>L</c> runs).</summary>
        private static List<List<Vector2>> ParsePathD(string d)
        {
            var subpaths = new List<List<Vector2>>();
            List<Vector2> current = null;
            Vector2 cursor = Vector2.zero, subpathStart = Vector2.zero;
            int i = 0;
            while (i < d.Length)
            {
                char ch = d[i];
                if ("MLCAZ".IndexOf(ch) < 0) { i++; continue; }

                char cmd = ch;
                i++;
                int start = i;
                while (i < d.Length && "MLCAZ".IndexOf(d[i]) < 0) i++;

                if (cmd == 'Z')
                {
                    if (current != null) { subpaths.Add(current); current = null; }
                    cursor = subpathStart;
                    continue;
                }

                var nums = new List<float>();
                foreach (Match m in NumberRegex.Matches(d.Substring(start, i - start)))
                    nums.Add(float.Parse(m.Value, CultureInfo.InvariantCulture));

                int arity = cmd == 'M' || cmd == 'L' ? 2 : cmd == 'C' ? 6 : cmd == 'A' ? 7 : 0;
                if (arity == 0) continue;

                for (int k = 0; k + arity <= nums.Count; k += arity)
                {
                    if (cmd == 'M')
                    {
                        if (current != null) subpaths.Add(current);
                        cursor = new Vector2(nums[k], nums[k + 1]);
                        subpathStart = cursor;
                        current = new List<Vector2> { cursor };
                        cmd = 'L';   // extra pairs after M are implicit linetos
                    }
                    else if (cmd == 'L')
                    {
                        cursor = new Vector2(nums[k], nums[k + 1]);
                        current.Add(cursor);
                    }
                    else if (cmd == 'C')
                    {
                        var p1 = new Vector2(nums[k], nums[k + 1]);
                        var p2 = new Vector2(nums[k + 2], nums[k + 3]);
                        var p3 = new Vector2(nums[k + 4], nums[k + 5]);
                        current.AddRange(FlattenCubic(cursor, p1, p2, p3, 10));
                        cursor = p3;
                    }
                    else if (cmd == 'A')
                    {
                        float radius = nums[k];   // rx == ry for every arc this data file authors
                        bool largeArc = nums[k + 3] != 0f;
                        bool sweep = nums[k + 4] != 0f;
                        var end = new Vector2(nums[k + 5], nums[k + 6]);
                        current.AddRange(FlattenArc(cursor, end, radius, largeArc, sweep, 12));
                        cursor = end;
                    }
                }
            }
            if (current != null) subpaths.Add(current);
            return subpaths;
        }

        private static List<Vector2> FlattenCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int segments)
        {
            var pts = new List<Vector2>(segments);
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments, u = 1f - t;
                pts.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
            }
            return pts;
        }

        /// <summary>Endpoint-to-centre circular-arc flattening (SVG's <c>A</c> command, restricted to
        /// <c>rx == ry</c> and no axis rotation — every arc in <c>rig_board.json</c>'s icon set).</summary>
        private static List<Vector2> FlattenArc(Vector2 p0, Vector2 p1, float r, bool largeArc, bool sweep, int segments)
        {
            float dx = (p0.x - p1.x) * 0.5f, dy = (p0.y - p1.y) * 0.5f;
            float radius = r;
            float lambda = (dx * dx + dy * dy) / (radius * radius);
            if (lambda > 1f) radius *= Mathf.Sqrt(lambda);

            float sign = largeArc != sweep ? 1f : -1f;
            float r2 = radius * radius;
            float num = r2 * r2 - r2 * dy * dy - r2 * dx * dx;
            float den = r2 * dy * dy + r2 * dx * dx;
            float co = den > 1e-6f ? sign * Mathf.Sqrt(Mathf.Max(0f, num / den)) : 0f;

            Vector2 mid = (p0 + p1) * 0.5f;
            var center = new Vector2(mid.x + co * dy, mid.y - co * dx);

            float startAngle = Mathf.Atan2(p0.y - center.y, p0.x - center.x);
            float endAngle = Mathf.Atan2(p1.y - center.y, p1.x - center.x);
            float delta = endAngle - startAngle;
            if (sweep && delta < 0f) delta += 2f * Mathf.PI;
            if (!sweep && delta > 0f) delta -= 2f * Mathf.PI;

            var pts = new List<Vector2>(segments);
            for (int i = 1; i <= segments; i++)
            {
                float ang = startAngle + delta * i / segments;
                pts.Add(new Vector2(center.x + radius * Mathf.Cos(ang), center.y + radius * Mathf.Sin(ang)));
            }
            return pts;
        }

        private static bool PointInPolygon(Vector2 p, List<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 pi = poly[i], pj = poly[j];
                if ((pi.y > p.y) != (pj.y > p.y) &&
                    p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y) + pi.x)
                    inside = !inside;
            }
            return inside;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float sq = ab.sqrMagnitude;
            float t = sq > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / sq) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        // --- helpers ---

        private static float RoundedAlpha(int x, int y, int w, int h, float radius)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float cx = Mathf.Clamp(px, radius, w - radius);
            float cy = Mathf.Clamp(py, radius, h - radius);
            float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            return Mathf.Clamp01(radius - d + 0.5f);
        }

        private static Texture2D NewTex(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            return tex;
        }

        private static void Fill(Texture2D tex, Color c)
        {
            var px = new Color32[tex.width * tex.height];
            var c32 = (Color32)c;
            for (int i = 0; i < px.Length; i++) px[i] = c32;
            tex.SetPixels32(px); tex.Apply();
        }

        private static Sprite Cache(string key, Texture2D tex, float ppu)
        {
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), ppu);
            sprite.name = key;
            s_cache[key] = sprite;
            return sprite;
        }
    }
}
