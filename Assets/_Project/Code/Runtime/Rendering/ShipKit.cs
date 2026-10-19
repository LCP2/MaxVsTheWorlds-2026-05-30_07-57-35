using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Rendering
{
    /// <summary>One of the 8 atlas-baked deck plate looks <see cref="ShipKit.BuildAtlasFloor"/> tiles a
    /// deck floor from (MV-1259). Int value doubles as the atlas cell index: cell = row*4 + col, where
    /// row/col come from <c>(int)variant % 4</c>/<c>(int)variant / 4</c> — see
    /// <see cref="ShipKit.AtlasCellUv"/>.</summary>
    public enum FloorPlateVariant { PlainLight = 0, PlainMid = 1, PlainDark = 2, Vent = 3, Hatch = 4, Tread = 5, RivetRing = 6, FloorLight = 7 }

    /// <summary>MV-1230: spins a conveyor roller slowly around its own long axis (ticket, change item
    /// 2: "rollers animated slowly"). <see cref="ShipKit.BuildConveyor"/> bakes the roller's rotation
    /// so its local X axis already lies along the belt's own run — <see cref="Space.Self"/> spins
    /// around that axis regardless of which way the belt itself is authored to face.</summary>
    [PerfSection("rendering")]
    public sealed class ShipConveyorRoller : MonoBehaviour
    {
        private const float DegreesPerSecond = 40f;
        private void Update() => transform.Rotate(Vector3.right, DegreesPerSecond * Time.deltaTime, Space.Self);
    }

    /// <summary>
    /// World 5's Mothership ship kit (MV-1230) — the robot design formula at ship scale (ticket's own
    /// "Context"): a panel split by ONE hard straight line into a rust-orange half and a teal half,
    /// near-black machinery, small gold lens lamps. Follows <see cref="CityKit"/>'s own contract:
    /// every builder takes a parent <see cref="Transform"/> plus world-space positions/sizes, never a
    /// map or config, so the Hangar Bay layout ticket (MV-1232) decides where a piece lands, not this
    /// one. Nothing here carries a collider — <see cref="StormdrainKit.Strip"/> strips every piece —
    /// colliders come from the map's own cover entries, routed through <see cref="MaxWorlds.Arena.ShipDressing"/>,
    /// same "collider stays, art swaps" contract every kit in this namespace keeps.
    ///
    /// RESERVED COLOUR GUARD. The ticket's own "Reserved colours" list (cradle red, vent orange, Light
    /// Missile violet, Orbitals gold) — see <see cref="IsReservedColour"/>.
    /// </summary>
    public static class ShipKit
    {
        // ---------------------------------------------------------------- palette

        /// <summary>#B5572E family — the ticket's own rust-orange half of every split panel.</summary>
        public static readonly Color RustOrange = Rgb(181, 87, 46);

        /// <summary>#2F8F95 family — the ticket's own teal half of every split panel.</summary>
        public static readonly Color Teal = Rgb(47, 143, 149);

        /// <summary>Near-black machinery — the seam rib between a split panel's two halves and every
        /// bare-metal fitting (conveyor belt body, glass case plinth).</summary>
        public static readonly Color NearBlack = Rgb(18, 18, 22);

        /// <summary>The Mothership's own gold lens tone — reused, not re-authored, from
        /// <c>WorldCatalog</c>'s own Sentinel bolt/eye colour for this world (MV-1225, Lee 17 Oct 2026,
        /// hex #F3D27A), so every gold lamp this kit builds reads as the same metal the world's own
        /// robots already wear. The ticket's own explicit exemption ("except the small gold lamps")
        /// — see <see cref="IsReservedColour"/>'s own doc for why this tone never trips that guard.</summary>
        public static readonly Color GoldLamp = Rgb(243, 210, 122);

        private static readonly Color DeckPlate = Rgb(58, 64, 76);
        private static readonly Color DeckSeam = Rgb(40, 45, 54);
        private static readonly Color HazardYellow = Rgb(224, 182, 48);
        private static readonly Color GlassPane = Rgb(150, 205, 210);

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        // ---------------------------------------------------------------- reserved colour guard

        /// <summary>True for a saturated, bright colour in the cradle-red, vent-orange or Light Missile
        /// violet bands (ticket's own "Reserved colours": "no kit piece may use the cradle red, vent
        /// orange, Light Missile violet or Orbitals gold except the small gold lamps"). Same shape as
        /// <see cref="CityKit.IsReservedRelayColour"/>'s own guard — only ever meaningful against an
        /// unlit/emissive material; a lit one is unrestricted.
        ///
        /// Bands, each centred on an existing named colour already shipped elsewhere in this project:
        /// cradle red ~4&#176; (<c>MaxWorlds.VFX.FactoryBodies.CradleRed</c>, #E2483D, MV-1227's own
        /// "red is reserved for cradles in this world"); vent orange ~27&#176; — now MV-1240's own
        /// confirmed value, <c>MaxWorlds.VFX.PlasmaVentVisual.GlowOrange</c>, #FF8C2E family, the actual
        /// plasma vent this band was reserved for; Light Missile violet ~275&#176; (<c>LightMissileBall.HaloColor</c>,
        /// #B040FF). Orbitals (MV-1236) has shipped no colour yet, so it is not guarded here.
        ///
        /// Every band leaves a clear margin either side of this kit's own <see cref="RustOrange"/>
        /// (~18&#176;) and <see cref="GoldLamp"/> (~44&#176;, and separately exempt on saturation alone
        /// — 0.50 already sits under this guard's own 0.6 floor) so the kit can never trip its own
        /// guard.</summary>
        public static bool IsReservedColour(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.6f || v < 0.6f) return false;

            float deg = h * 360f;
            bool red = deg >= 350f || deg <= 12f;
            bool vent = deg >= 22f && deg <= 34f;
            bool violet = deg >= 260f && deg <= 290f;
            return red || vent || violet;
        }

        // ---------------------------------------------------------------- internals: shared helpers

        private static Transform NewRoot(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<KeepsOwnMaterial>();
            return go.transform;
        }

        private static Quaternion FacingInward(Vector3 n) => Quaternion.LookRotation(-n.normalized, Vector3.up);

        private static void AddGoldLamp(Transform root, string name, Vector3 pos, Vector3 facing)
        {
            StormdrainKit.Glow(root, name, pos, new Vector3(0.22f, 0.14f, 1f), FacingInward(facing), GoldLamp);
        }

        /// <summary>A solid prop's own compliance body: sized to 96%/94% of <paramref name="colliderSize"/>
        /// on X/Z and Y respectively (CityKit's own <c>SolidBody</c> fraction), so the union of a
        /// prop's renderers always lands inside the AC1 fit window (at least 90%, at most size + 0.15 m,
        /// at least 90% tall) regardless of whatever cosmetic detail the specific piece adds on top.</summary>
        private static GameObject SolidBody(Transform root, string name, Vector3 centre, Vector3 colliderSize,
                                            Color tone, SurfaceKind kind = SurfaceKind.Metal)
        {
            Vector3 size = new Vector3(colliderSize.x * 0.96f, colliderSize.y * 0.94f, colliderSize.z * 0.96f);
            return StormdrainKit.Box(root, name, centre + Vector3.up * (size.y * 0.5f), size, tone, kind);
        }

        /// <summary>The robot-design formula at ship scale (ticket's own "Context"): a solid body split
        /// by ONE hard straight horizontal line into a rust-orange lower half and a teal upper half,
        /// with a near-black rib marking the seam. Every solid ShipKit piece below is built from this.</summary>
        private static void BuildSplitBody(Transform root, string name, Vector3 centre, Vector3 colliderSize,
                                           SurfaceKind kind = SurfaceKind.Metal)
        {
            Vector3 size = new Vector3(colliderSize.x * 0.96f, colliderSize.y * 0.94f, colliderSize.z * 0.96f);
            SolidBody(root, name, centre, colliderSize, RustOrange, kind);

            float halfY = size.y * 0.5f;
            StormdrainKit.Box(root, $"{name} Upper", centre + Vector3.up * (halfY + halfY * 0.5f),
                new Vector3(size.x + 0.01f, halfY, size.z + 0.01f), Teal, kind);
            StormdrainKit.Box(root, $"{name} Seam", centre + Vector3.up * halfY,
                new Vector3(size.x + 0.015f, size.y * 0.05f, size.z + 0.015f), NearBlack, kind);
        }

        // ---------------------------------------------------------------- wall / floor dressing

        /// <summary>Bulkhead wall dressing (ticket, change item 2): plating with a hard rust/teal split
        /// line, near-black ribs every ~2 m, a small gold lamp every 6 m. A reusable per-segment
        /// builder, same "axis-aligned run in XZ" assumption <see cref="CityKit.BuildShopfrontRun"/>
        /// already makes — ready for the Hangar Bay layout ticket (MV-1232) to call once real wall
        /// segments exist; this ticket's own one-area placeholder has none worth dressing yet.</summary>
        public static GameObject BuildBulkheadPanel(Transform parent, Vector2 a, Vector2 b, Vector3 inward, float wallHeight)
        {
            Transform root = NewRoot(parent, "Bulkhead Panel");
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) { StormdrainKit.Strip(root.gameObject); return root.gameObject; }

            Vector2 dir = d / length;
            Vector3 n = new Vector3(inward.x, 0f, inward.z);
            n = n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.forward;
            float h = Mathf.Max(0.1f, wallHeight);
            Vector3 mid = new Vector3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);

            float lowerH = h * 0.5f;
            StormdrainKit.Box(root, "Lower Plate", mid + n * 0.06f + Vector3.up * (lowerH * 0.5f),
                new Vector3(length, lowerH, 0.12f).Oriented(dir), RustOrange, SurfaceKind.Metal);
            StormdrainKit.Box(root, "Upper Plate", mid + n * 0.06f + Vector3.up * (lowerH + (h - lowerH) * 0.5f),
                new Vector3(length, h - lowerH, 0.12f).Oriented(dir), Teal, SurfaceKind.Metal);
            StormdrainKit.Box(root, "Seam Rib", mid + n * 0.07f + Vector3.up * lowerH,
                new Vector3(length, h * 0.04f, 0.14f).Oriented(dir), NearBlack, SurfaceKind.Metal);

            int ribCount = Mathf.Max(1, Mathf.FloorToInt(length / 2f));
            for (int i = 0; i <= ribCount; i++)
            {
                float t = (float)i / ribCount;
                Vector2 at = Vector2.Lerp(a, b, t);
                StormdrainKit.Box(root, $"Rib{i}", new Vector3(at.x, 0f, at.y) + n * 0.065f + Vector3.up * (h * 0.5f),
                    new Vector3(0.08f, h * 0.98f, 0.1f).Oriented(dir), NearBlack, SurfaceKind.Metal);
            }

            int lampCount = Mathf.Max(1, Mathf.FloorToInt(length / 6f));
            for (int i = 0; i < lampCount; i++)
            {
                float t = (i + 0.5f) / lampCount;
                Vector2 at = Vector2.Lerp(a, b, t);
                Vector3 lampPos = new Vector3(at.x, h * 0.78f, at.y) + n * 0.09f;
                AddGoldLamp(root, $"Lamp{i}", lampPos, n);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>Deck floor (ticket, change item 2): dark blue-grey plates in 4 m panels with faint
        /// seams, yellow hazard stripes along the north/south edges for launch markings. Flat and
        /// walkable — same reusable, layout-agnostic contract as <see cref="BuildBulkheadPanel"/>.</summary>
        public static GameObject BuildDeckFloor(Transform parent, Rect area)
        {
            Transform root = NewRoot(parent, "Deck Floor");
            StormdrainKit.Box(root, "Slab", new Vector3(area.center.x, 0.006f, area.center.y),
                new Vector3(area.width, 0.012f, area.height), DeckPlate, SurfaceKind.Metal);

            bool alongX = area.width >= area.height;
            float longExtent = alongX ? area.width : area.height;
            int seams = Mathf.Max(0, Mathf.FloorToInt(longExtent / 4f));
            for (int i = 1; i <= seams; i++)
            {
                float t = i * 4f;
                if (t >= longExtent) break;
                Vector3 pos = alongX
                    ? new Vector3(area.xMin + t, 0.014f, area.center.y)
                    : new Vector3(area.center.x, 0.014f, area.yMin + t);
                Vector3 size = alongX
                    ? new Vector3(0.03f, 0.004f, area.height)
                    : new Vector3(area.width, 0.004f, 0.03f);
                StormdrainKit.Box(root, $"Seam{i}", pos, size, DeckSeam, SurfaceKind.Metal);
            }

            const float stripe = 0.3f;
            StormdrainKit.Box(root, "Hazard N", new Vector3(area.center.x, 0.016f, area.yMax - stripe * 0.5f),
                new Vector3(area.width, 0.006f, stripe), HazardYellow, SurfaceKind.Metal);
            StormdrainKit.Box(root, "Hazard S", new Vector3(area.center.x, 0.016f, area.yMin + stripe * 0.5f),
                new Vector3(area.width, 0.006f, stripe), HazardYellow, SurfaceKind.Metal);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        // ---------------------------------------------------------------- atlas floor (MV-1259)

        private const float PlateSize = 2f;
        private const int AtlasCols = 4, AtlasRows = 2, CellPx = 128;

        // Y-stack, all below ShipDressing.BuildLaunchPad's own hazard slab (0 to 0.012): the generated
        // floor must read as the base layer everywhere EXCEPT the Hangar Bay's launch-pad patch, where
        // the hazard slab already built on top of it hides it without either one needing to know about
        // the other.
        private const float FloorY = 0.001f, DecalY = 0.0016f, SeamY = 0.0022f, GlowY = 0.0028f;

        // MV-1259 fix-pass 1 (verifier subagent, capture read): the first-pass tones read as a near-
        // uniform dark grey under the Mothership's own already-shipped, deliberately-dim per-deck
        // lighting (MV-1249, QA Passed — not this ticket's to retune) — a real EditMode pass off the
        // resolved mesh still proves 8 distinct variants exist, but a human eye couldn't tell plain
        // from vent from tread apart in a fresh MV-1249 capture. Widened the light/dark spread here
        // (never touching MV-1249's own light levels) so the SAME lighting reads the variety instead
        // of flattening it.
        private static readonly Color PlainLightTone = Rgb(175, 180, 190);
        private static readonly Color PlainMidTone = Rgb(120, 126, 138);
        private static readonly Color PlainDarkTone = Rgb(55, 58, 68);
        private static readonly Color VentSlatTone = Rgb(32, 34, 40);
        private static readonly Color TreadBumpTone = Rgb(44, 47, 54);

        private static readonly Quaternion FaceUp = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        private static Texture2D s_floorAlbedo, s_floorEmission;
        private static Material s_floorMaterial, s_decalMaterial;

        /// <summary>The ticket's own per-area deck floor (MV-1259): one combined mesh of 2 m atlas-plate
        /// quads (<see cref="BuildPlateMesh"/>), dark seams with a thin deck-coloured emissive line every
        /// 8 m (<see cref="BuildSeams"/>), and one combined, sparse wear-decal mesh
        /// (<see cref="BuildWearDecalMesh"/>). <paramref name="areaIndex"/> seeds every random pick —
        /// stable between runs, never <see cref="UnityEngine.Random"/> — and <paramref name="seamGlow"/>
        /// is the caller's already-resolved deck accent colour (<c>MothershipDecks.LookFor(areaIndex).RimColor</c>,
        /// an Arena/Gameplay-assembly lookup this Rendering-assembly kit cannot make itself — see this
        /// file's own header on why every builder here takes plain data, never a map or config). No
        /// collider anywhere: purely a visual overlay on top of the zone's own real, already-walkable
        /// floor.</summary>
        public static GameObject BuildAtlasFloor(Transform parent, Rect area, int areaIndex, Color seamGlow)
        {
            Transform root = NewRoot(parent, $"Atlas Floor A{areaIndex}");

            var plates = new GameObject("Plates") { isStatic = true };
            plates.transform.SetParent(root, false);
            plates.AddComponent<MeshFilter>().sharedMesh = BuildPlateMesh(area, areaIndex);
            plates.AddComponent<MeshRenderer>().sharedMaterial = FloorMaterial();

            BuildSeams(root, area, seamGlow);

            Mesh wear = BuildWearDecalMesh(area, areaIndex);
            if (wear != null)
            {
                var decals = new GameObject("Wear Decals") { isStatic = true };
                decals.transform.SetParent(root, false);
                decals.AddComponent<MeshFilter>().sharedMesh = wear;
                decals.AddComponent<MeshRenderer>().sharedMaterial = DecalMaterial();
            }

            root.gameObject.isStatic = true;
            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary>One mesh of <see cref="PlateSize"/> quads tiling <paramref name="area"/> exactly (the
        /// last row/column is clipped to the remainder rather than overhanging, so the built mesh's own
        /// bounds always equal the area's footprint to within float error — AC1's "at least 98%"
        /// coverage is 100% by construction). Each quad's UVs pick one of 8 atlas cells from a seeded
        /// weighted hash (ticket: "70% plain, 10% vent, 5% hatch, 6% tread, 3% floor light, 6% rivet"),
        /// never <see cref="UnityEngine.Random"/> — same stable-hash idiom <c>ReefFloorDetail.StableHash</c>
        /// already established for this project, duplicated here rather than shared across the Rendering/
        /// Arena assembly boundary (see <see cref="StableHash"/>'s own doc).</summary>
        private static Mesh BuildPlateMesh(Rect area, int areaIndex)
        {
            int cols = Mathf.Max(1, Mathf.CeilToInt(area.width / PlateSize));
            int rows = Mathf.Max(1, Mathf.CeilToInt(area.height / PlateSize));

            var verts = new List<Vector3>(cols * rows * 4);
            var uvs = new List<Vector2>(cols * rows * 4);
            var normals = new List<Vector3>(cols * rows * 4);
            var tris = new List<int>(cols * rows * 6);

            for (int gz = 0; gz < rows; gz++)
            {
                float z0 = area.yMin + gz * PlateSize;
                float z1 = Mathf.Min(area.yMax, z0 + PlateSize);
                for (int gx = 0; gx < cols; gx++)
                {
                    float x0 = area.xMin + gx * PlateSize;
                    float x1 = Mathf.Min(area.xMax, x0 + PlateSize);

                    Rect uv = AtlasCellUv(PickVariant(areaIndex, gx, gz));

                    int b = verts.Count;
                    verts.Add(new Vector3(x0, FloorY, z0));
                    verts.Add(new Vector3(x1, FloorY, z0));
                    verts.Add(new Vector3(x1, FloorY, z1));
                    verts.Add(new Vector3(x0, FloorY, z1));

                    uvs.Add(new Vector2(uv.xMin, uv.yMin));
                    uvs.Add(new Vector2(uv.xMax, uv.yMin));
                    uvs.Add(new Vector2(uv.xMax, uv.yMax));
                    uvs.Add(new Vector2(uv.xMin, uv.yMax));

                    normals.Add(Vector3.up); normals.Add(Vector3.up); normals.Add(Vector3.up); normals.Add(Vector3.up);

                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                }
            }

            var mesh = new Mesh { name = "ShipFloorPlates" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The weighted pick itself (ticket's own percentages). The 70% "plain" band resolves to
        /// one of the 3 plain shades via a second, independent hash, so a cell's shade never correlates
        /// with its neighbour's band roll.</summary>
        private static FloorPlateVariant PickVariant(int areaIndex, int gx, int gz)
        {
            string key = $"deckplate_{areaIndex}_{gx}_{gz}";
            float h = Hash01(key);

            if (h < 0.70f)
            {
                int shade = Mathf.Clamp((int)(Hash01(key + "_shade") * 3f), 0, 2);
                return (FloorPlateVariant)shade;
            }
            if (h < 0.80f) return FloorPlateVariant.Vent;
            if (h < 0.85f) return FloorPlateVariant.Hatch;
            if (h < 0.91f) return FloorPlateVariant.Tread;
            if (h < 0.94f) return FloorPlateVariant.FloorLight;
            return FloorPlateVariant.RivetRing;
        }

        /// <summary>The atlas-space UV rect for one plate cell, inset 3% of a cell's own size on every
        /// edge so bilinear filtering never bleeds a neighbouring cell's colour across the seam — the
        /// atlas itself bakes no mips (<see cref="NewAtlasTexture"/>) for the same reason.</summary>
        private static Rect AtlasCellUv(FloorPlateVariant variant)
        {
            int cell = (int)variant;
            int cx = cell % AtlasCols, cy = cell / AtlasCols;
            float cellW = 1f / AtlasCols, cellH = 1f / AtlasRows;
            float insetU = cellW * 0.03f, insetV = cellH * 0.03f;
            return Rect.MinMaxRect(cx * cellW + insetU, cy * cellH + insetV,
                                    (cx + 1) * cellW - insetU, (cy + 1) * cellH - insetV);
        }

        /// <summary>Dark seams every 8 m on both axes, each paired with a thin emissive line in the
        /// deck's own accent colour (<paramref name="seamGlow"/>) dimmed to "low intensity" (ticket) by a
        /// flat 0.6x multiplier — the only place a per-deck colour reaches this kit, since every other
        /// ShipKit tone is fixed (see this file's own header). MV-1259 fix-pass 1: raised from an initial
        /// 0.35x, which read as barely-there against a fresh MV-1249 capture — this is still well under
        /// <see cref="GoldLamp"/>'s own full-intensity use, so "low" still holds.</summary>
        private static void BuildSeams(Transform root, Rect area, Color seamGlow)
        {
            const float spacing = 8f, seamWidth = 0.05f, glowWidth = 0.025f;
            Color dim = seamGlow * 0.6f;
            dim.a = 1f;

            for (float x = area.xMin + spacing; x < area.xMax - 0.01f; x += spacing)
            {
                StormdrainKit.Box(root, $"Seam X{x:F0}", new Vector3(x, SeamY, area.center.y),
                    new Vector3(seamWidth, 0.01f, area.height), NearBlack, SurfaceKind.Metal);
                StormdrainKit.Glow(root, $"Seam Glow X{x:F0}", new Vector3(x, GlowY, area.center.y),
                    new Vector3(glowWidth, area.height, 1f), FaceUp, dim);
            }

            for (float z = area.yMin + spacing; z < area.yMax - 0.01f; z += spacing)
            {
                StormdrainKit.Box(root, $"Seam Z{z:F0}", new Vector3(area.center.x, SeamY, z),
                    new Vector3(area.width, 0.01f, seamWidth), NearBlack, SurfaceKind.Metal);
                StormdrainKit.Glow(root, $"Seam Glow Z{z:F0}", new Vector3(area.center.x, GlowY, z),
                    new Vector3(area.width, glowWidth, 1f), FaceUp, dim);
            }
        }

        /// <summary>One combined, sparse wear mesh per area (ticket: "oil stains, scuffs, tread tracks
        /// ... one batched transparent mesh") — a 4 m scan grid, ~35% of cells seeded a decal, jittered
        /// off its own cell centre so the grid itself never reads as a grid. Returns null for an area with
        /// no decals at all (never built smaller than 4 m across) rather than an empty renderer.</summary>
        private static Mesh BuildWearDecalMesh(Rect area, int areaIndex)
        {
            const float scanStep = 4f, keepChance = 0.35f, minSize = 0.4f, maxSize = 1.1f;

            int cols = Mathf.Max(1, Mathf.FloorToInt(area.width / scanStep));
            int rows = Mathf.Max(1, Mathf.FloorToInt(area.height / scanStep));
            float cellW = area.width / cols, cellH = area.height / rows;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int gz = 0; gz < rows; gz++)
            {
                for (int gx = 0; gx < cols; gx++)
                {
                    string key = $"deckdecal_{areaIndex}_{gx}_{gz}";
                    if (Hash01(key) > keepChance) continue;

                    float cx = area.xMin + (gx + 0.5f) * cellW + (Hash01(key + "_jx") - 0.5f) * cellW * 0.6f;
                    float cz = area.yMin + (gz + 0.5f) * cellH + (Hash01(key + "_jz") - 0.5f) * cellH * 0.6f;
                    float yaw = Hash01(key + "_yaw") * 360f;
                    float size = Mathf.Lerp(minSize, maxSize, Hash01(key + "_size"));
                    int kind = Mathf.Clamp((int)(Hash01(key + "_kind") * 3f), 0, 2);

                    AddDecalQuads(verts, uvs, tris, new Vector2(cx, cz), yaw, size, kind);
                }
            }

            if (verts.Count == 0) return null;

            var mesh = new Mesh { name = "ShipFloorWear" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>One of the ticket's own three wear kinds: 0 = oil stain (a square-ish blob), 1 = scuff
        /// (an elongated streak), 2 = tread track (two short parallel bars straddling the centre).</summary>
        private static void AddDecalQuads(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                           Vector2 center, float yawDeg, float size, int kind)
        {
            switch (kind)
            {
                case 0:
                    AddFlatQuad(verts, uvs, tris, center, yawDeg, size, size);
                    break;
                case 1:
                    AddFlatQuad(verts, uvs, tris, center, yawDeg, size * 2.4f, size * 0.35f);
                    break;
                default:
                    float rad = (yawDeg + 90f) * Mathf.Deg2Rad;
                    var side = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (size * 0.3f);
                    AddFlatQuad(verts, uvs, tris, center + side, yawDeg, size * 1.6f, size * 0.18f);
                    AddFlatQuad(verts, uvs, tris, center - side, yawDeg, size * 1.6f, size * 0.18f);
                    break;
            }
        }

        private static void AddFlatQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                         Vector2 center, float yawDeg, float lengthX, float lengthZ)
        {
            float rad = yawDeg * Mathf.Deg2Rad;
            Vector2 fwd = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (lengthX * 0.5f);
            Vector2 side = new Vector2(-fwd.y, fwd.x).normalized * (lengthZ * 0.5f);

            int b = verts.Count;
            verts.Add(new Vector3(center.x - fwd.x - side.x, DecalY, center.y - fwd.y - side.y));
            verts.Add(new Vector3(center.x + fwd.x - side.x, DecalY, center.y + fwd.y - side.y));
            verts.Add(new Vector3(center.x + fwd.x + side.x, DecalY, center.y + fwd.y + side.y));
            verts.Add(new Vector3(center.x - fwd.x + side.x, DecalY, center.y - fwd.y + side.y));

            uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.one); uvs.Add(Vector2.up);

            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }

        /// <summary>The shared atlas material (<see cref="MaterialLibrary.SurfaceShader"/>, lit, so the
        /// floor reads the same per-deck ambient light as everything else) — built and cached once, the
        /// same lazy-static idiom <c>StylizedTextures</c> uses for its own baked textures.</summary>
        private static Material FloorMaterial()
        {
            if (s_floorMaterial != null && s_floorAlbedo != null && s_floorEmission != null) return s_floorMaterial;

            BuildFloorAtlasTextures(out s_floorAlbedo, out s_floorEmission);

            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader) { name = "M_ShipFloorAtlas", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", s_floorAlbedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", s_floorEmission);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.white);
            m.EnableKeyword("_EMISSION");

            s_floorMaterial = m;
            return m;
        }

        /// <summary>An alpha-blended (never additive — a dark stain must darken, not add light) unlit
        /// material, cached once: the ticket's "one batched transparent mesh" needs exactly one material.
        /// Same URP Unlit property-guarded setup as <c>StormdrainKit.AdditiveUnlit</c>, with standard
        /// alpha <c>SrcAlpha</c>/<c>OneMinusSrcAlpha</c> blend constants in place of its additive ones.</summary>
        private static Material DecalMaterial()
        {
            if (s_decalMaterial != null) return s_decalMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null || !shader.isSupported) shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var tone = new Color(0.04f, 0.04f, 0.045f, 0.3f);
            var m = new Material(shader) { name = "M_ShipFloorWear", hideFlags = HideFlags.HideAndDontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tone);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tone);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            s_decalMaterial = m;
            return m;
        }

        /// <summary>Bakes the shared 8-cell atlas (ticket: "ONE shared floor atlas texture... no imported
        /// image files"), 128 px per cell — one 512x256 albedo plus a matching emission mask (black except
        /// the <see cref="FloorPlateVariant.FloorLight"/> cell's own dot). No mips: an atlas's own packed
        /// cells would bleed into each other under minification the moment Unity generates them.</summary>
        private static void BuildFloorAtlasTextures(out Texture2D albedo, out Texture2D emission)
        {
            int w = AtlasCols * CellPx, h = AtlasRows * CellPx;
            var albedoPx = new Color32[w * h];
            var emissionPx = new Color32[w * h];

            for (int cell = 0; cell < AtlasCols * AtlasRows; cell++)
            {
                int ox = (cell % AtlasCols) * CellPx;
                int oy = (cell / AtlasCols) * CellPx;
                DrawPlateCell((FloorPlateVariant)cell, albedoPx, emissionPx, w, ox, oy);
            }

            albedo = NewAtlasTexture("ShipFloorAtlasAlbedo", w, h, albedoPx);
            emission = NewAtlasTexture("ShipFloorAtlasEmission", w, h, emissionPx);
        }

        private static Texture2D NewAtlasTexture(string name, int w, int h, Color32[] px)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            t.SetPixels32(px);
            t.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return t;
        }

        /// <summary>Every variant shares the ticket's own common framing — a bevel highlight on the top
        /// edge, a shadow on the bottom edge, four corner rivets — then layers its own pattern on top.</summary>
        private static void DrawPlateCell(FloorPlateVariant variant, Color32[] albedo, Color32[] emission, int atlasW, int ox, int oy)
        {
            Color baseTone = variant switch
            {
                FloorPlateVariant.PlainLight => PlainLightTone,
                FloorPlateVariant.PlainMid => PlainMidTone,
                FloorPlateVariant.PlainDark => PlainDarkTone,
                FloorPlateVariant.Vent => PlainDarkTone,
                FloorPlateVariant.Tread => PlainMidTone,
                FloorPlateVariant.RivetRing => PlainMidTone,
                FloorPlateVariant.FloorLight => PlainDarkTone,
                _ => PlainMidTone, // Hatch
            };

            for (int ly = 0; ly < CellPx; ly++)
            {
                for (int lx = 0; lx < CellPx; lx++)
                {
                    Color c = baseTone;
                    switch (variant)
                    {
                        case FloorPlateVariant.Vent when (ly / 8) % 2 == 0:
                            c = VentSlatTone;
                            break;
                        case FloorPlateVariant.Hatch when IsCornerMark(lx, ly):
                            c = HazardYellow;
                            break;
                        case FloorPlateVariant.Tread when IsTreadBump(lx, ly):
                            c = TreadBumpTone;
                            break;
                        case FloorPlateVariant.RivetRing when IsRingRivet(lx, ly):
                            c = NearBlack;
                            break;
                    }

                    if (ly >= CellPx - 6) c = Color.Lerp(c, Color.white, 0.22f);       // bevel highlight
                    else if (ly < 6) c = Color.Lerp(c, Color.black, 0.28f);            // bottom shadow
                    if (IsCornerRivet(lx, ly)) c = NearBlack;                          // 4 corner rivets

                    int gi = (oy + ly) * atlasW + (ox + lx);
                    albedo[gi] = c;
                    emission[gi] = new Color(0f, 0f, 0f, 1f);
                }
            }

            if (variant == FloorPlateVariant.FloorLight)
            {
                int ccx = ox + CellPx / 2, ccy = oy + CellPx / 2;
                const int r = 10;
                for (int ly = -r; ly <= r; ly++)
                    for (int lx = -r; lx <= r; lx++)
                        if (lx * lx + ly * ly <= r * r)
                        {
                            int gi = (ccy + ly) * atlasW + (ccx + lx);
                            albedo[gi] = GoldLamp;
                            emission[gi] = GoldLamp;
                        }
            }
        }

        private static bool IsCornerRivet(int lx, int ly) => NearCorner(lx, ly, inset: 14, r: 5);

        private static bool NearCorner(int lx, int ly, int inset, int r)
        {
            int[] c = { inset, CellPx - inset };
            foreach (int cx in c)
                foreach (int cy in c)
                {
                    int dx = lx - cx, dy = ly - cy;
                    if (dx * dx + dy * dy <= r * r) return true;
                }
            return false;
        }

        private static bool IsCornerMark(int lx, int ly)
        {
            const int inset = 8, size = 12;
            int[] starts = { inset, CellPx - inset - size };
            foreach (int cx in starts)
                foreach (int cy in starts)
                    if (lx >= cx && lx < cx + size && ly >= cy && ly < cy + size) return true;
            return false;
        }

        private static bool IsTreadBump(int lx, int ly)
        {
            const int cell = 16;
            int mx = lx % cell - cell / 2, my = ly % cell - cell / 2;
            return Mathf.Abs(mx) + Mathf.Abs(my) < cell / 4;
        }

        private static bool IsRingRivet(int lx, int ly)
        {
            const float ringR = 48f, dotR = 4f;
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                float dx = lx - (CellPx / 2f + Mathf.Cos(a) * ringR);
                float dy = ly - (CellPx / 2f + Mathf.Sin(a) * ringR);
                if (dx * dx + dy * dy <= dotR * dotR) return true;
            }
            return false;
        }

        /// <summary>Stable, cross-platform string hash (FNV-1a) — same algorithm as
        /// <c>ReefFloorDetail.StableHash</c>/<c>ReefKit.StableHash</c>, its own small copy rather than a
        /// cross-assembly dependency for one pure function (<c>MaxWorlds.Rendering.asmdef</c> references
        /// only Core and the render pipeline packages, never the Arena/Gameplay assembly those live in).</summary>
        private static uint StableHash(string s)
        {
            uint hash = 2166136261u;
            if (s != null)
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
            return hash;
        }

        private static float Hash01(string key) => (StableHash(key) % 100000u) / 100000f;

        // ---------------------------------------------------------------- cover pieces

        /// <summary><c>dropship</c> cover (ticket: hull 10 x 4 m at 1.6 m, wings 4 x 1.4 m at 0.8 m).
        /// The wings deliberately stand outside the hull's own Z footprint — the same "a deliberate new
        /// form, not the AC1 fit regression" exemption <see cref="CityKit.BuildTruck"/>'s Tail Lift
        /// already uses, excluded from a fit check by name, not by being inside the collider.</summary>
        public static GameObject BuildDropship(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Dropship");
            BuildSplitBody(root, "Hull", center, colliderSize, SurfaceKind.Metal);

            var wingSize = new Vector3(4f, 0.8f, 1.4f);
            float wingReach = colliderSize.z * 0.5f + wingSize.z * 0.5f - 0.05f;
            foreach (float sz in new[] { -1f, 1f })
                StormdrainKit.Box(root, sz < 0 ? "Wing A" : "Wing B",
                    center + new Vector3(0f, wingSize.y * 0.5f, sz * wingReach), wingSize, Teal, SurfaceKind.Metal);

            AddGoldLamp(root, "Nav Lamp", center + new Vector3(colliderSize.x * 0.46f, colliderSize.y * 0.6f, 0f), Vector3.right);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>drop_pod</c> cover (ticket: 1.4 m square, 1.2 m tall).</summary>
        public static GameObject BuildDropPod(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Drop Pod");
            BuildSplitBody(root, "Body", center, colliderSize, SurfaceKind.Metal);
            AddGoldLamp(root, "Hatch Lamp", center + new Vector3(0f, colliderSize.y * 0.72f, colliderSize.z * 0.46f), Vector3.forward);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>crate</c>/<c>crate_low</c> cover (ticket: full 3 x 3 m at 1.6 m, low at 1.0 m) —
        /// one builder for both: the only difference is the authored <paramref name="colliderSize"/>'s
        /// own height.</summary>
        public static GameObject BuildCrate(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Crate");
            BuildSplitBody(root, "Body", center, colliderSize, SurfaceKind.Metal);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>glass_case</c> cover (ticket: 2.2 m square, 1.6 m, glass — blocks walking, shots
        /// pass, as World 2's pipe barrier — with a plinth inside). <see cref="CoverKind"/> (see-through)
        /// is authored on the map entity, not decided here — this only builds the look.</summary>
        public static GameObject BuildGlassCase(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Glass Case");
            SolidBody(root, "Pane", center, colliderSize, GlassPane, SurfaceKind.Metal);

            var plinthSize = new Vector3(colliderSize.x * 0.5f, colliderSize.y * 0.3f, colliderSize.z * 0.5f);
            StormdrainKit.Box(root, "Plinth", center + Vector3.up * (plinthSize.y * 0.5f), plinthSize, NearBlack, SurfaceKind.Metal);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>conveyor</c> cover (ticket: 0.6 m tall, 3 m deep, blocks walking, shots pass
        /// over, rollers animated slowly) — a near-black belt body carrying a row of rust-orange rollers,
        /// each spun by <see cref="ShipConveyorRoller"/>.</summary>
        public static GameObject BuildConveyor(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Conveyor");
            SolidBody(root, "Belt", center, colliderSize, NearBlack, SurfaceKind.Metal);

            float beltTop = colliderSize.y * 0.94f;
            float rollerR = beltTop * 0.3f;
            int count = Mathf.Max(2, Mathf.FloorToInt(colliderSize.z / 0.5f));
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count - 0.5f;
                Vector3 pos = center + new Vector3(0f, beltTop * 0.5f, t * colliderSize.z * 0.9f);
                GameObject roller = StormdrainKit.Tube(root, $"Roller{i}", pos, rollerR, colliderSize.x * 0.92f,
                    Quaternion.Euler(0f, 0f, 90f), RustOrange);
                roller.AddComponent<ShipConveyorRoller>();
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>console_bank</c> cover (ticket: 1.0 m tall) — a split-panel desk with a row of
        /// gold indicator lamps along its front face.</summary>
        public static GameObject BuildConsoleBank(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Console Bank");
            BuildSplitBody(root, "Body", center, colliderSize, SurfaceKind.Metal);

            int lights = Mathf.Max(1, Mathf.FloorToInt(colliderSize.x / 0.4f));
            for (int i = 0; i < lights; i++)
            {
                float t = (i + 0.5f) / lights - 0.5f;
                Vector3 pos = center + new Vector3(t * colliderSize.x * 0.8f, colliderSize.y * 0.8f, colliderSize.z * 0.46f);
                AddGoldLamp(root, $"Indicator{i}", pos, Vector3.forward);
            }

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }

        /// <summary><c>bulkhead_stub</c> cover (ticket: 1.4 m wide, 1.6 m tall) — a standalone upright
        /// split panel, the free-standing-cover counterpart of <see cref="BuildBulkheadPanel"/>'s own
        /// wall-run dressing.</summary>
        public static GameObject BuildBulkheadStub(Transform parent, Vector3 center, Vector3 colliderSize)
        {
            Transform root = NewRoot(parent, "Bulkhead Stub");
            BuildSplitBody(root, "Panel", center, colliderSize, SurfaceKind.Metal);
            AddGoldLamp(root, "Lamp", center + new Vector3(0f, colliderSize.y * 0.82f, colliderSize.z * 0.46f), Vector3.forward);

            StormdrainKit.Strip(root.gameObject);
            return root.gameObject;
        }
    }
}
