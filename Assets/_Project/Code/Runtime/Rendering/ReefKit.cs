using UnityEngine;

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
    /// World 3's Reef ship kit (MV-713) — the pieces the ticket's material/prefab pass adds that don't
    /// already fall out of the existing per-world plumbing.
    ///
    /// Three of the ticket's six re-skins need NOTHING here: deck plate (Ground), bulkhead (Wall) and
    /// a bare cargo crate (Prop, <see cref="MaxWorlds.Arena.CoverDressing.None"/>) are all shape-
    /// classified surfaces <see cref="WorldMaterials"/> already sweeps every frame it installs, so once
    /// <see cref="BiomePalette.Reef"/> is the active palette (<see cref="BiomePalette.ForWorld"/>,
    /// World 3 = index 2) they re-skin themselves with zero collider risk, the same way World 2's
    /// floor/walls/props already do.
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
}
