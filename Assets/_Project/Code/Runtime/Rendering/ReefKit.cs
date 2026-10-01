using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Rendering
{
    /// <summary>Which of World 3's four ocean-backdrop layers a piece is (MV-713, AC3). A plain marker
    /// — nothing reads the kind at runtime yet beyond the EditMode test that counts them — kept as an
    /// enum rather than a name string so a stray typo in a layer's GameObject name can never silently
    /// pass a count check that was actually looking for the wrong thing.</summary>
    public enum OceanLayerKind { FishSchool, Jellyfish, Manta, Whale }

    /// <summary>Tags one flat parallax layer of the ocean pressing in behind a Reef observation window
    /// (MV-713). Never carries a collider — see <see cref="ReefKit.BuildOceanBackdrop"/> — because
    /// nothing out there is ever gameplay (ticket, "Change" §3).</summary>
    public sealed class OceanLayer : MonoBehaviour
    {
        public OceanLayerKind Kind;
    }

    /// <summary>Marks one hydroponic bed (MV-1055) so an EditMode test — or any future gameplay
    /// query — can find every one <c>MaxWorlds.Arena.ReefHydroponics</c> placed without walking the
    /// whole scene by name. Carries no data of its own: a bed's shape and position are baked into
    /// its built geometry, same as every other Reef dressing piece.</summary>
    public sealed class HydroponicBed : MonoBehaviour
    {
    }

    /// <summary>
    /// World 3's Reef ship kit (MV-713) — the pieces the ticket's material/prefab pass adds that don't
    /// already fall out of the existing per-world plumbing.
    ///
    /// The deck plate (Ground) and bulkhead (Wall) turned out NOT to need nothing, whatever this used
    /// to say here: <see cref="WorldMaterials.Apply"/>'s shape-classified sweep re-skins them with
    /// <see cref="MaterialLibrary.Surface"/> — the SAME generic, procedural, world-space-noise ground/
    /// wall shader every biome shares, only recoloured by <see cref="BiomePalette.Reef"/> — never with
    /// the ticket's own <see cref="WorldMaterials.M_ShipFloor"/>/<see cref="WorldMaterials.M_ShipWall"/>.
    /// That is the bug MV-745 fixed: a floor that read as a flat, speckled void rather than riveted
    /// deck plate, because it was never wearing the named material at all. <see cref="DressHull"/> is
    /// the fix — called from <see cref="MaxWorlds.Arena.BackyardPath"/>'s existing per-world sweep
    /// hook, right after that generic sweep runs, so it overrides rather than races it. A bare cargo
    /// crate (Prop, <see cref="MaxWorlds.Arena.CoverDressing.None"/>) still needs nothing here — Prop
    /// was never part of the bug, only Ground and Wall were.
    ///
    /// The hydroponic reactor (<see cref="MaxWorlds.Factories.MowerHutch"/>) and the power hatch
    /// (<see cref="MaxWorlds.Arena.AreaGate"/>) are <c>IDamageable</c> and explicitly excluded from that
    /// sweep (<see cref="WorldMaterials.IsWorldSurface"/> — gameplay owns their tint), so each grew its
    /// own <c>ApplyReefSkin()</c> — a MaterialPropertyBlock-only cosmetic override, called from
    /// <see cref="MaxWorlds.Arena.BackyardPath"/>'s existing per-world sweep hook, that touches no health,
    /// gate-wiring or collider field either object was built with.
    ///
    /// The coolant turret (was the Tree cover-dressing prop) and the observation window have no live
    /// authoring hook yet: <see cref="MaxWorlds.Arena.BackyardDressing"/>'s kit-prop dressing pass
    /// (Tree/Hedge/Planter/Shed) is world-agnostic today for EVERY world, not just this one — World 2's
    /// Stormdrain never re-skinned it either (MV-690's own documented scope cut). Wiring per-biome
    /// dressing is real, separate work spanning a different subsystem; this ticket instead lands the two
    /// buildable pieces themselves — collider-free, ArenaCover's own box stays the collider exactly as
    /// it does for every other dressing case — ready for that follow-up to place. Building them here
    /// keeps them independently testable and keeps this pass from reaching into BackyardDressing's kit-
    /// import/Strip/KitSurfaces ordering, which nothing about a material/prefab ticket needs to touch.
    /// </summary>
    public static class ReefKit
    {
        /// <summary>Height of a hull-base strip light (MV-745, ticket change item 3: "a strip light
        /// along [each wall's] base"). Low and thin — a baseboard glow, not a second wall.</summary>
        private const float CircuitStripHeight = 0.12f;

        /// <summary>How far a strip's footprint grows past its wall's, so the two never share an exact
        /// coplanar face (the same anti-z-fight idiom <c>MapRuntime.AntiZFightMargin</c> already uses
        /// for a gate against its wall).</summary>
        private const float CircuitStripProud = 0.02f;

        /// <summary>UV units/second the circuit spine's shared material scrolls (MV-745, ticket change
        /// item 2: "animated by a scrolling shader mask, never animated meshes").</summary>
        private static readonly Vector2 CircuitScrollSpeed = new Vector2(0f, 0.15f);

        /// <summary>
        /// Re-skins World 3's already-built floor and walls with the ticket's own named materials
        /// instead of the generic biome sweep (MV-745, ticket change items 1 and 3), and lays a cyan
        /// circuit-spine strip light along the base of every wall (change items 2 and 3 — one spine
        /// serves both, since a wall's base line IS the seam between it and the floor).
        ///
        /// Runs AFTER <see cref="WorldMaterials.Apply"/>'s sweep has already painted <paramref name="host"/>'s
        /// renderers with the generic Ground/Wall material, and overrides exactly those two
        /// classifications — nothing here touches a Prop (the coolant turret and cargo crate already
        /// have their own routing) or a damageable (<see cref="WorldMaterials.IsWorldSurface"/> already
        /// excludes those from the sweep this mirrors).
        /// </summary>
        public static void DressHull(Transform host)
        {
            if (host == null) return;

            var walls = new List<StructuralWall>();

            foreach (MeshRenderer r in host.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!WorldMaterials.IsWorldSurface(r)) continue;

                SurfaceKind kind = WorldMaterials.KindOf(r);
                if (kind == SurfaceKind.Ground)
                {
                    r.sharedMaterial = WorldMaterials.M_ShipFloor;
                    ApplyDeckTiling(r);
                }
                else if (kind == SurfaceKind.Wall)
                {
                    r.sharedMaterial = WorldMaterials.M_ShipWall;
                    StructuralWall wall = r.GetComponent<StructuralWall>();
                    if (wall != null) walls.Add(wall);
                }
            }

            BuildCircuitSpine(host, walls);
            BuildObservationGlass(host, walls);
            BuildWallLamps(host, walls);
        }

        /// <summary>Sets the shared deck material's mesh-UV tiling from the floor's OWN resolved
        /// world size (MV-1053) — not a hardcoded guess, so a differently-sized World 3 floor still
        /// reads as 2 m plates rather than stretched or shrunk ones. The floor is a single Box
        /// primitive scaled to its footprint, and a Unity cube's default face UVs already span 0..1
        /// across that exact footprint, so this tiling count is the real one, not an approximation.
        /// </summary>
        private static void ApplyDeckTiling(Renderer floorRenderer)
        {
            Vector3 size = floorRenderer.bounds.size;
            float plate = WorldMaterials.ReefDeckPlateSizeMetres;
            Vector2 tiling = new Vector2(
                Mathf.Max(1f, size.x / plate),
                Mathf.Max(1f, size.z / plate));

            // SetTextureScale("_BaseMap", ...), not the mainTextureScale shortcut: that shortcut
            // targets whichever property carries the shader's [MainTexture] tag (or _MainTex if none
            // does), and URP/Lit's own Properties block never tags _BaseMap with it — mainTextureScale
            // silently no-ops on this shader. Naming the property directly is what actually reaches
            // the _BaseMap_ST the shader samples through.
            floorRenderer.sharedMaterial.SetTextureScale("_BaseMap", tiling);
        }

        /// <summary>One strip per built wall, hugging its base, plus the single driver that scrolls
        /// them all — every strip shares <see cref="WorldMaterials.M_Circuit_Cyan"/>'s one instance, so
        /// one driver moves every strip's glow together; a driver per strip would scroll that shared
        /// material once per strip per frame instead of once.</summary>
        private static void BuildCircuitSpine(Transform host, List<StructuralWall> walls)
        {
            if (walls.Count == 0) return;

            var root = new GameObject("Circuit Spine");
            root.transform.SetParent(host, false);

            foreach (StructuralWall wall in walls)
            {
                Transform wt = wall.transform;
                Vector3 scale = wt.lossyScale;

                GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = $"{wt.name} Circuit";
                strip.transform.SetParent(root.transform, false);
                strip.transform.position = new Vector3(wt.position.x, CircuitStripHeight * 0.5f, wt.position.z);
                strip.transform.localScale = new Vector3(
                    scale.x + CircuitStripProud, CircuitStripHeight, scale.z + CircuitStripProud);
                StripColliders(strip);

                var rend = strip.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;
            }

            root.AddComponent<ReefCircuitFlow>().Configure(WorldMaterials.M_Circuit_Cyan, CircuitScrollSpeed);
        }

        // MV-1054, change item 2: every structural wall on the map's outer edge becomes observation
        // glass — a low dark frame, a translucent cyan-blue pane, a cyan top strip, and dark mullions
        // every 4 m along its length.
        private const float GlassFrameHeight = 0.12f;
        private const float GlassTopStripHeight = 0.12f;
        private const float GlassMullionWidth = 0.08f;
        private const float GlassMullionPitch = 4f;
        private const float GlassProud = 0.02f;   // same anti-z-fight idiom as CircuitStripProud

        /// <summary>Re-skins every outer-edge wall in <paramref name="walls"/> (<see cref="StructuralWall.IsOuterEdge"/>,
        /// set by <c>MapRuntime.Build</c> from the wall solver's own room-adjacency data — the only
        /// thing that can tell a hull wall from an interior partition on a non-convex, spiral-shaped
        /// map) into observation glass. The wall's own renderer becomes the pane
        /// (<see cref="WorldMaterials.M_GlassOcean"/>); frame, strip and mullions are new, collider-
        /// free overlay geometry parented under one "Observation Glass" root (own sibling of "Circuit
        /// Spine" and "Reef Props" — <c>MapRuntime.TagReefDressing</c> looks it up by that exact name
        /// to keep it zone-gated the same way). Wall colliders are untouched (ticket, "Change" 2):
        /// same "collider stays, art swaps" contract every other Reef dressing case keeps.</summary>
        private static void BuildObservationGlass(Transform host, List<StructuralWall> walls)
        {
            if (walls.Count == 0) return;

            GameObject root = null;

            foreach (StructuralWall wall in walls)
            {
                if (!wall.IsOuterEdge) continue;

                Transform wt = wall.transform;
                Renderer wr = wall.GetComponent<Renderer>();
                if (wr == null) continue;

                wr.sharedMaterial = WorldMaterials.M_GlassOcean;

                if (root == null)
                {
                    root = new GameObject("Observation Glass");
                    root.transform.SetParent(host, false);
                }

                Vector3 scale = wt.lossyScale;
                bool alongX = scale.x >= scale.z;
                float length = alongX ? scale.x : scale.z;
                float height = scale.y;

                GlassBar(root.transform, $"{wt.name} Glass Frame", wt.position, scale,
                    GlassFrameHeight, -height * 0.5f + GlassFrameHeight * 0.5f, WorldMaterials.M_MetalDark);
                GlassBar(root.transform, $"{wt.name} Glass Strip", wt.position, scale,
                    GlassTopStripHeight, height * 0.5f - GlassTopStripHeight * 0.5f, WorldMaterials.M_Circuit_Cyan);

                int mullions = Mathf.Max(0, Mathf.FloorToInt(length / GlassMullionPitch));
                for (int i = 1; i <= mullions; i++)
                {
                    float offset = -length * 0.5f + i * GlassMullionPitch;
                    if (offset >= length * 0.5f - 0.01f) continue;

                    Vector3 pos = alongX
                        ? new Vector3(wt.position.x + offset, wt.position.y, wt.position.z)
                        : new Vector3(wt.position.x, wt.position.y, wt.position.z + offset);

                    GameObject mullion = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    mullion.name = $"{wt.name} Mullion {i}";
                    mullion.transform.SetParent(root.transform, false);
                    mullion.transform.position = pos;
                    mullion.transform.localScale = alongX
                        ? new Vector3(GlassMullionWidth, height + GlassProud, scale.z + GlassProud)
                        : new Vector3(scale.x + GlassProud, height + GlassProud, GlassMullionWidth);
                    StripColliders(mullion);
                    var mRend = mullion.GetComponent<Renderer>();
                    if (mRend != null) mRend.sharedMaterial = WorldMaterials.M_MetalDark;
                }
            }
        }

        /// <summary>One frame/strip bar, hugging a wall's own footprint — same "parent's cumulative
        /// scale does the metric conversion" idiom <see cref="BuildCircuitSpine"/>'s own strip uses.
        /// </summary>
        private static void GlassBar(Transform parent, string name, Vector3 wallPosition, Vector3 wallScale,
            float barHeight, float yOffset, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(wallPosition.x, wallPosition.y + yOffset, wallPosition.z);
            go.transform.localScale = new Vector3(wallScale.x + GlassProud, barHeight, wallScale.z + GlassProud);
            StripColliders(go);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = material;
        }

        // MV-1055, change item 2: small violet accent lamps mounted along the top of every hull
        // wall, roughly every 6 m — the ticket's own pitch, distinct from the 4 m glass-mullion
        // pitch above (a different rhythm reads as two different kit pieces, not one mis-spaced).
        private const float WallLampPitch = 6f;
        private const float WallLampDiameter = 0.18f;
        private const float WallLampProud = 0.05f;   // perches on the wall's own top face, not embedded

        /// <summary>One small violet bead per <see cref="WallLampPitch"/> along EVERY built wall
        /// (not just the outer-edge glass walls <see cref="BuildObservationGlass"/> re-skins) —
        /// "hull walls" in the ticket's own words, and every <see cref="StructuralWall"/> IS hull
        /// plate in World 3. Evenly spread along each wall's own length (same "(i + 0.5) / count"
        /// centring idiom <see cref="BuildObservationGlass"/>'s own mullion loop uses), never flush
        /// with an end cap, so a short wall still reads as dressed rather than empty.</summary>
        private static void BuildWallLamps(Transform host, List<StructuralWall> walls)
        {
            if (walls.Count == 0) return;

            GameObject root = null;

            foreach (StructuralWall wall in walls)
            {
                Transform wt = wall.transform;
                Vector3 scale = wt.lossyScale;

                bool alongX = scale.x >= scale.z;
                float length = alongX ? scale.x : scale.z;
                float height = scale.y;

                int lamps = Mathf.Max(1, Mathf.FloorToInt(length / WallLampPitch));
                for (int i = 0; i < lamps; i++)
                {
                    float t = (i + 0.5f) / lamps - 0.5f;
                    float offset = t * length;

                    Vector3 pos = alongX
                        ? new Vector3(wt.position.x + offset, wt.position.y + height * 0.5f + WallLampProud, wt.position.z)
                        : new Vector3(wt.position.x, wt.position.y + height * 0.5f + WallLampProud, wt.position.z + offset);

                    if (root == null)
                    {
                        root = new GameObject("Wall Lamps");
                        root.transform.SetParent(host, false);
                    }

                    GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    lamp.name = $"{wt.name} Lamp {i}";
                    lamp.transform.SetParent(root.transform, false);
                    lamp.transform.position = pos;
                    lamp.transform.localScale = Vector3.one * WallLampDiameter;
                    StripColliders(lamp);

                    var rend = lamp.GetComponent<Renderer>();
                    if (rend != null) rend.sharedMaterial = WorldMaterials.M_LampViolet;
                }
            }
        }

        // MV-1054, change item 1: the ocean pressing in beyond the map's own edge.
        private const float OceanVoidDepthBelowFloor = 6f;   // ticket: "~6 m below floor level"
        private const float OceanVoidMarginMetres = 45f;     // ticket AC: >= 40 m past the map's XZ bounds on every side; kept with headroom above that minimum
        private const float OceanVoidThickness = 0.2f;

        /// <summary>The ocean beyond World 3's own hull (MV-1054, change item 1): one large, unlit,
        /// collider-free slab far below the floor, its footprint the map's own XZ bounds (read off the
        /// floor's OWN resolved renderer, the single map-spanning "Map Floor" <see cref="MaxWorlds.Arena.MapRuntime.Build"/>
        /// always builds and tags <see cref="StructuralFloor"/>) inflated by <see cref="OceanVoidMarginMetres"/>
        /// on every side — not threaded in as <c>MapData</c>, the same "ask the built geometry, not the
        /// author data" idiom <see cref="ApplyDeckTiling"/> already uses for the deck's own tiling.
        /// Replaces the flat black void a camera used to see past the map's edge — distinct from
        /// <see cref="BuildOceanBackdrop"/>'s four small per-window parallax layers, which stay exactly
        /// as they are (MV713ReefKitTests pins that shape).</summary>
        public static GameObject BuildOceanVoid(Transform host)
        {
            if (host == null) return null;

            Renderer floor = FindFloorRenderer(host);
            if (floor == null) return null;

            Bounds b = floor.bounds;
            float sizeX = b.size.x + OceanVoidMarginMetres * 2f;
            float sizeZ = b.size.z + OceanVoidMarginMetres * 2f;
            float topY = b.max.y - OceanVoidDepthBelowFloor;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ocean Void";
            go.transform.SetParent(host, false);
            go.transform.position = new Vector3(b.center.x, topY - OceanVoidThickness * 0.5f, b.center.z);
            go.transform.localScale = new Vector3(sizeX, OceanVoidThickness, sizeZ);
            StripColliders(go);

            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = WorldMaterials.M_OceanVoid;

            return go;
        }

        private static Renderer FindFloorRenderer(Transform host)
        {
            foreach (MeshRenderer r in host.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!WorldMaterials.IsWorldSurface(r)) continue;
                if (WorldMaterials.KindOf(r) == SurfaceKind.Ground) return r;
            }
            return null;
        }

        // Bounding box in metres for a coolant turret prop — tall enough to read as the tree it replaces
        // (BackyardDressing.TreeHeightMetres is 3.5 m; a turret's stack is a touch shorter and wider).
        private const float DefaultTurretHeight = 3f;
        private const float DefaultTurretRadius = 0.5f;

        /// <summary>The coolant turret (was the Tree cover-dressing obstacle) — a cylindrical stack in
        /// <see cref="WorldMaterials.M_Circuit_Cyan"/> with a <see cref="WorldMaterials.M_Hazard"/> band,
        /// standing in for the machinery a tree's crown used to fill. Collider-free, same contract every
        /// <c>BackyardDressing.DressCover</c> prop keeps (the block's own box is what stops the player).</summary>
        public static GameObject BuildCoolantTurret(Transform parent, Vector3 localPosition,
            float heightMetres = DefaultTurretHeight)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "CoolantTurret";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(DefaultTurretRadius * 2f, heightMetres * 0.5f, DefaultTurretRadius * 2f);
            StripColliders(go);

            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;

            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = "HazardBand";
            band.transform.SetParent(go.transform, false);
            band.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            band.transform.localScale = new Vector3(1.08f, 0.06f, 1.08f);
            StripColliders(band);
            var bandRend = band.GetComponent<Renderer>();
            if (bandRend != null) bandRend.sharedMaterial = WorldMaterials.M_Hazard;

            return go;
        }

        // How thick a crate's orange corner cap reads at, in metres (MV-1019, ticket change item 2:
        // "orange only on the corner caps"). Thin enough to accent the edge without swallowing the
        // steel body colour it sits on.
        private const float CrateCapThickness = 0.10f;

        // How far a cap's outer face sits proud of its crate's own face — the same anti-z-fight
        // idiom CircuitStripProud uses for a strip against its wall.
        private const float CrateCapProud = 0.01f;

        /// <summary>Recolours a bare "crate" cover piece with its own Reef material (MV-1019) — a
        /// blue-grey steel body plus orange accent caps at its four vertical edges. Runs in place of
        /// leaving the piece to the general shape-classified sweep, which painted it
        /// <see cref="BiomePalette.Reef"/>'s Prop tone (#0C1622) — darker than the floor it sits on.
        /// Collider and geometry are untouched: only the body's renderer is re-pointed, and the four
        /// caps are new, collider-free children, same "collider stays, art swaps" contract every
        /// other Reef dressing case keeps. <paramref name="addCornerCaps"/> is the caller's own
        /// <c>CoverShape.Box</c> check (a bool, not the enum itself — <c>MaxWorlds.Rendering</c> has no
        /// reference to the assembly <c>CoverShape</c> lives in) — a box's corners have edges to
        /// accent, a cylinder has none.</summary>
        public static void ApplyCrateSkin(GameObject crateBody, bool addCornerCaps)
        {
            if (crateBody == null) return;

            var rend = crateBody.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = WorldMaterials.M_CrateBody;

            if (!addCornerCaps) return;

            Vector3 size = crateBody.transform.localScale;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f) return;

            float capX = Mathf.Min(CrateCapThickness, size.x * 0.4f);
            float capZ = Mathf.Min(CrateCapThickness, size.z * 0.4f);
            if (capX <= 0f || capZ <= 0f) return;

            var caps = new GameObject("Corner Caps");
            caps.transform.SetParent(crateBody.transform, false);

            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.name = "CrateCap";
                cap.transform.SetParent(caps.transform, false);
                // Positions/scales are expressed as fractions of the parent's own local unit cube
                // (-0.5..0.5) — the parent's cumulative scale (== size) does the metric conversion,
                // same idiom BuildCircuitSpine's strips use against their wall.
                cap.transform.localPosition = new Vector3(
                    sx * (0.5f - (capX / size.x) * 0.5f),
                    0f,
                    sz * (0.5f - (capZ / size.z) * 0.5f));
                cap.transform.localScale = new Vector3(
                    capX / size.x + CrateCapProud, 1f + CrateCapProud, capZ / size.z + CrateCapProud);
                StripColliders(cap);

                var capRend = cap.GetComponent<Renderer>();
                if (capRend != null) capRend.sharedMaterial = WorldMaterials.M_CrateCap;
            }
        }

        /// <summary>An observation window panel — new geometry with no World 1 predecessor (the ticket
        /// names it without a "was X" annotation, unlike the other five reskins), wearing
        /// <see cref="WorldMaterials.M_GlassOcean"/>'s near/far gradient. Collider-free: it is a wall
        /// dressing piece, not a thing that blocks Max.</summary>
        public static GameObject BuildObservationWindow(Transform parent, Vector3 localPosition, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ObservationWindow";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            StripColliders(go);

            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = WorldMaterials.M_GlassOcean;

            return go;
        }

        // World-space depth spacing between the four flat layers, nearest to farthest (metres behind
        // the window plane) — keeps them from z-fighting without needing render-queue plumbing.
        private const float LayerDepthStep = 0.6f;

        /// <summary>The ocean pressing in through an observation window (MV-713, AC3): exactly four flat
        /// parallax layers — fish schools, jellyfish, one manta, one whale silhouette — none carrying a
        /// collider (nothing out there is ever gameplay). Vertex-animated jellyfish and a spline-driven
        /// manta are visual polish (Tier 3, not EditMode-testable under -batchmode -nographics) and are
        /// explicitly out of scope for this pass; what ships here is the four static, correctly-tinted,
        /// collider-free layers the AC actually asks for.</summary>
        public static GameObject BuildOceanBackdrop(Transform parent)
        {
            var root = new GameObject("Ocean Backdrop");
            root.transform.SetParent(parent, false);

            BuildLayer(root.transform, OceanLayerKind.FishSchool, WorldMaterials.ReefCircuitCyan, 0f * LayerDepthStep, instanced: true);
            BuildLayer(root.transform, OceanLayerKind.Jellyfish, WorldMaterials.ReefBioGlow, 1f * LayerDepthStep, instanced: false);
            BuildLayer(root.transform, OceanLayerKind.Manta, WorldMaterials.ReefCircuitPurple, 2f * LayerDepthStep, instanced: false);
            BuildLayer(root.transform, OceanLayerKind.Whale, WorldMaterials.ReefMetalDark, 3f * LayerDepthStep, instanced: false);

            return root;
        }

        private static void BuildLayer(Transform parent, OceanLayerKind kind, Color tint, float depth, bool instanced)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"Ocean_{kind}";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.5f, depth);
            go.transform.localScale = new Vector3(6f, 3f, 1f);
            StripColliders(go);
            go.AddComponent<OceanLayer>().Kind = kind;

            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;

            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return;

            var mat = new Material(shader)
            {
                name = $"OceanLayer_{kind}",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = instanced,
            };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            rend.sharedMaterial = mat;
        }

        // MV-1055, change item 1: low hexagonal hydroponic beds. WHERE each bed stands is decided by
        // MaxWorlds.Arena.ReefHydroponics (it needs cover/gate/garrison data this Rendering-assembly
        // class has no reference to); this builds the geometry for one bed at a position already
        // chosen to clear everything the ticket names.
        private const float BedRadius = 1.5f;          // ticket: "~3 m across" is the diameter
        private const float BedRimHeight = 0.16f;
        private const float BedRimThickness = 0.12f;
        private const float BedSoilInset = 0.25f;      // keeps the soil pad inside the rim's own ring
        private const float BedSoilHeight = 0.12f;
        private const float KelpSpikeHeight = 0.10f;
        private const float KelpSpikeThickness = 0.05f;
        private const int BedRimSides = 6;

        /// <summary>Tallest point any of a bed's own pieces reaches (the kelp spikes, planted on top
        /// of the soil pad) — kept below the ticket's own 0.3 m ceiling with headroom, so it can never
        /// read as cover (ticket, "Change" §1).</summary>
        private const float BedMaxHeight = BedSoilHeight + KelpSpikeHeight;

        /// <summary>One hydroponic bed — a glowing cyan hex rim, a dark soil pad recessed inside it,
        /// and a few bioluminescent kelp spikes — at <paramref name="worldCenterXz"/> (Y ignored; a
        /// bed always rests on the floor, never authored half-buried or floating). Collider-free
        /// throughout, <see cref="BedMaxHeight"/> tall at its tallest point.</summary>
        public static GameObject BuildHydroponicBed(Transform parent, Vector3 worldCenterXz)
        {
            var root = new GameObject("HydroponicBed");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(worldCenterXz.x, 0f, worldCenterXz.z);
            root.AddComponent<HydroponicBed>();

            BuildBedSoil(root.transform);
            BuildBedRim(root.transform);
            BuildKelpSpikes(root.transform);

            return root;
        }

        private static void BuildBedSoil(Transform parent)
        {
            float radius = BedRadius - BedSoilInset;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Soil";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, BedSoilHeight * 0.5f, 0f);
            // A default Unity cylinder is 2 units tall, 1 unit across at scale 1 — halving the
            // requested height into the Y scale and doubling the radius into X/Z is what turns those
            // unit dimensions into the actual metres asked for.
            go.transform.localScale = new Vector3(radius * 2f, BedSoilHeight * 0.5f, radius * 2f);
            StripColliders(go);

            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = WorldMaterials.M_MetalDark;
        }

        /// <summary>Six bar segments joining the vertices of a regular hexagon — a regular hexagon's
        /// own side length equals its circumradius, so each bar's length is just <see cref="BedRadius"/>,
        /// no extra trig beyond the vertex positions themselves. Same "CreatePrimitive(Cube), scale
        /// and rotate to fit" idiom every other edge-hugging piece in this file already uses (the glass
        /// mullions, the crate corner caps) — never a hand-authored mesh.</summary>
        private static void BuildBedRim(Transform parent)
        {
            var verts = new Vector3[BedRimSides];
            for (int i = 0; i < BedRimSides; i++)
            {
                float angle = Mathf.Deg2Rad * (60f * i);
                verts[i] = new Vector3(Mathf.Cos(angle) * BedRadius, 0f, Mathf.Sin(angle) * BedRadius);
            }

            for (int i = 0; i < BedRimSides; i++)
            {
                Vector3 a = verts[i];
                Vector3 b = verts[(i + 1) % BedRimSides];
                Vector3 mid = (a + b) * 0.5f;
                Vector3 dir = b - a;

                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = $"Rim {i}";
                bar.transform.SetParent(parent, false);
                bar.transform.localPosition = new Vector3(mid.x, BedRimHeight * 0.5f, mid.z);
                bar.transform.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                bar.transform.localScale = new Vector3(BedRimThickness, BedRimHeight, dir.magnitude);
                StripColliders(bar);

                var rend = bar.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;
            }
        }

        // Fixed local offsets, not a per-instance random scatter (same "deterministic, never Random"
        // idiom MaxWorlds.Enemies.CorrosionPuddle.BuildFanMesh's own seeded-hash comment keeps) — a
        // bed built on CI must look identical to the same bed built in the editor.
        private static readonly Vector3[] KelpSpikeOffsets =
        {
            new Vector3(0.4f, 0f, 0.25f),
            new Vector3(-0.3f, 0f, 0.45f),
            new Vector3(0.1f, 0f, -0.5f),
        };

        /// <summary>A few kelp spikes planted in the soil pad — mostly <see cref="WorldMaterials.ReefKelpGreen"/>
        /// (#46FF9A), with "some magenta" (ticket, "Change" §1) at <see cref="WorldMaterials.ReefKelpMagenta"/>
        /// (#FF4FD8): one in three, the odd one out rather than a 50/50 split.</summary>
        private static void BuildKelpSpikes(Transform parent)
        {
            for (int i = 0; i < KelpSpikeOffsets.Length; i++)
            {
                var spike = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spike.name = $"Kelp {i}";
                spike.transform.SetParent(parent, false);
                spike.transform.localPosition = new Vector3(
                    KelpSpikeOffsets[i].x, BedSoilHeight + KelpSpikeHeight * 0.5f, KelpSpikeOffsets[i].z);
                spike.transform.localRotation = Quaternion.Euler(0f, i * 35f, 10f);
                spike.transform.localScale = new Vector3(KelpSpikeThickness, KelpSpikeHeight, KelpSpikeThickness);
                StripColliders(spike);

                var rend = spike.GetComponent<Renderer>();
                if (rend != null)
                    rend.sharedMaterial = (i == 1) ? WorldMaterials.M_KelpMagenta : WorldMaterials.M_KelpGreen;
            }
        }

        /// <summary>Ocean backdrop and dressing props are scenery — never a thing Max, a robot or the
        /// boss can collide with. Same idiom as <see cref="MaxWorlds.Arena.BackyardDressing"/>'s own
        /// <c>Strip</c>.</summary>
        private static void StripColliders(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
        }
    }

    /// <summary>Scrolls the Reef circuit spine's shared material (MV-745) — the "scrolling shader
    /// mask" the ticket asks for, never an animated mesh. Same idiom as
    /// <see cref="MaxWorlds.Arena.SludgeFlow"/>, except there is exactly ONE of these per spine rather
    /// than one per strip: every strip shares <see cref="WorldMaterials.M_Circuit_Cyan"/>'s single
    /// cached instance, and <c>Update</c> mutates that shared instance, so a driver per strip would
    /// scroll it once per strip per frame instead of once for the whole spine.</summary>
    [MaxWorlds.Core.PerfSection("rendering")]
    public sealed class ReefCircuitFlow : MonoBehaviour
    {
        private Material _material;
        private Vector2 _scrollSpeed;

        public void Configure(Material material, Vector2 scrollSpeed)
        {
            _material = material;
            _scrollSpeed = scrollSpeed;
        }

        private void Update()
        {
            if (_material == null) return;
            _material.mainTextureOffset += _scrollSpeed * Time.deltaTime;
        }
    }
}
