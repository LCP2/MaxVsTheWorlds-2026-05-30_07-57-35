using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The loose weapon-upgrade components and the power cell, as low-poly greybox props (YT-134).
    ///
    /// These are MODELS ONLY — the drop/spin/walk-over-pickup behaviour is gameplay's (YT-131's
    /// <c>Pickup</c>), and the identity→effect mapping is YT-133's. This catalog just builds each thing
    /// so it reads at game zoom as a distinct object: the five parts have to be tellable apart at a
    /// glance on the lawn, so each gets a bold silhouette AND a signature colour, and the two that glow
    /// (the Hydro device and the power cell) carry an additive core so the eye finds them.
    ///
    /// Built from primitives the house way (<see cref="MaterialLibrary.Tinted"/> for solids, an
    /// additive glow for the lit cores), colliders stripped, and a single <see cref="KeepsOwnMaterial"/>
    /// on the root so <see cref="RuntimeSurfaceDirector"/> never repaints a nozzle as a paving stone.
    /// Nothing here is parented to a damageable, so <see cref="CharacterSkinDirector"/> leaves it alone.
    ///
    /// Authored facing +Z with the base at y = 0, ~0.45 m tall — a hand-sized component. The caller
    /// places, scales, spins and bobs it (the Pickup does exactly that with the generic greybox today).
    /// </summary>
    public static class WeaponPartArt
    {
        // Signature colours — each part owns one, so "which part is that?" is answerable from the colour
        // before you can resolve the shape at game zoom.
        private static readonly Color BeamCyan = new Color(0.35f, 0.85f, 0.95f);
        private static readonly Color PowerBlue = new Color(0.20f, 0.42f, 0.85f);
        private static readonly Color HarnessGreen = new Color(0.28f, 0.62f, 0.34f);
        private static readonly Color EngineOrange = new Color(0.92f, 0.48f, 0.16f);
        private static readonly Color HydroGlow = new Color(0.45f, 0.9f, 1f);
        private static readonly Color Steel = new Color(0.55f, 0.58f, 0.63f);
        private static readonly Color DarkSteel = new Color(0.24f, 0.26f, 0.30f);
        // Bright cool chrome — the accent/trim on the parts and the power-cell caps. Replaces the old
        // brass (0.72,0.55,0.22): brass is a warm mid-value that the 0.6 sunlit-albedo ceiling
        // (SunlitAlbedo.Clamp, under the yard's 1.8x key) scaled down into a muddy BROWN, so the caps,
        // the power-nozzle ring and the harness clip all read dull/dirty (YT-146). A near-neutral
        // chrome stays a bright metal at any value — it can't go brown — so the pickups read as clean
        // collectibles, not rust.
        private static readonly Color Chrome = new Color(0.80f, 0.83f, 0.88f);
        private static readonly Color CellCyan = new Color(0.31f, 0.86f, 0.98f);
        // The GLISTEN (YT-167): near-white, not cyan — a specular highlight is the light source's
        // colour reflecting off metal, not the cell's own charge colour. Kept off-white rather than
        // pure white so it still reads as "on the cell" instead of a stray sprite. Public: PickupArtDirector
        // reads it back to flicker the glints it built here (same idiom as CollectibleGlow).
        public static readonly Color GlistenColor = new Color(0.92f, 0.98f, 1f);

        /// <summary>Child name prefix for the power cell's specular glint dots (YT-167) — the director
        /// finds them by this to animate the sparkle without knowing the cell's geometry.</summary>
        public const string GlistenPrefix = "Glisten";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>The art keys YT-133 maps its five part identities onto. Kept as strings, not a shared
        /// enum, so the gameplay ticket can own its own identity type without a compile dependency here.</summary>
        public static class Keys
        {
            public const string BeamNozzle = "beam_nozzle";
            public const string PowerNozzle = "power_nozzle";
            public const string AugmentationHarness = "augmentation_harness";
            public const string AccelerationEngine = "acceleration_engine";
            public const string HydroDevice = "hydro_device";
            public const string PowerCell = "power_cell";
        }

        /// <summary>Build a prop by key (see <see cref="Keys"/>). Returns null for an unknown key rather
        /// than throwing, so a gameplay drop table with a typo drops nothing instead of erroring a run.</summary>
        public static GameObject Build(string key, Transform parent = null)
        {
            switch (key)
            {
                case Keys.BeamNozzle: return BuildBeamNozzle(parent);
                case Keys.PowerNozzle: return BuildPowerNozzle(parent);
                case Keys.AugmentationHarness: return BuildAugmentationHarness(parent);
                case Keys.AccelerationEngine: return BuildAccelerationEngine(parent);
                case Keys.HydroDevice: return BuildHydroDevice(parent);
                case Keys.PowerCell: return BuildPowerCell(parent);
                default:
                    Debug.LogWarning($"[WeaponPartArt] unknown part key '{key}' — no prop built.");
                    return null;
            }
        }

        // ---------------------------------------------------------------- the five parts

        /// <summary>Beam nozzle — narrows the beam, same length. A slim tapering nozzle: a short collar
        /// and a long thin cone. The thinnest, pointiest of the five, so "focus" reads from the shape.</summary>
        public static GameObject BuildBeamNozzle(Transform parent = null)
        {
            var root = Root("BeamNozzle", parent);
            Material body = MaterialLibrary.Tinted(SurfaceKind.Metal, BeamCyan);
            Material trim = MaterialLibrary.Tinted(SurfaceKind.Metal, Steel);

            Part(root, "Collar", PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0f),
                 new Vector3(0.22f, 0.12f, 0.22f), null, trim);
            // The long thin cone — a cylinder tapered by scaling its far end down would need a mesh, so
            // greybox it as a stack: a barrel narrowing to a fine tip.
            Part(root, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.3f, 0f),
                 new Vector3(0.16f, 0.16f, 0.16f), null, body);
            Part(root, "Tip", PrimitiveType.Cylinder, new Vector3(0f, 0.46f, 0f),
                 new Vector3(0.07f, 0.1f, 0.07f), null, body);
            Glow(root, "Aperture", new Vector3(0f, 0.56f, 0f), 0.09f, BeamCyan);
            return root;
        }

        /// <summary>Power nozzle — narrows AND lengthens. Longer and chunkier than the beam nozzle: a
        /// stepped barrel with a heavy brass focusing ring. Reads as the same family as the beam nozzle
        /// but bigger and meaner, which is exactly the upgrade relationship.</summary>
        public static GameObject BuildPowerNozzle(Transform parent = null)
        {
            var root = Root("PowerNozzle", parent);
            Material body = MaterialLibrary.Tinted(SurfaceKind.Metal, PowerBlue);
            Material ring = MaterialLibrary.Tinted(SurfaceKind.Metal, Chrome);

            Part(root, "Collar", PrimitiveType.Cylinder, new Vector3(0f, 0.11f, 0f),
                 new Vector3(0.26f, 0.11f, 0.26f), null, body);
            Part(root, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.34f, 0f),
                 new Vector3(0.2f, 0.24f, 0.2f), null, body);
            Part(root, "FocusRing", PrimitiveType.Cylinder, new Vector3(0f, 0.5f, 0f),
                 new Vector3(0.28f, 0.05f, 0.28f), null, ring);
            Part(root, "Muzzle", PrimitiveType.Cylinder, new Vector3(0f, 0.62f, 0f),
                 new Vector3(0.12f, 0.12f, 0.12f), null, body);
            Glow(root, "Aperture", new Vector3(0f, 0.74f, 0f), 0.11f, BeamCyan);
            return root;
        }

        /// <summary>Augmentation harness (backpack) — +water capacity, and the mount the Hydro clips
        /// into. A fat rounded tank with two shoulder straps and an empty clip-bracket on its face, so
        /// the "something bolts on here later" read is built in.</summary>
        public static GameObject BuildAugmentationHarness(Transform parent = null)
        {
            var root = Root("AugmentationHarness", parent);
            Material tank = MaterialLibrary.Tinted(SurfaceKind.Metal, HarnessGreen);
            Material strap = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);
            Material clip = MaterialLibrary.Tinted(SurfaceKind.Metal, Chrome);

            // The tank — a rounded box, the biggest single mass of the five so it reads as "the backpack".
            Part(root, "Tank", PrimitiveType.Capsule, new Vector3(0f, 0.3f, 0f),
                 new Vector3(0.42f, 0.34f, 0.42f), Quaternion.Euler(90f, 0f, 0f), tank);
            // Two straps arcing over the front.
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.16f : 0.16f;
                Part(root, $"Strap{i}", PrimitiveType.Cube, new Vector3(x, 0.3f, 0.2f),
                     new Vector3(0.06f, 0.5f, 0.06f), Quaternion.Euler(12f, 0f, 0f), strap);
            }
            // The clip-bracket — an open C where the Hydro device seats.
            Part(root, "Clip", PrimitiveType.Cube, new Vector3(0f, 0.5f, 0.12f),
                 new Vector3(0.24f, 0.08f, 0.14f), null, clip);
            return root;
        }

        /// <summary>Acceleration engine — Max moves faster. A little motor: a boxy block, an angled
        /// exhaust stack and an intake fan. Orange with a hot exhaust, so it reads as "goes fast."</summary>
        public static GameObject BuildAccelerationEngine(Transform parent = null)
        {
            var root = Root("AccelerationEngine", parent);
            Material block = MaterialLibrary.Tinted(SurfaceKind.Metal, EngineOrange);
            Material metal = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);

            Part(root, "Block", PrimitiveType.Cube, new Vector3(0f, 0.2f, 0f),
                 new Vector3(0.4f, 0.32f, 0.34f), null, block);
            // Cooling fins across the top.
            for (int i = 0; i < 3; i++)
            {
                Part(root, $"Fin{i}", PrimitiveType.Cube, new Vector3(-0.12f + i * 0.12f, 0.4f, 0f),
                     new Vector3(0.04f, 0.14f, 0.36f), null, metal);
            }
            // The exhaust stack, kicked back.
            Part(root, "Exhaust", PrimitiveType.Cylinder, new Vector3(0f, 0.34f, -0.24f),
                 new Vector3(0.12f, 0.16f, 0.12f), Quaternion.Euler(28f, 0f, 0f), metal);
            // Intake fan on the front.
            Part(root, "Fan", PrimitiveType.Cylinder, new Vector3(0f, 0.2f, 0.19f),
                 new Vector3(0.22f, 0.03f, 0.22f), Quaternion.Euler(90f, 0f, 0f), metal);
            Glow(root, "ExhaustGlow", new Vector3(0f, 0.42f, -0.28f), 0.08f, EngineOrange);
            return root;
        }

        /// <summary>Hydro rapid condensation device — pulls water from the air, cuts the tether. The
        /// techiest of the five: a glowing core wrapped in condenser coils with radiator fins. It is the
        /// one that GLOWS brightest, because it is the endgame part that frees Max from the hose.</summary>
        public static GameObject BuildHydroDevice(Transform parent = null)
        {
            var root = Root("HydroDevice", parent);
            Material shell = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);
            Material coil = MaterialLibrary.Tinted(SurfaceKind.Metal, Steel);

            Part(root, "Base", PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f),
                 new Vector3(0.34f, 0.08f, 0.34f), null, shell);
            // The glowing condensation core.
            Glow(root, "Core", new Vector3(0f, 0.32f, 0f), 0.26f, HydroGlow);
            // Coil rings stacked around the core.
            for (int i = 0; i < 3; i++)
            {
                float y = 0.22f + i * 0.11f;
                float r = 0.3f - i * 0.03f;
                Part(root, $"Coil{i}", PrimitiveType.Cylinder, new Vector3(0f, y, 0f),
                     new Vector3(r, 0.025f, r), null, coil);
            }
            // Radiator fins splaying out — the "condenser" read.
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Part(root, $"Fin{i}", PrimitiveType.Cube, dir * 0.24f + Vector3.up * 0.16f,
                     new Vector3(0.05f, 0.2f, 0.16f), Quaternion.Euler(0f, a, 0f), coil);
            }
            return root;
        }

        // ---------------------------------------------------------------- the power cell

        /// <summary>The power cell — the common collectible that banks into the HUD counter. A stubby
        /// battery: a dark casing with a bright cyan core band and a terminal nub, so it reads as
        /// "energy" from across the lawn and is never mistaken for a part.</summary>
        public static GameObject BuildPowerCell(Transform parent = null)
        {
            var root = Root("PowerCell", parent);
            // A bright cool casing, not the old near-black DarkSteel: a dark shell + brown brass caps
            // is exactly what made the cell read as a dull brown lump on the lawn (YT-146). A mid steel
            // body with chrome caps lets the cyan charge core do the talking, so the cell reads as a
            // bright, lit collectible.
            Material casing = MaterialLibrary.Tinted(SurfaceKind.Metal, Steel);
            Material cap = MaterialLibrary.Tinted(SurfaceKind.Metal, Chrome);

            Part(root, "Casing", PrimitiveType.Cylinder, new Vector3(0f, 0.18f, 0f),
                 new Vector3(0.2f, 0.18f, 0.2f), null, casing);
            // The bright core band — the "charge" the eye reads.
            Glow(root, "Core", new Vector3(0f, 0.18f, 0f), 0.17f, CellCyan);
            // ...but the band should ring the middle, not be a ball: squash it and let the casing hide
            // its top and bottom, leaving a lit stripe. Cheap greybox: a slightly larger glow disc.
            Part(root, "TopCap", PrimitiveType.Cylinder, new Vector3(0f, 0.36f, 0f),
                 new Vector3(0.14f, 0.03f, 0.14f), null, cap);
            Part(root, "Terminal", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f),
                 new Vector3(0.06f, 0.04f, 0.06f), null, cap);

            // The GLISTEN (YT-167): the soft additive Core band above reads as a lit charge, but Lee's
            // playtest on device still saw the cell as flat — an aura around a shape isn't the same as a
            // shape looking SHINY. A specular highlight has to sit ON the surface, not haloed around it.
            // Two dots, not one, at different heights/angles/sizes on the casing: PickupArtDirector spins
            // this root and flickers each on its own phase, so between the spin and the twinkle at least
            // one glint is always sweeping across a visible face rather than one dot parked on the back.
            Glisten(root, GlistenPrefix + "0", OnCasing(35f, 0.24f), 0.05f);
            Glisten(root, GlistenPrefix + "1", OnCasing(200f, 0.13f), 0.035f);
            return root;
        }

        /// <summary>A point on the casing cylinder's surface — <paramref name="angleDeg"/> around the
        /// vertical axis at height <paramref name="y"/>. Unity's cylinder primitive has a 0.5 radius, so
        /// the "Casing" part's 0.2 local scale is an actual world radius of 0.1, not 0.2 — this sits just
        /// outside that so the glint reads as sitting on the metal rather than buried inside it.</summary>
        private static Vector3 OnCasing(float angleDeg, float y, float radius = 0.105f)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad) * radius, y, Mathf.Sin(rad) * radius);
        }

        // ---------------------------------------------------------------- helpers

        private static GameObject Root(string name, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: false);
            // One marker on the root covers everything below it, keeping the surface sweep off the props.
            go.AddComponent<KeepsOwnMaterial>();
            return go;
        }

        private static Transform Part(GameObject root, string name, PrimitiveType shape, Vector3 pos,
                                      Vector3 scale, Quaternion? rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Strip(go);
            go.transform.SetParent(root.transform, worldPositionStays: false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>An additive glowing sphere — a lit core. Shared VFX material + a per-renderer block
        /// so many props can glow different colours without minting a material each (the boss ports and
        /// the Hutch vents do exactly this).</summary>
        private static void Glow(GameObject root, string name, Vector3 pos, float size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Strip(go);
            go.transform.SetParent(root.transform, worldPositionStays: false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * size;

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, color);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>A tight, near-white specular sparkle (YT-167) — small and bright rather than soft and
        /// coloured, so it reads as light catching metal instead of another <see cref="Glow"/> light
        /// source. Same additive glow sprite as <see cref="Glow"/>, just far smaller and off-centre on
        /// the casing so it sits ON the surface, not haloed around the whole prop.</summary>
        private static void Glisten(GameObject root, string name, Vector3 pos, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Strip(go);
            go.transform.SetParent(root.transform, worldPositionStays: false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * size;

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, GlistenColor);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Props are scenery — nothing on them is shot or collided with; the Pickup's own
        /// trigger is what the player walks into. A stray collider here would fight it.</summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
        }
    }
}
