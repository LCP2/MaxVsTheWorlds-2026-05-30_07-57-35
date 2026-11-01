using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Puts the stylised materials onto the greybox (YT-50). Self-installing, so the scene file
    /// stays untouched and CI/WebGL get the same surfaces as the editor.
    ///
    /// It deliberately only dresses *world* surfaces — ground, walls, props. Anything that can be
    /// damaged (Max, enemies, the boss, the Mower Hutch) is left alone: those renderers are tinted
    /// at runtime by gameplay to show hit flashes, tells and damage state, and re-skinning them
    /// here would mean this system and the gameplay code fighting over the same colour every frame.
    /// They keep their existing look until they get real models.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldMaterials : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<WorldMaterials>() != null) return;
            new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
        }

        [SerializeField] private BiomePalette palette = BiomePalette.Backyard;

        private void Awake() => Apply(palette);

        /// <summary>Dress every world surface in the biome. Returns how many renderers it touched.</summary>
        public int Apply(BiomePalette p)
        {
            palette = p;
            MaterialLibrary.Palette = p;

            int dressed = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!IsWorldSurface(r)) continue;

                var mat = MaterialLibrary.Surface(KindOf(r));
                if (mat == null) continue;

                r.sharedMaterial = mat;
                dressed++;
            }
            return dressed;
        }

        /// <summary>World surfaces are the things gameplay never recolours, and that don't already
        /// have a look of their own. Anything damageable is owned by gameplay's tint logic (see the
        /// class summary); anything marked <see cref="KeepsOwnMaterial"/> is imported art that
        /// arrived with its materials attached, and repainting a tree in flat lawn-green is the
        /// opposite of what the art pass is for.</summary>
        public static bool IsWorldSurface(Renderer r)
        {
            if (r == null) return false;
            if (r.GetComponentInParent<KeepsOwnMaterial>() != null) return false;
            return r.GetComponentInParent<IDamageable>() == null;
        }

        /// <summary>Classify by shape rather than by name: a flat plane is ground, a tall box is a
        /// wall, anything else is a prop. Name-matching would break the moment the arena is
        /// generated rather than scaffolded.
        ///
        /// The height check alone stopped being reliable the moment a world authored walls shorter
        /// than its own cover (MV-742: World 2's wallHeight is 1.5 m, some of its cover is 1.6 m) — no
        /// single threshold can separate the two in either direction for that data. A box built as a
        /// boundary wall carries <see cref="StructuralWall"/>, put there by the one place that knows
        /// for certain what it is (<c>MapRuntime.Build</c>), so that marker is checked before shape
        /// ever has to guess. Same reasoning for <see cref="StructuralFloor"/> (MV-954): the map's own
        /// floor slab grew thick enough (2.0 m, for a physics reason unrelated to how it looks) that
        /// the flat-shape check below stopped recognising it as ground.</summary>
        public static SurfaceKind KindOf(Renderer r)
        {
            if (r.GetComponentInParent<StructuralWall>() != null) return SurfaceKind.Wall;
            if (r.GetComponentInParent<StructuralFloor>() != null) return SurfaceKind.Ground;

            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null && mesh.name.StartsWith("Plane")) return SurfaceKind.Ground;

            Vector3 size = r.bounds.size;
            bool flat = size.y < 0.25f && (size.x > 4f || size.z > 4f);
            if (flat) return SurfaceKind.Ground;

            return size.y >= 2f ? SurfaceKind.Wall : SurfaceKind.Prop;
        }

        // --- MV-713: World 3 Reef ship kit — eight fixed-hex named materials. ---
        //
        // These sit ALONGSIDE the biome sweep above rather than inside it: BiomePalette.Reef feeds the
        // same eight tones into the shape-classified Ground/Wall/Prop/Metal/Foliage sweep every world
        // already gets, but the ticket's AC1 asks for the exact hex values to be readable back as named
        // materials in their own right — the guard against a typo'd hex surviving unnoticed inside a
        // recoloured/contrast-adjusted procedural variant. Built via the same shader-resolution chain
        // MaterialLibrary already exposes (MaterialLibrary.SurfaceShader), cached the same way, and
        // GPU-instanced per the ticket's own "URP emissive, GPU-instanced" instruction — unlike the
        // procedural world surfaces, none of these are static-batched with anything, so there is no
        // batching/instancing conflict to avoid (see MaterialLibrary.Build's own note on that).
        private static readonly Dictionary<string, Material> s_reefCache = new Dictionary<string, Material>();

        private static Color HexColor(uint rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        // MV-1019: was 0x132234 — the floor read near-black against the robots' own colouring
        // (linear luminance ~0.015). Lee: the floor changes, not the robots. Kept in sync with
        // BiomePalette.Reef.GroundBase, which samples the same concept-art tone.
        public static readonly Color ReefShipFloor = HexColor(0x1D2C3B);
        public static readonly Color ReefShipWall = HexColor(0x1E3247);
        public static readonly Color ReefCircuitCyan = HexColor(0x3CDCF2);
        public static readonly Color ReefCircuitPurple = HexColor(0xC455E8);
        public static readonly Color ReefBioGlow = HexColor(0x5CF2A4);
        public static readonly Color ReefHazard = HexColor(0xFF8B2E);
        public static readonly Color ReefMetalDark = HexColor(0x0C1622);

        // MV-1019: a bare cargo crate used to read SurfaceKind.Prop off the general biome sweep
        // (BiomePalette.Reef.Prop, #0C1622) — darker than the floor it sits on. These give it its
        // own named material instead, per the ticket's own Reef art direction: blue-grey steel body,
        // hazard-orange corner caps (see ReefKit.ApplyCrateSkin).
        public static readonly Color ReefCrateBody = HexColor(0x3A4250);
        public static readonly Color ReefCrateCap = HexColor(0xE07A1F);

        /// <summary>The glass gradient's NEAR (top) tone — brighter, closer to the surface.</summary>
        public static readonly Color ReefGlassOceanNear = HexColor(0x0E6FA8);

        /// <summary>The glass gradient's FAR (bottom) tone — darker, deeper water.</summary>
        public static readonly Color ReefGlassOceanFar = HexColor(0x052B49);

        // MV-1054: the ocean void backdrop's own radial gradient — centre brighter/closer, edge
        // darker/farther, per the ticket's own art direction.
        public static readonly Color ReefOceanVoidCenter = HexColor(0x0B4C72);
        public static readonly Color ReefOceanVoidEdge = HexColor(0x031426);

        // MV-1053: World 3's deck floor — plated hull, seams, rivets, a grate, and an emissive mask
        // of circuit traces. Named separately from M_Circuit_Cyan/M_Circuit_Purple (MV-713) because
        // those are shared by OTHER renderers (the wall-base circuit spine, the coolant turret) that
        // this ticket never touches; giving the deck's own mask its own constants keeps a value tweak
        // here from silently relighting something else.
        public static readonly Color ReefFloorCircuitCyan = HexColor(0x27E6FF);
        public static readonly Color ReefFloorCircuitViolet = HexColor(0xC25BFF);

        // MV-1055: the hydroponic bed's kelp spikes and the hull-top accent lamp — the ticket's own
        // exact hex values, named separately from the MV-713/MV-1053 cyan/violet tones above (which
        // are close but not identical) for the same reason those were split out: a value tweak to one
        // must never silently relight the other.
        public static readonly Color ReefKelpGreen = HexColor(0x46FF9A);
        public static readonly Color ReefKelpMagenta = HexColor(0xFF4FD8);
        public static readonly Color ReefLampViolet = HexColor(0xC45CFF);

        // MV-1256: the floor-detail pass's own two new surfaces — a DIM drainage-grate glow (its own
        // instance, separate from M_Circuit_Cyan/M_Circuit_Purple above, which are shared by renderers
        // this ticket never touches) and the Reef's own flooded-deck water, replacing the Stormdrain-
        // shared acid-green sludge tint for this biome only (see ReefFloorDetail.DressFlood).
        public static readonly Color ReefGrateGlowCyan = HexColor(0x1B7A8C);
        public static readonly Color ReefFloodWaterTeal = HexColor(0x0C3A42);

        // MV-1258: the engine room's own propeller-shaft landmark — a dark, non-emissive oil-sheen
        // patch on the floor beneath it. Its own material rather than reusing M_MetalDark (shared by
        // every other dark steel body on the Reef) so a future tint pass can darken the sheen alone.
        public static readonly Color ReefOilSheen = HexColor(0x080C10);

        /// <summary>World size, in metres, of one deck plate (MV-1053 ticket: "2 m square plates").
        /// Shared with <see cref="MaxWorlds.Rendering.ReefKit.DressHull"/>, which uses it to turn the
        /// floor's own resolved world size into the material's mesh-UV tiling — one authored number
        /// instead of two that have to be kept in step by hand.</summary>
        public const float ReefDeckPlateSizeMetres = 2f;

        /// <summary>How many plates across/down one baked deck "supertile" spans (MV-1256) — the
        /// repeating unit <see cref="StylizedTextures.ReefDeckAlbedo"/>/<see cref="StylizedTextures.ReefDeckEmission"/>
        /// now bake, instead of baking a single plate with no plate-to-plate variation at all (MV-1053's
        /// own "reads as a uniform tile grid" gap). <see cref="MaxWorlds.Rendering.ReefKit.ApplyDeckTiling"/>
        /// tiles the floor's material at <c>ReefDeckPlateSizeMetres * ReefDeckGridCells</c>, so the
        /// floor's own world-space repeat period grows from 2 m to 8 m.</summary>
        public const int ReefDeckGridCells = 4;

        /// <summary>How many distinct circuit-inlay patterns <see cref="StylizedTextures.ReefDeckPatternIndexAt"/>
        /// picks between (MV-1256 ticket: "at least 4 inlay patterns").</summary>
        public const int ReefDeckInlayPatternCount = 4;

        /// <summary>Deck emissive brightness on D3D (MV-1334 ticket, Change item 2: "on D3D 0.85 ->
        /// 0.40"), named so <see cref="ReefDeckFloorMaterial"/>'s platform branch reads as a tuned
        /// value rather than a bare literal next to <see cref="ReefDeckEmissionOther"/>.</summary>
        public const float ReefDeckEmissionD3D = 0.40f;

        /// <summary>Deck emissive brightness on every other backend - Metal/GLES/WebGL (MV-1334
        /// ticket, Change item 2: "on non-D3D ... 0.55 -> 0.25"). MV-1316's own platform split
        /// (see <see cref="ReefDeckFloorMaterial"/>'s doc comment) is unchanged; only the two values
        /// it picks between are dimmer now, because MV-1273's Mobile-tier HDR made the old ones bloom.</summary>
        public const float ReefDeckEmissionOther = 0.25f;

        public static Material M_ShipFloor => ReefDeckFloorMaterial();
        public static Material M_ShipWall => ReefMaterial("M_ShipWall", ReefShipWall);
        public static Material M_Circuit_Cyan => ReefMaterial("M_Circuit_Cyan", ReefCircuitCyan, emissive: true);
        public static Material M_Circuit_Purple => ReefMaterial("M_Circuit_Purple", ReefCircuitPurple, emissive: true);
        public static Material M_BioGlow => ReefMaterial("M_BioGlow", ReefBioGlow, emissive: true);
        public static Material M_Hazard => ReefMaterial("M_Hazard", ReefHazard, emissive: true);
        public static Material M_MetalDark => ReefMaterial("M_MetalDark", ReefMetalDark);
        public static Material M_CrateBody => ReefMaterial("M_CrateBody", ReefCrateBody);
        public static Material M_CrateCap => ReefMaterial("M_CrateCap", ReefCrateCap);

        // MV-1055: bioluminescent kelp and the hull-top accent lamp all read as LIT, same reasoning as
        // M_Circuit_Cyan's own emissive branch above.
        public static Material M_KelpGreen => ReefMaterial("M_KelpGreen", ReefKelpGreen, emissive: true);
        public static Material M_KelpMagenta => ReefMaterial("M_KelpMagenta", ReefKelpMagenta, emissive: true);
        public static Material M_LampViolet => ReefMaterial("M_LampViolet", ReefLampViolet, emissive: true);

        /// <summary>A grate's own dim under-glow (MV-1256) — deliberately its own material rather than
        /// reusing <see cref="M_Circuit_Cyan"/>: that one is shared by the wall-base circuit spine and
        /// the coolant turret, and dimming it for a grate would dim every one of those too.</summary>
        public static Material M_GrateGlow => ReefMaterial("M_GrateGlow", ReefGrateGlowCyan, emissive: true);

        /// <summary>MV-1258: the propeller shaft's own oil-sheen floor patch — dark and non-emissive,
        /// its own material rather than <see cref="M_MetalDark"/> (shared by every other dark steel
        /// body on the Reef).</summary>
        public static Material M_OilSheen => ReefMaterial("M_OilSheen", ReefOilSheen);

        /// <summary>The Reef's own flooded-deck water (MV-1256, Change item 5) — replaces
        /// <c>MapRuntime.BuildSludge</c>'s shared Stormdrain acid-green tint for this biome only. See
        /// <c>MaxWorlds.Arena.ReefFloorDetail.DressFlood</c>.</summary>
        public static Material M_FloodWater => ReefFloodWaterMaterial();

        /// <summary>The observation-window glass — a two-stop vertical gradient (near/far) rather than a
        /// flat colour, built the same way <see cref="MaterialLibrary"/> bakes its own two-tone surfaces
        /// (<c>StylizedTextures.Blend</c>): a small vertical texture is cheaper than a second shader, and
        /// keeps this material readable through the same <c>_BaseMap</c>/<c>_BaseColor</c> pair every
        /// other surface in the game already uses.</summary>
        public static Material M_GlassOcean => ReefGlassMaterial();

        /// <summary>The ocean void backdrop (MV-1054) — a baked radial gradient plus light shafts,
        /// fish specks and faint silhouettes (<see cref="StylizedTextures.OceanVoidAlbedo"/>), the same
        /// "small baked texture, not a second shader" idiom <see cref="M_GlassOcean"/> already uses.
        /// </summary>
        public static Material M_OceanVoid => ReefOceanVoidMaterial();

        /// <summary><paramref name="detailScale"/> &gt; 0 asks for the triplanar
        /// <see cref="MaterialLibrary.StylizedSurfaceShader"/> (it is the one shader in the chain that
        /// has a <c>_DetailScale</c> property at all) rather than the plain Lit/SimpleLit/Standard
        /// chain every other Reef material uses — falling back to that chain if the stylised shader
        /// isn't in the build, same "flat but correctly coloured, never magenta" degrade every other
        /// caller of <see cref="MaterialLibrary.SurfaceShader"/> already gets (YT-58).</summary>
        private static Material ReefMaterial(string key, Color color, bool emissive = false, float detailScale = 0f)
        {
            if (s_reefCache.TryGetValue(key, out var cached) && cached != null) return cached;

            Shader shader = detailScale > 0f
                ? MaterialLibrary.StylizedSurfaceShader ?? MaterialLibrary.SurfaceShader
                : MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = key,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (detailScale > 0f && m.HasProperty("_DetailScale")) m.SetFloat("_DetailScale", detailScale);

            if (emissive)
            {
                // Dense cyan/purple circuitry and the hazard tell both read as LIT, not just coloured
                // (ticket: "dense cyan emissive circuitry", "hazard orange used only for danger").
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            s_reefCache[key] = m;
            return m;
        }

        /// <summary>
        /// World 3's deck floor (MV-1053): a baked, tiled deck texture (plates, seams, corner
        /// rivets, a grate — see <see cref="StylizedTextures.ReefDeckAlbedo"/>) plus an emissive mask
        /// of cyan/violet circuit traces (<see cref="StylizedTextures.ReefDeckEmission"/>), replacing
        /// the flat tint <see cref="ReefMaterial"/> gave this before.
        ///
        /// <see cref="ReefShipFloor"/>'s own value is kept EXACTLY as MV-1019 tuned it — that number
        /// is also what anchors <c>MV1019ReefFloorContrastTests</c>'s "crate must read >= 1.6x the
        /// floor" margin, and <see cref="StylizedTextures.ReefDeckAlbedo"/> is baked as a MULTIPLIER
        /// around a mean of 1 (plate interior ~1, seam ~0.46 — see its own comment), never as a
        /// colour in its own right, so this never has to touch that number to add the new detail.
        ///
        /// Deliberately NOT built through <see cref="ReefMaterial"/>: this is the one Reef surface
        /// that shader-switches off the triplanar <see cref="MaterialLibrary.StylizedSurfaceShader"/>
        /// onto the plain chain. The ticket's own AC computes the deck's world-space plate period
        /// from the material's mesh-UV tiling against the floor's resolved world size
        /// (<see cref="ReefKit.DressHull"/> sets <c>mainTextureScale</c> for exactly that reason), and
        /// the triplanar shader never reads that tiling — it samples its base map from world
        /// position instead (see StylizedSurface.shader's own header) — so it is the wrong shader
        /// for an AC phrased that way.
        ///
        /// This material's OWN identity is cached forever, same as every other entry in
        /// <see cref="s_reefCache"/> — but its two baked TEXTURES are not: they live in
        /// <see cref="StylizedTextures"/>'s own cache, which <see cref="StylizedTextures.Clear"/>
        /// sweeps on every biome change, same as every other generated texture in this project. So
        /// the cache-hit path below unconditionally re-points both slots at whatever
        /// <see cref="StylizedTextures"/> currently holds for them, rather than either (a) exempting
        /// these two from the sweep — which would make them the only generated textures in the
        /// project that outlive a palette change, for no reason tied to their own content, since
        /// unlike a <see cref="Tinted"/> albedo they carry no biome colour of their own to begin with
        /// — or (b) re-pointing only when the current map reads back null (MV-1103: in a running
        /// player <see cref="StylizedTextures.Clear"/>'s destroy is deferred to end of frame, so a
        /// same-frame clear-then-fetch — exactly what a scene reload into World 3 does — finds the
        /// stale texture still non-null, skips the re-point, and the floor goes dark a moment later
        /// when that texture is actually destroyed). Deciding "stale" by identity against the source
        /// of truth, not by nullness, is what MV-1103 fixed this to.
        /// </summary>
        private static Material ReefDeckFloorMaterial()
        {
            const string key = "M_ShipFloor";
            if (s_reefCache.TryGetValue(key, out var cached) && cached != null)
            {
                RefreshReefDeckTextures(cached);
                return cached;
            }

            // MV-1316: cloned from an AUTHORED asset (Resources/Materials/M_ReefDeckKeepEmission.mat),
            // not `new Material(shader)` like every other material in this file. The deck's circuit-
            // inlay emission mask rendered on Windows D3D12 but was invisible on WebGL/iPhone (GLES/
            // Metal) even after disabling GPU instancing alone (measured on a local WebGL capture:
            // zero trace pixels near Max matched the AC2 hue/saturation band with instancing off,
            // _EMISSION still enabled only via runtime EnableKeyword). No material anywhere in this
            // project ships with _EMISSION serialized on — every emissive Reef material is built from
            // `new Material(shader)` plus a runtime EnableKeyword call the same way this one used to be
            // — so Unity's build-time shader-variant stripper has no serialized material to discover
            // the _EMISSION variant of URP/Lit from, and nothing here proves per-platform stripping
            // behaves identically. Cloning an authored .mat that already has _EMISSION baked in and
            // lives under Resources/ (guaranteed to ship on every platform, same as every other
            // Resources-loaded asset in this project) removes that gap: the variant is now declared
            // statically, not discovered by chance from whatever else happens to be in the build.
            Material template = Resources.Load<Material>("Materials/M_ReefDeckKeepEmission");
            Material m;
            if (template != null)
            {
                m = new Material(template)
                {
                    name = key,
                    hideFlags = HideFlags.HideAndDontSave,
                    enableInstancing = false,
                };
            }
            else
            {
                Shader shader = MaterialLibrary.SurfaceShader;
                if (shader == null) return null;
                m = new Material(shader)
                {
                    name = key,
                    hideFlags = HideFlags.HideAndDontSave,
                    enableInstancing = false,
                };
            }

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", ReefShipFloor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", ReefShipFloor);

            RefreshReefDeckTextures(m);

            // Circuitry reads as LIT, not just coloured — same idiom ReefMaterial's own emissive
            // branch uses. The mask already carries the real cyan/violet/magenta hues, so this is a
            // brightness multiplier only, not a tint.
            //
            // MV-1275 baked 2.14x in here (the same factor that ticket removed from PostExposure) on
            // the reasoning that the emissive trace's own partial-value edge/multiplied texels needed
            // it back since they no longer rode the post lift. That reasoning assumed the emission
            // BINDING itself was intact — on WebGL it was not (MV-1316): the mask never rendered there
            // at all, at any multiplier, so 2.14x was never actually exercised on that platform. Now
            // that MV-1316 fixes the binding, 2.14x on an already-full-value (255) baked mask blows the
            // floor out white-cyan on both platforms.
            //
            // MV-1316: the two platforms don't converge on one shared value. Measured on local
            // captures (floor-only crop, same AC3 band both times): D3D (Editor Mobile-tier substitute)
            // reads median luminance ~0 (invisible) at 0.55x and 31.7/255 (in band) at 0.85x; WebGL
            // reads 40.7/255 (in band) at 0.55x and 55.0/255 (over band) at 0.85x. A single scalar
            // can't land both in the 25-45 band at once, so this reads the actual graphics backend —
            // the same GLES/Metal-vs-D3D split the ticket's own hypothesis names — rather than guess
            // one compromise value that fails both. See MV-1316's fix comment for the measured numbers.
            bool isDirect3D = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11
                || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12;
            // MV-1334: the floor's traces were reading louder than the robots once MV-1273 put HDR on
            // the Mobile tier and bloomed this mask - both platform values dialled down from MV-1316's
            // own 0.85/0.55 pair, same platform split, just dimmer.
            float emission = isDirect3D ? ReefDeckEmissionD3D : ReefDeckEmissionOther;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(emission, emission, emission));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            s_reefCache[key] = m;
            return m;
        }

        /// <summary>(Re-)points the deck material's base/emission map slots at
        /// <see cref="StylizedTextures.ReefDeckAlbedo"/>/<see cref="StylizedTextures.ReefDeckEmission"/>
        /// — both self-rebuild on next access if <see cref="StylizedTextures.Clear"/> destroyed the
        /// previous instance, so this always hands the material a live texture.</summary>
        private static void RefreshReefDeckTextures(Material m)
        {
            Texture2D albedo = StylizedTextures.ReefDeckAlbedo();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);

            Texture2D emission = StylizedTextures.ReefDeckEmission();
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emission);
        }

        /// <summary>(MV-1256) A dark-teal, high-smoothness surface standing in for "reflective" water
        /// the same way <see cref="ReefGlassMaterial"/> stands in for glass: this project bakes a look
        /// into the texture/shader rather than reaching for real-time reflections or alpha transparency
        /// — an engine feature nothing else in this stylised-surface chain uses, and not one this
        /// ticket's floor-detail slice needs to be the one to introduce. <c>_BaseColor</c> carries
        /// the teal tone; <see cref="StylizedTextures.ReefFloodWaterAlbedo"/> is a MULTIPLIER texture
        /// (mean ~1) for the slow caustic shimmer, same idiom as <see cref="ReefDeckAlbedo"/>'s own
        /// plate noise — <see cref="MaxWorlds.Arena.ReefFloorDetail.DressFlood"/> scrolls it via the
        /// tile's own existing <c>SludgeFlow</c>. Same MV-1103 unconditional re-point as
        /// <see cref="ReefDeckFloorMaterial"/>'s own cache-hit path, for the same reason.</summary>
        private static Material ReefFloodWaterMaterial()
        {
            const string key = "M_FloodWater";
            if (s_reefCache.TryGetValue(key, out var cached) && cached != null)
            {
                RefreshReefFloodWaterTexture(cached);
                return cached;
            }

            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = key,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", ReefFloodWaterTeal);
            if (m.HasProperty("_Color")) m.SetColor("_Color", ReefFloodWaterTeal);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.8f);   // wetter/more reflective than M_GlassOcean's 0.6

            RefreshReefFloodWaterTexture(m);

            s_reefCache[key] = m;
            return m;
        }

        private static void RefreshReefFloodWaterTexture(Material m)
        {
            Texture2D caustics = StylizedTextures.ReefFloodWaterAlbedo();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", caustics);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", caustics);
        }

        private static Material ReefGlassMaterial()
        {
            const string key = "M_GlassOcean";
            if (s_reefCache.TryGetValue(key, out var cached) && cached != null) return cached;

            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = key,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };

            var tex = new Texture2D(1, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = "ReefGlassOceanGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            // Row 0 = near (top of the window), row 1 = far (bottom) — Texture2D rows run bottom-to-top,
            // so the FAR tone goes in row 0 and the NEAR tone in row 1 to read top-lit, bottom-dark.
            tex.SetPixel(0, 0, ReefGlassOceanFar);
            tex.SetPixel(0, 1, ReefGlassOceanNear);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            // The mean of the gradient, so a shader that ignores the texture (the URP/Lit fallback with
            // no UVs, same degrade path MaterialLibrary.Build documents) still renders a plausible glass
            // tone rather than default white.
            Color mean = Color.Lerp(ReefGlassOceanNear, ReefGlassOceanFar, 0.5f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", mean);
            if (m.HasProperty("_Color")) m.SetColor("_Color", mean);

            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.6f);   // wet glass, not matte hull

            s_reefCache[key] = m;
            return m;
        }

        /// <summary>Unlit, since this plane sits ~6 m below the floor with nothing ever casting light
        /// on it — same "bake the look into the texture, don't ask the shader to compute it" reasoning
        /// <see cref="ReefGlassMaterial"/> already uses for the window gradient.
        ///
        /// Same MV-1103 unconditional re-point as <see cref="ReefDeckFloorMaterial"/>'s own cache-hit
        /// path, and for the same reason: this material's one baked texture also lives in
        /// <see cref="StylizedTextures"/>'s own cache, swept by the same <see cref="StylizedTextures.Clear"/>.
        /// </summary>
        private static Material ReefOceanVoidMaterial()
        {
            const string key = "M_OceanVoid";
            if (s_reefCache.TryGetValue(key, out var cached) && cached != null)
            {
                RefreshReefOceanVoidTexture(cached);
                return cached;
            }

            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = key,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };

            Color mean = Color.Lerp(ReefOceanVoidCenter, ReefOceanVoidEdge, 0.5f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", mean);
            if (m.HasProperty("_Color")) m.SetColor("_Color", mean);

            RefreshReefOceanVoidTexture(m);

            s_reefCache[key] = m;
            return m;
        }

        private static void RefreshReefOceanVoidTexture(Material m)
        {
            Texture2D albedo = StylizedTextures.OceanVoidAlbedo();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
        }
    }
}
