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
        private static readonly Color Steel = new Color(0.55f, 0.58f, 0.63f);
        private static readonly Color DarkSteel = new Color(0.24f, 0.26f, 0.30f);
        // The ability module's own colour family (MV-431) — it used to wear DarkSteel + Steel + the cell's
        // own HydroGlow cyan, so at the 72° camera it read as "a slightly larger part" instead of the
        // run-defining drop it is. Public: PickupArtDirector's ground ring already wears ModuleGlow's value
        // (MV-429, ahead of this ticket) so the ring and the prop land on the same red without drifting.
        private static readonly Color ModuleRed = new Color(0.85f, 0.12f, 0.10f);
        public static readonly Color ModuleGlow = new Color(1.00f, 0.24f, 0.08f);
        // Bright cool chrome — the accent/trim on the parts and the power-cell caps. Replaces the old
        // brass (0.72,0.55,0.22): brass is a warm mid-value that the 0.6 sunlit-albedo ceiling
        // (SunlitAlbedo.Clamp, under the yard's 1.8x key) scaled down into a muddy BROWN, so the caps,
        // the power-nozzle ring and the harness clip all read dull/dirty (YT-146). A near-neutral
        // chrome stays a bright metal at any value — it can't go brown — so the pickups read as clean
        // collectibles, not rust.
        // Public: MV-429's ground ring for a Part pickup reads this back so the ring agrees with the
        // chrome trim of the machine-internals prop it surrounds.
        public static readonly Color Chrome = new Color(0.80f, 0.83f, 0.88f);
        // MV-454 — the machine-internals designs' warm salvage accent. Deliberately NOT the exact
        // literal the ticket suggested (~0.78, 0.58, 0.22): that peak is close enough to the brass
        // YT-146 already tried and pulled (0.72, 0.55, 0.22) to reproduce the same failure. In
        // MaterialLibrary.Tinted, a Metal surface's grain highlight is tone * (1 + Contrast), Contrast =
        // 0.20, so a tone that hot pushes the highlight straight into SunlitAlbedo.Ceiling (0.6).
        // Clamp() preserves hue — it scales rather than clipping per channel — but clamping the
        // highlight back down toward the shadow end collapses the grain's contrast to almost nothing,
        // which is what actually read as "muddy" in YT-146: the brass didn't shift hue, it went flat and
        // dim. Kept here at a peak (~0.46) that survives the 1.2x highlight multiply with headroom to
        // spare, so the grain keeps its contrast and the accent reads as bright warm metal rather than
        // repeating the exact regression YT-146 already shipped and reverted once.
        public static readonly Color Brass = new Color(0.46f, 0.35f, 0.15f);
        public static readonly Color Copper = new Color(0.46f, 0.25f, 0.12f);
        // Public: PickupArtDirector reads this back to drive the cell's gentle radiance (MV-304), the
        // same idiom as GlistenColor below.
        public static readonly Color CellCyan = new Color(0.31f, 0.86f, 0.98f);
        // The GLISTEN (YT-167): near-white, not cyan — a specular highlight is the light source's
        // colour reflecting off metal, not the cell's own charge colour. Kept off-white rather than
        // pure white so it still reads as "on the cell" instead of a stray sprite. Public: PickupArtDirector
        // reads it back to flicker the glints it built here (same idiom as CollectibleGlow).
        public static readonly Color GlistenColor = new Color(0.92f, 0.98f, 1f);

        /// <summary>Child name prefix for the power cell's and Hydro device's specular glint dots
        /// (YT-167, WV-236) — the director finds them by this to animate the sparkle without knowing
        /// either prop's geometry.</summary>
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

            // WV-237 — the machine-internals designs a dropped part is randomly dressed as. Purely
            // cosmetic (see MachineInternalsKeys below, which PickupArtDirector draws from); unlike
            // BeamNozzle/PowerNozzle/etc. above, none of these carry a gameplay identity.
            public const string Gear = "part_gear";
            public const string Coil = "part_coil";
            public const string CircuitBlock = "part_circuit_block";
            public const string Piston = "part_piston";
            public const string ValveManifold = "part_valve_manifold";
            public const string CapacitorBank = "part_capacitor_bank";
            public const string CogCluster = "part_cog_cluster";
            public const string HydraulicRam = "part_hydraulic_ram";
            public const string FuseBlock = "part_fuse_block";
            public const string WiringLoom = "part_wiring_loom";
        }

        /// <summary>The pool <see cref="PickupArtDirector"/> draws from to dress a dropped part
        /// (WV-237) — one entry per machine-internals design, kept as one array so "how many designs
        /// exist" and "which keys count" can't drift apart.
        ///
        /// MV-430 collapsed this to one design (<see cref="Keys.Gear"/>), citing the fixed 72° camera
        /// discarding ~70% of vertical detail and four of the ten (piston, hydraulic ram, coil,
        /// capacitor bank) collapsing to "a small stack on a white disc". MV-454 restores the full pool:
        /// that finding was made against the OLD <see cref="Chrome"/> plinth (MV-430's own fix, landed in
        /// the same commit, already made every plinth dark instead of near-white) and against designs
        /// that were still uniformly grey/neutral — i.e. shape was the only differentiator, and shape is
        /// exactly what the 72° camera discards. Colour survives that projection far better than height
        /// does, so every design below now also carries its own <see cref="Brass"/>/<see cref="Copper"/>
        /// accent (see each <c>Build*</c> method) — the read is no longer "tell the silhouette apart",
        /// it's "tell the colour apart", which is a different, and at this camera angle much easier,
        /// problem than the one MV-430 measured. Also worth the widened pool on its own terms: several of
        /// the nine had been wearing the exact signature hues MV-431/the five named parts use
        /// (<see cref="PowerBlue"/> on PowerNozzle, <see cref="HarnessGreen"/> on the harness, etc.) —
        /// a purely cosmetic loot drop wearing a named upgrade's own colour risked being misread as that
        /// upgrade, so those accents are now <see cref="Brass"/>/<see cref="Copper"/>/<see cref="ModuleGlow"/>
        /// instead, which no named part wears.</summary>
        public static readonly string[] MachineInternalsKeys =
        {
            Keys.Gear, Keys.Coil, Keys.CircuitBlock, Keys.Piston, Keys.ValveManifold,
            Keys.CapacitorBank, Keys.CogCluster, Keys.HydraulicRam, Keys.FuseBlock, Keys.WiringLoom,
        };

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
                case Keys.Gear: return BuildGear(parent);
                case Keys.Coil: return BuildCoil(parent);
                case Keys.CircuitBlock: return BuildCircuitBlock(parent);
                case Keys.Piston: return BuildPiston(parent);
                case Keys.ValveManifold: return BuildValveManifold(parent);
                case Keys.CapacitorBank: return BuildCapacitorBank(parent);
                case Keys.CogCluster: return BuildCogCluster(parent);
                case Keys.HydraulicRam: return BuildHydraulicRam(parent);
                case Keys.FuseBlock: return BuildFuseBlock(parent);
                case Keys.WiringLoom: return BuildWiringLoom(parent);
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

        /// <summary>The Hydro device's pickup reads a chunk bigger than the power cell (WV-236) — it's
        /// "a new weapon/ability", not just another drop — applied by <c>PickupArtDirector</c> as a
        /// uniform scale on top of this authored geometry, same idiom as the untouched four parts.
        ///
        /// MV-431: bumped 1.6 -> 2.0 alongside the device's own red colour pass, so it stays the largest
        /// of the three ground scales (power cell 1.6, part 1.8, device 2.0) — the rarest drop reads as
        /// the biggest thing on the lawn.</summary>
        public const float HydroDeviceGroundScale = 2.0f;

        /// <summary>The power cell reads too small in-arena at its authored size (MV-316) — bumped up
        /// on top of its authored geometry, same idiom as <see cref="HydroDeviceGroundScale"/>. Stays
        /// below the Hydro device's multiplier so the cell still reads as "the common collectible", not
        /// the rarer device.
        ///
        /// MV-429: bumped again, 1.4 -> 1.6, now that the oversized <c>CollectibleGlow</c> aura that used
        /// to pad the cell's apparent size is gone (replaced by a ground-hugging ring) — the cell needs
        /// its own geometry to carry more of the "read me" job the aura used to do for free.
        ///
        /// MV-629: Lee wants the everyday cell pickup to read smaller on the ground — cut to 60% of the
        /// above, 1.6 -> 0.96. <see cref="PartGroundScale"/> and <see cref="HydroDeviceGroundScale"/> are
        /// untouched, so the rarer drops still read as the bigger finds.</summary>
        public const float PowerCellGroundScale = 0.96f;

        /// <summary>A dropped part's machine-internals design (<see cref="MachineInternalsKeys"/>) was
        /// never given a ground multiplier at all — it stayed at its authored size while the power cell
        /// above got scaled up 1.4x, so the part actually rendered SMALLER than the cell in-arena despite
        /// their already-distinct shapes/colours (MV-326: "Max is shown approaching what look like two
        /// identical cells, but one is actually a part"). MV-326's first pass set this to a bare 1.75x —
        /// only 1.25x relative to the cell's own 1.4x, still inside the noise at the fixed 72° camera
        /// (MV-347), so it was then expressed as <see cref="PowerCellGroundScale"/> * 2f so the ratio
        /// couldn't drift.
        ///
        /// MV-429 breaks that derivation: bumping <see cref="PowerCellGroundScale"/> to 1.6 would have
        /// silently dragged this to 3.2 along with it, growing the part on a ticket that never asked for
        /// that. Pinned back to its previous effective value (2.8 = the old 1.4 * 2) as an explicit
        /// literal instead — unchanged part size, no more silent coupling to the cell's own constant.
        ///
        /// MV-430 retunes it: with the machine-internals pool collapsed to one clean gear design and the
        /// power cell already at 1.6 (MV-429), a part no longer needs to out-size the cell 2x to read as
        /// its own thing — 1.8 keeps it just barely the larger of the two.</summary>
        public const float PartGroundScale = 1.8f;

        /// <summary>Hydro rapid condensation device — pulls water from the air, cuts the tether. The
        /// techiest of the five: a glowing core wrapped in condenser coils with radiator fins. It is the
        /// one that GLOWS brightest, because it is the endgame part that frees Max from the hose — and,
        /// as the ground pickup (WV-236, "the shed device"), it SHIMMERS like the power cell rather than
        /// sitting flat like the other four parts' greybox (YT-180), so it reads as the special find it is.</summary>
        public static GameObject BuildHydroDevice(Transform parent = null)
        {
            var root = Root("HydroDevice", parent);
            Material shell = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);
            Material coil = MaterialLibrary.Tinted(SurfaceKind.Metal, ModuleRed);
            Material cap = MaterialLibrary.Tinted(SurfaceKind.Metal, Chrome);

            Part(root, "Base", PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f),
                 new Vector3(0.34f, 0.08f, 0.34f), null, shell);
            // The glowing condensation core — MV-431: bigger and its own red-orange, not the cell's cyan.
            Glow(root, "Core", new Vector3(0f, 0.34f, 0f), 0.32f, ModuleGlow);
            // Coil rings stacked around the core — MV-431: ModuleRed, not neutral Steel.
            for (int i = 0; i < 3; i++)
            {
                float y = 0.22f + i * 0.11f;
                float r = 0.3f - i * 0.03f;
                Part(root, $"Coil{i}", PrimitiveType.Cylinder, new Vector3(0f, y, 0f),
                     new Vector3(r, 0.03f, r), null, coil);
            }
            // Radiator fins splaying out — the "condenser" read. MV-431: dark, not Steel, so the red core
            // stays the only bright thing; already seated against the base (do not "fix" the position).
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Part(root, $"Fin{i}", PrimitiveType.Cube, dir * 0.24f + Vector3.up * 0.16f,
                     new Vector3(0.055f, 0.2f, 0.16f), Quaternion.Euler(0f, a, 0f), shell);
            }
            // The cap (MV-431) — a readable top face at the 72° camera, chrome to match every other prop's
            // trim accent.
            Part(root, "Cap", PrimitiveType.Cylinder, new Vector3(0f, 0.52f, 0f),
                 new Vector3(0.10f, 0.02f, 0.10f), null, cap);

            // The SHIMMER (WV-236): "shimmers like a cell" — the same specular-dot language as the power
            // cell's own glints (YT-167), riding the coil rings so the sparkle sits on a surface that's
            // already part of the read rather than tacked onto a flat face. MV-431: two, not three — this
            // prop already reads busier than the cell, so it needs fewer, bolder catches of light rather
            // than a dense cluster that would just blur into the coils.
            Glisten(root, GlistenPrefix + "0", OnCircle(20f, 0.24f, 0.31f), 0.07f);
            Glisten(root, GlistenPrefix + "1", OnCircle(150f, 0.36f, 0.28f), 0.06f);
            return root;
        }

        // ---------------------------------------------------------------- the power cell

        /// <summary>The power cell — the common collectible that banks into the HUD counter. A stubby
        /// battery: a dark casing with a bright cyan core band and a terminal nub, so it reads as
        /// "energy" from across the lawn and is never mistaken for a part.</summary>
        public static GameObject BuildPowerCell(Transform parent = null)
        {
            var root = Root("PowerCell", parent);
            // Consistently CYAN (MV-304): the neutral Steel casing this used to wear was the majority
            // of the prop's surface area, so next to the bright cyan "Core" band it still read as a
            // grey/drab battery — inconsistent with the equally-cyan greybox sphere (Pickup.CellColor)
            // a just-spawned, not-yet-dressed cell shows. Casing now wears the cell's own CellCyan
            // instead of a neutral metal tone, so the whole body reads as one charged object. The caps
            // stay Chrome as a small trim accent, matching every other prop in this catalog's
            // "coloured body + chrome trim" language.
            Material casing = MaterialLibrary.Tinted(SurfaceKind.Metal, CellCyan);
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

            // The GLISTEN (YT-167, extended WV-236): the soft additive Core band above reads as a lit
            // charge, but Lee's playtest on device still saw the cell as flat — an aura around a shape
            // isn't the same as a shape looking SHINY. A specular highlight has to sit ON the surface,
            // not haloed around it. Four dots, not two — "shine and glisten like DIAMONDS" (WV-236) reads
            // as several facets catching light at once, not a single pair — at different heights/angles/
            // sizes on the casing: PickupArtDirector spins this root and flickers each on its own phase,
            // so between the spin and the twinkle a facet is sweeping across the eye almost constantly
            // rather than one dot parked on the back half of the turn.
            Glisten(root, GlistenPrefix + "0", OnCircle(35f, 0.24f, CasingRadius), 0.05f);
            Glisten(root, GlistenPrefix + "1", OnCircle(200f, 0.13f, CasingRadius), 0.035f);
            Glisten(root, GlistenPrefix + "2", OnCircle(120f, 0.30f, CasingRadius), 0.04f);
            Glisten(root, GlistenPrefix + "3", OnCircle(300f, 0.05f, CasingRadius), 0.045f);
            return root;
        }

        /// <summary>Unity's cylinder primitive has a 0.5 radius, so the power cell casing's 0.2 local
        /// scale is an actual world radius of 0.1, not 0.2 — <see cref="OnCircle"/> callers sit just
        /// outside that so a glint reads as sitting on the metal rather than buried inside it.</summary>
        private const float CasingRadius = 0.105f;

        /// <summary>A point on a circle — <paramref name="angleDeg"/> around the vertical axis at height
        /// <paramref name="y"/> and the given <paramref name="radius"/>. Shared by the power cell's
        /// casing glints and the Hydro device's coil-ring glints (WV-236) — both are "a sparkle riding a
        /// cylindrical surface", just at different radii/heights.</summary>
        private static Vector3 OnCircle(float angleDeg, float y, float radius)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad) * radius, y, Mathf.Sin(rad) * radius);
        }

        // ---------------------------------------------------------------- machine-internals parts (WV-237)

        /// <summary>Every design below reads as "the guts of a machine" (gears, coils, circuit
        /// blocks, pistons...) — built when a part was a purely cosmetic universal token (WV-228) with
        /// room to be interesting on the ground. <see cref="PartPlinth"/> gives all ten the same base
        /// footprint and height, so the ticket's "consistent pickup silhouette" holds even though the
        /// crown on top of each is completely different; each also gets its own <see cref="Glisten"/>
        /// dot(s) so every design shimmers, not just the old power-cell/Hydro-device pair.
        /// MV-305 draws from these today (<see cref="PickupArtDirector.RollPartArtKey"/>) — MV-430
        /// briefly narrowed <see cref="MachineInternalsKeys"/> to just <see cref="Keys.Gear"/>; MV-454
        /// restored the full ten now that every design also carries its own colour accent (see
        /// <see cref="MachineInternalsKeys"/>'s doc for why that changes the 72°-camera calculus).</summary>
        private const float PartPlinthRadius = 0.22f;
        private const float PartPlinthHeight = 0.06f;

        /// <summary>MV-430: the plinth was <see cref="Chrome"/> — near-white at radius 0.2, so on most
        /// designs it out-shone the part sitting on it and the eye read the base instead of the object.
        /// <see cref="DarkSteel"/> instead, hardcoded here rather than taken as a parameter, so every
        /// part builder gets the same dark, recessive base without each call site having to remember to
        /// ask for it.</summary>
        private static void PartPlinth(GameObject root)
        {
            // A cylinder's local scale.y IS its half-height (default primitive spans -1..1), so
            // position.y has to equal that half-height too for the plinth's underside to sit at y = 0
            // instead of poking below the ground.
            float half = PartPlinthHeight * 0.5f;
            Material mat = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);
            Part(root, "Plinth", PrimitiveType.Cylinder, new Vector3(0f, half, 0f),
                 new Vector3(PartPlinthRadius, half, PartPlinthRadius), null, mat);
        }

        /// <summary>A toothed cog — a flat disc ringed with square teeth around a hub. MV-430: rebuilt
        /// so the teeth sit flush against the disc rim instead of floating clear of it — the same
        /// detached-geometry trap <see cref="BuildPowerCell"/>'s <see cref="CasingRadius"/> doc already
        /// called out. The tooth ring radius is derived from the disc's own scale, not written as a
        /// second literal, so the two can never drift apart again.</summary>
        public static GameObject BuildGear(Transform parent = null)
        {
            var root = Root("Gear", parent);
            // MV-454: the hub was Chrome — the file's third neutral tone alongside Steel/DarkSteel, which
            // is the "grey, grey and grey" this ticket exists to fix. Brass instead, as the design's one
            // substantial secondary accent; the disc/teeth (the largest mass) stay Steel.
            Material brass = MaterialLibrary.Tinted(SurfaceKind.Metal, Brass);
            Material darkSteel = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);
            Material body = MaterialLibrary.Tinted(SurfaceKind.Metal, Steel);
            PartPlinth(root);

            Vector3 discScale = new Vector3(0.30f, 0.055f, 0.30f);
            Part(root, "Disc", PrimitiveType.Cylinder, new Vector3(0f, 0.22f, 0f), discScale, null, body);

            float toothRadius = discScale.x * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Part(root, $"Tooth{i}", PrimitiveType.Cube, dir * toothRadius + Vector3.up * 0.22f,
                     new Vector3(0.055f, 0.07f, 0.075f), Quaternion.Euler(0f, a, 0f), body);
            }
            Part(root, "Hub", PrimitiveType.Cylinder, new Vector3(0f, 0.24f, 0f),
                 new Vector3(0.11f, 0.075f, 0.11f), null, brass);
            // The hub pin (MV-430): gives the gear's top face a readable centre at the 72° camera.
            Part(root, "HubPin", PrimitiveType.Cylinder, new Vector3(0f, 0.26f, 0f),
                 new Vector3(0.05f, 0.07f, 0.05f), null, darkSteel);
            Glisten(root, GlistenPrefix + "0", new Vector3(0.10f, 0.25f, 0.07f), 0.05f);
            Glisten(root, GlistenPrefix + "1", new Vector3(-0.08f, 0.25f, -0.09f), 0.042f);
            return root;
        }

        /// <summary>A wound induction coil — stacked rings around a lit core. MV-454: was uniformly
        /// <c>PowerBlue</c> — the exact hue <c>BuildPowerNozzle</c> uses for its own signature colour, so a
        /// cosmetic drop risked reading as that named upgrade. Now Steel for the majority of the winding
        /// (the largest mass) with Copper on the two rings nearest the core — coils really are wound
        /// copper wire, so the accent doubles as the "what is this" read.</summary>
        public static GameObject BuildCoil(Transform parent = null)
        {
            var root = Root("Coil", parent);
            Material wire = MaterialLibrary.Tinted(SurfaceKind.Metal, Steel);
            Material accent = MaterialLibrary.Tinted(SurfaceKind.Metal, Copper);
            PartPlinth(root);

            for (int i = 0; i < 5; i++)
            {
                float y = 0.12f + i * 0.07f;
                float r = 0.16f - i * 0.008f;
                Material ringMat = i >= 3 ? accent : wire;   // top two rings (nearest the core) are copper
                Part(root, $"Ring{i}", PrimitiveType.Cylinder, new Vector3(0f, y, 0f),
                     new Vector3(r, 0.02f, r), null, ringMat);
            }
            Glow(root, "Core", new Vector3(0f, 0.3f, 0f), 0.09f, ModuleGlow);
            Glisten(root, GlistenPrefix + "0", OnCircle(40f, 0.26f, 0.15f), 0.04f);
            Glisten(root, GlistenPrefix + "1", OnCircle(210f, 0.19f, 0.14f), 0.035f);
            return root;
        }

        /// <summary>A circuit block — a board studded with mounted components and a lit trace. MV-454:
        /// the studs and trace glow were <c>BeamCyan</c> — <c>BuildBeamNozzle</c>'s own signature colour.
        /// Studs are Copper now (a board's mounted pins/contacts are copper in reality too), trace glow
        /// is <c>ModuleGlow</c>; the board itself stays DarkSteel, the largest mass.</summary>
        public static GameObject BuildCircuitBlock(Transform parent = null)
        {
            var root = Root("CircuitBlock", parent);
            Material board = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);
            Material accent = MaterialLibrary.Tinted(SurfaceKind.Metal, Copper);
            PartPlinth(root);

            Part(root, "Board", PrimitiveType.Cube, new Vector3(0f, 0.18f, 0f),
                 new Vector3(0.3f, 0.2f, 0.3f), null, board);
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Part(root, $"Stud{x}_{z}", PrimitiveType.Cube, new Vector3(x * 0.09f, 0.3f, z * 0.09f),
                         new Vector3(0.06f, 0.05f, 0.06f), null, accent);
                }
            }
            Glow(root, "Trace", new Vector3(0f, 0.29f, 0f), 0.05f, ModuleGlow);
            Glisten(root, GlistenPrefix + "0", new Vector3(0.1f, 0.29f, -0.1f), 0.04f);
            return root;
        }

        /// <summary>A hydraulic piston — a barrel, rod and head, ready to fire. MV-454: this design was
        /// wholly neutral (Chrome trim, DarkSteel housing) — the same "grey, grey and grey" complaint as
        /// the gear, just with an extra shade of grey. The head is Brass now, the design's one
        /// substantial accent; the cylinder (the largest mass) stays DarkSteel, the rod keeps its Chrome
        /// trim.</summary>
        public static GameObject BuildPiston(Transform parent = null)
        {
            var root = Root("Piston", parent);
            Material trim = MaterialLibrary.Tinted(SurfaceKind.Metal, Chrome);
            Material housing = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);
            Material accent = MaterialLibrary.Tinted(SurfaceKind.Metal, Brass);
            PartPlinth(root);

            Part(root, "Cylinder", PrimitiveType.Cylinder, new Vector3(0f, 0.22f, 0f),
                 new Vector3(0.15f, 0.2f, 0.15f), null, housing);
            Part(root, "Rod", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f),
                 new Vector3(0.045f, 0.1f, 0.045f), null, trim);
            Part(root, "Head", PrimitiveType.Cylinder, new Vector3(0f, 0.5f, 0f),
                 new Vector3(0.1f, 0.03f, 0.1f), null, accent);
            Glisten(root, GlistenPrefix + "0", new Vector3(0.08f, 0.42f, 0f), 0.035f);
            Glisten(root, GlistenPrefix + "1", new Vector3(0f, 0.5f, 0.08f), 0.04f);
            return root;
        }

        /// <summary>A valve manifold — a body with radiating pipe nubs and a wheel handle on top.
        /// MV-454: the wheel was <c>EngineOrange</c> — <c>BuildAccelerationEngine</c>'s own signature
        /// colour. Copper now (a brass/copper valve wheel is the real-world fixture anyway); the body and
        /// nubs (the largest mass) stay Steel.</summary>
        public static GameObject BuildValveManifold(Transform parent = null)
        {
            var root = Root("ValveManifold", parent);
            Material pipe = MaterialLibrary.Tinted(SurfaceKind.Metal, Steel);
            Material wheel = MaterialLibrary.Tinted(SurfaceKind.Metal, Copper);
            PartPlinth(root);

            Part(root, "Body", PrimitiveType.Cylinder, new Vector3(0f, 0.18f, 0f),
                 new Vector3(0.14f, 0.16f, 0.14f), null, pipe);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Part(root, $"Nub{i}", PrimitiveType.Cylinder, dir * 0.2f + Vector3.up * 0.18f,
                     new Vector3(0.035f, 0.08f, 0.035f), Quaternion.Euler(90f, a, 0f), pipe);
            }
            Part(root, "Wheel", PrimitiveType.Cylinder, new Vector3(0f, 0.4f, 0f),
                 new Vector3(0.1f, 0.02f, 0.1f), null, wheel);
            Glisten(root, GlistenPrefix + "0", new Vector3(0.07f, 0.4f, 0f), 0.04f);
            return root;
        }

        /// <summary>A bank of three capacitor cans, each with a lit terminal cap. MV-454: tips were
        /// <see cref="PowerBlue"/> (MV-347 moved them off the cell's own <see cref="CellCyan"/>, but
        /// PowerBlue is <c>BuildPowerNozzle</c>'s signature colour, the same collision this ticket found
        /// elsewhere in the pool) — now <see cref="ModuleGlow"/>. Cans stay DarkSteel, the largest mass;
        /// a Brass mounting bracket across their base is the design's substantial accent.</summary>
        public static GameObject BuildCapacitorBank(Transform parent = null)
        {
            var root = Root("CapacitorBank", parent);
            Material can = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);
            Material accent = MaterialLibrary.Tinted(SurfaceKind.Metal, Brass);
            PartPlinth(root);

            Part(root, "Bracket", PrimitiveType.Cube, new Vector3(0f, 0.08f, 0f),
                 new Vector3(0.34f, 0.03f, 0.09f), null, accent);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.12f;
                Part(root, $"Can{i}", PrimitiveType.Cylinder, new Vector3(x, 0.2f, 0f),
                     new Vector3(0.07f, 0.18f, 0.07f), null, can);
                Glow(root, $"Tip{i}", new Vector3(x, 0.38f, 0f), 0.04f, ModuleGlow);
            }
            Glisten(root, GlistenPrefix + "0", new Vector3(0.12f, 0.14f, 0.07f), 0.035f);
            return root;
        }

        /// <summary>Two small interlocking gears at different heights — a busier cousin of
        /// <see cref="BuildGear"/>. MV-454: both cogs were uniformly <see cref="HarnessGreen"/> —
        /// <c>BuildAugmentationHarness</c>'s own signature colour. The larger cog (the majority mass) is
        /// Steel now; the smaller one is Brass, the design's accent.</summary>
        public static GameObject BuildCogCluster(Transform parent = null)
        {
            var root = Root("CogCluster", parent);
            Material body = MaterialLibrary.Tinted(SurfaceKind.Metal, Steel);
            Material accent = MaterialLibrary.Tinted(SurfaceKind.Metal, Brass);
            Material[] cogMats = { body, accent };
            PartPlinth(root);

            Vector3[] offsets = { new Vector3(0.08f, 0.2f, 0f), new Vector3(-0.09f, 0.3f, 0.04f) };
            float[] radii = { 0.11f, 0.09f };
            for (int i = 0; i < offsets.Length; i++)
            {
                Material cog = cogMats[i];
                Part(root, $"Cog{i}", PrimitiveType.Cylinder, offsets[i],
                     new Vector3(radii[i], 0.03f, radii[i]), null, cog);
                for (int t = 0; t < 5; t++)
                {
                    float a = t * 72f;
                    Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    Part(root, $"Cog{i}Tooth{t}", PrimitiveType.Cube, offsets[i] + dir * radii[i],
                         new Vector3(0.03f, 0.04f, 0.03f), Quaternion.Euler(0f, a, 0f), cog);
                }
            }
            Glisten(root, GlistenPrefix + "0", offsets[0] + Vector3.up * 0.02f, 0.04f);
            Glisten(root, GlistenPrefix + "1", offsets[1] + Vector3.up * 0.02f, 0.035f);
            return root;
        }

        /// <summary>A hydraulic ram — a housing with an extended ram toward a cap. MV-454: the ram was
        /// <see cref="PowerBlue"/> — the same signature-colour collision as the coil and capacitor bank.
        /// Copper now; the housing and cap (the largest mass) stay Steel.</summary>
        public static GameObject BuildHydraulicRam(Transform parent = null)
        {
            var root = Root("HydraulicRam", parent);
            Material housing = MaterialLibrary.Tinted(SurfaceKind.Metal, Steel);
            Material fluid = MaterialLibrary.Tinted(SurfaceKind.Metal, Copper);
            PartPlinth(root);

            Part(root, "Housing", PrimitiveType.Cylinder, new Vector3(0f, 0.16f, 0f),
                 new Vector3(0.14f, 0.15f, 0.14f), null, housing);
            Part(root, "Ram", PrimitiveType.Cylinder, new Vector3(0f, 0.36f, 0f),
                 new Vector3(0.075f, 0.12f, 0.075f), null, fluid);
            Part(root, "Cap", PrimitiveType.Cylinder, new Vector3(0f, 0.46f, 0f),
                 new Vector3(0.095f, 0.02f, 0.095f), null, housing);
            Glisten(root, GlistenPrefix + "0", new Vector3(0.07f, 0.36f, 0f), 0.04f);
            Glisten(root, GlistenPrefix + "1", new Vector3(-0.06f, 0.16f, 0.06f), 0.035f);
            return root;
        }

        /// <summary>A fuse block — three lit fuses set in a dark housing. MV-454: the three fuses wore
        /// <see cref="EngineOrange"/>, <see cref="BeamCyan"/> and <see cref="HarnessGreen"/> — all three
        /// other named parts' own signature colours at once, the worst of the pool's collisions with
        /// them. All three now glow <see cref="ModuleGlow"/> instead; the block stays DarkSteel, and a
        /// Brass rail along its front face is the design's substantial accent.</summary>
        public static GameObject BuildFuseBlock(Transform parent = null)
        {
            var root = Root("FuseBlock", parent);
            Material block = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);
            Material accent = MaterialLibrary.Tinted(SurfaceKind.Metal, Brass);
            PartPlinth(root);

            Part(root, "Block", PrimitiveType.Cube, new Vector3(0f, 0.14f, 0f),
                 new Vector3(0.28f, 0.16f, 0.17f), null, block);
            Part(root, "Rail", PrimitiveType.Cube, new Vector3(0f, 0.14f, 0.087f),
                 new Vector3(0.28f, 0.02f, 0.02f), null, accent);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.1f;
                Glow(root, $"Fuse{i}", new Vector3(x, 0.28f, 0f), 0.045f, ModuleGlow);
            }
            Glisten(root, GlistenPrefix + "0", new Vector3(0.13f, 0.14f, 0.09f), 0.035f);
            return root;
        }

        /// <summary>A wiring loom — a connector block with looping cable runs and a lit junction. MV-454:
        /// the whole prop was DarkSteel with a <see cref="BeamCyan"/> junction glow (another named part's
        /// signature colour). The junction is <see cref="ModuleGlow"/> now; the three loops are Copper —
        /// wiring is copper wire, so the accent is also the correct read — while the connector block
        /// (the largest single mass) stays DarkSteel.</summary>
        public static GameObject BuildWiringLoom(Transform parent = null)
        {
            var root = Root("WiringLoom", parent);
            Material wire = MaterialLibrary.Tinted(SurfaceKind.Metal, DarkSteel);
            Material accent = MaterialLibrary.Tinted(SurfaceKind.Metal, Copper);
            PartPlinth(root);

            Part(root, "Connector", PrimitiveType.Cube, new Vector3(0f, 0.14f, 0f),
                 new Vector3(0.13f, 0.14f, 0.13f), null, wire);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 40f - 40f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Part(root, $"Loop{i}", PrimitiveType.Cylinder, dir * 0.14f + Vector3.up * 0.24f,
                     new Vector3(0.03f, 0.1f, 0.03f), Quaternion.Euler(60f, a, 0f), accent);
            }
            Glow(root, "Junction", new Vector3(0f, 0.22f, 0f), 0.05f, ModuleGlow);
            Glisten(root, GlistenPrefix + "0", new Vector3(0f, 0.14f, 0.08f), 0.04f);
            return root;
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
        /// trigger is what the player walks into. A stray collider here would fight it.
        ///
        /// Application.isPlaying-gated (MV-304, same idiom as MaterialLibrary.Clear): Object.Destroy is
        /// deferred to end-of-frame and is what every runtime caller already relies on, but it logs an
        /// error and never actually runs in edit mode — a builder called from an EditMode test (as
        /// WeaponPartArtTests now does) needs DestroyImmediate instead.</summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }
    }
}
