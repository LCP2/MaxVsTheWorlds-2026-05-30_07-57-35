using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// World 3's City kit (MV-1175, Phase 1 Slice A) — the reusable buildable pieces Market Street
    /// (MV-1177) and the climb corridor dress with. Nothing here is placed yet: every builder takes a
    /// parent <see cref="Transform"/> plus world-space positions/sizes, never a map or config, so a
    /// later ticket's own layout data is the only thing that decides where a piece lands.
    ///
    /// Same contract every kit in this namespace keeps (see <see cref="StormdrainKit"/>'s own header):
    /// nothing built here carries a collider — <see cref="StormdrainKit.Strip"/> strips every piece,
    /// and colliders come from the map's own cover entries, never from dressing. Every builder also
    /// marks its own root with <see cref="KeepsOwnMaterial"/>, so neither the per-world material sweep
    /// nor the runtime re-dress pass ever repaints it (see that component's own doc).
    ///
    /// RELAY COLOUR GUARD. The relay a robot defends glows red/amber/cyan (<see cref="IsReservedRelayColour"/>),
    /// and nothing else in a City scene may borrow that read — a neon sign, a lamp or a car painted into
    /// the same band would make the one thing that matters at a glance stop being unique. Every unlit,
    /// emissive or additive material this kit builds is checked against that guard by this ticket's own
    /// EditMode test; lit (shaded) materials are unrestricted, because a shaded surface's apparent colour
    /// shifts with the scene light and was never the thing the guard is protecting.
    ///
    /// <c>Rect</c> parameters below are world XZ footprints: <c>Rect.x</c>/<c>Rect.y</c> are the area's
    /// world X/Z minimum corner, <c>width</c>/<c>height</c> its X/Z extents — the same "a Rect is a flat
    /// footprint in the ground plane" convention a floor-paint builder needs, there being no 3D
    /// equivalent in the engine.
    ///
    /// Every line/segment builder below assumes an axis-aligned run in XZ (the same assumption
    /// <see cref="SizeOrientation.Oriented"/> already bakes in for <see cref="StormdrainKit.DressWallFace"/> —
    /// a City street grid is axis-aligned by construction, same as a map's own wall faces.
    /// </summary>
    public static class CityKit
    {
        // ---------------------------------------------------------------- palette

        // MV-1205: lifted from Rgb(52, 56, 72) (mean ~0.235 linear) — Lee's TestFlight shot (17 Oct)
        // read the road as "nondescript... I don't feel like I'm standing on anything" at that value,
        // and the ticket's own number is "about 0.32 linear".
        public static readonly Color Asphalt = new Color(0.28f, 0.32f, 0.36f);

        // MV-1205: "lighter than the road, about 0.45 linear" (was Rgb(112, 118, 134), mean ~0.476 —
        // already close; nudged to land exactly on the ticket's number on the same blue-grey hue).
        public static readonly Color Pavement = new Color(0.41f, 0.44f, 0.50f);

        // MV-1205: "15% lighter than the pavement" — derived, not authored, so the relationship the
        // ticket asks for can never drift out of step with Pavement.
        public static readonly Color Kerb = Lighten(Pavement, 1.15f);
        public static readonly Color Line = Rgb(232, 234, 240);
        public static readonly Color Joint = Rgb(92, 98, 114);
        public static readonly Color WindowWarm = Rgb(255, 214, 150);
        public static readonly Color LampHead = Rgb(255, 222, 160);
        public static readonly Color PoolWarm = Rgb(255, 200, 130);
        public static readonly Color PoolCool = Rgb(215, 225, 255);
        public static readonly Color NeonPink = Rgb(255, 79, 216);
        public static readonly Color NeonWhite = Rgb(255, 255, 255);
        public static readonly Color Cream = Rgb(232, 222, 196);
        public static readonly Color Navy = Rgb(44, 58, 118);
        public static readonly Color Slate = Rgb(92, 100, 122);
        public static readonly Color SlateLight = Rgb(128, 136, 158);
        public static readonly Color Mustard = Rgb(214, 172, 56);
        public static readonly Color Olive = Rgb(112, 124, 62);
        public static readonly Color Plum = Rgb(118, 64, 108);
        public static readonly Color Pale = Rgb(204, 208, 218);
        public static readonly Color Pole = Rgb(34, 36, 46);
        public static readonly Color Wood = Rgb(160, 120, 80);
        public static readonly Color WoodDark = Rgb(118, 88, 60);
        public static readonly Color Concrete = Rgb(176, 180, 190);
        public static readonly Color BarrierStripe = Rgb(52, 60, 96);
        public static readonly Color Glass = Rgb(22, 28, 44);
        public static readonly Color Tyre = Rgb(14, 14, 18);

        /// <summary>The three brick tones a <see cref="BuildBuildingEnd"/> call site picks from.</summary>
        public static readonly Color[] Bricks =
        {
            Rgb(168, 104, 84), Rgb(142, 92, 96), Rgb(156, 112, 86),
        };

        /// <summary>The six shop-front tones <see cref="BuildShopfrontRun"/> cycles through, one per
        /// unit.</summary>
        public static readonly Color[] ShopTones =
        {
            Rgb(124, 138, 190), Rgb(196, 176, 148), Rgb(150, 118, 150),
            Rgb(110, 126, 170), Rgb(150, 160, 132), Rgb(188, 150, 128),
        };

        private static readonly Color DeckBoard = Rgb(126, 108, 100);
        private static readonly Color DeckJoint = Rgb(90, 76, 72);
        private static readonly Color PadTone = Rgb(196, 190, 172);
        private static readonly Color CutawaySlab = Rgb(34, 37, 50);
        private static readonly Color BinBody = Rgb(70, 92, 84);
        private static readonly Color TubBody = Rgb(150, 110, 90);

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        // ---------------------------------------------------------------- relay colour guard

        /// <summary>True for a saturated, bright colour in the relay's own red/amber/cyan hue bands —
        /// see this class's own header. Only ever meaningful against an unlit/emissive/additive
        /// material; a lit one is unrestricted.</summary>
        public static bool IsReservedRelayColour(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.6f || v < 0.6f) return false;

            float deg = h * 360f;
            bool red = deg >= 340f || deg <= 20f;
            bool amber = deg >= 25f && deg <= 55f;
            bool cyan = deg >= 170f && deg <= 205f;
            return red || amber || cyan;
        }

        // ---------------------------------------------------------------- internals: shared helpers

        private const float PaintThickness = 0.006f;
        private const float PaintY = 0.016f;

        private static Transform NewRoot(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<KeepsOwnMaterial>();
            return go.transform;
        }

        private static Color Lighten(Color c, float factor) =>
            new Color(Mathf.Clamp01(c.r * factor), Mathf.Clamp01(c.g * factor), Mathf.Clamp01(c.b * factor), c.a);

        private static Color Blend(Color a, Color b, float t) => Color.Lerp(a, b, t);

        private static Quaternion FacingInward(Vector3 n) => Quaternion.LookRotation(-n, Vector3.up);

        private static readonly Quaternion FaceUp = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        /// <summary>An axis-aligned line segment from <paramref name="a"/> to <paramref name="b"/>, lying
        /// flat at height <paramref name="y"/>. Mirrors <see cref="StormdrainKit.DressWallFace"/>'s own
        /// axis-aligned assumption via <see cref="SizeOrientation.Oriented"/>.</summary>
        private static GameObject SegmentLine(Transform parent, string name, Vector2 a, Vector2 b,
                                              float width, float height, float y, Color tone)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            Vector2 dir = length > 0.0001f ? d / length : Vector2.right;
            Vector3 mid = new Vector3((a.x + b.x) * 0.5f, y, (a.y + b.y) * 0.5f);
            Vector3 size = new Vector3(length, height, width).Oriented(dir);
            return StormdrainKit.Box(parent, name, mid, size, tone);
        }

        private static void AppendDashed(Transform parent, Vector2 a, Vector2 b, float dash, float gap, float width)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.001f) return;
            Vector2 dir = d / length;
            float pitch = dash + gap;
            int count = Mathf.Max(1, Mathf.FloorToInt((length + gap) / pitch));
            for (int i = 0; i < count; i++)
            {
                float start = i * pitch;
                float end = Mathf.Min(start + dash, length);
                if (end <= start) continue;
                SegmentLine(parent, $"Dash{i}", a + dir * start, a + dir * end, width, PaintThickness, PaintY, Line);
            }
        }

        /// <summary>A collider-free box with an arbitrary rotation — <see cref="StormdrainKit.Box"/>'s
        /// own primitive, duplicated here only because that one has no rotation parameter (every existing
        /// call site is axis-aligned via <see cref="SizeOrientation.Oriented"/>; a diagonal hatch bar or a
        /// lane arrow head is not).</summary>
        private static GameObject RotatedBox(Transform parent, string name, Vector3 localPos, Vector3 size,
                                             Quaternion rot, Color tone, SurfaceKind kind = SurfaceKind.Stone)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.transform.localScale = Vector3.one;
            go.GetComponent<MeshFilter>().sharedMesh = CharacterMeshes.Bevelled(size, CharacterMeshes.DefaultBevel(size));
            StormdrainKit.Strip(go);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = MaterialLibrary.Tinted(kind, tone);
            return go;
        }

        /// <summary>A solid prop's own compliance body: sized to 96%/94% of <paramref name="colliderSize"/>
        /// on X/Z and Y respectively, so the union of a prop's renderers always lands inside the AC1
        /// fit window (at least 90%, at most size + 0.15 m, at least 90% tall) regardless of whatever
        /// cosmetic detail the specific prop adds on top.</summary>
        private static GameObject SolidBody(Transform root, string name, Vector3 centre, Vector3 colliderSize,
                                            Color tone, SurfaceKind kind = SurfaceKind.Prop)
        {
            Vector3 size = new Vector3(colliderSize.x * 0.96f, colliderSize.y * 0.94f, colliderSize.z * 0.96f);
            return StormdrainKit.Box(root, name, centre + Vector3.up * (size.y * 0.5f), size, tone, kind);
        }

        private static float Frac(float v) => Mathf.Abs(v - Mathf.Floor(v));

        // ---------------------------------------------------------------- floor paint

        /// <summary>MV-1205: a single slab wearing the Pavement surface kind's own baked texture —
        /// 1.0 x 0.6 m running-bond slabs with 3 cm darker joints (see
        /// <see cref="StylizedTextures.PavingSlabHeight"/>) — replacing the old hand-painted joint
        /// lines every 2 m, which drew a coarser, non-running-bond grid the texture now owns.</summary>
        public static GameObject BuildPavement(Transform parent, Rect area)
        {
            Transform root = NewRoot(parent, "Pavement");
            StormdrainKit.Box(root, "Slab", new Vector3(area.center.x, 0.006f, area.center.y),
                new Vector3(area.width, 0.012f, area.height), Pavement, SurfaceKind.Pavement);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildKerbLine(Transform parent, Vector2 a, Vector2 b)
        {
            Transform root = NewRoot(parent, "Kerb Line");
            SegmentLine(root, "Kerb", a, b, 0.26f, 0.02f, 0.01f, Kerb);
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildEdgeLine(Transform parent, Vector2 a, Vector2 b)
        {
            Transform root = NewRoot(parent, "Edge Line");
            SegmentLine(root, "Edge", a, b, 0.12f, PaintThickness, PaintY, Line);
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildDashedLine(Transform parent, Vector2 a, Vector2 b,
                                                 float dash = 2f, float gap = 2f, float width = 0.16f)
        {
            Transform root = NewRoot(parent, "Dashed Line");
            AppendDashed(root, a, b, dash, gap, width);
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Crossing bars, full rect width, 1.0 m pitch along the rect's long axis.</summary>
        public static GameObject BuildZebra(Transform parent, Rect area)
        {
            Transform root = NewRoot(parent, "Zebra");
            bool alongX = area.width >= area.height;
            float longExtent = alongX ? area.width : area.height;
            const float barDepth = 0.5f, pitch = 1.0f;
            int count = Mathf.Max(1, Mathf.FloorToInt(longExtent / pitch));

            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) * pitch;
                if (t > longExtent) break;
                Vector3 pos = alongX
                    ? new Vector3(area.xMin + t, PaintY, area.center.y)
                    : new Vector3(area.center.x, PaintY, area.yMin + t);
                Vector3 size = alongX
                    ? new Vector3(barDepth, PaintThickness, area.height)
                    : new Vector3(area.width, PaintThickness, barDepth);
                StormdrainKit.Box(root, $"Bar{i}", pos, size, Line);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>A hatched keep-clear box: a solid outline plus diagonal bars at a faint tone
        /// approximating the ticket's own "42% alpha" over asphalt (opaque greybox geometry has no real
        /// alpha channel to spend, so the overlap is baked in as a colour blend instead).</summary>
        public static GameObject BuildHatchBox(Transform parent, Rect area)
        {
            Transform root = NewRoot(parent, "Hatch Box");
            Color faint = Blend(Asphalt, Line, 0.42f);

            SegmentLine(root, "Outline N", new Vector2(area.xMin, area.yMax), new Vector2(area.xMax, area.yMax), 0.18f, PaintThickness, PaintY, Line);
            SegmentLine(root, "Outline S", new Vector2(area.xMin, area.yMin), new Vector2(area.xMax, area.yMin), 0.18f, PaintThickness, PaintY, Line);
            SegmentLine(root, "Outline E", new Vector2(area.xMax, area.yMin), new Vector2(area.xMax, area.yMax), 0.18f, PaintThickness, PaintY, Line);
            SegmentLine(root, "Outline W", new Vector2(area.xMin, area.yMin), new Vector2(area.xMin, area.yMax), 0.18f, PaintThickness, PaintY, Line);

            float diag = Mathf.Sqrt(area.width * area.width + area.height * area.height);
            int bars = Mathf.Max(1, Mathf.FloorToInt(diag / 1.25f));
            Quaternion rot = Quaternion.Euler(0f, 45f, 0f);
            for (int i = 0; i < bars; i++)
            {
                float t = (i + 0.5f) / bars;
                Vector2 at = Vector2.Lerp(new Vector2(area.xMin, area.yMin), new Vector2(area.xMax, area.yMax), t);
                RotatedBox(root, $"Hatch{i}", new Vector3(at.x, PaintY, at.y), new Vector3(diag, PaintThickness, 0.2f), rot, faint);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildBayOutline(Transform parent, Rect area)
        {
            Transform root = NewRoot(parent, "Bay Outline");
            Vector2 a = new Vector2(area.xMin, area.yMin), b = new Vector2(area.xMax, area.yMin);
            Vector2 c = new Vector2(area.xMax, area.yMax), d = new Vector2(area.xMin, area.yMax);
            AppendDashed(root, a, b, 0.6f, 0.4f, 0.12f);
            AppendDashed(root, b, c, 0.6f, 0.4f, 0.12f);
            AppendDashed(root, c, d, 0.6f, 0.4f, 0.12f);
            AppendDashed(root, d, a, 0.6f, 0.4f, 0.12f);
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildManhole(Transform parent, Vector3 centre, float diameter = 1.3f)
        {
            Transform root = NewRoot(parent, "Manhole");
            var go = new GameObject("Cover");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(centre.x, PaintY, centre.z);
            go.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Lathe(
                new[] { new Vector2(0f, 0f), new Vector2(diameter * 0.5f, 0f) }, 24);
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, Joint);
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildDrainGrate(Transform parent, Rect area)
        {
            Transform root = NewRoot(parent, "Drain Grate");
            Vector3 center = new Vector3(area.center.x, PaintY, area.center.y);
            StormdrainKit.Box(root, "Frame", center, new Vector3(area.width, PaintThickness, area.height), Joint, SurfaceKind.Metal);

            int slots = Mathf.Max(1, Mathf.FloorToInt(area.width / 0.15f));
            for (int i = 0; i < slots; i++)
            {
                float t = slots <= 1 ? 0.5f : (float)i / (slots - 1);
                float x = Mathf.Lerp(area.xMin + 0.05f, area.xMax - 0.05f, t);
                StormdrainKit.Box(root, $"Slot{i}", new Vector3(x, PaintY + 0.003f, area.center.y),
                    new Vector3(0.03f, 0.002f, Mathf.Max(0.05f, area.height - 0.1f)), Pole, SurfaceKind.Metal);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildPuddle(Transform parent, Vector3 centre, Vector2 size)
        {
            Transform root = NewRoot(parent, "Puddle");
            StormdrainKit.Box(root, "Puddle", centre + Vector3.up * PaintY,
                new Vector3(size.x, PaintThickness, size.y), Blend(Asphalt, PoolCool, 0.35f));
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildLaneArrow(Transform parent, Vector3 centre, Vector2 dir)
        {
            Transform root = NewRoot(parent, "Lane Arrow");
            Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up;
            Quaternion rot = Quaternion.LookRotation(new Vector3(d.x, 0f, d.y), Vector3.up);
            Vector3 pos = centre + Vector3.up * PaintY;
            const float length = 2.4f;

            RotatedBox(root, "Shaft", pos, new Vector3(0.18f, PaintThickness, length * 0.65f), rot, Line);
            Vector3 tip = pos + new Vector3(d.x, 0f, d.y) * (length * 0.25f);
            RotatedBox(root, "Head A", tip, new Vector3(0.18f, PaintThickness, 0.6f), rot * Quaternion.Euler(0f, 35f, 0f), Line);
            RotatedBox(root, "Head B", tip, new Vector3(0.18f, PaintThickness, 0.6f), rot * Quaternion.Euler(0f, -35f, 0f), Line);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildDeck(Transform parent, Rect area)
        {
            Transform root = NewRoot(parent, "Deck");
            StormdrainKit.Box(root, "Joint Base", new Vector3(area.center.x, 0f, area.center.y),
                new Vector3(area.width, 0.015f, area.height), DeckJoint, SurfaceKind.Wood);

            const int boards = 10;
            bool alongX = area.width >= area.height;
            float longExtent = alongX ? area.width : area.height;
            float boardSpan = longExtent / boards;
            for (int i = 0; i < boards; i++)
            {
                float centerOffset = i * boardSpan + boardSpan * 0.5f;
                Vector3 pos = alongX
                    ? new Vector3(area.xMin + centerOffset, 0.02f, area.center.y)
                    : new Vector3(area.center.x, 0.02f, area.yMin + centerOffset);
                Vector3 size = alongX
                    ? new Vector3(boardSpan - 0.02f, 0.04f, area.height)
                    : new Vector3(area.width, 0.04f, boardSpan - 0.02f);
                StormdrainKit.Box(root, $"Board{i}", pos, size, DeckBoard, SurfaceKind.Wood);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildPad(Transform parent, Rect area)
        {
            Transform root = NewRoot(parent, "Pad");
            StormdrainKit.Box(root, "Slab", new Vector3(area.center.x, 0.008f, area.center.y),
                new Vector3(area.width, 0.016f, area.height), PadTone);

            int cols = Mathf.Max(1, Mathf.FloorToInt(area.width / 0.3f));
            int rows = Mathf.Max(1, Mathf.FloorToInt(area.height / 0.3f));
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float tx = cols <= 1 ? 0.5f : (float)c / (cols - 1);
                float tz = rows <= 1 ? 0.5f : (float)r / (rows - 1);
                float x = Mathf.Lerp(area.xMin + 0.15f, area.xMax - 0.15f, tx);
                float z = Mathf.Lerp(area.yMin + 0.15f, area.yMax - 0.15f, tz);

                var stud = new GameObject($"Stud{r}_{c}");
                stud.transform.SetParent(root, false);
                stud.transform.localPosition = new Vector3(x, 0.02f, z);
                stud.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Lathe(
                    new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0f), new Vector2(0.025f, 0.01f) }, 8);
                var rend = stud.AddComponent<MeshRenderer>();
                rend.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Stone, Lighten(PadTone, 0.85f));
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        // ---------------------------------------------------------------- floor pool

        /// <summary>A public additive floor disc named "Pool" — the fitting-less light pool a lamp or a
        /// shop window throws, built through <see cref="StormdrainLightKit.AdditiveUnlit"/> exactly as
        /// that kit's own floor pools are.</summary>
        public static GameObject BuildPool(Transform parent, Vector3 groundCentre, float rx, float rz, Color tone, float strength)
        {
            var go = new GameObject("Pool");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = groundCentre + Vector3.up * 0.03f;
            go.transform.localScale = new Vector3(Mathf.Max(0.001f, rx), 1f, Mathf.Max(0.001f, rz));
            go.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Lathe(
                new[] { new Vector2(0f, 0f), new Vector2(1f, 0f) }, 24);
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = StormdrainLightKit.AdditiveUnlit(tone * strength, "CityPool");
            go.AddComponent<KeepsOwnMaterial>();
            StormdrainKit.Strip(go);
            return go;
        }

        // ---------------------------------------------------------------- building faces

        /// <summary>A run of shop units — <c>round(length / 7)</c> of them — each a facade, four warm
        /// windows, a striped awning, a pink/white neon sign and a floor pool, plus one roof block
        /// spanning the whole run.</summary>
        public static GameObject BuildShopfrontRun(Transform parent, Vector2 a, Vector2 b, Vector3 inward,
                                                    float wallHeight, int seed)
        {
            Transform root = NewRoot(parent, "Shopfront Run");
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) { StormdrainKit.Strip(root.gameObject); return root.gameObject; }

            Vector2 dir = d / length;
            Vector3 n = new Vector3(inward.x, 0f, inward.z);
            n = n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.forward;
            Vector3 along = new Vector3(dir.x, 0f, dir.y);
            float h = Mathf.Max(0.1f, wallHeight);

            int units = Mathf.Max(1, Mathf.RoundToInt(length / 7f));
            float unitLength = length / units;

            for (int i = 0; i < units; i++)
            {
                float t = (i + 0.5f) / units;
                Vector2 at2 = Vector2.Lerp(a, b, t);
                Vector3 mid = new Vector3(at2.x, 0f, at2.y);
                Color shopTone = ShopTones[(i + seed) % ShopTones.Length];
                Color facadeTone = Lighten(shopTone, 1.9f);

                Transform unit = NewRoot(root, $"Shop Unit {i}");

                StormdrainKit.Box(unit, "Facade", mid + Vector3.up * (h * 0.5f),
                    new Vector3(unitLength, h, 0.15f).Oriented(dir), facadeTone);

                float winH = h * 0.43f;
                float winY = h * 0.55f;
                for (int w = 0; w < 4; w++)
                {
                    float wt = (w + 0.5f) / 4f - 0.5f;
                    Vector3 winPos = mid + along * (wt * unitLength * 0.82f) + n * 0.08f + Vector3.up * winY;
                    StormdrainKit.Glow(unit, $"Window{w}", winPos,
                        new Vector3(unitLength * 0.16f, winH, 1f), FacingInward(n), WindowWarm);
                }

                Color[] awningStripes = { Cream, Navy, Plum, Olive };
                float awningY = h * 0.72f, awningDepth = 0.4f;
                for (int s = 0; s < awningStripes.Length; s++)
                {
                    float st = (s + 0.5f) / awningStripes.Length;
                    Vector3 stripePos = mid + n * (awningDepth * st) + Vector3.up * awningY;
                    StormdrainKit.Box(unit, $"Awning{s}", stripePos,
                        new Vector3(unitLength * 0.96f, 0.05f, awningDepth / awningStripes.Length).Oriented(dir), awningStripes[s]);
                }

                Color neon = (i % 2 == 0) ? NeonPink : NeonWhite;
                Vector3 signPos = mid + n * 0.1f + Vector3.up * (h * 0.78f);
                StormdrainKit.Glow(unit, "Neon Sign", signPos, new Vector3(2.0f, 0.25f, 1f), FacingInward(n), neon);

                Vector3 poolCentre = mid + n * 1.4f;
                BuildPool(unit, poolCentre, 1.2f, 0.95f, PoolWarm, 0.15f);
            }

            Vector3 outward = -n;
            Vector3 roofMid = new Vector3((a.x + b.x) * 0.5f, h, (a.y + b.y) * 0.5f) + outward * 3f;
            StormdrainKit.Box(root, "Roof", roofMid, new Vector3(length, 0.3f, 6f).Oriented(dir), Slate);
            for (int v = 0; v < 2; v++)
            {
                float vt = (v + 1f) / 3f;
                Vector2 vAt = Vector2.Lerp(a, b, vt);
                Vector3 ventPos = new Vector3(vAt.x, h + 0.2f, vAt.y) + outward * 3f;
                StormdrainKit.Box(root, $"Roof Vent{v}", ventPos, new Vector3(0.8f, 0.3f, 0.8f), Slate, SurfaceKind.Metal);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>A brick building end: a facade, <paramref name="litWindows"/> warm windows each over
        /// their own floor pool, and a roof block 7 m outward.</summary>
        public static GameObject BuildBuildingEnd(Transform parent, Vector2 a, Vector2 b, Vector3 inward,
                                                   float wallHeight, Color brick, int litWindows)
        {
            Transform root = NewRoot(parent, "Building End");
            Vector2 d = b - a;
            float length = d.magnitude;
            Vector2 dir = length > 0.0001f ? d / length : Vector2.up;
            Vector3 n = new Vector3(inward.x, 0f, inward.z);
            n = n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.forward;
            float h = Mathf.Max(0.1f, wallHeight);

            Vector3 mid = new Vector3((a.x + b.x) * 0.5f, h * 0.5f, (a.y + b.y) * 0.5f);
            StormdrainKit.Box(root, "Brick Facade", mid, new Vector3(length, h, 0.2f).Oriented(dir), brick);

            int count = Mathf.Max(0, litWindows);
            for (int i = 0; i < count; i++)
            {
                float t = (i + 1f) / (count + 1f);
                Vector2 at2 = Vector2.Lerp(a, b, t);
                Vector3 winPos = new Vector3(at2.x, h * 0.55f, at2.y) + n * 0.08f;
                StormdrainKit.Glow(root, $"Window{i}", winPos, new Vector3(1.15f, 1.5f, 1f), FacingInward(n), WindowWarm);

                Vector3 poolCentre = new Vector3(at2.x, 0f, at2.y) + n * 1.2f;
                BuildPool(root, poolCentre, 1.1f, 0.9f, PoolWarm, 0.15f);
            }

            Vector3 outward = -n;
            Vector3 roofMid = new Vector3((a.x + b.x) * 0.5f, h, (a.y + b.y) * 0.5f) + outward * 3.5f;
            StormdrainKit.Box(root, "Roof", roofMid, new Vector3(length, 0.3f, 7f).Oriented(dir), Slate);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildCutaway(Transform parent, Vector2 a, Vector2 b, Vector3 outward)
        {
            Transform root = NewRoot(parent, "Cutaway");
            Vector2 d = b - a;
            float length = d.magnitude;
            Vector2 dir = length > 0.0001f ? d / length : Vector2.up;
            Vector3 o = new Vector3(outward.x, 0f, outward.z);
            o = o.sqrMagnitude > 0.0001f ? o.normalized : Vector3.back;

            Vector3 mid = new Vector3((a.x + b.x) * 0.5f, 0.25f, (a.y + b.y) * 0.5f) + o * 1.7f;
            StormdrainKit.Box(root, "Cutaway", mid, new Vector3(length, 0.5f, 3.4f).Oriented(dir), CutawaySlab);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        // ---------------------------------------------------------------- street lamp

        /// <summary>A post, an arm, an unlit head and a wide floor pool. Posts are the kit's one
        /// exception to the usual 1.6 m cover-fit rule: the art may rise to 4 m above a collider no
        /// wider than 0.3 m, which is why — unlike every piece in Change item 7 — this takes no
        /// <c>colliderSize</c> at all.</summary>
        public static GameObject BuildStreetLamp(Transform parent, Vector3 foot, Vector3 armDir,
                                                  float height = 4f, float arm = 2.2f)
        {
            Transform root = NewRoot(parent, "Street Lamp");
            Vector3 a = armDir.sqrMagnitude > 0.0001f ? new Vector3(armDir.x, 0f, armDir.z).normalized : Vector3.forward;

            StormdrainKit.AddMeshPart(root, "Post", CharacterMeshes.Beam(height, 0.07f, 0.055f),
                foot + Vector3.up * (height * 0.5f), SurfaceKind.Metal, Pole);

            Vector3 armStart = foot + Vector3.up * height;
            var armPart = StormdrainKit.AddMeshPart(root, "Arm", CharacterMeshes.Beam(arm, 0.045f, 0.03f),
                armStart + a * (arm * 0.5f), SurfaceKind.Metal, Pole);
            armPart.transform.localRotation = Quaternion.FromToRotation(Vector3.up, a);

            Vector3 headPos = armStart + a * arm;
            StormdrainKit.Box(root, "Head", headPos, new Vector3(0.4f, 0.14f, 0.62f), LampHead);

            BuildPool(root, new Vector3(headPos.x, 0f, headPos.z), 3.2f, 3.2f, LampHead, 0.26f);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        // ---------------------------------------------------------------- solid props

        public static GameObject BuildCar(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Car");
            Color[] bodyColors = { Navy, Olive, Mustard, Slate };
            int idx = Mathf.FloorToInt(Frac(centre.x * 0.317f + centre.z * 0.871f) * bodyColors.Length) % bodyColors.Length;

            SolidBody(root, "Body", centre, colliderSize, bodyColors[idx]);
            float bodyTopRel = colliderSize.y * 0.94f; // SolidBody's own Y fraction, relative to centre.y

            Vector3 cabinSize = new Vector3(colliderSize.x * 0.6f, colliderSize.y * 0.4f, colliderSize.z * 0.5f);
            StormdrainKit.Box(root, "Cabin", centre + Vector3.up * (bodyTopRel - 0.02f + cabinSize.y * 0.4f), cabinSize, Glass, SurfaceKind.Metal);

            float wheelR = colliderSize.y * 0.14f;
            float xOff = colliderSize.x * 0.32f, zOff = colliderSize.z * 0.42f;
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var wheel = new GameObject($"Tyre{(sx < 0 ? "L" : "R")}{(sz < 0 ? "B" : "F")}");
                wheel.transform.SetParent(root, false);
                wheel.transform.localPosition = centre + new Vector3(sx * xOff, wheelR, sz * zOff);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheel.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Beam(colliderSize.x * 0.12f, wheelR, wheelR, 12);
                var rend = wheel.AddComponent<MeshRenderer>();
                rend.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, Tyre);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildTruck(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Truck");
            float cabSpan = colliderSize.x * 0.28f;
            float boxSpan = colliderSize.x - cabSpan;
            Vector3 cabCentre = centre + new Vector3(-colliderSize.x * 0.5f + cabSpan * 0.5f, 0f, 0f);
            Vector3 boxCentre = centre + new Vector3(colliderSize.x * 0.5f - boxSpan * 0.5f, 0f, 0f);

            StormdrainKit.Box(root, "Cab", cabCentre + Vector3.up * (colliderSize.y * 0.46f),
                new Vector3(cabSpan, colliderSize.y * 0.92f, colliderSize.z * 0.94f), Navy);
            StormdrainKit.Box(root, "Box", boxCentre + Vector3.up * (colliderSize.y * 0.46f),
                new Vector3(boxSpan, colliderSize.y * 0.92f, colliderSize.z), Cream);
            StormdrainKit.Box(root, "Stripe", boxCentre + Vector3.up * (colliderSize.y * 0.36f),
                new Vector3(boxSpan, colliderSize.y * 0.08f, colliderSize.z + 0.01f), Navy);

            StormdrainKit.Glow(root, "Cab Light", cabCentre + new Vector3(-cabSpan * 0.3f, colliderSize.y * 0.55f, 0f),
                new Vector3(0.1f, 0.1f, 1f), FacingInward(Vector3.right), WindowWarm);
            foreach (float sz in new[] { -1f, 1f })
                StormdrainKit.Glow(root, $"Headlight{(sz < 0 ? "L" : "R")}",
                    cabCentre + new Vector3(-cabSpan * 0.48f, colliderSize.y * 0.16f, sz * colliderSize.z * 0.35f),
                    new Vector3(0.12f, 0.12f, 1f), FacingInward(Vector3.left), NeonWhite);

            // The tail-lift deliberately sits OUTSIDE the collider on the floor (ticket, change item 7) —
            // not part of the solid body's own fit, so it carries no collider and is excluded from the
            // AC1 fit check by name, the same "a deliberate new form, not the regression this guards
            // against" exemption MV765KitProportionTests already uses for a different kit.
            Vector3 liftCentre = centre + new Vector3(colliderSize.x * 0.5f + 0.9f, 0.01f, 0f);
            StormdrainKit.Box(root, "Tail Lift", liftCentre, new Vector3(1.8f, 0.02f, 1.0f), Slate);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildBus(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Bus");
            Vector3 bodySize = new Vector3(colliderSize.x * 0.96f, colliderSize.y * 0.92f, colliderSize.z * 0.96f);
            StormdrainKit.Box(root, "Body", centre + Vector3.up * (bodySize.y * 0.5f), bodySize, Olive);
            StormdrainKit.Box(root, "Band", centre + Vector3.up * (bodySize.y * 0.78f),
                new Vector3(bodySize.x, colliderSize.y * 0.1f, bodySize.z + 0.01f), Cream);
            StormdrainKit.Box(root, "Window Band", centre + Vector3.up * (bodySize.y * 0.55f),
                new Vector3(bodySize.x * 0.98f, colliderSize.y * 0.2f, bodySize.z + 0.015f), Glass, SurfaceKind.Metal);

            StormdrainKit.Glow(root, "Route Board", centre + new Vector3(colliderSize.x * 0.2f, bodySize.y + 0.05f, 0f),
                new Vector3(colliderSize.x * 0.3f, colliderSize.z * 0.3f, 1f), FaceUp, NeonWhite);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildConcreteBarrier(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Concrete Barrier");
            SolidBody(root, "Body", centre, colliderSize, Concrete, SurfaceKind.Stone);
            for (int i = 0; i < 3; i++)
            {
                float t = (i + 0.5f) / 3f;
                StormdrainKit.Box(root, $"Stripe{i}", centre + Vector3.up * (colliderSize.y * 0.94f * t),
                    new Vector3(colliderSize.x * 0.92f, colliderSize.y * 0.08f, colliderSize.z * 0.98f), BarrierStripe);
            }
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Two posts, a top rail and a few thin wires — reads see-through rather than a solid
        /// panel (ticket, change item 7), so unlike every other prop here this never uses
        /// <see cref="SolidBody"/>'s opaque filler.</summary>
        public static GameObject BuildWireFence(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Wire Fence");
            float postW = Mathf.Min(colliderSize.z, colliderSize.x) * 0.3f;
            Vector3 postSize = new Vector3(Mathf.Max(postW, colliderSize.x * 0.05f), colliderSize.y * 0.94f, colliderSize.z * 0.94f);
            float halfX = colliderSize.x * 0.46f;
            foreach (float sx in new[] { -1f, 1f })
                StormdrainKit.Box(root, $"Post{(sx < 0 ? "L" : "R")}", centre + new Vector3(sx * halfX, 0f, 0f) + Vector3.up * (postSize.y * 0.5f), postSize, Pole, SurfaceKind.Metal);

            StormdrainKit.Box(root, "Top Rail", centre + Vector3.up * postSize.y,
                new Vector3(colliderSize.x * 0.96f, colliderSize.y * 0.06f, colliderSize.z * 0.6f), Pole, SurfaceKind.Metal);

            const int wireCount = 4;
            for (int i = 0; i < wireCount; i++)
            {
                float t = (i + 1f) / (wireCount + 1f);
                StormdrainKit.Box(root, $"Wire{i}", centre + Vector3.up * (postSize.y * t),
                    new Vector3(colliderSize.x * 0.96f, 0.02f, colliderSize.z * 0.3f), SlateLight, SurfaceKind.Metal);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildBollard(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Bollard");
            // A Beam/Prism polygon's own vertices sit at angle offset pi/sides (CharacterMeshes.Prism),
            // so its axis-aligned bounding box is narrower than its circumradius by cos(pi/sides) — at
            // the default 8 sides that is a ~7.6% shrink, enough on its own to miss AC1's 90% floor.
            // More sides and a radius that compensates for the exact factor keep the built post's own
            // resolved bounds inside the fit window regardless of that approximation.
            const int sides = 16;
            float shrink = Mathf.Cos(Mathf.PI / sides);
            float r = Mathf.Min(colliderSize.x, colliderSize.z) * 0.49f / shrink;
            float h = colliderSize.y * 0.94f;
            StormdrainKit.AddMeshPart(root, "Post", CharacterMeshes.Beam(h, r, r * 0.9f, sides), centre + Vector3.up * (h * 0.5f), SurfaceKind.Metal, Pale);
            StormdrainKit.Glow(root, "Band", centre + Vector3.up * (h * 0.62f), new Vector3(r * 1.8f, h * 0.12f, 1f), FacingInward(Vector3.forward), NeonWhite);
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildBench(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Bench");
            float seatY = colliderSize.y * 0.5f;

            // Legs floor-to-seat — without these the seat/back float above the ground and the union of
            // renderers never reaches y = 0, which is most of AC1's own height-fit window.
            foreach (float sx in new[] { -1f, 1f })
                StormdrainKit.Box(root, $"Leg{(sx < 0 ? "L" : "R")}", centre + new Vector3(sx * colliderSize.x * 0.42f, seatY * 0.5f, 0f),
                    new Vector3(colliderSize.x * 0.08f, seatY, colliderSize.z * 0.9f), WoodDark, SurfaceKind.Wood);

            for (int i = 0; i < 4; i++)
            {
                float t = (i + 0.5f) / 4f - 0.5f;
                StormdrainKit.Box(root, $"Slat{i}", centre + new Vector3(0f, seatY, t * colliderSize.z * 0.96f),
                    new Vector3(colliderSize.x * 0.96f, colliderSize.y * 0.14f, colliderSize.z * 0.22f), Wood, SurfaceKind.Wood);
            }
            StormdrainKit.Box(root, "Back", centre + new Vector3(0f, colliderSize.y * 0.78f, -colliderSize.z * 0.46f),
                new Vector3(colliderSize.x * 0.96f, colliderSize.y * 0.5f, colliderSize.z * 0.14f), Olive, SurfaceKind.Wood);
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildBin(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Bin");
            SolidBody(root, "Body", centre, colliderSize, BinBody);
            StormdrainKit.Box(root, "Lid", centre + Vector3.up * (colliderSize.y * 0.94f),
                new Vector3(colliderSize.x * 0.9f, colliderSize.y * 0.08f, colliderSize.z * 0.9f), Pole, SurfaceKind.Metal);
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildPostBox(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Post Box");
            SolidBody(root, "Body", centre, colliderSize, Navy, SurfaceKind.Metal);
            StormdrainKit.Glow(root, "Slot", centre + Vector3.up * (colliderSize.y * 0.6f) + new Vector3(0f, 0f, colliderSize.z * 0.46f),
                new Vector3(colliderSize.x * 0.5f, colliderSize.y * 0.06f, 1f), FacingInward(Vector3.forward), NeonWhite);
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildTub(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Tub");
            SolidBody(root, "Body", centre, colliderSize, TubBody, SurfaceKind.Stone);
            for (int i = 0; i < 3; i++)
            {
                float t = (i + 0.5f) / 3f - 0.5f;
                StormdrainKit.Box(root, $"Tuft{i}", centre + new Vector3(t * colliderSize.x * 0.5f, colliderSize.y * 0.94f + colliderSize.y * 0.1f, 0f),
                    new Vector3(colliderSize.x * 0.22f, colliderSize.y * 0.2f, colliderSize.z * 0.5f), Olive, SurfaceKind.Foliage);
            }
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        public static GameObject BuildPlanterBox(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Planter Box");
            SolidBody(root, "Body", centre, colliderSize, Slate, SurfaceKind.Stone);
            StormdrainKit.Box(root, "Foot Strip", centre + Vector3.up * (colliderSize.y * 0.04f),
                new Vector3(colliderSize.x * 0.98f, colliderSize.y * 0.06f, colliderSize.z * 0.98f), Pale);
            for (int i = 0; i < 3; i++)
            {
                float t = (i + 0.5f) / 3f - 0.5f;
                StormdrainKit.Box(root, $"Tuft{i}", centre + new Vector3(t * colliderSize.x * 0.6f, colliderSize.y * 0.94f + colliderSize.y * 0.08f, 0f),
                    new Vector3(colliderSize.x * 0.2f, colliderSize.y * 0.16f, colliderSize.z * 0.5f), Olive, SurfaceKind.Foliage);
            }
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Two faces straight up rather than a true pitched A-frame — a pitch tilt couples the
        /// panel's own HEIGHT into its Z footprint (spread grows with <c>panelH * sin(angle)</c>), which
        /// blows past the AC1 fit window for any board taller than it is deep. Keeping both faces upright
        /// means the Z extent is purely <c>2 x offset + thickness</c>, safe for any <c>colliderSize</c> a
        /// caller hands this rather than only the one this ticket's own test happens to try.</summary>
        public static GameObject BuildABoard(Transform parent, Vector3 centre, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "A Board");
            float panelH = colliderSize.y * 0.94f;
            float panelW = colliderSize.x * 0.92f;
            float halfDepth = colliderSize.z * 0.44f;

            StormdrainKit.Box(root, "Face A", centre + new Vector3(0f, panelH * 0.5f, -halfDepth), new Vector3(panelW, panelH, 0.04f), NeonWhite);
            StormdrainKit.Box(root, "Face B", centre + new Vector3(0f, panelH * 0.5f, halfDepth), new Vector3(panelW, panelH, 0.04f), NeonWhite);

            for (int i = 0; i < 2; i++)
            {
                float t = (i + 1f) / 3f;
                StormdrainKit.Box(root, $"Bar A{i}", centre + new Vector3(0f, panelH * t, -halfDepth - 0.005f), new Vector3(panelW * 0.9f, panelH * 0.08f, 0.01f), NeonPink);
                StormdrainKit.Box(root, $"Bar B{i}", centre + new Vector3(0f, panelH * t, halfDepth + 0.005f), new Vector3(panelW * 0.9f, panelH * 0.08f, 0.01f), NeonPink);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }
    }
}
