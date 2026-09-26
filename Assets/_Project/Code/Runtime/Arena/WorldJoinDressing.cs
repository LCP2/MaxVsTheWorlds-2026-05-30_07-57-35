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
    }
}
