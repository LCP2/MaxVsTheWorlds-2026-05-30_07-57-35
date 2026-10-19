using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

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

    /// <summary>
    /// Animates one <see cref="ReefKit.BuildOceanBackdrop"/> group (MV-1257, Change item 3: "animated
    /// fish shoals crossing the deep, and roughly every 60-90 s one large whale silhouette gliding past
    /// one visible window"). Drifts the FishSchool/Jellyfish/Manta layers gently side-to-side/up-down in
    /// place, and keeps the Whale layer disabled except for a brief glide across the window on its own
    /// seeded 60-90 s cycle — hidden rather than just parked off to the side, since a static silhouette
    /// sitting in frame the other 98% of the time would read as a fixture, not a passing animal.
    ///
    /// Lives here, not in <c>MaxWorlds.VFX</c>, because <c>MaxWorlds.Rendering.asmdef</c> references
    /// only Core and the render pipeline packages (see this file's own header comment on the
    /// WHERE/WHAT split) — it cannot see anything under <c>Runtime/VFX</c>, so this carries its own
    /// tiny hash rather than reusing <c>MaxWorlds.VFX.SludgeBubbles</c>'s private one.
    /// Visual only: <c>Update</c> never runs outside Play mode, so nothing here is EditMode-tested
    /// (testing policy Rule 2 — motion over time is a Tier 3 rendered-pixel concern, not a resolved
    /// value).
    /// </summary>
    [MaxWorlds.Core.PerfSection("rendering")]
    public sealed class OceanBackdropLife : MonoBehaviour
    {
        private const float WhaleCycleMinSeconds = 60f;
        private const float WhaleCycleMaxSeconds = 90f;
        private const float WhalePassSeconds = 7f;

        private Transform _fish, _jelly, _manta, _whale;
        private Vector3 _fishBase, _jellyBase, _mantaBase;
        private float _whaleCycleSeconds;
        private float _whalePhase;
        private bool _whaleActive;

        public void Configure(Transform fish, Transform jelly, Transform manta, Transform whale, int seed)
        {
            _fish = fish;
            _jelly = jelly;
            _manta = manta;
            _whale = whale;

            if (_fish != null) _fishBase = _fish.localPosition;
            if (_jelly != null) _jellyBase = _jelly.localPosition;
            if (_manta != null) _mantaBase = _manta.localPosition;

            _whaleCycleSeconds = Mathf.Lerp(WhaleCycleMinSeconds, WhaleCycleMaxSeconds, Hash01(seed, 0));
            _whalePhase = Hash01(seed, 1) * _whaleCycleSeconds;

            if (_whale != null) _whale.gameObject.SetActive(false);
        }

        private void Update()
        {
            float t = Time.time;

            if (_fish != null)
                _fish.localPosition = _fishBase + Vector3.right * Mathf.Sin(t * 0.18f) * 1.4f;
            if (_jelly != null)
                _jelly.localPosition = _jellyBase + Vector3.up * Mathf.Sin(t * 0.25f + 1.3f) * 0.3f;
            if (_manta != null)
                _manta.localPosition = _mantaBase + Vector3.right * Mathf.Sin(t * 0.12f + 2.1f) * 1.8f;

            if (_whale == null) return;

            float u = Mathf.Repeat((t + _whalePhase) / _whaleCycleSeconds, 1f);
            float passFraction = WhalePassSeconds / _whaleCycleSeconds;
            bool shouldBeActive = u < passFraction;

            if (shouldBeActive != _whaleActive)
            {
                _whaleActive = shouldBeActive;
                _whale.gameObject.SetActive(shouldBeActive);
            }

            if (shouldBeActive)
            {
                float p = u / passFraction;
                Vector3 pos = _whale.localPosition;
                pos.x = Mathf.Lerp(-6f, 6f, p);
                _whale.localPosition = pos;
            }
        }

        private static float Hash01(int seed, int i)
        {
            unchecked
            {
                int h = seed * 374761393 + i * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }
    }

    /// <summary>Marks one hydroponic bed (MV-1055) so an EditMode test — or any future gameplay
    /// query — can find every one <c>MaxWorlds.Arena.ReefHydroponics</c> placed without walking the
    /// whole scene by name. Carries no data of its own: a bed's shape and position are baked into
    /// its built geometry, same as every other Reef dressing piece.</summary>
    public sealed class HydroponicBed : MonoBehaviour
    {
    }

    /// <summary>Records the resolved circuit-inlay pattern layout <see cref="ReefKit.ApplyDeckTiling"/>
    /// baked into the floor it's attached to (MV-1256, AC1) — a RESOLVED value (testing policy Tier 2)
    /// an EditMode test can read straight off the built floor, rather than re-deriving
    /// <see cref="StylizedTextures.ReefDeckPatternIndexAt"/>'s own hash a second time in test code.
    /// One entry per cell of the baked supertile, row-major (<c>gy * ReefDeckGridCells + gx</c>).
    /// </summary>
    public sealed class ReefDeckPlateField : MonoBehaviour
    {
        public int[] PatternIndices;
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
            BuildWallDetail(host, walls);
            BuildCornerColumns(host, walls);
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
        ///
        /// MV-1256: the baked image is no longer one plate but a <see cref="WorldMaterials.ReefDeckGridCells"/>-
        /// square "supertile" of individually varied plates (<see cref="StylizedTextures.ReefDeckAlbedo"/>/
        /// <see cref="StylizedTextures.ReefDeckEmission"/>), so the repeat period this sets is
        /// <c>ReefDeckPlateSizeMetres * ReefDeckGridCells</c>, not one plate's own size — the floor
        /// still reads as 2 m plates (the mesh is just as fine-grained as before), only the texture
        /// that no longer repeats every single one of them. Also attaches a
        /// <see cref="ReefDeckPlateField"/> recording the supertile's own resolved pattern layout
        /// (AC1) — same real hash <see cref="StylizedTextures.ReefDeckPatternIndexAt"/> the texture
        /// bake itself used, read back rather than re-derived.</summary>
        private static void ApplyDeckTiling(Renderer floorRenderer)
        {
            Vector3 size = floorRenderer.bounds.size;
            float plate = WorldMaterials.ReefDeckPlateSizeMetres;
            int gridCells = WorldMaterials.ReefDeckGridCells;
            float supertile = plate * gridCells;
            Vector2 tiling = new Vector2(
                Mathf.Max(1f, size.x / supertile),
                Mathf.Max(1f, size.z / supertile));

            // SetTextureScale("_BaseMap", ...), not the mainTextureScale shortcut: that shortcut
            // targets whichever property carries the shader's [MainTexture] tag (or _MainTex if none
            // does), and URP/Lit's own Properties block never tags _BaseMap with it — mainTextureScale
            // silently no-ops on this shader. Naming the property directly is what actually reaches
            // the _BaseMap_ST the shader samples through.
            floorRenderer.sharedMaterial.SetTextureScale("_BaseMap", tiling);

            var patterns = new int[gridCells * gridCells];
            for (int gy = 0; gy < gridCells; gy++)
            for (int gx = 0; gx < gridCells; gx++)
                patterns[gy * gridCells + gx] = StylizedTextures.ReefDeckPatternIndexAt(gx, gy);

            var field = floorRenderer.GetComponent<ReefDeckPlateField>();
            if (field == null) field = floorRenderer.gameObject.AddComponent<ReefDeckPlateField>();
            field.PatternIndices = patterns;
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

                Vector3 size = new Vector3(scale.x + CircuitStripProud, CircuitStripHeight, scale.z + CircuitStripProud);
                GameObject strip = MeshBox(root.transform, $"{wt.name} Circuit", Vector3.zero, size, WorldMaterials.M_Circuit_Cyan);
                strip.transform.position = new Vector3(wt.position.x, CircuitStripHeight * 0.5f, wt.position.z);
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
            Vector3 size = new Vector3(wallScale.x + GlassProud, barHeight, wallScale.z + GlassProud);
            GameObject go = MeshBox(parent, name, Vector3.zero, size, material);
            go.transform.position = new Vector3(wallPosition.x, wallPosition.y + yOffset, wallPosition.z);
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

        // MV-1255: the room walls' own ribs/panels/conduit, a per-room magenta far-wall strip and
        // heavy corner columns — upgrades DressHull's own edge-wall dressing past the plain slab +
        // base trim it used to leave a 3 m Reef wall as (Lee, 19 Oct 2026: "make it amazing").
        // Generated meshes only (ticket: "Do not re-raise ... primitives for visible parts"), same
        // vocabulary as MV-1251's bulkhead rebuild.
        private const float WallRibPitch = 3f;          // ticket: "structural ribs every 3 m"
        private const float WallRibRadius = 0.09f;
        private const float WallRibProud = 0.05f;       // how far a rib stands proud of its wall face
        private const float WallPanelSeamWidth = 0.04f;
        private const float WallPanelSeamProud = 0.015f; // same anti-z-fight idiom as CircuitStripProud
        private const float WallConduitHeightFraction = 0.6f; // ticket: "about 60% of wall height"
        private const float WallConduitRadius = 0.05f;
        private const float CornerColumnDiameterFactor = 1.6f; // column footprint, relative to wall thickness
        private const float CornerClusterTolerance = 0.6f;     // metres - endpoints this close share one corner

        /// <summary>Ribs, recessed-panel seams and a conduit run on BOTH long faces of every built
        /// wall — an interior partition borders two rooms, and the design frames show the dressed
        /// read from inside either one, so there is no single "front" face to pick.</summary>
        private static void BuildWallDetail(Transform host, List<StructuralWall> walls)
        {
            foreach (StructuralWall wall in walls)
            {
                Transform wt = wall.transform;
                Vector3 scale = wt.lossyScale;
                bool alongX = scale.x >= scale.z;
                float faceLength = alongX ? scale.x : scale.z;
                float faceDepth = alongX ? scale.z : scale.x;

                var root = new GameObject($"{wt.name} Wall Detail");
                root.transform.SetParent(host, false);
                root.transform.position = wt.position;
                MarkDressed(root);

                foreach (int side in new[] { -1, 1 })
                    BuildWallFaceDetail(root.transform, alongX, faceLength, faceDepth, scale.y, side);
            }
        }

        /// <summary>One face's own ribs (standing proud, chamfered), the seam line down the centre of
        /// each recessed bay between them, and one horizontal conduit crossing the face at
        /// <see cref="WallConduitHeightFraction"/> of the wall's own height — all local to
        /// <paramref name="root"/>, which <see cref="BuildWallDetail"/> already positioned at the
        /// wall's own world centre, same "root carries the position, children are pure local offsets"
        /// idiom <see cref="BuildBulkheadFace"/> already uses for the hull-fill blocks.</summary>
        private static void BuildWallFaceDetail(Transform root, bool alongX, float faceLength, float faceDepth,
            float height, int side)
        {
            Vector3 faceNormal = alongX ? new Vector3(0f, 0f, side) : new Vector3(side, 0f, 0f);
            float faceOffset = faceDepth * 0.5f + WallRibProud;

            int ribs = Mathf.Max(2, Mathf.FloorToInt(faceLength / WallRibPitch) + 1);
            Mesh ribMesh = CharacterMeshes.Prism(4, WallRibRadius, WallRibRadius, height, 0.12f);

            for (int i = 0; i < ribs; i++)
            {
                float t = ribs <= 1 ? 0f : (float)i / (ribs - 1) - 0.5f;
                float along = t * faceLength;
                Vector3 ribPos = faceNormal * faceOffset + (alongX ? new Vector3(along, 0f, 0f) : new Vector3(0f, 0f, along));
                MeshPart(root, "Wall Rib", ribMesh, ribPos, WorldMaterials.M_MetalDark);

                if (i < ribs - 1)
                {
                    float nextAlong = (ribs <= 1 ? 0f : (float)(i + 1) / (ribs - 1) - 0.5f) * faceLength;
                    float mid = (along + nextAlong) * 0.5f;
                    BuildWallPanelSeam(root, alongX, faceNormal, faceDepth, mid, height);
                }
            }

            BuildWallConduit(root, alongX, faceNormal, faceOffset, faceLength, height);
        }

        /// <summary>A thin seam line flush with the wall's own face, down the centre of the recessed
        /// bay between two ribs (ticket: "recessed panels between the ribs, with seam lines") — the
        /// generated-mesh substitute for carving the panel itself, which the wall's own single body
        /// mesh has no geometry budget to do per-ticket ("keep everything within the wall's own
        /// footprint").</summary>
        private static void BuildWallPanelSeam(Transform root, bool alongX, Vector3 faceNormal, float faceDepth,
            float along, float height)
        {
            float seamOffset = faceDepth * 0.5f + WallPanelSeamProud;
            Vector3 pos = faceNormal * seamOffset + (alongX ? new Vector3(along, 0f, 0f) : new Vector3(0f, 0f, along));
            Vector3 size = alongX
                ? new Vector3(WallPanelSeamWidth, height * 0.86f, WallPanelSeamProud * 2f)
                : new Vector3(WallPanelSeamProud * 2f, height * 0.86f, WallPanelSeamWidth);
            MeshBox(root, "Wall Panel Seam", pos, size, WorldMaterials.M_MetalDark);
        }

        /// <summary>One horizontal conduit run the full length of the face (ticket: "one horizontal
        /// conduit run at about 60% of wall height").</summary>
        private static void BuildWallConduit(Transform root, bool alongX, Vector3 faceNormal, float faceOffset,
            float faceLength, float height)
        {
            float y = height * (WallConduitHeightFraction - 0.5f);
            Vector3 pos = faceNormal * faceOffset + new Vector3(0f, y, 0f);
            Quaternion lie = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
            Mesh pipe = CharacterMeshes.Beam(faceLength * 0.94f, WallConduitRadius, WallConduitRadius, 8);
            MeshPart(root, "Wall Conduit", pipe, pos, WorldMaterials.M_MetalDark, lie);
        }

        /// <summary>A heavy column at every point where two or more DISTINCT built walls' own
        /// endpoints meet (ticket: "every inner corner gets a heavy column") — found by clustering
        /// every wall's two endpoints rather than threaded in as authored room-adjacency data, the
        /// same "ask the built geometry, not the author data" idiom <see cref="ApplyDeckTiling"/>
        /// already uses. Reuses <see cref="BuildPillar"/> outright: "a chamfered 8-sided Prism column
        /// ... with banding" is exactly what it already builds.</summary>
        private static void BuildCornerColumns(Transform host, List<StructuralWall> walls)
        {
            var endpoints = new List<(Vector3 pos, float thickness, float height, StructuralWall wall)>();
            foreach (StructuralWall wall in walls)
            {
                Transform wt = wall.transform;
                Vector3 scale = wt.lossyScale;
                bool alongX = scale.x >= scale.z;
                float length = alongX ? scale.x : scale.z;
                float thickness = alongX ? scale.z : scale.x;

                Vector3 a = alongX
                    ? new Vector3(wt.position.x - length * 0.5f, wt.position.y, wt.position.z)
                    : new Vector3(wt.position.x, wt.position.y, wt.position.z - length * 0.5f);
                Vector3 b = alongX
                    ? new Vector3(wt.position.x + length * 0.5f, wt.position.y, wt.position.z)
                    : new Vector3(wt.position.x, wt.position.y, wt.position.z + length * 0.5f);

                endpoints.Add((a, thickness, scale.y, wall));
                endpoints.Add((b, thickness, scale.y, wall));
            }

            var placed = new List<Vector3>();
            foreach (var ep in endpoints)
            {
                var meeting = new HashSet<StructuralWall>();
                foreach (var other in endpoints)
                    if (FlatDistance(ep.pos, other.pos) <= CornerClusterTolerance) meeting.Add(other.wall);
                if (meeting.Count < 2) continue;

                bool already = false;
                foreach (Vector3 p in placed)
                    if (FlatDistance(p, ep.pos) <= CornerClusterTolerance) { already = true; break; }
                if (already) continue;

                placed.Add(ep.pos);
                float diameter = ep.thickness * CornerColumnDiameterFactor;
                BuildPillar(host, ep.pos, new Vector3(diameter, ep.height, diameter));
            }
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private const float FarWallStripHeight = 0.12f;
        private const float FarWallStripProud = 0.02f;

        /// <summary>A magenta emissive strip along the TOP of ONE specific wall (ticket: "a magenta
        /// emissive strip along the TOP of each room's far wall (the north wall, the one the camera
        /// faces most)"). Per-ROOM, not per-map: <see cref="MaxWorlds.Arena.ReefAreaKit"/> is the
        /// caller, since picking "this room's own north wall" needs <c>WorldArea</c> data this
        /// Rendering-assembly class has no reference to — the same WHERE/WHAT split
        /// <see cref="BuildGlowHatch"/>/<see cref="BuildObservationWindow"/> already keep with
        /// <c>ReefAreaKit</c>. Distinct from <see cref="BuildObservationGlass"/>'s cyan top strip
        /// (outer-hull walls only) and <see cref="BuildCircuitSpine"/>'s cyan BASE trim (every wall):
        /// this one is magenta, top-mounted, and per-room.</summary>
        public static void BuildFarWallStrip(Transform host, Transform wall, float wallHeight)
        {
            if (host == null || wall == null) return;

            Vector3 scale = wall.lossyScale;
            Vector3 size = new Vector3(scale.x + FarWallStripProud, FarWallStripHeight, scale.z + FarWallStripProud);
            GameObject strip = MeshBox(host, $"{wall.name} Far Wall Strip", Vector3.zero, size, WorldMaterials.M_KelpMagenta);
            strip.transform.position = new Vector3(
                wall.position.x, wall.position.y + wallHeight * 0.5f - FarWallStripHeight * 0.5f, wall.position.z);
            MarkDressed(strip);
        }

        private const float GateFrameThickness = 0.14f;
        private const float GateFrameProud = 0.03f;
        private const float GateFrameEdgeMarkWidth = 0.06f;

        /// <summary>A reinforced surround for a gate opening (ticket: "a reinforced frame (thick
        /// ribbed surround plus hazard-orange edge marks) around each gate opening, consistent with
        /// the existing Reef gate skin") — two ribbed posts (chamfered 4-sided Prism, same "ribbed"
        /// vocabulary <see cref="BuildPillar"/>/<see cref="BuildBulkheadFace"/> already use) plus a
        /// header, with a hazard-orange edge mark down each post's outer edge. Built in the caller's
        /// own already-cancelled local space (<see cref="MaxWorlds.Arena.AreaGate"/>'s own inverse-
        /// scale root, the same idiom its <c>ApplyCitySkin</c> uses — see that method's own doc), so
        /// <paramref name="width"/>/<paramref name="height"/>/<paramref name="depth"/> are the
        /// doorway's own real metres regardless of which wall the gate sits on.</summary>
        public static void BuildGateFrame(Transform parent, float width, float height, float depth)
        {
            Mesh post = CharacterMeshes.Prism(4, GateFrameThickness * 0.5f, GateFrameThickness * 0.5f, height, 0.15f);

            foreach (int side in new[] { -1, 1 })
            {
                float x = side * (width * 0.5f + GateFrameThickness * 0.5f - GateFrameProud);
                MeshPart(parent, "Gate Frame Post", post, new Vector3(x, 0f, 0f), WorldMaterials.M_MetalDark);

                Vector3 markSize = new Vector3(GateFrameEdgeMarkWidth, height * 0.92f, depth + GateFrameProud);
                MeshBox(parent, "Gate Frame Edge Mark", new Vector3(x + side * GateFrameThickness * 0.5f, 0f, 0f),
                    markSize, WorldMaterials.M_Hazard);
            }

            Mesh header = CharacterMeshes.Prism(4, GateFrameThickness * 0.5f, GateFrameThickness * 0.5f,
                width + GateFrameThickness, 0.15f);
            MeshPart(parent, "Gate Frame Header", header,
                new Vector3(0f, height * 0.5f + GateFrameThickness * 0.5f - GateFrameProud, 0f),
                WorldMaterials.M_MetalDark, Quaternion.Euler(0f, 0f, 90f));
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

        /// <summary>0.5/cos(45deg) == cos(45deg) (the two happen to be numerically identical, both
        /// sqrt(2)/2): a 4-sided <see cref="CharacterMeshes.Prism"/> built at this radius, height 1,
        /// lands its flat faces exactly +-0.5 from the axis on every side and spans -0.5..+0.5 in
        /// height too — a UNIT mesh matching Unity's own primitive cube convention exactly (a cube
        /// primitive is also -0.5..+0.5 on every axis). <see cref="ApplyCrateSkin"/> uses it so the
        /// crate's own <c>transform.localScale</c> can carry the real metres directly, on all three
        /// axes independently, the same way a bare <c>CreatePrimitive(Cube)</c> + <c>localScale = size</c>
        /// always did — which is what makes a non-square crate (most of World 3's are rectangular, not
        /// square) still read at its own full authored footprint rather than an averaged square.
        /// </summary>
        private const float CrateMeshUnitRadius = 0.70710678f;

        /// <summary>MV-1251: the fraction of a crate body's own height the Prism chamfer eats at each
        /// end — bevels only the horizontal top/bottom rim, keeping the four vertical corners sharp,
        /// the same "machined housing" read <see cref="BuildPillar"/> and <see cref="BuildConsole"/>'s
        /// own Prism bodies already carry.</summary>
        private const float CrateBodyChamfer = 0.08f;

        /// <summary>Recolours a bare "crate" cover piece with its own Reef material (MV-1019) — a
        /// blue-grey steel body plus orange accent caps at its four vertical edges. Runs in place of
        /// leaving the piece to the general shape-classified sweep, which painted it
        /// <see cref="BiomePalette.Reef"/>'s Prop tone (#0C1622) — darker than the floor it sits on.
        ///
        /// MV-1251: the body's own mesh is rebuilt as a chamfered 4-sided <see cref="CharacterMeshes.Prism"/>
        /// (ticket: "a Prism with 4 sides and a chamfer ... instead of a sharp cube") rather than left
        /// at whatever <c>MaxWorlds.Arena.Map.MapRuntime.ApplyBevelledBoxMesh</c> (MV-778) already gave
        /// it. That mesh is built UNIT-sized (<see cref="CrateMeshUnitRadius"/>) and the body's own
        /// <c>transform.localScale</c> is set back to its real metres — read off its live
        /// <see cref="BoxCollider"/>, which MV-778 had set to those metres while leaving the GameObject's
        /// own scale at <see cref="Vector3.one"/> — with the collider's own <c>size</c> correspondingly
        /// reset to <see cref="Vector3.one"/>, so the collider's WORLD-SPACE extent
        /// (<c>transform.localScale * collider.size</c>) is bit-identical before and after: this
        /// ticket's own "colliders ... unchanged" contract holds on the only thing that actually matters
        /// for gameplay (what Max can stand on/behind), even though which field carries the metres
        /// flips. A single Prism radius can only ever describe a SQUARE cross-section, so the mesh must
        /// get its rectangular footprint from a non-uniform scale, not from the radius itself — most of
        /// World 3's crates are rectangular, not square (MV-1052's own a10 fixture caught the earlier,
        /// averaged-square version of this undercovering an elongated crate's footprint and failing its
        /// gate). Collider and geometry are otherwise untouched: only the body's renderer is re-pointed,
        /// and the four caps are new, collider-free children, same "collider stays, art swaps" contract
        /// every other Reef dressing case keeps. <paramref name="addCornerCaps"/> is the caller's own
        /// <c>CoverShape.Box</c> check (a bool, not the enum itself — <c>MaxWorlds.Rendering</c> has no
        /// reference to the assembly <c>CoverShape</c> lives in) — a box's corners have edges to accent,
        /// a cylinder has none.</summary>
        public static void ApplyCrateSkin(GameObject crateBody, bool addCornerCaps)
        {
            if (crateBody == null) return;

            var meshFilter = crateBody.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                var bodyCollider = crateBody.GetComponent<BoxCollider>();
                Vector3 realSize = bodyCollider != null ? bodyCollider.size : crateBody.transform.localScale;
                meshFilter.sharedMesh = CharacterMeshes.Prism(4, CrateMeshUnitRadius, CrateMeshUnitRadius, 1f, CrateBodyChamfer);
                crateBody.transform.localScale = realSize;
                if (bodyCollider != null) bodyCollider.size = Vector3.one;
            }

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

            // Same "fraction of the parent's own local unit cube" idiom the cube primitive these caps
            // used to be relied on: crateBody's own cumulative scale does the metric conversion, so the
            // mesh itself only needs to be the generated-mesh substitute for that unit cube.
            Mesh capMesh = CharacterMeshes.Bevelled(Vector3.one, CrateCapProud * 4f);
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                MeshPart(caps.transform, "CrateCap", capMesh,
                    new Vector3(
                        sx * (0.5f - (capX / size.x) * 0.5f),
                        0f,
                        sz * (0.5f - (capZ / size.z) * 0.5f)),
                    WorldMaterials.M_CrateCap,
                    localScale: new Vector3(
                        capX / size.x + CrateCapProud, 1f + CrateCapProud, capZ / size.z + CrateCapProud));
            }
        }

        // MV-1215: the Reef's own structure kit (Lee's 17 Oct 2026 concept-frame approval). MV-1251
        // rebuilt every builder below from MaxWorlds.VFX.CharacterMeshes' generated-mesh vocabulary
        // (Lathe/Prism/Beam/Ring/Sphere/Bevelled) — ZERO GameObject.CreatePrimitive calls remain in
        // this section (Lee, 19 Aug 2026: primitives read "glued together from cardboard boxes and
        // tubes"). Each builder is called by MaxWorlds.Arena.ReefDressing with the cover piece's own
        // authored centre/size, in place of a hidden body renderer — the same "collider stays, art
        // swaps" contract BuildCoolantTurret already keeps. Every piece here is a fresh, collider-free
        // GameObject tree (no CreatePrimitive ever ran on it, so there is nothing to strip), built at
        // literal metre offsets under an identity-scale piece root exactly as before.

        private const float PillarSeamWidth = 0.08f;
        private const float ManifoldPipeRadius = 0.12f;
        private const float ConsoleScreenProud = 0.01f;
        private const float PlanterRimHeight = 0.5f;   // fraction of the piece's own authored height
        private const float FloorPipeBracketWidth = 0.06f;

        /// <summary>1/cos(45deg): <see cref="CharacterMeshes.Prism"/>'s own "+PI/sides" rotation for a
        /// 4-sided prism lands its flat faces exactly <c>radius * cos(45deg)</c> from the axis on each
        /// side — the same figure <c>MaxWorlds.VFX.FactoryBodies.BuildReplicator</c>'s own "Hull" uses
        /// to turn a unit square into a non-uniformly-scaled rectangle. <see cref="BuildConsole"/>'s
        /// desk body is the one piece in this file that still needs a rectangular footprint from a
        /// 4-sided Prism.</summary>
        private const float Diag = 1.41421356f;

        /// <summary>A collider-free generated-mesh part — the replacement for every
        /// <c>GameObject.CreatePrimitive</c> call this file used to make. Never carries a collider (no
        /// primitive ever ran here to add one), so there is nothing for <see cref="StripColliders"/>
        /// to strip.</summary>
        private static GameObject MeshPart(Transform parent, string name, Mesh mesh, Vector3 localPosition,
            Material material, Quaternion? localRotation = null, Vector3? localScale = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation ?? Quaternion.identity;
            go.transform.localScale = localScale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = material;
            return go;
        }

        /// <summary>A collider-free chamfered box at literal metre <paramref name="size"/> — the
        /// generated-mesh substitute for <c>CreatePrimitive(PrimitiveType.Cube)</c> plus a
        /// <c>localScale = size</c>. Same mesh <see cref="StormdrainKit.Box"/> already builds for
        /// World 2's own kit.</summary>
        private static GameObject MeshBox(Transform parent, string name, Vector3 localPosition, Vector3 size,
            Material material)
            => MeshPart(parent, name, CharacterMeshes.Bevelled(size, CharacterMeshes.DefaultBevel(size)),
                localPosition, material);

        /// <summary>P — a chamfered 8-sided <see cref="CharacterMeshes.Prism"/> column: a recessed
        /// shaft between a heavier base and cap, three rib bands (<see cref="CharacterMeshes.Ring"/>)
        /// at even heights, and a recessed cyan seam light (<see cref="CharacterMeshes.Beam"/>) running
        /// the full height on one face (ticket table: 2x2, solid).</summary>
        public static GameObject BuildPillar(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefPillar");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float radius = Mathf.Min(size.x, size.z) * 0.5f;

            Mesh shaft = CharacterMeshes.Prism(8, radius * 0.82f, radius * 0.82f, size.y * 0.84f, 0.12f);
            MeshPart(root.transform, "Body", shaft, Vector3.zero, WorldMaterials.M_MetalDark);

            Mesh baseCap = CharacterMeshes.Prism(8, radius, radius, size.y * 0.10f, 0.22f);
            MeshPart(root.transform, "Base", baseCap, new Vector3(0f, -size.y * 0.45f, 0f), WorldMaterials.M_MetalDark);

            Mesh topCap = CharacterMeshes.Prism(8, radius, radius, size.y * 0.08f, 0.22f);
            MeshPart(root.transform, "Cap", topCap, new Vector3(0f, size.y * 0.46f, 0f), WorldMaterials.M_MetalDark);

            Mesh rib = CharacterMeshes.Ring(radius * 0.8f, radius * 0.88f, 0.05f, 8);
            for (int i = 0; i < 3; i++)
            {
                float t = (i + 1f) / 4f - 0.5f;
                MeshPart(root.transform, $"Rib {i}", rib, new Vector3(0f, t * size.y * 0.7f, 0f), WorldMaterials.M_MetalDark);
            }

            float seamR = PillarSeamWidth * 0.5f;
            Mesh seam = CharacterMeshes.Beam(size.y * 0.92f, seamR, seamR, 6);
            MeshPart(root.transform, "Seam Light", seam, new Vector3(0f, 0f, radius * 0.82f + seamR), WorldMaterials.M_Circuit_Cyan);

            return root;
        }

        /// <summary>A lathed rounded flange bulb capping a manifold pipe's own end — the generated-mesh
        /// "U-bend" accent <see cref="BuildManifold"/> mounts at each pipe end.</summary>
        private static Mesh ManifoldFlangeMesh()
        {
            float r = ManifoldPipeRadius;
            return CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, -r),
                new Vector2(r, -r * 0.6f),
                new Vector2(r * 1.25f, 0f),
                new Vector2(r, r * 0.6f),
                new Vector2(0f, r),
            }, 12);
        }

        /// <summary>K — a low base housing plus a row of real pipes (<see cref="CharacterMeshes.Beam"/>),
        /// each capped at both ends with a lathed flanged "U-bend" collar (ticket table: 4x2, solid).
        /// Pipe count follows the piece's own authored width, same "ask the authored footprint, never a
        /// hardcoded guess" idiom <see cref="ApplyDeckTiling"/> already keeps for the deck's own tiling.
        /// </summary>
        public static GameObject BuildManifold(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefManifold");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float baseHeight = size.y * 0.3f;
            MeshBox(root.transform, "Base", new Vector3(0f, -size.y * 0.5f + baseHeight * 0.5f, 0f),
                new Vector3(size.x, baseHeight, size.z), WorldMaterials.M_MetalDark);

            int pipes = Mathf.Max(1, Mathf.FloorToInt(size.x / 1f));
            float pipeLength = size.z * 0.8f;
            float pipeY = -size.y * 0.5f + baseHeight + ManifoldPipeRadius;
            Mesh pipeMesh = CharacterMeshes.Beam(pipeLength, ManifoldPipeRadius, ManifoldPipeRadius, 10);
            Mesh flangeMesh = ManifoldFlangeMesh();

            for (int i = 0; i < pipes; i++)
            {
                float t = (i + 0.5f) / pipes - 0.5f;
                float px = t * size.x;

                MeshPart(root.transform, $"Pipe {i}", pipeMesh, new Vector3(px, pipeY, 0f),
                    WorldMaterials.M_MetalDark, Quaternion.Euler(90f, 0f, 0f));

                foreach (int sz in new[] { -1, 1 })
                    MeshPart(root.transform, "U-Bend", flangeMesh, new Vector3(px, pipeY, sz * pipeLength * 0.5f),
                        WorldMaterials.M_Circuit_Cyan);
            }

            return root;
        }

        /// <summary>A single continuous lathed tank profile: a flat base, two raised ribbed bands and a
        /// tapered domed top — one <see cref="CharacterMeshes.Lathe"/> call builds the whole silhouette
        /// as one turned form, with no seam anywhere along it.</summary>
        private static Mesh DrumTankProfile(float radius, float height)
        {
            float bandR = radius * 1.03f;
            return CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, 0f),
                new Vector2(radius, 0f),
                new Vector2(radius, height * 0.12f),
                new Vector2(bandR, height * 0.15f),
                new Vector2(radius, height * 0.18f),
                new Vector2(radius, height * 0.55f),
                new Vector2(bandR, height * 0.58f),
                new Vector2(radius, height * 0.61f),
                new Vector2(radius, height * 0.82f),
                new Vector2(radius * 0.82f, height * 0.94f),
                new Vector2(0f, height),
            }, 20);
        }

        /// <summary>D — a <see cref="CharacterMeshes.Lathe"/> tank with a domed top, two ribbed bands
        /// and a glowing cyan core window set into the dome (ticket table: 3x3, solid).
        /// <paramref name="size"/>'s X/Z is the tank's own diameter, same convention
        /// <see cref="ArenaCover"/> already uses for a cylinder's footprint.</summary>
        public static GameObject BuildDrum(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefDrum");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float radius = Mathf.Min(size.x, size.z) * 0.5f;
            Mesh tank = DrumTankProfile(radius, size.y);
            MeshPart(root.transform, "Tank", tank, new Vector3(0f, -size.y * 0.5f, 0f), WorldMaterials.M_MetalDark);

            float coreRadius = radius * 0.35f;
            Mesh core = CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, 0f),
                new Vector2(coreRadius, 0f),
                new Vector2(coreRadius, size.y * 0.05f),
                new Vector2(0f, size.y * 0.08f),
            }, 16);
            MeshPart(root.transform, "Core", core, new Vector3(0f, size.y * 0.47f, 0f), WorldMaterials.M_Circuit_Cyan);

            return root;
        }

        /// <summary>N — an angled control desk (ticket table: 2x1, solid): a chamfered 4-sided
        /// <see cref="CharacterMeshes.Prism"/> body with a glowing cyan screen riser and a small rim of
        /// key lights along its front edge. The "angle" reads from a stepped silhouette (low front
        /// desk, taller rear screen riser) rather than an arbitrary rotation — every other box piece in
        /// this file stays axis-aligned too.</summary>
        public static GameObject BuildConsole(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefConsole");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float deskHeight = size.y * 0.5f;
            Mesh desk = CharacterMeshes.Prism(4, Diag, Diag, deskHeight, 0.1f);
            MeshPart(root.transform, "Desk", desk, new Vector3(0f, -size.y * 0.5f + deskHeight * 0.5f, 0f),
                WorldMaterials.M_MetalDark, localScale: new Vector3(size.x * 0.5f, 1f, size.z * 0.5f));

            float screenHeight = size.y - deskHeight;
            MeshBox(root.transform, "Screen",
                new Vector3(0f, -size.y * 0.5f + deskHeight + screenHeight * 0.5f, -size.z * 0.5f + size.z * 0.2f),
                new Vector3(size.x * 0.85f, screenHeight, size.z * 0.4f + ConsoleScreenProud), WorldMaterials.M_Circuit_Cyan);

            Mesh lamp = CharacterMeshes.Sphere(8);
            int lamps = Mathf.Max(2, Mathf.FloorToInt(size.x / 0.3f));
            for (int i = 0; i < lamps; i++)
            {
                float t = (i + 0.5f) / lamps - 0.5f;
                MeshPart(root.transform, $"Lamp {i}", lamp,
                    new Vector3(t * size.x * 0.8f, -size.y * 0.5f + deskHeight + 0.02f, size.z * 0.3f),
                    WorldMaterials.M_Circuit_Cyan, localScale: Vector3.one * 0.06f);
            }

            return root;
        }

        /// <summary>A lathed plant pot: a narrow foot flaring to a wide lip.</summary>
        private static Mesh PlanterPotProfile(float radius, float height)
        {
            return CharacterMeshes.Lathe(new[]
            {
                new Vector2(radius * 0.55f, 0f),
                new Vector2(radius * 0.7f, height * 0.08f),
                new Vector2(radius, height * 0.9f),
                new Vector2(radius * 1.08f, height),
            }, 10);
        }

        /// <summary>T — a low row of lathed pots in a chamfered trough, each carrying a faceted
        /// bioluminescent green plant spike (ticket table: 1xN, see-through). Runs along whichever of
        /// the footprint's two axes is longer — the "N" the ticket's own glyph leaves open.</summary>
        public static GameObject BuildPlanterRow(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefPlanter");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float rimHeight = size.y * PlanterRimHeight;
            MeshBox(root.transform, "Trough", new Vector3(0f, -size.y * 0.5f + rimHeight * 0.5f, 0f),
                new Vector3(size.x, rimHeight, size.z), WorldMaterials.M_MetalDark);

            bool alongX = size.x >= size.z;
            float runLength = alongX ? size.x : size.z;
            int plants = Mathf.Max(1, Mathf.FloorToInt(runLength / 0.5f));
            float plantHeight = size.y - rimHeight;
            float potRadius = Mathf.Min(0.18f, (alongX ? size.z : size.x) * 0.4f);
            float potHeight = plantHeight * 0.4f;
            Mesh pot = PlanterPotProfile(potRadius, potHeight);
            Mesh plant = CharacterMeshes.Prism(5, potRadius * 0.5f, 0.02f, plantHeight * 0.75f, 0.1f);

            for (int i = 0; i < plants; i++)
            {
                float t = (i + 0.5f) / plants - 0.5f;
                float offset = t * runLength;
                Vector3 potPos = alongX
                    ? new Vector3(offset, -size.y * 0.5f + rimHeight, 0f)
                    : new Vector3(0f, -size.y * 0.5f + rimHeight, offset);

                MeshPart(root.transform, $"Pot {i}", pot, potPos, WorldMaterials.M_MetalDark);
                MeshPart(root.transform, $"Plant {i}", plant,
                    potPos + new Vector3(0f, potHeight + plantHeight * 0.375f, 0f),
                    WorldMaterials.M_BioGlow, Quaternion.Euler(0f, i * 47f, 0f));
            }

            return root;
        }

        /// <summary>L — a tapered <see cref="CharacterMeshes.Beam"/> pipe run on small chamfered
        /// bracket saddles, with a thin cyan highlight beam along the top (ticket table: 1xN,
        /// see-through). The pipe lies along whichever axis is the footprint's longer one, same
        /// rotate-the-mesh idiom <see cref="BuildWallPipes"/> already uses for "along X or along Z".
        /// </summary>
        public static GameObject BuildFloorPipe(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefFloorPipe");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            bool alongX = size.x >= size.z;
            float runLength = alongX ? size.x : size.z;
            float radius = size.y * 0.5f;
            Quaternion lie = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);

            Mesh pipe = CharacterMeshes.Beam(runLength, radius * 1.05f, radius * 0.9f, 10);
            MeshPart(root.transform, "Pipe", pipe, Vector3.zero, WorldMaterials.M_MetalDark, lie);

            Mesh highlight = CharacterMeshes.Beam(runLength * 0.96f, radius * 0.12f, radius * 0.12f, 6);
            MeshPart(root.transform, "Highlight", highlight, new Vector3(0f, radius * 0.75f, 0f),
                WorldMaterials.M_Circuit_Cyan, lie);

            int brackets = Mathf.Max(2, Mathf.FloorToInt(runLength / 1f) + 1);
            for (int i = 0; i < brackets; i++)
            {
                float t = brackets <= 1 ? 0f : (float)i / (brackets - 1) - 0.5f;
                float offset = t * runLength;

                Vector3 pos = alongX
                    ? new Vector3(offset, -radius * 0.5f, 0f)
                    : new Vector3(0f, -radius * 0.5f, offset);
                Vector3 bracketSize = new Vector3(
                    alongX ? FloorPipeBracketWidth : radius * 1.6f,
                    radius,
                    alongX ? radius * 1.6f : FloorPipeBracketWidth);
                MeshBox(root.transform, $"Bracket {i}", pos, bracketSize, WorldMaterials.M_Circuit_Cyan);
            }

            return root;
        }

        // MV-1254: the bulkhead — ribbed machinery wall dressing for the 281 aN_hM hull-fill pieces
        // the V9 layout raised from a plain 1.8 m "none" slab to a 3 m wall-height block (Lee, 19 Oct
        // 2026: "I want beautiful, detailed areas ... make it amazing"). Unlike every other structure
        // kit piece above, a bulkhead's own footprint is whatever the hull fill happens to need to
        // shape a room (the a3 octagon, a1's stepped corners) — from 1 m stubs to an 18 m run — so its
        // ribs/conduits/detail modules are spaced along the piece's own longer axis rather than
        // authored as a fixed N, the same "ask the authored footprint, never a hardcoded guess" idiom
        // ApplyDeckTiling/BuildManifold already keep.
        private const float BulkheadRibPitch = 1.5f;          // ticket: "vertical structural ribs about every 1.5 m"
        private const float BulkheadRibRadius = 0.09f;
        private const float BulkheadRibProud = 0.05f;         // how far a rib stands proud of its panel face
        private const int BulkheadConduitCount = 3;           // ticket: "2-3 pipes"
        private const float BulkheadConduitRadius = 0.045f;
        private const float BulkheadSeamThickness = 0.05f;
        private const float BulkheadSeamProud = 0.015f;       // same anti-z-fight idiom as CircuitStripProud
        private const float BulkheadTopPlateThickness = 0.05f;
        private const float BulkheadInlayWidth = 0.035f;
        private const float BulkheadBoltRadius = 0.025f;
        private const float BulkheadDetailPitch = 4f;         // ticket: "every few metres a detail module"
        private const float BulkheadDetailSize = 0.4f;

        /// <summary>A stable, cross-platform string hash (FNV-1a) — <see cref="string.GetHashCode"/> is
        /// explicitly process-randomised by .NET for security and must never drive anything that needs
        /// to look the same from one run to the next, which "deterministically chosen from the piece
        /// id" (ticket, Change §4 "Variety") requires: the same <c>aN_hM</c> id must pick the same
        /// detail module in the Editor, in a WebGL build, and in CI, every time.</summary>
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

        /// <summary>B — a ribbed machinery bulkhead wall (ticket table: full-height, solid), replacing
        /// one of the 281 hull-fill blocks the V9 layout raised to wall height. A chamfered body at the
        /// piece's own authored footprint, carrying on its two long faces: vertical ribs every
        /// <see cref="BulkheadRibPitch"/>, a bolt row in each recessed panel between them, and a
        /// horizontal conduit bundle running the face's full length; a cyan emissive seam frames the
        /// top edge, the top surface (what the fixed ~72° camera mostly sees) carries a thin cyan
        /// circuit inlay cross, and every <see cref="BulkheadDetailPitch"/> along each long face a vent
        /// grille / junction box / pressure gauge module appears, chosen by <paramref name="pieceId"/>'s
        /// own <see cref="StableHash"/> (ticket: "chosen deterministically from the piece id").
        /// <paramref name="pieceId"/> is <see cref="ArenaCover.Name"/> — the authored <c>aN_hM</c> id,
        /// unique per piece, so no two adjacent bulkheads ever roll the same module by coincidence of
        /// position. Every mesh here depends only on its own fixed parameters, never on
        /// <paramref name="size"/> beyond the body/top plate — ribs, conduits, bolts, seam and detail
        /// modules are each built once and shared by every bulkhead on the map (ticket: "meshes are
        /// built once per segment type and shared").</summary>
        public static GameObject BuildBulkhead(Transform parent, string pieceId, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefBulkhead");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            MeshBox(root.transform, "Body", Vector3.zero, size, WorldMaterials.M_MetalDark);

            bool alongX = size.x >= size.z;
            float faceLength = alongX ? size.x : size.z;
            float faceDepth = alongX ? size.z : size.x;

            foreach (int side in new[] { -1, 1 })
                BuildBulkheadFace(root.transform, pieceId, size, alongX, faceLength, faceDepth, side);

            BuildBulkheadTopSeam(root.transform, size);
            BuildBulkheadTopPlate(root.transform, size);

            return root;
        }

        /// <summary>One long face's own ribs, bolt rows, conduit bundle and detail modules, mounted
        /// <paramref name="side"/> (-1/+1) of the piece's shorter axis — the two faces
        /// <see cref="BuildBulkhead"/> calls this for are the "long face" the ticket's conduit-bundle
        /// line means; the two short end caps keep the plain chamfered body only, same as a rib's own
        /// un-dressed end.</summary>
        private static void BuildBulkheadFace(Transform root, string pieceId, Vector3 size, bool alongX,
            float faceLength, float faceDepth, int side)
        {
            float faceOffset = faceDepth * 0.5f + BulkheadRibProud;
            Vector3 faceNormal = alongX ? new Vector3(0f, 0f, side) : new Vector3(side, 0f, 0f);

            int ribs = Mathf.Max(2, Mathf.FloorToInt(faceLength / BulkheadRibPitch) + 1);
            Mesh ribMesh = CharacterMeshes.Prism(4, BulkheadRibRadius, BulkheadRibRadius, size.y, 0.1f);
            Mesh boltMesh = CharacterMeshes.Sphere(8);
            Quaternion ribRotation = alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

            for (int i = 0; i < ribs; i++)
            {
                float t = ribs <= 1 ? 0f : (float)i / (ribs - 1) - 0.5f;
                float along = t * faceLength;
                Vector3 ribPos = faceNormal * faceOffset + (alongX ? new Vector3(along, 0f, 0f) : new Vector3(0f, 0f, along));
                MeshPart(root, "Bulkhead Rib", ribMesh, ribPos, WorldMaterials.M_MetalDark, ribRotation);

                // A bolt row in the recessed panel between this rib and the next.
                if (i < ribs - 1)
                {
                    float nextAlong = (ribs <= 1 ? 0f : (float)(i + 1) / (ribs - 1) - 0.5f) * faceLength;
                    float mid = (along + nextAlong) * 0.5f;
                    for (int b = 0; b < 2; b++)
                    {
                        float by = (b == 0 ? 0.3f : -0.3f) * size.y;
                        Vector3 boltPos = faceNormal * (faceOffset - BulkheadRibProud * 0.4f)
                            + (alongX ? new Vector3(mid, by, 0f) : new Vector3(0f, by, mid));
                        MeshPart(root, "Bulkhead Bolt", boltMesh, boltPos, WorldMaterials.M_MetalDark,
                            localScale: Vector3.one * (BulkheadBoltRadius * 2f));
                    }
                }
            }

            BuildBulkheadConduits(root, pieceId, size, alongX, faceLength, faceNormal, faceOffset);
            BuildBulkheadDetails(root, pieceId, size, alongX, faceLength, faceNormal, faceOffset, side);
        }

        /// <summary>A bundle of 2-3 horizontal pipes (ticket: "a horizontal conduit bundle (2-3 pipes)
        /// running the length of each long face"), count chosen from <paramref name="pieceId"/>'s own
        /// <see cref="StableHash"/> so neighbouring bulkheads don't all read identically.</summary>
        private static void BuildBulkheadConduits(Transform root, string pieceId, Vector3 size, bool alongX,
            float faceLength, Vector3 faceNormal, float faceOffset)
        {
            int count = 2 + (int)(StableHash(pieceId + "_conduits") % (BulkheadConduitCount - 1));
            Mesh pipe = CharacterMeshes.Beam(faceLength * 0.92f, BulkheadConduitRadius, BulkheadConduitRadius, 8);
            Quaternion lie = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);

            for (int i = 0; i < count; i++)
            {
                float t = (i + 1f) / (count + 1f) - 0.5f;
                float y = t * size.y * 0.6f;
                Vector3 pos = faceNormal * faceOffset + new Vector3(0f, y, 0f);
                MeshPart(root, "Bulkhead Conduit", pipe, pos, WorldMaterials.M_MetalDark, lie);
            }
        }

        /// <summary>One vent grille / junction box / pressure gauge every
        /// <see cref="BulkheadDetailPitch"/> along the face, picked by <paramref name="pieceId"/>'s own
        /// <see cref="StableHash"/> combined with the module's own index along the run, so a long
        /// bulkhead's several modules don't all roll the same kind.</summary>
        private static void BuildBulkheadDetails(Transform root, string pieceId, Vector3 size, bool alongX,
            float faceLength, Vector3 faceNormal, float faceOffset, int side)
        {
            int modules = Mathf.FloorToInt(faceLength / BulkheadDetailPitch);
            if (modules <= 0) return;

            for (int i = 0; i < modules; i++)
            {
                float t = (i + 0.5f) / modules - 0.5f;
                float along = t * faceLength;
                Vector3 pos = faceNormal * (faceOffset + 0.01f) + (alongX ? new Vector3(along, 0f, 0f) : new Vector3(0f, 0f, along));

                uint variant = StableHash($"{pieceId}_detail{side}_{i}") % 3;
                Quaternion face = alongX
                    ? Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f)
                    : Quaternion.Euler(0f, side > 0 ? -90f : 90f, 0f);

                switch (variant)
                {
                    case 0: BuildVentGrille(root, pos, face); break;
                    case 1: BuildJunctionBox(root, pos, face); break;
                    default: BuildPressureGauge(root, pos, face); break;
                }
            }
        }

        /// <summary>A recessed grille of thin horizontal slats over a faint cyan glow backing (ticket:
        /// "a vent grille with a faint cyan glow"). Every mesh built here goes through
        /// <see cref="CharacterMeshes"/>' own parameter cache (its class doc: "built once ... and
        /// shared by every instance of that kind"), same as every other builder in this file — no local
        /// caching needed, and none added, since a hand-rolled cache here would go stale the moment
        /// <see cref="CharacterMeshes.ClearCache"/> destroys the mesh it was still holding onto.</summary>
        private static void BuildVentGrille(Transform root, Vector3 pos, Quaternion rotation)
        {
            var go = new GameObject("Bulkhead Vent Grille");
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rotation;

            Mesh backing = CharacterMeshes.Bevelled(new Vector3(BulkheadDetailSize, BulkheadDetailSize, 0.02f), 0.01f);
            MeshPart(go.transform, "Backing", backing, new Vector3(0f, 0f, -0.01f), WorldMaterials.M_Circuit_Cyan);

            Mesh slat = CharacterMeshes.Bevelled(new Vector3(BulkheadDetailSize * 0.85f, 0.04f, 0.02f), 0.005f);
            for (int i = 0; i < 4; i++)
            {
                float y = (i - 1.5f) * (BulkheadDetailSize / 4f);
                MeshPart(go.transform, $"Slat {i}", slat, new Vector3(0f, y, 0f), WorldMaterials.M_MetalDark);
            }
        }

        /// <summary>A small proud housing with a single lit indicator (ticket: "a junction box").</summary>
        private static void BuildJunctionBox(Transform root, Vector3 pos, Quaternion rotation)
        {
            var go = new GameObject("Bulkhead Junction Box");
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rotation;

            Mesh shell = CharacterMeshes.Bevelled(
                new Vector3(BulkheadDetailSize * 0.7f, BulkheadDetailSize * 0.7f, 0.08f), 0.015f);
            MeshPart(go.transform, "Shell", shell, Vector3.zero, WorldMaterials.M_MetalDark);

            Mesh lamp = CharacterMeshes.Sphere(8);
            MeshPart(go.transform, "Indicator", lamp, new Vector3(0f, 0f, -0.06f), WorldMaterials.M_Circuit_Purple,
                localScale: Vector3.one * 0.06f);
        }

        /// <summary>A round lathed gauge face with a small cyan dial centre (ticket: "a pressure
        /// gauge").</summary>
        private static void BuildPressureGauge(Transform root, Vector3 pos, Quaternion rotation)
        {
            var go = new GameObject("Bulkhead Pressure Gauge");
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rotation * Quaternion.Euler(90f, 0f, 0f);

            float r = BulkheadDetailSize * 0.4f;
            Mesh face = CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, -0.02f), new Vector2(r, -0.02f), new Vector2(r, 0.02f), new Vector2(0f, 0.02f),
            }, 16);
            MeshPart(go.transform, "Face", face, Vector3.zero, WorldMaterials.M_MetalDark);

            Mesh dial = CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(r * 0.45f, 0f), new Vector2(r * 0.45f, 0.01f), new Vector2(0f, 0.015f),
            }, 12);
            MeshPart(go.transform, "Dial", dial, new Vector3(0f, 0.025f, 0f), WorldMaterials.M_Circuit_Cyan);
        }

        /// <summary>A thin cyan emissive seam hugging the top edge's full perimeter (ticket: "a cyan
        /// emissive seam line") — four strips rather than a <see cref="CharacterMeshes.Ring"/> since the
        /// footprint is rectangular, not circular, the same "four edge strips" idiom
        /// <see cref="BuildObservationGlass"/> already uses per-wall.</summary>
        private static void BuildBulkheadTopSeam(Transform root, Vector3 size)
        {
            float y = size.y * 0.5f - BulkheadSeamThickness * 0.5f;
            MeshBox(root, "Seam North", new Vector3(0f, y, size.z * 0.5f - BulkheadSeamThickness * 0.5f),
                new Vector3(size.x + BulkheadSeamProud, BulkheadSeamThickness, BulkheadSeamThickness), WorldMaterials.M_Circuit_Cyan);
            MeshBox(root, "Seam South", new Vector3(0f, y, -size.z * 0.5f + BulkheadSeamThickness * 0.5f),
                new Vector3(size.x + BulkheadSeamProud, BulkheadSeamThickness, BulkheadSeamThickness), WorldMaterials.M_Circuit_Cyan);
            MeshBox(root, "Seam East", new Vector3(size.x * 0.5f - BulkheadSeamThickness * 0.5f, y, 0f),
                new Vector3(BulkheadSeamThickness, BulkheadSeamThickness, size.z + BulkheadSeamProud), WorldMaterials.M_Circuit_Cyan);
            MeshBox(root, "Seam West", new Vector3(-size.x * 0.5f + BulkheadSeamThickness * 0.5f, y, 0f),
                new Vector3(BulkheadSeamThickness, BulkheadSeamThickness, size.z + BulkheadSeamProud), WorldMaterials.M_Circuit_Cyan);
        }

        /// <summary>The top surface — what the fixed ~72° top-down camera mostly sees (ticket: "This is
        /// what the camera mostly sees"): a dark plate proud of the body, with a thin cyan circuit
        /// inlay cross.</summary>
        private static void BuildBulkheadTopPlate(Transform root, Vector3 size)
        {
            float y = size.y * 0.5f + BulkheadTopPlateThickness * 0.5f;
            MeshBox(root, "Top Plate", new Vector3(0f, y, 0f),
                new Vector3(size.x * 0.96f, BulkheadTopPlateThickness, size.z * 0.96f), WorldMaterials.M_MetalDark);

            float inlayY = y + BulkheadTopPlateThickness * 0.5f + 0.005f;
            MeshBox(root, "Top Inlay X", new Vector3(0f, inlayY, 0f),
                new Vector3(size.x * 0.9f, 0.01f, BulkheadInlayWidth), WorldMaterials.M_Circuit_Cyan);
            MeshBox(root, "Top Inlay Z", new Vector3(0f, inlayY, 0f),
                new Vector3(BulkheadInlayWidth, 0.01f, size.z * 0.9f), WorldMaterials.M_Circuit_Cyan);
        }

        // MV-1258 (Lee, 19 Oct 2026: "I want interest"): one hero set piece per Reef room type, built
        // once per landmark area by MaxWorlds.Arena.ReefSetPieces (WHERE) and standing in the chosen
        // cover piece's or wall's own footprint, same "collider stays, art swaps" contract every other
        // builder above keeps. Every builder here is pure geometry only, same as the structure kit
        // above it — the Rendering assembly has no reference to MaxWorlds.Gameplay (where the VFX
        // motion components live), so ReefSetPieces itself finds the named "Ring N"/"Coupling Collar"/
        // "Hook Pivot" children by hierarchy AFTER calling these and attaches
        // MaxWorlds.VFX.ReefSetPieceSpin/Sway to them — the same "Rendering builds it, Gameplay finds
        // it by name and animates it" split ReefAmbientLife already keeps for ReefDrumCorePulse.

        private const float ReactorHousingRadiusFraction = 0.46f;
        private const float ReactorCoreRadiusFraction = 0.3f;
        private const int ReactorRingCount = 3;

        /// <summary>The reactor bay's own landmark (Change item 1): a glowing cyan core cylinder
        /// (<see cref="CharacterMeshes.Lathe"/>) in a ribbed octagonal housing (<see cref="CharacterMeshes.Prism"/>),
        /// three "Ring N" rings (<see cref="CharacterMeshes.Ring"/> — <see cref="MaxWorlds.Arena.ReefSetPieces"/>
        /// spins them in alternating directions) and four coolant pipes radiating to the housing's own
        /// edge. Stands in whichever cover piece's own footprint the caller hands it — the area's
        /// largest hull-fill mass (ticket), the a3 central octagon among them.</summary>
        public static GameObject BuildReactorCore(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefReactorCore");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float footprint = Mathf.Min(size.x, size.z);
            float housingRadius = footprint * ReactorHousingRadiusFraction;
            float coreRadius = footprint * ReactorCoreRadiusFraction;

            Mesh housing = CharacterMeshes.Prism(8, housingRadius, housingRadius * 0.9f, size.y, 0.12f);
            MeshPart(root.transform, "Housing", housing, Vector3.zero, WorldMaterials.M_MetalDark);

            Mesh core = CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, -size.y * 0.48f),
                new Vector2(coreRadius, -size.y * 0.4f),
                new Vector2(coreRadius, size.y * 0.4f),
                new Vector2(0f, size.y * 0.48f),
            }, 16);
            MeshPart(root.transform, "Core", core, Vector3.zero, WorldMaterials.M_Circuit_Cyan);

            Mesh ring = CharacterMeshes.Ring(housingRadius * 0.68f, housingRadius * 0.8f, 0.05f, 20);
            for (int i = 0; i < ReactorRingCount; i++)
            {
                float y = (i - (ReactorRingCount - 1) * 0.5f) * size.y * 0.3f;
                MeshPart(root.transform, $"Ring {i}", ring, new Vector3(0f, y, 0f),
                    WorldMaterials.M_Circuit_Purple, Quaternion.Euler(90f, 0f, 0f));
            }

            Mesh pipe = CharacterMeshes.Beam(footprint * 0.35f, 0.07f, 0.07f, 8);
            foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
            {
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                MeshPart(root.transform, "Coolant Pipe", pipe,
                    dir * (housingRadius + footprint * 0.2f), WorldMaterials.M_Circuit_Cyan,
                    Quaternion.Euler(0f, yaw, 90f));
            }

            return root;
        }

        private const float PropellerShaftRadiusFraction = 0.32f;

        /// <summary>The engine room's own landmark (Change item 2): a giant propeller-shaft section —
        /// a tapered <see cref="CharacterMeshes.Beam"/> shaft, a "Coupling Collar" ring
        /// (<see cref="CharacterMeshes.Ring"/> — <see cref="MaxWorlds.Arena.ReefSetPieces"/> spins it),
        /// two bearing-housing drums (<see cref="CharacterMeshes.Prism"/>) and a dark, glossy oil-sheen
        /// patch on the floor beneath — along <paramref name="alongX"/>, the area's own longest wall.
        /// </summary>
        public static GameObject BuildPropellerShaft(Transform parent, Vector3 center, Vector3 size, bool alongX)
        {
            var root = new GameObject("ReefPropellerShaft");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float shaftLength = alongX ? size.x : size.z;
            float radius = Mathf.Min(size.y, alongX ? size.z : size.x) * PropellerShaftRadiusFraction;
            Quaternion lie = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);

            Mesh shaft = CharacterMeshes.Beam(shaftLength, radius, radius * 0.92f, 14);
            MeshPart(root.transform, "Shaft", shaft, Vector3.zero, WorldMaterials.M_MetalDark, lie);

            Mesh collar = CharacterMeshes.Ring(radius * 1.05f, radius * 1.4f, radius * 0.5f, 18);
            MeshPart(root.transform, "Coupling Collar", collar, Vector3.zero, WorldMaterials.M_Circuit_Cyan, lie);

            Mesh bearing = CharacterMeshes.Prism(10, radius * 1.6f, radius * 1.5f, radius * 1.1f, 0.14f);
            foreach (float t in new[] { -0.32f, 0.32f })
            {
                Vector3 offset = alongX ? new Vector3(t * shaftLength, 0f, 0f) : new Vector3(0f, 0f, t * shaftLength);
                MeshPart(root.transform, "Bearing Housing", bearing, offset, WorldMaterials.M_MetalDark, lie);
            }

            float sheenY = -size.y * 0.5f + 0.01f;
            Vector3 sheenSize = alongX
                ? new Vector3(shaftLength * 0.9f, 0.02f, radius * 3.2f)
                : new Vector3(radius * 3.2f, 0.02f, shaftLength * 0.9f);
            MeshBox(root.transform, "Oil Sheen", new Vector3(0f, sheenY, 0f), sheenSize, WorldMaterials.M_OilSheen);

            return root;
        }

        private const float ContainerUnitHeightFraction = 0.32f;
        private const float CraneRailHeight = 3.2f;
        private const float CraneHookDrop = 1.6f;

        /// <summary>The cargo hold's own landmark (Change item 3): a stacked wall of containers in
        /// varied widths — chamfered <see cref="CharacterMeshes.Bevelled"/> boxes two rows high,
        /// alternating <see cref="WorldMaterials.M_CrateBody"/>/<see cref="WorldMaterials.M_Hazard"/>
        /// so no run of same-coloured boxes reads as one slab.</summary>
        public static GameObject BuildContainerWall(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefContainerWall");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            bool alongX = size.x >= size.z;
            float runLength = alongX ? size.x : size.z;
            float depth = alongX ? size.z : size.x;
            float rowHeight = size.y * ContainerUnitHeightFraction;

            int columns = Mathf.Max(2, Mathf.FloorToInt(runLength / 1.8f));
            int variant = 0;
            for (int row = 0; row < 2; row++)
            {
                float y = -size.y * 0.5f + rowHeight * (row + 0.5f);
                for (int i = 0; i < columns; i++)
                {
                    float t = (i + 0.5f) / columns - 0.5f;
                    float along = t * runLength;
                    float width = runLength / columns * 0.86f;

                    Vector3 pos = alongX ? new Vector3(along, y, 0f) : new Vector3(0f, y, along);
                    Vector3 boxSize = alongX
                        ? new Vector3(width, rowHeight * 0.92f, depth * 0.9f)
                        : new Vector3(depth * 0.9f, rowHeight * 0.92f, width);

                    Material mat = variant % 3 == 0 ? WorldMaterials.M_Hazard : WorldMaterials.M_CrateBody;
                    MeshBox(root.transform, $"Container {row}-{i}", pos, boxSize, mat);
                    variant++;
                }
            }

            return root;
        }

        /// <summary>A ceiling crane rail with a hook and chain hanging over the open floor (Change item
        /// 3) — one per cargo-hold area, independent of the container wall's own footprint (it spans
        /// the room, not one cover piece). The chain/hook sit under their own "Hook Pivot" child, which
        /// <see cref="MaxWorlds.Arena.ReefSetPieces"/> finds by name and swings slowly via
        /// <see cref="MaxWorlds.VFX.ReefSetPieceSway"/>. <paramref name="span"/> is how long the rail
        /// runs.</summary>
        public static GameObject BuildCraneRig(Transform parent, Vector3 center, float span)
        {
            var root = new GameObject("ReefCraneRig");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(center.x, CraneRailHeight, center.z);
            MarkDressed(root);

            Mesh rail = CharacterMeshes.Beam(span, 0.12f, 0.12f, 8);
            MeshPart(root.transform, "Crane Rail", rail, Vector3.zero, WorldMaterials.M_MetalDark,
                Quaternion.Euler(0f, 0f, 90f));

            var chainRoot = new GameObject("Hook Pivot");
            chainRoot.transform.SetParent(root.transform, false);

            Mesh link = CharacterMeshes.Ring(0.05f, 0.09f, 0.04f, 8);
            int links = Mathf.Max(2, Mathf.FloorToInt(CraneHookDrop / 0.25f));
            for (int i = 0; i < links; i++)
            {
                float y = -(i + 0.5f) * (CraneHookDrop / links);
                MeshPart(chainRoot.transform, $"Chain Link {i}", link, new Vector3(0f, y, 0f),
                    WorldMaterials.M_MetalDark, Quaternion.Euler(i % 2 == 0 ? 0f : 90f, 0f, 0f));
            }

            Mesh hook = CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, 0.1f), new Vector2(0.1f, 0.06f), new Vector2(0.12f, -0.04f),
                new Vector2(0.07f, -0.12f), new Vector2(0f, -0.1f),
            }, 10);
            MeshPart(chainRoot.transform, "Hook", hook, new Vector3(0f, -CraneHookDrop, 0f), WorldMaterials.M_MetalDark);

            return root;
        }

        private const float AnchorChainLinkSpacing = 0.5f;

        /// <summary>a30's own background drama (Change item 7, Anchorhead's Cargo Hold): a giant lathed
        /// anchor plus a chain of <see cref="CharacterMeshes.Ring"/> links coiling across the floor and
        /// up one wall. Decoration only — background silhouette for the boss arena, no collider.</summary>
        public static GameObject BuildAnchorChain(Transform parent, Vector3 center, float span)
        {
            var root = new GameObject("ReefAnchorChain");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float anchorScale = span * 0.5f;
            Mesh anchor = CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.12f, 0.02f), new Vector2(0.1f, 0.5f),
                new Vector2(0.16f, 0.55f), new Vector2(0.05f, 0.62f),
            }, 14);
            MeshPart(root.transform, "Anchor Shank", anchor, new Vector3(0f, 0f, 0f), WorldMaterials.M_MetalDark,
                localScale: Vector3.one * anchorScale);

            Mesh fluke = CharacterMeshes.Prism(4, anchorScale * 0.5f, 0.02f, anchorScale * 0.18f, 0.1f);
            foreach (float yaw in new[] { -35f, 35f })
                MeshPart(root.transform, "Anchor Fluke", fluke, new Vector3(0f, -anchorScale * 0.05f, 0f),
                    WorldMaterials.M_MetalDark, Quaternion.Euler(0f, yaw, 90f));

            Mesh link = CharacterMeshes.Ring(0.1f, 0.18f, 0.08f, 10);
            int links = Mathf.Max(3, Mathf.FloorToInt(span / AnchorChainLinkSpacing));
            for (int i = 0; i < links; i++)
            {
                float t = (i + 1f) / links;
                Vector3 pos = new Vector3(t * span * 0.5f, 0f, t * t * span * 0.2f);
                MeshPart(root.transform, $"Floor Chain Link {i}", link, pos, WorldMaterials.M_MetalDark,
                    Quaternion.Euler(i % 2 == 0 ? 0f : 90f, 30f, 0f));
            }

            return root;
        }

        private const float DomeRadiusFraction = 0.5f;

        /// <summary>The gallery's own landmark (Change item 4): the room's own window wall widened into
        /// a curved observation dome section — a lathed hemisphere (<see cref="CharacterMeshes.Sphere"/>)
        /// bulging out past the wall's own face, glazed the same <see cref="WorldMaterials.M_GlassOcean"/>
        /// gradient <see cref="BuildObservationWindow"/> already wears. MV-1257's own ambient-life pass
        /// supplies the fish/whale beyond it via <see cref="BuildOceanBackdrop"/>, already anchored at
        /// every authored window by <see cref="MaxWorlds.Arena.ReefAreaKit.BuildWindows"/> — this builder
        /// only widens the glass itself, it does not duplicate that backdrop.</summary>
        public static GameObject BuildObservationDome(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefObservationDome");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            float width = Mathf.Max(size.x, size.z);
            float radius = width * DomeRadiusFraction;
            float bulgeDepth = Mathf.Max(width * 0.35f, 0.6f);

            Mesh dome = CharacterMeshes.Sphere(18);   // unit (diameter-1) sphere -- localScale carries real metres
            MeshPart(root.transform, "Dome", dome, Vector3.zero, WorldMaterials.M_GlassOcean,
                localScale: new Vector3(width, size.y, bulgeDepth));

            Mesh rim = CharacterMeshes.Ring(radius * 0.98f, radius * 1.08f, 0.08f, 24);
            MeshPart(root.transform, "Rim", rim, Vector3.zero, WorldMaterials.M_MetalDark, Quaternion.Euler(90f, 0f, 0f));

            return root;
        }

        private const float VaultFrameRadiusFraction = 0.62f;
        private const int VaultBoltCount = 8;
        private const float VaultBoltLength = 0.22f;

        /// <summary>The pressure lock's own landmark (Change item 5): a massive circular vault-door
        /// frame around a gate opening — a <see cref="CharacterMeshes.Ring"/> seal plus
        /// <see cref="VaultBoltCount"/> radial locking bolts (<see cref="CharacterMeshes.Beam"/>), built
        /// in the caller's own already-cancelled local space exactly like <see cref="BuildGateFrame"/>
        /// (so <paramref name="width"/>/<paramref name="height"/>/<paramref name="depth"/> are the
        /// doorway's own real metres regardless of which wall the gate sits on). Returns the bolts, so
        /// the caller (<see cref="MaxWorlds.Arena.ReefSetPieces"/>) can wire
        /// <see cref="MaxWorlds.Arena.ReefVaultDoorBolts"/> to retract them the instant the gate opens.
        /// </summary>
        public static GameObject[] BuildVaultDoorFrame(Transform parent, float width, float height, float depth)
        {
            float radius = Mathf.Min(width, height) * VaultFrameRadiusFraction;

            Mesh seal = CharacterMeshes.Ring(radius * 0.82f, radius, 0.1f, 28);
            MeshPart(parent, "Vault Seal", seal, Vector3.zero, WorldMaterials.M_MetalDark);

            Mesh hazardRing = CharacterMeshes.Ring(radius * 1.02f, radius * 1.1f, 0.06f, 28);
            MeshPart(parent, "Vault Hazard Ring", hazardRing, Vector3.zero, WorldMaterials.M_Hazard);

            Mesh bolt = CharacterMeshes.Beam(VaultBoltLength, 0.06f, 0.05f, 8);
            var bolts = new GameObject[VaultBoltCount];
            for (int i = 0; i < VaultBoltCount; i++)
            {
                float angle = (i / (float)VaultBoltCount) * 360f;
                Quaternion spin = Quaternion.Euler(0f, 0f, angle);
                Vector3 pos = spin * new Vector3(radius * 0.91f, 0f, 0f);
                GameObject boltGo = MeshPart(parent, $"Vault Bolt {i}", bolt, pos,
                    WorldMaterials.M_Circuit_Cyan, spin * Quaternion.Euler(0f, 0f, 90f));
                bolts[i] = boltGo;
            }

            return bolts;
        }

        private const float CanopyLampHeight = 0.12f;
        private const float CanopyPoolRadiusFraction = 0.7f;

        /// <summary>A crew/hydroponics area's own landmark (Change item 6): a grow-lamp canopy hanging
        /// over one planter rack, casting a soft magenta-violet light pool (a flat
        /// <see cref="WorldMaterials.M_LampViolet"/> disc) onto the plants beneath it.</summary>
        public static GameObject BuildGrowLampCanopy(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefGrowLampCanopy");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            Vector3 fixtureSize = new Vector3(size.x * 0.9f, CanopyLampHeight, Mathf.Max(size.z * 0.9f, 0.3f));
            MeshBox(root.transform, "Fixture", Vector3.zero, fixtureSize, WorldMaterials.M_MetalDark);

            Mesh glowStrip = CharacterMeshes.Beam(fixtureSize.x * 0.85f, 0.02f, 0.02f, 6);
            MeshPart(root.transform, "Glow Strip", glowStrip, new Vector3(0f, -CanopyLampHeight * 0.55f, 0f),
                WorldMaterials.M_LampViolet, Quaternion.Euler(0f, 0f, 90f));

            float poolRadius = Mathf.Max(size.x, size.z) * CanopyPoolRadiusFraction;
            Mesh pool = CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(poolRadius, 0f), new Vector2(poolRadius, 0.01f), new Vector2(0f, 0.01f),
            }, 16);
            MeshPart(root.transform, "Light Pool", pool, new Vector3(0f, -CanopyLampHeight * 0.5f - 0.6f, 0f),
                WorldMaterials.M_LampViolet);

            return root;
        }

        // MV-1215, Change item 2: the glow hatch — a recessed, collider-free floor decal. Walkable by
        // construction: nothing built here ever gets a collider, same contract every other Reef
        // dressing piece keeps for its own visual-only overlays.
        private const float HatchRecess = 0.06f;
        private const float HatchRimThickness = 0.04f;
        private const float HatchRimProud = 0.1f;

        /// <summary>H — a recessed floor hatch: a chamfered panel, a cyan octagonal rim ring
        /// (<see cref="CharacterMeshes.Ring"/>) and a flat cyan glow disc inside it, with a cross-brace
        /// of two <see cref="CharacterMeshes.Beam"/> struts, <paramref name="w"/> x <paramref name="d"/>
        /// (ticket: 2x2). Collider-free and walkable — a decoration, not an obstacle.</summary>
        public static GameObject BuildGlowHatch(Transform parent, Vector3 center, float w, float d)
        {
            var root = new GameObject("ReefGlowHatch");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            MeshBox(root.transform, "Panel", new Vector3(0f, -HatchRecess * 0.5f, 0f),
                new Vector3(w, HatchRecess, d), WorldMaterials.M_MetalDark);

            float outerR = Mathf.Min(w, d) * 0.5f + HatchRimProud * 0.5f;
            float innerR = Mathf.Max(0.02f, outerR - HatchRimThickness);
            Mesh rim = CharacterMeshes.Ring(innerR, outerR, HatchRecess, 8);
            MeshPart(root.transform, "Rim", rim, new Vector3(0f, -HatchRecess * 0.5f, 0f), WorldMaterials.M_Circuit_Cyan);

            Mesh glow = CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(innerR * 0.9f, 0f), new Vector2(innerR * 0.9f, 0.01f), new Vector2(0f, 0.01f),
            }, 8);
            MeshPart(root.transform, "Glow", glow, new Vector3(0f, -HatchRecess - 0.005f, 0f), WorldMaterials.M_Circuit_Cyan);

            Mesh brace = CharacterMeshes.Beam(Mathf.Max(w, d) * 0.9f, 0.03f, 0.03f, 6);
            MeshPart(root.transform, "Cross Brace A", brace, new Vector3(0f, -HatchRecess * 0.5f, 0f),
                WorldMaterials.M_MetalDark, Quaternion.Euler(0f, 0f, 90f));
            MeshPart(root.transform, "Cross Brace B", brace, new Vector3(0f, -HatchRecess * 0.5f, 0f),
                WorldMaterials.M_MetalDark, Quaternion.Euler(90f, 0f, 0f));

            return root;
        }

        // MV-1256, Change item 3: drainage grates on the open floor — geometry/material only. WHERE
        // a grate goes (density, clearance of cover/gates/garrison) is MaxWorlds.Arena.ReefFloorDetail's
        // own concern, in the Gameplay assembly, which this one (MaxWorlds.Rendering.asmdef) cannot
        // reference back — so the sparse rising-particulate dressing (MaxWorlds.VFX.SludgeBubbles,
        // also Gameplay-side) is attached by that caller, onto the root this method returns.

        private const float FloorGrateSize = 1f;          // ticket gives no explicit footprint; reads clearly against the 2 m deck plate
        private const float FloorGrateRecess = 0.03f;
        private const float FloorGrateRimThickness = 0.08f;

        /// <summary>A floor grate (MV-1256, Change item 3): a shallow recessed panel, a thin rim and a
        /// DIM cyan under-glow disc (<see cref="WorldMaterials.M_GrateGlow"/> — its own, dimmer
        /// instance, never <see cref="WorldMaterials.M_Circuit_Cyan"/>, which other renderers share).
        /// Same panel/rim/glow shape as <see cref="BuildGlowHatch"/> without its cross braces (a hatch
        /// reads as a hull panel; a grate reads as an opening).</summary>
        public static GameObject BuildFloorGrate(Transform parent, Vector3 center)
        {
            var root = new GameObject("ReefFloorGrate");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            MeshBox(root.transform, "Panel", new Vector3(0f, -FloorGrateRecess * 0.5f, 0f),
                new Vector3(FloorGrateSize, FloorGrateRecess, FloorGrateSize), WorldMaterials.M_MetalDark);

            float outerR = FloorGrateSize * 0.5f;
            float innerR = Mathf.Max(0.02f, outerR - FloorGrateRimThickness);
            Mesh rim = CharacterMeshes.Ring(innerR, outerR, FloorGrateRecess, 8);
            MeshPart(root.transform, "Rim", rim, new Vector3(0f, -FloorGrateRecess * 0.5f, 0f), WorldMaterials.M_GrateGlow);

            Mesh glow = CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(innerR * 0.9f, 0f), new Vector2(innerR * 0.9f, 0.01f), new Vector2(0f, 0.01f),
            }, 8);
            MeshPart(root.transform, "Glow", glow, new Vector3(0f, -FloorGrateRecess - 0.005f, 0f), WorldMaterials.M_GrateGlow);

            return root;
        }

        // MV-1256, Change item 4: loose decal clutter — a cable run and two small debris variants, flat
        // and collider-free (MeshBox/MeshPart never add one — see each helper's own doc). Placement
        // (sparse, clear of cover) is MaxWorlds.Arena.ReefFloorDetail's own concern, same split as the
        // grate above.

        private const float CableRunWidth = 0.05f;
        private const float CableRunHeight = 0.02f;
        private const float DebrisScrapHeight = 0.02f;
        private const float DebrisScrapSize = 0.18f;

        /// <summary>A loose cable lying flat on the deck between two points (MV-1256, Change item 4:
        /// "loose cable runs ... flat with no collision").</summary>
        public static GameObject BuildCableRunDecal(Transform parent, Vector3 from, Vector3 to)
        {
            Vector3 mid = (from + to) * 0.5f;
            float length = Vector3.Distance(from, to);
            Vector3 dir = to - from;
            float yaw = length > 0.001f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : 0f;

            var root = new GameObject("ReefCableRun");
            root.transform.SetParent(parent, false);
            root.transform.position = mid + Vector3.up * (CableRunHeight * 0.5f);
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            MarkDressed(root);

            MeshBox(root.transform, "Cable", Vector3.zero, new Vector3(CableRunWidth, CableRunHeight, length),
                WorldMaterials.M_MetalDark);

            return root;
        }

        /// <summary>A small scrap of debris lying on the deck (MV-1256, Change item 4: "small debris
        /// (bolts, panel scraps)") — <paramref name="variant"/> picks bolt (small block) or panel
        /// scrap (a flat rectangle), <paramref name="yaw"/> gives each piece its own resting
        /// rotation.</summary>
        public static GameObject BuildDebrisScrap(Transform parent, Vector3 position, int variant, float yaw)
        {
            var root = new GameObject("ReefDebrisScrap");
            root.transform.SetParent(parent, false);
            root.transform.position = position + Vector3.up * (DebrisScrapHeight * 0.5f);
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            MarkDressed(root);

            Vector3 size = (variant % 2 == 0)
                ? new Vector3(DebrisScrapSize * 0.4f, DebrisScrapHeight, DebrisScrapSize * 0.4f)
                : new Vector3(DebrisScrapSize, DebrisScrapHeight, DebrisScrapSize * 0.55f);
            MeshBox(root.transform, "Scrap", Vector3.zero, size, WorldMaterials.M_MetalDark);

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

            MeshBox(root.transform, "Backing", Vector3.zero, size, WorldMaterials.M_MetalDark);

            bool alongX = size.x >= size.z;
            float runLength = alongX ? size.x : size.z;
            int crates = Mathf.Max(1, Mathf.FloorToInt(runLength / 1.2f));
            for (int i = 0; i < crates; i++)
            {
                float t = (i + 0.5f) / crates - 0.5f;
                float offset = t * runLength;
                Vector3 pos = alongX ? new Vector3(offset, 0f, 0f) : new Vector3(0f, 0f, offset);
                Vector3 crateSize = new Vector3(
                    alongX ? size.x / crates * 0.7f : size.x * 0.8f,
                    size.y * 0.6f,
                    alongX ? size.z * 0.8f : size.z / crates * 0.7f);
                MeshBox(root.transform, $"Crate {i}", pos, crateSize, WorldMaterials.M_CrateBody);
            }

            return root;
        }

        /// <summary>Wall-hung planters with a faceted bioluminescent green plant, for a window-gallery/
        /// crew/hydroponics area's own <c>wallDressing: "planters"</c>.</summary>
        public static GameObject BuildWallPlanters(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefWallPlanters");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            MeshBox(root.transform, "Box", Vector3.zero, new Vector3(size.x, size.y * 0.4f, size.z), WorldMaterials.M_MetalDark);

            Mesh plant = CharacterMeshes.Prism(5, size.x * 0.22f, 0.015f, size.y * 0.6f, 0.12f);
            MeshPart(root.transform, "Plant", plant, new Vector3(0f, size.y * 0.3f, 0f), WorldMaterials.M_BioGlow);

            return root;
        }

        /// <summary>Horizontal <see cref="CharacterMeshes.Beam"/> pipe runs, for an engine-room/
        /// reactor-bay/pressure-lock area's own <c>wallDressing: "pipes"</c>.</summary>
        public static GameObject BuildWallPipes(Transform parent, Vector3 center, Vector3 size)
        {
            var root = new GameObject("ReefWallPipes");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            MarkDressed(root);

            bool alongX = size.x >= size.z;
            float runLength = alongX ? size.x : size.z;
            float diameter = Mathf.Min(size.y * 0.3f, size.z);

            Mesh pipe = CharacterMeshes.Beam(runLength, diameter * 0.5f, diameter * 0.5f, 10);
            MeshPart(root.transform, "Pipe", pipe, Vector3.zero, WorldMaterials.M_MetalDark,
                alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f));

            return root;
        }

        /// <summary>An observation window panel — a chamfered <see cref="CharacterMeshes.Bevelled"/>
        /// pane wearing <see cref="WorldMaterials.M_GlassOcean"/>'s near/far gradient. Collider-free: it
        /// is a wall dressing piece, not a thing that blocks Max.</summary>
        public static GameObject BuildObservationWindow(Transform parent, Vector3 localPosition, Vector3 size)
        {
            GameObject go = MeshBox(parent, "ObservationWindow", localPosition, size, WorldMaterials.M_GlassOcean);
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

            GameObject fish = BuildLayer(root.transform, OceanLayerKind.FishSchool, WorldMaterials.ReefCircuitCyan, 0f * LayerDepthStep, instanced: true);
            GameObject jelly = BuildLayer(root.transform, OceanLayerKind.Jellyfish, WorldMaterials.ReefBioGlow, 1f * LayerDepthStep, instanced: false);
            GameObject manta = BuildLayer(root.transform, OceanLayerKind.Manta, WorldMaterials.ReefCircuitPurple, 2f * LayerDepthStep, instanced: false);
            GameObject whale = BuildLayer(root.transform, OceanLayerKind.Whale, WorldMaterials.ReefMetalDark, 3f * LayerDepthStep, instanced: false);

            // MV-1257, Change item 3: "animated fish shoals ... roughly every 60-90 s one large whale
            // silhouette gliding past" — the parent's own InstanceID seeds each window's cycle so two
            // windows in the same room don't all pass their whale at once.
            root.AddComponent<OceanBackdropLife>().Configure(
                fish != null ? fish.transform : null,
                jelly != null ? jelly.transform : null,
                manta != null ? manta.transform : null,
                whale != null ? whale.transform : null,
                parent.GetInstanceID());

            return root;
        }

        private static GameObject BuildLayer(Transform parent, OceanLayerKind kind, Color tint, float depth, bool instanced)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"Ocean_{kind}";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.5f, depth);
            go.transform.localScale = new Vector3(6f, 3f, 1f);
            StripColliders(go);
            go.AddComponent<OceanLayer>().Kind = kind;

            var rend = go.GetComponent<Renderer>();
            if (rend == null) return go;

            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return go;

            var mat = new Material(shader)
            {
                name = $"OceanLayer_{kind}",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = instanced,
            };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            rend.sharedMaterial = mat;
            return go;
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
