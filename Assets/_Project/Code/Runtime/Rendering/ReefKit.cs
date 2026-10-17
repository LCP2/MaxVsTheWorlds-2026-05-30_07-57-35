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
                    MarkDressed(r.gameObject);
                }
                else if (kind == SurfaceKind.Wall)
                {
                    r.sharedMaterial = WorldMaterials.M_ShipWall;
                    MarkDressed(r.gameObject);
                    StructuralWall wall = r.GetComponent<StructuralWall>();
                    if (wall != null) walls.Add(wall);
                }
            }

            BuildCircuitSpine(host, walls);
            BuildObservationGlass(host, walls);
            BuildWallLamps(host, walls);
        }

        /// <summary>Stops <see cref="MaxWorlds.VFX.RuntimeSurfaceDirector"/>'s own later, generic
        /// <c>Start()</c> sweep — guaranteed by Unity to run after every object's <c>Awake</c>, this
        /// one included — from finding a freshly Reef-dressed renderer "undressed" and repainting it
        /// back onto the shape-classified generic material (MV-1063: that is why World 3's deck,
        /// circuit traces, hydroponic glow, hull lamps and ocean glass all rendered flat in a built
        /// player — this method's own <see cref="DressHull"/> and every other Reef piece below assign
        /// the right material during <c>BackyardPath.Awake</c>, and the sweep undoes it one phase
        /// later). <see cref="KeepsOwnMaterial"/> is the existing, general escape hatch for exactly
        /// this — both that sweep and <see cref="WorldMaterials.Apply"/>'s own generic one already
        /// check for it via <c>GetComponentInParent</c> — so tagging a piece's root protects every
        /// renderer under it with one call, no new marker type needed.</summary>
        private static void MarkDressed(GameObject go)
        {
            if (go != null && go.GetComponent<KeepsOwnMaterial>() == null) go.AddComponent<KeepsOwnMaterial>();
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
            MarkDressed(root);

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

        // MV-1094: the observation-glass pane and its mullions (MV-1054) are withdrawn for outer-edge
        // walls — on a phone the translucent pane read as no wall at all over a dark void (Lee, World 3
        // areas 10/11). An outer-edge wall keeps its ordinary M_ShipWall skin from DressHull's own sweep
        // above; only the dark foot frame and cyan top strip survive, so the map edge still reads as
        // distinct from an interior partition.
        private const float GlassFrameHeight = 0.12f;
        private const float GlassTopStripHeight = 0.12f;
        private const float GlassProud = 0.02f;   // same anti-z-fight idiom as CircuitStripProud

        /// <summary>Adds a dark foot frame and a cyan top strip to every outer-edge wall in
        /// <paramref name="walls"/> (<see cref="StructuralWall.IsOuterEdge"/>, set by
        /// <c>MapRuntime.Build</c> from the wall solver's own room-adjacency data — the only thing that
        /// can tell a hull wall from an interior partition on a non-convex, spiral-shaped map). The
        /// wall's own renderer and material are untouched (MV-1094: it keeps the same M_ShipWall every
        /// interior wall wears, set by <see cref="DressHull"/>'s own sweep just above this call) — frame
        /// and strip are new, collider-free overlay geometry parented under one "Observation Glass" root
        /// (own sibling of "Circuit Spine" and "Reef Props" — <c>MapRuntime.TagReefDressing</c> looks it
        /// up by that exact name to keep it zone-gated the same way; kept as-is rather than renamed, since
        /// that lookup is a second, independent reference this pass has no reason to touch). Wall
        /// colliders are untouched: same "collider stays, art overlays" contract every other Reef
        /// dressing case keeps.</summary>
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

                if (root == null)
                {
                    root = new GameObject("Observation Glass");
                    root.transform.SetParent(host, false);
                    MarkDressed(root);
                }

                Vector3 scale = wt.lossyScale;
                float height = scale.y;

                GlassBar(root.transform, $"{wt.name} Glass Frame", wt.position, scale,
                    GlassFrameHeight, -height * 0.5f + GlassFrameHeight * 0.5f, WorldMaterials.M_MetalDark);
                GlassBar(root.transform, $"{wt.name} Glass Strip", wt.position, scale,
                    GlassTopStripHeight, height * 0.5f - GlassTopStripHeight * 0.5f, WorldMaterials.M_Circuit_Cyan);
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
        // wall, roughly every 6 m — the ticket's own pitch.
        private const float WallLampPitch = 6f;
        private const float WallLampDiameter = 0.18f;
        private const float WallLampProud = 0.05f;   // perches on the wall's own top face, not embedded

        /// <summary>One small violet bead per <see cref="WallLampPitch"/> along EVERY built wall
        /// (not just the outer-edge walls <see cref="BuildObservationGlass"/> trims with a frame/strip) —
        /// "hull walls" in the ticket's own words, and every <see cref="StructuralWall"/> IS hull
        /// plate in World 3. Evenly spread along each wall's own length (same "(i + 0.5) / count"
        /// centring idiom <see cref="BuildWallLamps"/> itself uses below), never flush
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
                        MarkDressed(root);
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
            MarkDressed(go);

            return go;
        }

        /// <summary>Finds the map's own floor slab by the authoritative <see cref="StructuralFloor"/>
        /// tag <c>MapRuntime.Build</c> always sets on it — NOT <see cref="WorldMaterials.IsWorldSurface"/>
        /// (MV-1063 regression: that check excludes anything carrying <see cref="KeepsOwnMaterial"/>,
        /// and <see cref="DressHull"/>'s own fix for this ticket now tags the floor with exactly that,
        /// so the old <c>IsWorldSurface</c> + shape-classified <c>KindOf</c> combination here stopped
        /// finding a freshly Reef-dressed floor at all — <see cref="BuildOceanVoid"/> started returning
        /// null for every real World 3 load). Whether a renderer keeps its own material has nothing to
        /// do with whether it IS the map floor.</summary>
        private static Renderer FindFloorRenderer(Transform host)
        {
            foreach (MeshRenderer r in host.GetComponentsInChildren<MeshRenderer>(true))
                if (r.GetComponentInParent<StructuralFloor>() != null) return r;
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
            MarkDressed(go);

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
            MarkDressed(crateBody);

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

        // MV-1215: the Reef's own structure kit (Lee's 17 Oct 2026 concept-frame approval). Each
        // builder below is called by MaxWorlds.Arena.ReefDressing with the cover piece's own authored
        // centre/size, in place of a hidden body renderer — the same "collider stays, art swaps"
        // contract BuildCoolantTurret already keeps. Every child here is built UNSCALED, under an
        // identity-scale piece root, at literal metre offsets — not as a child of a scaled body the way
        // CrateCap's own fraction-of-parent trick works — so none of these need that division-by-size
        // arithmetic to stay a fixed metre thickness regardless of the piece's own authored footprint.

        private const float PillarSeamWidth = 0.08f;
        private const float ManifoldPipeRadius = 0.12f;
        private const float ConsoleScreenProud = 0.01f;
        private const float PlanterRimHeight = 0.5f;   // fraction of the piece's own authored height
        private const float FloorPipeBracketWidth = 0.06f;

        /// <summary>P — a heavy structural column, <see cref="WorldMaterials.M_MetalDark"/> with a
        /// <see cref="WorldMaterials.M_Circuit_Cyan"/> seam light running its full height on one face
        /// (ticket table: 2x2, solid).</summary>
        public static GameObject BuildPillar(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefPillar");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = size;
            StripColliders(body);
            var bodyRend = body.GetComponent<Renderer>();
            if (bodyRend != null) bodyRend.sharedMaterial = WorldMaterials.M_MetalDark;

            var seam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seam.name = "Seam Light";
            seam.transform.SetParent(root.transform, false);
            seam.transform.localPosition = new Vector3(0f, 0f, size.z * 0.5f + CircuitStripProud);
            seam.transform.localScale = new Vector3(PillarSeamWidth, size.y + CircuitStripProud, PillarSeamWidth);
            StripColliders(seam);
            var seamRend = seam.GetComponent<Renderer>();
            if (seamRend != null) seamRend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;

            return root;
        }

        /// <summary>K — a row of pipes on a base, each capped with a small cyan "U-bend" accent
        /// (ticket table: 4x2, solid). Pipe count follows the piece's own authored width, same
        /// "ask the authored footprint, never a hardcoded guess" idiom <see cref="ApplyDeckTiling"/>
        /// already keeps for the deck's own tiling.</summary>
        public static GameObject BuildManifold(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefManifold");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float baseHeight = size.y * 0.3f;
            var baseBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseBox.name = "Base";
            baseBox.transform.SetParent(root.transform, false);
            baseBox.transform.localPosition = new Vector3(0f, -size.y * 0.5f + baseHeight * 0.5f, 0f);
            baseBox.transform.localScale = new Vector3(size.x, baseHeight, size.z);
            StripColliders(baseBox);
            var baseRend = baseBox.GetComponent<Renderer>();
            if (baseRend != null) baseRend.sharedMaterial = WorldMaterials.M_MetalDark;

            int pipes = Mathf.Max(1, Mathf.FloorToInt(size.x / 1f));
            float pipeLength = size.z * 0.8f;
            float pipeY = -size.y * 0.5f + baseHeight + ManifoldPipeRadius;

            for (int i = 0; i < pipes; i++)
            {
                float t = (i + 0.5f) / pipes - 0.5f;
                float px = t * size.x;

                var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pipe.name = $"Pipe {i}";
                pipe.transform.SetParent(root.transform, false);
                pipe.transform.localPosition = new Vector3(px, pipeY, 0f);
                pipe.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                pipe.transform.localScale = new Vector3(ManifoldPipeRadius * 2f, pipeLength * 0.5f, ManifoldPipeRadius * 2f);
                StripColliders(pipe);
                var pipeRend = pipe.GetComponent<Renderer>();
                if (pipeRend != null) pipeRend.sharedMaterial = WorldMaterials.M_MetalDark;

                foreach (int sz in new[] { -1, 1 })
                {
                    var bend = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    bend.name = "U-Bend";
                    bend.transform.SetParent(root.transform, false);
                    bend.transform.localPosition = new Vector3(px, pipeY, sz * pipeLength * 0.5f);
                    bend.transform.localScale = Vector3.one * ManifoldPipeRadius * 2.2f;
                    StripColliders(bend);
                    var bendRend = bend.GetComponent<Renderer>();
                    if (bendRend != null) bendRend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;
                }
            }

            return root;
        }

        /// <summary>D — a round tank with a glowing cyan core, <see cref="CoverShape.Cylinder"/> (ticket
        /// table: 3x3, solid). <paramref name="size"/>'s X/Z is the tank's own diameter, same convention
        /// <see cref="ArenaCover"/> already uses for a cylinder's footprint.</summary>
        public static GameObject BuildDrum(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefDrum");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            var tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tank.name = "Tank";
            tank.transform.SetParent(root.transform, false);
            tank.transform.localScale = new Vector3(size.x, size.y * 0.5f, size.z);
            StripColliders(tank);
            var tankRend = tank.GetComponent<Renderer>();
            if (tankRend != null) tankRend.sharedMaterial = WorldMaterials.M_MetalDark;

            var core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            core.name = "Core";
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = new Vector3(size.x * 0.35f, size.y * 0.5f + CircuitStripProud, size.x * 0.35f);
            StripColliders(core);
            var coreRend = core.GetComponent<Renderer>();
            if (coreRend != null) coreRend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;

            return root;
        }

        /// <summary>N — an angled control desk with a glowing screen (ticket table: 2x1, solid). The
        /// "angle" reads from a stepped silhouette (low front desk, taller rear screen riser) rather
        /// than an arbitrary rotation — every other box piece in this file stays axis-aligned too.
        /// </summary>
        public static GameObject BuildConsole(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefConsole");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float deskHeight = size.y * 0.5f;
            var desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            desk.name = "Desk";
            desk.transform.SetParent(root.transform, false);
            desk.transform.localPosition = new Vector3(0f, -size.y * 0.5f + deskHeight * 0.5f, 0f);
            desk.transform.localScale = new Vector3(size.x, deskHeight, size.z);
            StripColliders(desk);
            var deskRend = desk.GetComponent<Renderer>();
            if (deskRend != null) deskRend.sharedMaterial = WorldMaterials.M_MetalDark;

            float screenHeight = size.y - deskHeight;
            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name = "Screen";
            screen.transform.SetParent(root.transform, false);
            screen.transform.localPosition = new Vector3(0f, -size.y * 0.5f + deskHeight + screenHeight * 0.5f,
                -size.z * 0.5f + size.z * 0.2f);
            screen.transform.localScale = new Vector3(size.x * 0.85f, screenHeight, size.z * 0.4f + ConsoleScreenProud);
            StripColliders(screen);
            var screenRend = screen.GetComponent<Renderer>();
            if (screenRend != null) screenRend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;

            return root;
        }

        /// <summary>T — a low, see-through row of pots with bioluminescent green plants (ticket table:
        /// 1xN, see-through). Runs along whichever of the footprint's two axes is longer — the "N" the
        /// ticket's own glyph leaves open.</summary>
        public static GameObject BuildPlanterRow(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefPlanter");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float rimHeight = size.y * PlanterRimHeight;
            var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rim.name = "Trough";
            rim.transform.SetParent(root.transform, false);
            rim.transform.localPosition = new Vector3(0f, -size.y * 0.5f + rimHeight * 0.5f, 0f);
            rim.transform.localScale = new Vector3(size.x, rimHeight, size.z);
            StripColliders(rim);
            var rimRend = rim.GetComponent<Renderer>();
            if (rimRend != null) rimRend.sharedMaterial = WorldMaterials.M_MetalDark;

            bool alongX = size.x >= size.z;
            float runLength = alongX ? size.x : size.z;
            int plants = Mathf.Max(1, Mathf.FloorToInt(runLength / 0.5f));
            float plantHeight = size.y - rimHeight;

            for (int i = 0; i < plants; i++)
            {
                float t = (i + 0.5f) / plants - 0.5f;
                float offset = t * runLength;

                var plant = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plant.name = $"Plant {i}";
                plant.transform.SetParent(root.transform, false);
                Vector3 pos = alongX
                    ? new Vector3(offset, -size.y * 0.5f + rimHeight + plantHeight * 0.5f, 0f)
                    : new Vector3(0f, -size.y * 0.5f + rimHeight + plantHeight * 0.5f, offset);
                plant.transform.localPosition = pos;
                plant.transform.localRotation = Quaternion.Euler(0f, i * 47f, 0f);
                plant.transform.localScale = new Vector3(size.x >= size.z ? 0.12f : size.x * 0.4f, plantHeight,
                    size.x >= size.z ? size.z * 0.4f : 0.12f);
                StripColliders(plant);
                var plantRend = plant.GetComponent<Renderer>();
                if (plantRend != null) plantRend.sharedMaterial = WorldMaterials.M_BioGlow;
            }

            return root;
        }

        /// <summary>L — a low, see-through pipe run on brackets (ticket table: 1xN, see-through). The
        /// pipe is built lying along whichever axis is the footprint's longer one, same rotate-the-
        /// mesh idiom <see cref="BuildWallLamps"/> already uses for "along X or along Z".</summary>
        public static GameObject BuildFloorPipe(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefFloorPipe");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            bool alongX = size.x >= size.z;
            float runLength = alongX ? size.x : size.z;
            float diameter = size.y;

            var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pipe.name = "Pipe";
            pipe.transform.SetParent(root.transform, false);
            pipe.transform.localPosition = new Vector3(0f, 0f, 0f);
            pipe.transform.localRotation = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
            pipe.transform.localScale = new Vector3(diameter, runLength * 0.5f, diameter);
            StripColliders(pipe);
            var pipeRend = pipe.GetComponent<Renderer>();
            if (pipeRend != null) pipeRend.sharedMaterial = WorldMaterials.M_MetalDark;

            int brackets = Mathf.Max(2, Mathf.FloorToInt(runLength / 1f) + 1);
            for (int i = 0; i < brackets; i++)
            {
                float t = brackets <= 1 ? 0f : (float)i / (brackets - 1) - 0.5f;
                float offset = t * runLength;

                var bracket = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bracket.name = $"Bracket {i}";
                bracket.transform.SetParent(root.transform, false);
                Vector3 pos = alongX
                    ? new Vector3(offset, -diameter * 0.5f, 0f)
                    : new Vector3(0f, -diameter * 0.5f, offset);
                bracket.transform.localPosition = pos;
                bracket.transform.localScale = new Vector3(
                    alongX ? FloorPipeBracketWidth : diameter * 0.8f,
                    diameter,
                    alongX ? diameter * 0.8f : FloorPipeBracketWidth);
                StripColliders(bracket);
                var bracketRend = bracket.GetComponent<Renderer>();
                if (bracketRend != null) bracketRend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;
            }

            return root;
        }

        // MV-1215, Change item 2: the glow hatch — a recessed, collider-free floor decal. Walkable by
        // construction: nothing built here ever gets a collider, same contract every other Reef
        // dressing piece keeps for its own visual-only overlays.
        private const float HatchRecess = 0.06f;
        private const float HatchRimThickness = 0.04f;
        private const float HatchRimProud = 0.1f;

        /// <summary>H — a recessed floor hatch with a cyan glow rim, <paramref name="w"/> x
        /// <paramref name="d"/> (ticket: 2x2). Collider-free and walkable — a decoration, not an
        /// obstacle.</summary>
        public static GameObject BuildGlowHatch(Transform parent, Vector3 center, float w, float d)
        {
            var root = new GameObject("ReefGlowHatch");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(root.transform, false);
            panel.transform.localPosition = new Vector3(0f, -HatchRecess * 0.5f, 0f);
            panel.transform.localScale = new Vector3(w, HatchRecess, d);
            StripColliders(panel);
            var panelRend = panel.GetComponent<Renderer>();
            if (panelRend != null) panelRend.sharedMaterial = WorldMaterials.M_MetalDark;

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rim.name = "Rim";
            rim.transform.SetParent(root.transform, false);
            rim.transform.localPosition = new Vector3(0f, -HatchRecess - HatchRimThickness * 0.5f, 0f);
            rim.transform.localScale = new Vector3(w + HatchRimProud, HatchRimThickness, d + HatchRimProud);
            StripColliders(rim);
            var rimRend = rim.GetComponent<Renderer>();
            if (rimRend != null) rimRend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;

            return root;
        }

        // MV-1215, Change item 6 (decided 17 Oct 2026): wall-mounted dressing — racks, planters or
        // pipes, mounted within the wall's own thickness. <paramref name="size"/>.x/.z follow the
        // wall's own run/thickness axes exactly as MaxWorlds.Arena.ReefAreaKit resolves them; .y is
        // the mounted band's own vertical extent. Collider-free throughout.

        /// <summary>Shallow cargo racks with crates, for a cargo-hold area's own <c>wallDressing:
        /// "racks"</c>.</summary>
        public static GameObject BuildWallRacks(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefWallRacks");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backing.name = "Backing";
            backing.transform.SetParent(root.transform, false);
            backing.transform.localScale = size;
            StripColliders(backing);
            var backingRend = backing.GetComponent<Renderer>();
            if (backingRend != null) backingRend.sharedMaterial = WorldMaterials.M_MetalDark;

            bool alongX = size.x >= size.z;
            float runLength = alongX ? size.x : size.z;
            int crates = Mathf.Max(1, Mathf.FloorToInt(runLength / 1.2f));
            for (int i = 0; i < crates; i++)
            {
                float t = (i + 0.5f) / crates - 0.5f;
                float offset = t * runLength;

                var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crate.name = $"Crate {i}";
                crate.transform.SetParent(root.transform, false);
                crate.transform.localPosition = alongX ? new Vector3(offset, 0f, 0f) : new Vector3(0f, 0f, offset);
                crate.transform.localScale = new Vector3(
                    alongX ? size.x / crates * 0.7f : size.x * 0.8f,
                    size.y * 0.6f,
                    alongX ? size.z * 0.8f : size.z / crates * 0.7f);
                StripColliders(crate);
                var crateRend = crate.GetComponent<Renderer>();
                if (crateRend != null) crateRend.sharedMaterial = WorldMaterials.M_CrateBody;
            }

            return root;
        }

        /// <summary>Wall-hung planters with bioluminescent green plants, for a window-gallery/crew/
        /// hydroponics area's own <c>wallDressing: "planters"</c>.</summary>
        public static GameObject BuildWallPlanters(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefWallPlanters");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Box";
            box.transform.SetParent(root.transform, false);
            box.transform.localScale = new Vector3(size.x, size.y * 0.4f, size.z);
            StripColliders(box);
            var boxRend = box.GetComponent<Renderer>();
            if (boxRend != null) boxRend.sharedMaterial = WorldMaterials.M_MetalDark;

            var plant = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plant.name = "Plant";
            plant.transform.SetParent(root.transform, false);
            plant.transform.localPosition = new Vector3(0f, size.y * 0.3f, 0f);
            plant.transform.localScale = new Vector3(size.x * 0.3f, size.y * 0.6f, size.z * 0.3f);
            StripColliders(plant);
            var plantRend = plant.GetComponent<Renderer>();
            if (plantRend != null) plantRend.sharedMaterial = WorldMaterials.M_BioGlow;

            return root;
        }

        /// <summary>Horizontal pipe runs with brackets, for an engine-room/reactor-bay/pressure-lock
        /// area's own <c>wallDressing: "pipes"</c>.</summary>
        public static GameObject BuildWallPipes(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefWallPipes");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            bool alongX = size.x >= size.z;
            float runLength = alongX ? size.x : size.z;
            float diameter = Mathf.Min(size.y * 0.3f, size.z);

            var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pipe.name = "Pipe";
            pipe.transform.SetParent(root.transform, false);
            pipe.transform.localRotation = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
            pipe.transform.localScale = new Vector3(diameter, runLength * 0.5f, diameter);
            StripColliders(pipe);
            var pipeRend = pipe.GetComponent<Renderer>();
            if (pipeRend != null) pipeRend.sharedMaterial = WorldMaterials.M_MetalDark;

            return root;
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
            MarkDressed(go);

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
            MarkDressed(root);

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
            MarkDressed(root);

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

        /// <summary>Every built wall under <paramref name="host"/>, found by its
        /// <see cref="StructuralWall"/> tag directly — NOT <see cref="WorldMaterials.IsWorldSurface"/>
        /// (the same MV-1063 trap <see cref="FindFloorRenderer"/>'s own doc already documents: a wall
        /// <see cref="DressHull"/> has already dressed carries <see cref="KeepsOwnMaterial"/>, which
        /// that check explicitly excludes). For a pass that runs AFTER <see cref="DressHull"/>, to place
        /// area-authored dressing (<see cref="MaxWorlds.Arena.ReefAreaKit"/>) against the real wall
        /// geometry (MV-1215).</summary>
        public static List<StructuralWall> CollectStructuralWalls(Transform host)
        {
            var list = new List<StructuralWall>();
            if (host == null) return list;
            list.AddRange(host.GetComponentsInChildren<StructuralWall>(true));
            return list;
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
