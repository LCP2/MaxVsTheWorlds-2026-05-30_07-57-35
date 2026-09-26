using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-965: dresses the shell MV-964's <see cref="MaxWorlds.Intro.WorldJoinSequence"/> already builds
    /// (the exit corridor's segments A/B/C and the World 2 arrival shell) — called right after that class
    /// builds each one, parenting every piece onto the segment root it exposes
    /// (<c>CorridorSegmentRoot</c>/<c>ArrivalRoot</c>) rather than reaching into its private geometry.
    ///
    /// `d` is metres outward from the door mouth along the wall's own travel axis (the design image's own
    /// convention). Every position below is one of the ticket's own authored d-values, not a procedural
    /// placement — a test asserting a specific d-slice holds a dressing piece always gets the same answer.
    ///
    /// Scenery only, same contract every kit in <see cref="MaxWorlds.Rendering"/> keeps: nothing built
    /// here carries a collider (<see cref="StormdrainKit.Strip"/> strips every piece), so the 2.0 m centre
    /// lane this ticket's own AC1b checks is never touched regardless of where a piece is placed.
    /// </summary>
    public static class WorldJoinDressing
    {
        // ------------------------------------------------------------------ geometry helpers
        // Mirrors MaxWorlds.Intro.WorldJoinSequence's own private OutwardDir/AcrossDir — duplicated
        // rather than exposed, the same call StormdrainKit's own colour constants make against Arena.

        private static Vector3 OutwardDir(Wall wall) => wall switch
        {
            Wall.N => Vector3.forward,
            Wall.S => Vector3.back,
            Wall.E => Vector3.right,
            Wall.W => Vector3.left,
            _ => Vector3.forward,
        };

        private static Vector3 AcrossDir(Wall wall)
        {
            Vector3 d = OutwardDir(wall);
            return new Vector3(-d.z, 0f, d.x);
        }

        private static Vector3 At(Vector2 doorMouth, Wall wall, float along, float across, float y) =>
            new Vector3(doorMouth.x, y, doorMouth.y) + OutwardDir(wall) * along + AcrossDir(wall) * across;

        /// <summary>A world-axis-aligned box spanning [<paramref name="dMin"/>, <paramref name="dMax"/>]
        /// outward from <paramref name="doorMouth"/>, offset <paramref name="acrossOffset"/> across it —
        /// same size formula <c>WorldJoinSequence.BuildBox</c> uses, so a piece built here always lines
        /// up with the shell it dresses regardless of which wall the transition cuts.</summary>
        private static GameObject Bar(Transform parent, string name, Vector2 doorMouth, Wall wall,
            float dMin, float dMax, float acrossOffset, float acrossSize, float height, float centerY,
            Color tone, SurfaceKind kind = SurfaceKind.Stone)
        {
            Vector3 dir = OutwardDir(wall);
            Vector3 across = AcrossDir(wall);
            float mid = (dMin + dMax) * 0.5f;
            float length = dMax - dMin;

            Vector3 center = new Vector3(doorMouth.x, centerY, doorMouth.y) + dir * mid + across * acrossOffset;
            Vector3 size = new Vector3(
                Mathf.Abs(dir.x) * length + Mathf.Abs(across.x) * acrossSize,
                height,
                Mathf.Abs(dir.z) * length + Mathf.Abs(across.z) * acrossSize);

            return StormdrainKit.Box(parent, name, center, size, tone, kind);
        }

        // ------------------------------------------------------------------ emissive materials
        // StormdrainKit.Unlit has no _EMISSION keyword (by design — a lamp LENS doesn't need one, it
        // reads as lit purely by staying unshaded). This ticket's own AC1c needs a real one, so these
        // two follow StormdrainKit's own HazardStripeMaterial idiom instead.

        private static Material _statusLampMaterial;
        private static Material _portalSpillMaterial;

        private static Material EmissiveMaterial(ref Material cache, string name, Color tone, float emissiveScale)
        {
            if (cache != null) return cache;
            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = "WorldJoin_" + name,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tone);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tone);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", tone * emissiveScale);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            cache = m;
            return m;
        }

        private static Material StatusLampMaterial() =>
            EmissiveMaterial(ref _statusLampMaterial, "StatusLamp", StormdrainKit.Status, 1.6f);

        private static readonly Color PortalViolet = new Color(0.55f, 0.25f, 0.85f);

        private static Material PortalSpillMaterial() =>
            EmissiveMaterial(ref _portalSpillMaterial, "PortalSpill", PortalViolet, 0.9f);

        /// <summary>Drops the cached materials this class owns — mirrors <see cref="StormdrainKit.Clear"/>
        /// so an EditMode run that builds the corridor repeatedly across tests never leaks one Material
        /// per run into the editor's asset-less object count.</summary>
        public static void Clear()
        {
            if (_statusLampMaterial != null)
            {
                if (Application.isPlaying) Object.Destroy(_statusLampMaterial);
                else Object.DestroyImmediate(_statusLampMaterial);
                _statusLampMaterial = null;
            }
            if (_portalSpillMaterial != null)
            {
                if (Application.isPlaying) Object.Destroy(_portalSpillMaterial);
                else Object.DestroyImmediate(_portalSpillMaterial);
                _portalSpillMaterial = null;
            }
        }

        private static GameObject StatusLamp(Transform parent, string name, Vector3 worldPos, float diameter)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = worldPos;
            go.transform.localScale = Vector3.one * diameter;
            StormdrainKit.Strip(go);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = StatusLampMaterial();
            return go;
        }

        // ================================================================== exit side

        /// <summary>Segments A (garden), B (kerb + grate) and C (culvert) — MV-964's own three
        /// fixed-distance stretches, dressed exactly per the ticket's own d-ranges (the World 1 -> World 2
        /// row is the only one this ticket covers).</summary>
        public static void DressExit(Transform segA, Transform segB, Transform segC,
            Vector2 doorMouth, Wall wall, float wallHeight, WorldTransitionEntry entry)
        {
            if (segA != null) DressGarden(segA, doorMouth, wall);
            if (segB != null) DressKerbAndGrate(segB, doorMouth, wall, entry.SegmentAEnd);
            if (segC != null) DressCulvertExit(segC, doorMouth, wall, wallHeight, entry.SegmentBEnd, entry.CorridorLength);
        }

        /// <summary>The World 2 arrival shell — segment C's own dressing continued (MV-965's own
        /// instruction), scaled to the shell's shorter length.</summary>
        public static void DressArrival(Transform arrivalRoot, Vector2 doorMouth, Wall wall, float wallHeight, float shellLength)
        {
            if (arrivalRoot != null) DressCulvertArrival(arrivalRoot, doorMouth, wall, wallHeight, shellLength);
        }

        // ------------------------------------------------------------------ segment A: garden (d 0-9)

        private static readonly float[] PaverD = { 1.0f, 2.5f, 4.0f, 5.5f, 7.0f, 8.5f };
        private const float BedInner = 1.0f;   // the 2.0 m centre lane's own edge
        private const float BedWidth = 0.5f;
        private const float BedAcross = BedInner + BedWidth * 0.5f;

        private static void DressGarden(Transform segA, Vector2 doorMouth, Wall wall)
        {
            BiomePalette yard = BiomePalette.Backyard;
            float dEnd = 9f;

            foreach (float d in PaverD)
            {
                Bar(segA, $"Paver d{d:0.0}", doorMouth, wall, d - 0.45f, d + 0.45f, 0f, 0.6f,
                    0.02f, 0.01f, yard.ColorFor(SurfaceKind.Stone), SurfaceKind.Stone);
            }

            foreach (float side in new[] { 1f, -1f })
            {
                Bar(segA, side > 0 ? "Bed E" : "Bed W", doorMouth, wall, 0f, dEnd, side * BedAcross, BedWidth,
                    0.02f, 0.01f, yard.ColorFor(SurfaceKind.Dirt), SurfaceKind.Dirt);

                for (float d = 0.75f; d < dEnd; d += 1.5f)
                {
                    Vector3 at = At(doorMouth, wall, d, side * BedAcross, 0.1f);
                    BuildFoliageClump(segA, $"Foliage {(side > 0 ? "E" : "W")} d{d:0.0}", at, 0.28f, yard);
                }
            }

            // Hose reel (W side, d 3), tipped terracotta pot (E side, d 6.5) — both inside their own bed,
            // both under 0.6 m footprint, both scenery only.
            BuildHoseReel(segA, At(doorMouth, wall, 3f, -BedAcross, 0f), yard);
            BuildTippedPot(segA, At(doorMouth, wall, 6.5f, BedAcross, 0f));
        }

        private static void BuildFoliageClump(Transform parent, string name, Vector3 worldPos, float diameter,
            BiomePalette palette)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = worldPos;
            go.transform.localScale = new Vector3(diameter, diameter * 0.8f, diameter);
            StormdrainKit.Strip(go);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Foliage, palette.ColorFor(SurfaceKind.Foliage));
        }

        private static void BuildHoseReel(Transform parent, Vector3 at, BiomePalette yard)
        {
            var root = new GameObject("Hose Reel");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = at;

            StormdrainKit.Box(root.transform, "Stand", new Vector3(0f, 0.25f, 0f), new Vector3(0.16f, 0.5f, 0.16f),
                yard.ColorFor(SurfaceKind.Wood), SurfaceKind.Wood);
            GameObject coil = StormdrainKit.Tube(root.transform, "Coil", new Vector3(0f, 0.5f, 0f), 0.2f, 0.1f,
                Quaternion.Euler(90f, 0f, 0f), yard.ColorFor(SurfaceKind.Metal));
            coil.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        }

        private static readonly Color TerracottaTone = new Color(0.72f, 0.40f, 0.28f);

        private static void BuildTippedPot(Transform parent, Vector3 at)
        {
            GameObject pot = StormdrainKit.Box(parent, "Tipped Pot", at + Vector3.up * 0.12f,
                new Vector3(0.34f, 0.24f, 0.28f), TerracottaTone, SurfaceKind.Dirt);
            pot.transform.localRotation = Quaternion.Euler(0f, 20f, 32f);
        }

        // ------------------------------------------------------------------ segment B: kerb + grate (d 9-13)

        private const float GrateD = 11f;
        private const float GrateSize = 1.2f;

        private static void DressKerbAndGrate(Transform segB, Vector2 doorMouth, Wall wall, float segStart)
        {
            Bar(segB, "Kerb Band", doorMouth, wall, segStart, segStart + 0.25f, 0f, 3.0f,
                0.12f, 0.06f, StormdrainKit.KerbConcrete);

            BuildLawnGrate(segB, doorMouth, wall, GrateD);

            foreach (float side in new[] { 1f, -1f })
            {
                for (float d = segStart + 0.3f; d <= segStart + 1.5f; d += 0.9f)
                {
                    Vector3 at = At(doorMouth, wall, d, side * 1.5f, 1.5f);
                    BuildFoliageClump(segB, $"Grass Tuft {(side > 0 ? "E" : "W")} d{d:0.0}", at, 0.3f, BiomePalette.Backyard);
                }
            }
        }

        /// <summary>The storm-water grate Max drops through in the World 2 story — seven bars over a dark
        /// void, sized to the ticket's own 1.2 x 1.2 m (StormdrainKit.BuildGrate is fixed at the drain's
        /// own 1 m tile, so this is a small local build rather than a reuse).</summary>
        private static void BuildLawnGrate(Transform parent, Vector2 doorMouth, Wall wall, float d)
        {
            var root = new GameObject("Lawn Grate");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = At(doorMouth, wall, d, 0f, 0f);

            const float outer = GrateSize;
            const float inner = GrateSize - 0.14f;
            float thickness = (outer - inner) * 0.5f;
            float ringOffset = inner * 0.5f + thickness * 0.5f;

            StormdrainKit.Box(root.transform, "Frame A", new Vector3(0f, -0.05f, ringOffset), new Vector3(outer, 0.1f, thickness), StormdrainKit.Rust, SurfaceKind.Metal);
            StormdrainKit.Box(root.transform, "Frame B", new Vector3(0f, -0.05f, -ringOffset), new Vector3(outer, 0.1f, thickness), StormdrainKit.Rust, SurfaceKind.Metal);
            StormdrainKit.Box(root.transform, "Frame C", new Vector3(ringOffset, -0.05f, 0f), new Vector3(thickness, 0.1f, inner), StormdrainKit.Rust, SurfaceKind.Metal);
            StormdrainKit.Box(root.transform, "Frame D", new Vector3(-ringOffset, -0.05f, 0f), new Vector3(thickness, 0.1f, inner), StormdrainKit.Rust, SurfaceKind.Metal);

            const int bars = 7;
            for (int i = 0; i < bars; i++)
            {
                float t = (i + 0.5f) / bars - 0.5f;
                StormdrainKit.Box(root.transform, $"Grille Bar{i}", new Vector3(t * inner, -0.06f, 0f),
                    new Vector3(0.06f, 0.06f, inner), StormdrainKit.Soffit, SurfaceKind.Metal);
            }

            StormdrainKit.Glow(root.transform, "Void", new Vector3(0f, -0.1f, 0f), new Vector3(inner, inner, 1f),
                Quaternion.Euler(90f, 0f, 0f), StormdrainKit.Soffit);
        }

        // ------------------------------------------------------------------ segment C: culvert (d 13-30)

        private static void DressCulvertExit(Transform segC, Vector2 doorMouth, Wall wall, float wallHeight,
            float dStart, float dEnd)
        {
            DressBaysAndJoints(segC, doorMouth, wall, dStart, dEnd);
            DressSludgeTrickle(segC, doorMouth, wall, 15f, dEnd);
            DressRustPipe(segC, doorMouth, wall, wallHeight, 14f, dEnd);
            DressStatusLamps(segC, doorMouth, wall, new[] { 16f, 20f, 24f, 28f });
            DressPortalSpill(segC, doorMouth, wall, dEnd - 3f, dEnd);
        }

        /// <summary>The arrival shell (MV-964 4.7): shorter, but the same culvert language — bays/joints
        /// on a 2 m pitch, a sludge trickle its whole length, the rust pipe and its collars, and a couple
        /// of status lamps. No portal spill (that reads as "leaving World 2", not "arriving in it") and no
        /// AC1c emission-window requirement here — this shell isn't segment C.</summary>
        private static void DressCulvertArrival(Transform arrivalRoot, Vector2 doorMouth, Wall wall, float wallHeight,
            float length)
        {
            DressBaysAndJoints(arrivalRoot, doorMouth, wall, 0f, length);
            DressSludgeTrickle(arrivalRoot, doorMouth, wall, 0f, length);
            DressRustPipe(arrivalRoot, doorMouth, wall, wallHeight, 0f, length);

            var lamps = new System.Collections.Generic.List<float>();
            for (float d = length * 0.3f; d < length; d += length * 0.4f) lamps.Add(d);
            DressStatusLamps(arrivalRoot, doorMouth, wall, lamps.ToArray());
        }

        private static void DressBaysAndJoints(Transform parent, Vector2 doorMouth, Wall wall, float dStart, float dEnd)
        {
            const float pitch = 2f;
            const float inset = 0.2f;
            int i = 0;
            for (float d = dStart + pitch; d < dEnd; d += pitch, i++)
            {
                float bayMid = d - pitch * 0.5f;
                Color tone = (i % 3 == 0) ? StormdrainKit.GroundDry : (i % 3 == 1) ? StormdrainKit.GroundAccent : StormdrainKit.GroundBase;
                Bar(parent, $"Bay{i}", doorMouth, wall, bayMid - (pitch - inset) * 0.5f, bayMid + (pitch - inset) * 0.5f,
                    0f, 3f - inset, 0.1f, -0.05f, tone);
                Bar(parent, $"Joint{i}", doorMouth, wall, d - 0.035f, d + 0.035f, 0f, 3f,
                    0.05f, -0.06f, StormdrainKit.PanelJoint);
            }
        }

        private static readonly Color SludgeChevron = StormdrainKit.SludgeBright;

        private static void DressSludgeTrickle(Transform parent, Vector2 doorMouth, Wall wall, float dStart, float dEnd)
        {
            if (dEnd - dStart < 0.5f) return;

            Bar(parent, "Sludge Trickle", doorMouth, wall, dStart, dEnd, 0f, 0.5f, 0.05f, -0.02f, StormdrainKit.Sludge, SurfaceKind.Foliage);
            Bar(parent, "Sludge Lip A", doorMouth, wall, dStart, dEnd, 0.27f, 0.06f, 0.05f, 0f, StormdrainKit.SludgeBright, SurfaceKind.Foliage);
            Bar(parent, "Sludge Lip B", doorMouth, wall, dStart, dEnd, -0.27f, 0.06f, 0.05f, 0f, StormdrainKit.SludgeBright, SurfaceKind.Foliage);

            int i = 0;
            for (float d = dStart + 2f; d < dEnd; d += 2f, i++)
            {
                GameObject chevron = Bar(parent, $"Sludge Chevron{i}", doorMouth, wall, d - 0.15f, d + 0.15f, 0f, 0.4f,
                    0.04f, 0.03f, SludgeChevron, SurfaceKind.Foliage);
                chevron.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            }
        }

        private const float PipeAcross = -1.3f;

        private static void DressRustPipe(Transform parent, Vector2 doorMouth, Wall wall, float wallHeight,
            float dStart, float dEnd)
        {
            if (dEnd - dStart < 1f) return;

            float mid = (dStart + dEnd) * 0.5f;
            float length = dEnd - dStart;
            float y = 0.6f * wallHeight;
            Vector3 dir = OutwardDir(wall);
            Vector3 across = AcrossDir(wall);
            Quaternion lie = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);

            Vector3 pipeCenter = new Vector3(doorMouth.x, y, doorMouth.y) + dir * mid + across * PipeAcross;
            StormdrainKit.Tube(parent, "Rust Pipe", pipeCenter, 0.15f, length, lie, StormdrainKit.Rust);

            for (float d = dStart; d <= dEnd + 0.01f; d += 3f)
            {
                Vector3 at = new Vector3(doorMouth.x, y, doorMouth.y) + dir * d + across * PipeAcross;
                StormdrainKit.Tube(parent, $"Rust Collar d{d:0.0}", at, 0.2f, 0.2f, lie, StormdrainKit.RustDark);
            }
        }

        private static void DressStatusLamps(Transform parent, Vector2 doorMouth, Wall wall, float[] ds)
        {
            foreach (float d in ds)
            {
                Vector3 at = At(doorMouth, wall, d, 1.35f, 0.9f);
                StatusLamp(parent, $"Status Lamp d{d:0.0}", at, 0.2f);
            }
        }

        private static void DressPortalSpill(Transform parent, Vector2 doorMouth, Wall wall, float dStart, float dEnd)
        {
            GameObject spill = Bar(parent, "Portal Spill", doorMouth, wall, dStart, dEnd, 0f, 3f, 0.02f, 0.01f, PortalViolet);
            var rend = spill.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = PortalSpillMaterial();
        }

        // ================================================================== World 2 -> World 3 (MV-967)
        //
        // "The Outfall Breach": segment A (outfall, d 0-10, Stormdrain flavoured), segment B (breach,
        // d 10-16, the hull tearing through), segment C (hull, d 16-30, full Reef dressing), and the
        // World 3 arrival shell (segment C's dressing continued, scaled to its own shorter length). The
        // exit wall for this row is always E and the arrival wall always W (WorldTransitions.For(1)), so
        // AcrossDir's own +Z result for one and -Z for the other are exactly opposite — NorthSign below
        // corrects for that so "north" always means the design image's own top-of-plan wall (+Z) on
        // either side of the transition.

        private static float NorthSign(Wall wall) => AcrossDir(wall).z >= 0f ? 1f : -1f;

        /// <summary>Segment A (outfall), B (breach) and C (hull) — MV-964's own three fixed-distance
        /// stretches for the World 2 -> World 3 row, dressed per the ticket's own d-ranges.</summary>
        public static void DressExitReef(Transform segA, Transform segB, Transform segC,
            Vector2 doorMouth, Wall wall, float wallHeight, WorldTransitionEntry entry)
        {
            if (segA != null) DressOutfall(segA, doorMouth, wall, wallHeight, entry.SegmentAEnd);
            if (segB != null) DressBreach(segB, doorMouth, wall, wallHeight, entry.SegmentAEnd, entry.SegmentBEnd);
            if (segC != null) DressHullSegment(segC, doorMouth, wall, wallHeight, entry.SegmentBEnd, entry.CorridorLength, isArrival: false);
        }

        /// <summary>The World 3 arrival shell — segment C's own hull dressing continued (MV-967's own
        /// instruction), scaled to the shell's shorter length.</summary>
        public static void DressArrivalReef(Transform arrivalRoot, Vector2 doorMouth, Wall wall, float wallHeight, float shellLength)
        {
            if (arrivalRoot != null) DressHullSegment(arrivalRoot, doorMouth, wall, wallHeight, 0f, shellLength, isArrival: true);
        }

        // ------------------------------------------------------------------ segment A: outfall (d 0-10)

        private static void DressOutfall(Transform segA, Vector2 doorMouth, Wall wall, float wallHeight, float dEnd)
        {
            DressBaysAndJoints(segA, doorMouth, wall, 0f, dEnd);

            float north = NorthSign(wall);
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;

            // Two flush standing-water pools, as drawn.
            Bar(segA, "Outfall Pool A", doorMouth, wall, 2.0f, 5.0f, north * 0.15f, 2.0f, 0.03f, 0.015f,
                StormdrainKit.StandingWater, SurfaceKind.Foliage);
            Bar(segA, "Outfall Pool B", doorMouth, wall, 6.0f, 8.0f, -north * 0.15f, 1.6f, 0.03f, 0.015f,
                StormdrainKit.StandingWater, SurfaceKind.Foliage);

            // A sludge channel along the N wall base.
            float wallHug = halfWidth - 0.225f;
            Bar(segA, "Outfall Sludge Channel", doorMouth, wall, 0f, dEnd, north * wallHug, 0.45f, 0.03f, 0.015f,
                StormdrainKit.Sludge, SurfaceKind.Foliage);

            // Rust pipe along the N wall at 0.6 x wallHeight, with RustDark collars every 3 m.
            DressOutfallPipe(segA, doorMouth, wall, wallHeight, north, dEnd);

            // Valve wheel on the N wall at d 6.
            BuildValveWheel(segA, doorMouth, wall, 6f, 0.6f * wallHeight, north);

            // Hazard striping on both door jambs at d 0.
            BuildJambStripes(segA, doorMouth, wall);
        }

        private static void DressOutfallPipe(Transform segA, Vector2 doorMouth, Wall wall, float wallHeight,
            float north, float dEnd)
        {
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;
            float y = 0.6f * wallHeight;
            float acrossOffset = north * (halfWidth - 0.35f);
            Vector3 dir = OutwardDir(wall);
            Vector3 across = AcrossDir(wall);
            Quaternion lie = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);

            Vector3 pipeCenter = new Vector3(doorMouth.x, y, doorMouth.y) + dir * (dEnd * 0.5f) + across * acrossOffset;
            StormdrainKit.Tube(segA, "Outfall Rust Pipe", pipeCenter, 0.15f, dEnd, lie, StormdrainKit.Rust);

            for (float d = 0f; d <= dEnd + 0.01f; d += 3f)
            {
                Vector3 at = new Vector3(doorMouth.x, y, doorMouth.y) + dir * d + across * acrossOffset;
                StormdrainKit.Tube(segA, $"Outfall Rust Collar d{d:0.0}", at, 0.2f, 0.2f, lie, StormdrainKit.RustDark);
            }
        }

        private static void BuildValveWheel(Transform segA, Vector2 doorMouth, Wall wall, float d, float y, float north)
        {
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;
            Vector3 across = AcrossDir(wall);
            Vector3 intoRoom = -north * across;
            Vector3 pos = At(doorMouth, wall, d, north * (halfWidth - 0.02f), y);
            Quaternion rot = Quaternion.LookRotation(intoRoom, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            StormdrainKit.Tube(segA, $"Valve Wheel d{d:0.0}", pos, 0.28f, 0.06f, rot, StormdrainKit.Rust);
        }

        private static void BuildJambStripes(Transform segA, Vector2 doorMouth, Wall wall)
        {
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;
            Vector3 across = AcrossDir(wall);
            Vector3 baseCentre = new Vector3(doorMouth.x, 0.02f, doorMouth.y) + OutwardDir(wall) * 0.2f;
            StormdrainKit.BuildHazardBanding(segA, baseCentre + across * (halfWidth - 0.3f), 0.5f, 0.06f, alongX: false, depth: 0.5f);
            StormdrainKit.BuildHazardBanding(segA, baseCentre - across * (halfWidth - 0.3f), 0.5f, 0.06f, alongX: false, depth: 0.5f);
        }

        // ------------------------------------------------------------------ segment B: breach (d 10-16)

        private static void DressBreach(Transform segB, Vector2 doorMouth, Wall wall, float wallHeight,
            float dStart, float dEnd)
        {
            float north = NorthSign(wall);
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;

            // The floor tone goes from Stormdrain ground to StandingWater across the segment.
            Bar(segB, "Breach Floor Sheen", doorMouth, wall, dStart, dEnd, 0f, 2.6f, 0.02f, 0.005f,
                Color.Lerp(StormdrainKit.GroundBase, StormdrainKit.StandingWater, 0.6f), SurfaceKind.Foliage);

            // Crack lines radiating across the floor from d 10.4 to d 13.8.
            float[] crackD = { 10.4f, 11.5f, 12.4f, 13.2f, 13.8f };
            float[] crackAcross = { 0.0f, 0.85f, -0.9f, 0.55f, -0.35f };
            for (int i = 0; i < crackD.Length; i++)
            {
                Vector3 at = At(doorMouth, wall, crackD[i], crackAcross[i], 0f);
                StormdrainKit.BuildCrack(segB, at, crackD[i] * 0.37f + i);
            }

            // A ReefShipWall hull plate jammed diagonally through the N wall, with a ReefCircuitCyan
            // seam along it — stays inside the wall band, never the 2.0 m centre lane.
            float plateD = 13f;   // centred in the ticket's own d 11-15 span, regardless of segment bounds
            Vector3 dir = OutwardDir(wall);
            Vector3 across = AcrossDir(wall);
            Vector3 plateCenter = new Vector3(doorMouth.x, wallHeight * 0.5f, doorMouth.y)
                + dir * plateD + across * (north * (halfWidth - 0.15f));

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Hull Breach Plate";
            plate.transform.SetParent(segB, false);
            plate.transform.position = plateCenter;
            plate.transform.rotation = Quaternion.Euler(0f, 18f, 0f);
            plate.transform.localScale = new Vector3(4f, wallHeight * 0.8f, 0.4f);
            StormdrainKit.Strip(plate);
            var plateRend = plate.GetComponent<MeshRenderer>();
            if (plateRend != null) plateRend.sharedMaterial = WorldMaterials.M_ShipWall;

            GameObject seam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seam.name = "Hull Breach Seam";
            seam.transform.SetParent(segB, false);
            seam.transform.position = plateCenter + Vector3.up * (wallHeight * 0.22f) + across * (north * 0.05f);
            seam.transform.rotation = plate.transform.rotation;
            seam.transform.localScale = new Vector3(3.6f, 0.08f, 0.05f);
            StormdrainKit.Strip(seam);
            var seamRend = seam.GetComponent<MeshRenderer>();
            if (seamRend != null) seamRend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;

            // The first ReefBioGlow growth, emissive, on the concrete floor.
            float[] glowD = { 12f, 13f, 14f };
            float[] glowAcross = { 0.7f, -0.6f, 0.5f };
            for (int i = 0; i < glowD.Length; i++)
                BuildBioGlow(segB, $"Bio Glow d{glowD[i]:0.0}", At(doorMouth, wall, glowD[i], glowAcross[i], 0.08f), 0.22f);
        }

        private static GameObject BuildBioGlow(Transform parent, string name, Vector3 pos, float diameter)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * diameter;
            StormdrainKit.Strip(go);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = WorldMaterials.M_BioGlow;
            return go;
        }

        // ------------------------------------------------------------------ segment C: hull (d 16-30)
        // and the World 3 arrival shell (segment C's own dressing, continued and scaled).

        private static void DressHullSegment(Transform host, Vector2 doorMouth, Wall wall, float wallHeight,
            float dStart, float dEnd, bool isArrival)
        {
            DressDeckAndWalls(host, doorMouth, wall, dStart, dEnd);
            DressHullStripLights(host, doorMouth, wall, dStart, dEnd);

            float span = dEnd - dStart;
            if (isArrival)
            {
                DressObservationWindows(host, doorMouth, wall, wallHeight, new[] { dStart + span * 0.5f });
                DressAccentLamps(host, doorMouth, wall, wallHeight, new[] { dStart + span * 0.3f, dStart + span * 0.7f });
                DressHullBioGlow(host, doorMouth, wall, new[] { dStart + span * 0.2f, dStart + span * 0.5f, dStart + span * 0.8f });
            }
            else
            {
                DressObservationWindows(host, doorMouth, wall, wallHeight, new[] { 19f, 25f });
                DressAccentLamps(host, doorMouth, wall, wallHeight, new[] { 18f, 22f, 26f });
                DressHullBioGlow(host, doorMouth, wall, new[] { 17.2f, 20.8f, 23.5f, 27.4f, 28.8f });
            }
        }

        /// <summary>Re-skins the shell's own floor and walls with the ReefShipFloor/ReefShipWall
        /// materials, and lays a 1 m seam grid plus one centreline seam on the deck plate.</summary>
        private static void DressDeckAndWalls(Transform host, Vector2 doorMouth, Wall wall, float dStart, float dEnd)
        {
            foreach (MeshRenderer r in host.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.transform.parent != host) continue;   // the shell's own Floor/Wall boxes only
                if (r.name.EndsWith("Floor")) r.sharedMaterial = WorldMaterials.M_ShipFloor;
                else if (r.name.Contains("Wall 1") || r.name.Contains("Wall 2")) r.sharedMaterial = WorldMaterials.M_ShipWall;
            }

            Bar(host, "Deck Centerline Seam", doorMouth, wall, dStart, dEnd, 0f, 0.06f, 0.02f, -0.015f, WorldMaterials.ReefMetalDark);
            for (float d = dStart + 1f; d < dEnd; d += 1f)
                Bar(host, $"Deck Seam d{d:0.0}", doorMouth, wall, d - 0.03f, d + 0.03f, 0f, 2.8f, 0.02f, -0.02f, WorldMaterials.ReefMetalDark);
        }

        /// <summary>ReefKit.DressHull-style base strip lights along both walls, built in contiguous 2 m
        /// chunks (not one long bar per wall) so every 3 m slice AND every 4 m emission window has its
        /// own renderer — AC1a and AC1c both read a renderer's own bounds, not a shared one spanning the
        /// whole segment.</summary>
        private static void DressHullStripLights(Transform host, Vector2 doorMouth, Wall wall, float dStart, float dEnd)
        {
            const float pitch = 2f;
            float north = NorthSign(wall);
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;
            float wallHug = halfWidth - 0.08f;

            for (float d = dStart; d < dEnd; d += pitch)
            {
                float segEnd = Mathf.Min(d + pitch, dEnd);
                foreach (float side in new[] { north, -north })
                {
                    string label = side > 0f ? "N" : "S";
                    GameObject strip = Bar(host, $"Hull Strip {label} d{d:0.0}", doorMouth, wall, d, segEnd,
                        side * wallHug, 0.12f, 0.1f, 0.05f, WorldMaterials.ReefCircuitCyan);
                    var rend = strip.GetComponent<Renderer>();
                    if (rend != null) rend.sharedMaterial = WorldMaterials.M_Circuit_Cyan;
                }
            }
        }

        /// <summary>Observation windows on the N wall only, 2.5 m wide, 0.25-0.85 x wallHeight, each with
        /// its own ocean backdrop behind it (ReefKit.BuildObservationWindow + BuildOceanBackdrop).</summary>
        private static void DressObservationWindows(Transform host, Vector2 doorMouth, Wall wall, float wallHeight, float[] ds)
        {
            float north = NorthSign(wall);
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;
            float yMin = 0.25f * wallHeight;
            float yMax = 0.85f * wallHeight;
            float yMid = (yMin + yMax) * 0.5f;
            float height = yMax - yMin;

            foreach (float d in ds)
            {
                Vector3 pos = At(doorMouth, wall, d, north * (halfWidth + 0.02f), yMid);
                GameObject window = ReefKit.BuildObservationWindow(host, pos, new Vector3(2.5f, height, 0.1f));
                ReefKit.BuildOceanBackdrop(window.transform);
            }
        }

        /// <summary>ReefCircuitPurple accent lamps at the S wall top — the S wall carries nothing taller
        /// than 0.2 x wallHeight above its own top edge, so these sit just under it, never above.</summary>
        private static void DressAccentLamps(Transform host, Vector2 doorMouth, Wall wall, float wallHeight, float[] ds)
        {
            float north = NorthSign(wall);
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;
            float y = wallHeight * 0.92f;

            foreach (float d in ds)
            {
                Vector3 pos = At(doorMouth, wall, d, -north * (halfWidth - 0.05f), y);
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Accent Lamp d{d:0.0}";
                go.transform.SetParent(host, false);
                go.transform.localPosition = pos;
                go.transform.localScale = Vector3.one * 0.2f;
                StormdrainKit.Strip(go);
                var rend = go.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = WorldMaterials.M_Circuit_Purple;
            }
        }

        /// <summary>ReefBioGlow growth clumps at alternating wall bases.</summary>
        private static void DressHullBioGlow(Transform host, Vector2 doorMouth, Wall wall, float[] ds)
        {
            float north = NorthSign(wall);
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;

            for (int i = 0; i < ds.Length; i++)
            {
                float side = (i % 2 == 0) ? north : -north;
                Vector3 pos = At(doorMouth, wall, ds[i], side * (halfWidth - 0.15f), 0.1f);
                BuildBioGlow(host, $"Hull Bio Glow d{ds[i]:0.0}", pos, 0.24f);
            }
        }
    }
}
