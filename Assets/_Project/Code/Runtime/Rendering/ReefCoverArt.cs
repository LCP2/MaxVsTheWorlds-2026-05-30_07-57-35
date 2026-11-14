using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// MV-1352 (Reef relook 4/4): "cover with a job". The detail layers for the Reef's cover pieces -
    /// cargo-container detailing, pillar flanges/lamp/conduit, console screen content, drum skirt/valve/
    /// pipes, bulkhead hazard band/rail/hatch/lamp and floor-pipe flanges - kept in this partial so
    /// <c>ReefKit.cs</c> stays the structure kit itself. Art only: every part here is collider-free and the
    /// authored footprint, height, collider and cover kind never change. Generated meshes only (no
    /// built-in primitive meshes); one shared material per surface type; glows are the shared additive
    /// floor pools (<see cref="ShipKit.BuildGraduatedPool"/>), never real lights. Floor-level extras
    /// (cable, flanges, loose boxes, coral nubs) are lower than <see cref="FloorPropMaxHeight"/>.
    /// </summary>
    public static partial class ReefKit
    {
        /// <summary>Ticket rule: every new floor prop is lower than this (metres).</summary>
        public const float FloorPropMaxHeight = 0.15f;

        // ------------------------------------------------------------------ generated text
        // A 3x5 block font as ONE flat mesh per word (a quad per lit pixel) - stencil lettering without a
        // font asset or a TextMesh, so it renders identically headless, in the Editor and on a phone.

        private static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['R'] = "110101110101101", ['-'] = "000000111000000", ['0'] = "111101101101111",
            ['7'] = "111001010010010", ['K'] = "101101110101101", ['4'] = "101101111001001",
            ['9'] = "111101111001111", ['H'] = "101101111101101", ['O'] = "111101101101111",
            ['L'] = "100100100100111", ['D'] = "110101101101110", ['X'] = "101101010101101",
            ['2'] = "111001111100111",
        };

        private static readonly Dictionary<string, Mesh> s_stencilCache = new Dictionary<string, Mesh>();

        /// <summary>Width in metres of <paramref name="word"/> at pixel size <paramref name="px"/>.</summary>
        public static float StencilWidth(string word, float px) => (word.Length * 4 - 1) * px;

        /// <summary>A flat (XZ plane, facing up) block-letter mesh centred on the origin.</summary>
        public static Mesh StencilMesh(string word, float px)
        {
            string key = word + "|" + Mathf.RoundToInt(px * 1000f);
            if (s_stencilCache.TryGetValue(key, out Mesh hit) && hit != null) return hit;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            float totalW = StencilWidth(word, px), totalH = 5 * px;
            for (int c = 0; c < word.Length; c++)
            {
                if (!Glyphs.TryGetValue(word[c], out string bits)) continue;
                for (int i = 0; i < 15; i++)
                {
                    if (bits[i] != '1') continue;
                    int gx = i % 3, gy = i / 3;                         // gy 0 = top row
                    float x0 = -totalW * 0.5f + (c * 4 + gx) * px;
                    float z1 = totalH * 0.5f - gy * px, z0 = z1 - px;   // top row sits at +Z
                    int b = verts.Count;
                    verts.Add(new Vector3(x0, 0f, z0)); verts.Add(new Vector3(x0, 0f, z1));
                    verts.Add(new Vector3(x0 + px, 0f, z1)); verts.Add(new Vector3(x0 + px, 0f, z0));
                    tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                }
            }

            var mesh = new Mesh { name = "ReefStencil_" + word, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            s_stencilCache[key] = mesh;
            return mesh;
        }

        private static readonly Dictionary<string, Mesh> s_stripeCache = new Dictionary<string, Mesh>();

        /// <summary>Slanted hazard stripes filling a <paramref name="length"/> x <paramref name="height"/>
        /// band in the local XY plane (double-sided), centred on the origin; the gaps between stripes let the
        /// dark body show through, so one mesh and one material make the whole "hazard-stripe band".</summary>
        private static Mesh HazardStripeMesh(float length, float height)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(length / (height * 1.6f)));
            string key = count + "|" + Mathf.RoundToInt(length * 100f) + "|" + Mathf.RoundToInt(height * 100f);
            if (s_stripeCache.TryGetValue(key, out Mesh hit) && hit != null) return hit;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            float pitch = length / count, w = pitch * 0.5f, slant = height * 0.5f, half = length * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float x0 = -half + i * pitch;
                int b = verts.Count;
                verts.Add(new Vector3(Mathf.Clamp(x0, -half, half), -height * 0.5f, 0f));
                verts.Add(new Vector3(Mathf.Clamp(x0 + slant, -half, half), height * 0.5f, 0f));
                verts.Add(new Vector3(Mathf.Clamp(x0 + w + slant, -half, half), height * 0.5f, 0f));
                verts.Add(new Vector3(Mathf.Clamp(x0 + w, -half, half), -height * 0.5f, 0f));
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3, b, b + 2, b + 1, b, b + 3, b + 2 });
            }

            var mesh = new Mesh { name = "ReefHazardStripes", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            s_stripeCache[key] = mesh;
            return mesh;
        }

        private static GameObject Stencil(Transform parent, string name, string word, float px, Vector3 pos,
            float yawDegrees, Material mat)
            => MeshPart(parent, name, StencilMesh(word, px), pos, mat, Quaternion.Euler(0f, yawDegrees, 0f));

        private static GameObject Barnacle(Transform parent, Vector3 pos, float radius)
            => MeshPart(parent, "Barnacle", CharacterMeshes.Sphere(6), pos, WorldMaterials.M_Barnacle,
                localScale: new Vector3(radius * 2f, radius * 1.4f, radius * 2f));

        /// <summary>A flange collar: a short lathed ring of <paramref name="radius"/> around a pipe/column.</summary>
        private static Mesh CollarMesh(float radius, float thickness)
            => CharacterMeshes.Ring(radius * 0.82f, radius, thickness, 12);

        /// <summary>A red valve wheel lying flat: a rim ring, a hub and two spokes. Always collider-free.</summary>
        private static void ValveWheel(Transform parent, Vector3 pos, float radius)
        {
            var go = new GameObject("Valve Wheel");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            MeshPart(go.transform, "Rim", CharacterMeshes.Ring(radius * 0.8f, radius, 0.03f, 14), Vector3.zero, WorldMaterials.M_ValveRed);
            MeshPart(go.transform, "Hub", CharacterMeshes.Sphere(6), Vector3.zero, WorldMaterials.M_ValveRed,
                localScale: Vector3.one * radius * 0.4f);
            Mesh spoke = CharacterMeshes.Bevelled(new Vector3(radius * 1.8f, 0.02f, 0.025f), 0.005f);
            MeshPart(go.transform, "Spoke A", spoke, Vector3.zero, WorldMaterials.M_ValveRed);
            MeshPart(go.transform, "Spoke B", spoke, Vector3.zero, WorldMaterials.M_ValveRed, Quaternion.Euler(0f, 90f, 0f));
        }

        // ------------------------------------------------------------------ cargo container

        /// <summary>Everything a cargo container adds to its authored crate body: corrugation ribs on the
        /// long sides, frame rails, corner posts and castings, a door end with 2-4 locking bars, a yellow
        /// tie-down strap, white stencils, a rust patch, six barnacles and 1-2 loose boxes beside it.
        /// <paramref name="detail"/> is a metre-space root centred on the body (the body itself is scaled
        /// to its footprint, so the caller cancels that scale on this root). The body wears the seeded
        /// container paint (<see cref="WorldMaterials.ContainerMaterial"/>).</summary>
        public static void BuildContainerDetail(Transform detail, Vector3 size, string seedKey)
        {
            uint seed = StableHash(seedKey);
            Material paint = WorldMaterials.ContainerMaterial(seed);
            Material dark = WorldMaterials.M_MetalDark;

            bool alongX = size.x >= size.z;
            float len = alongX ? size.x : size.z, wid = alongX ? size.z : size.x, h = size.y;
            // Build in a local frame (u along the long side, v across, y up), then yaw into place.
            var frame = new GameObject("Container Frame").transform;
            frame.SetParent(detail, false);
            frame.localRotation = Quaternion.Euler(0f, alongX ? 0f : 90f, 0f);

            // Corrugation ribs on both long faces.
            float pitch = 0.28f;
            int ribs = Mathf.Max(2, Mathf.FloorToInt((len - 0.5f) / pitch));
            Mesh rib = CharacterMeshes.Bevelled(new Vector3(0.07f, h * 0.78f, 0.05f), 0.012f);
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < ribs; i++)
                {
                    float u = (i + 0.5f) / ribs * (len - 0.5f) - (len - 0.5f) * 0.5f;
                    MeshPart(frame, "Rib", rib, new Vector3(u, 0f, s * (wid * 0.5f + 0.01f)), paint);
                }

            // Frame rails (top and bottom, both long sides) and corner posts + castings.
            Mesh rail = CharacterMeshes.Bevelled(new Vector3(len, 0.08f, 0.08f), 0.02f);
            Mesh post = CharacterMeshes.Bevelled(new Vector3(0.1f, h, 0.1f), 0.02f);
            Mesh casting = CharacterMeshes.Bevelled(new Vector3(0.16f, 0.14f, 0.16f), 0.03f);
            for (int sv = -1; sv <= 1; sv += 2)
            {
                MeshPart(frame, "Rail Top", rail, new Vector3(0f, h * 0.5f - 0.03f, sv * (wid * 0.5f)), dark);
                MeshPart(frame, "Rail Bottom", rail, new Vector3(0f, -h * 0.5f + 0.04f, sv * (wid * 0.5f)), dark);
                for (int su = -1; su <= 1; su += 2)
                {
                    Vector3 corner = new Vector3(su * (len * 0.5f - 0.04f), 0f, sv * (wid * 0.5f - 0.04f));
                    MeshPart(frame, "Corner Post", post, corner, dark);
                    MeshPart(frame, "Corner Casting Top", casting, corner + Vector3.up * (h * 0.5f - 0.05f), dark);
                    MeshPart(frame, "Corner Casting Bottom", casting, corner + Vector3.down * (h * 0.5f - 0.05f), dark);
                }
            }

            // Door end (+u): a seam, 2-4 locking bars and a handle plate.
            int bars = 2 + (int)((seed >> 3) % 3u);
            float doorU = len * 0.5f + 0.015f;
            MeshPart(frame, "Door Seam", CharacterMeshes.Bevelled(new Vector3(0.03f, h * 0.9f, 0.03f), 0.006f),
                new Vector3(doorU, 0f, 0f), dark);
            Mesh bar = CharacterMeshes.Beam(h * 0.86f, 0.025f, 0.025f, 6);
            for (int i = 0; i < bars; i++)
            {
                float v = ((i + 0.5f) / bars - 0.5f) * (wid * 0.8f);
                MeshPart(frame, "Locking Bar", bar, new Vector3(doorU + 0.02f, 0f, v), WorldMaterials.M_Steel);
            }

            // Top: yellow strap over the short axis, rust patch, stencils, barnacles.
            float topY = h * 0.5f + 0.012f;
            float strapU = ((int)((seed >> 7) % 5u) - 2) * len * 0.06f;
            MeshPart(frame, "Strap Top", CharacterMeshes.Bevelled(new Vector3(0.12f, 0.02f, wid + 0.04f), 0.004f),
                new Vector3(strapU, topY, 0f), WorldMaterials.M_StrapYellow);
            for (int sv = -1; sv <= 1; sv += 2)
                MeshPart(frame, "Strap Drop", CharacterMeshes.Bevelled(new Vector3(0.12f, h * 0.35f, 0.02f), 0.004f),
                    new Vector3(strapU, h * 0.5f - h * 0.175f, sv * (wid * 0.5f + 0.045f)), WorldMaterials.M_StrapYellow);

            MeshPart(frame, "Rust Patch", CharacterMeshes.Bevelled(new Vector3(len * 0.22f, 0.01f, wid * 0.3f), 0.004f),
                new Vector3(-len * 0.28f + strapU * 0.2f, topY - 0.002f, wid * 0.15f), WorldMaterials.M_RustPatch);

            float px = Mathf.Min(0.05f, wid * 0.07f);
            string[] words = { "R-07", "K4", "09", "HOLD" };
            Vector3[] spots =
            {
                new Vector3(-len * 0.33f, topY + 0.004f, -wid * 0.28f),
                new Vector3(-len * 0.33f, topY + 0.004f, wid * 0.0f),
                new Vector3(len * 0.12f, topY + 0.004f, -wid * 0.28f),
                new Vector3(len * 0.30f, topY + 0.004f, wid * 0.22f),
            };
            for (int i = 0; i < words.Length; i++)
            {
                if (StencilWidth(words[i], px) > len * 0.38f) continue;
                Stencil(frame, "Stencil " + words[i], words[i], px, spots[i], 0f, WorldMaterials.M_StencilWhite);
            }

            for (int i = 0; i < 6; i++)
            {
                uint a = StableHash(seedKey + "_b" + i);
                float bu = ((a % 1000u) / 1000f - 0.5f) * (len * 0.8f);
                float bv = (((a >> 10) % 1000u) / 1000f - 0.5f) * (wid * 0.7f);
                Barnacle(frame, new Vector3(bu, topY + 0.015f, bv), 0.035f + (a % 3u) * 0.008f);
            }

            // 1-2 small loose boxes beside the container (visual only, lower than the floor-prop cap).
            int boxes = 1 + (int)((seed >> 11) % 2u);
            Mesh looseBox = CharacterMeshes.Bevelled(new Vector3(0.3f, 0.14f, 0.24f), 0.02f);
            for (int i = 0; i < boxes; i++)
            {
                float lu = (i == 0 ? -0.3f : 0.3f) * len * 0.5f;
                MeshPart(frame, "Loose Box", looseBox, new Vector3(lu, -h * 0.5f + 0.07f, wid * 0.5f + 0.3f),
                    i == 0 ? WorldMaterials.M_Barnacle : WorldMaterials.M_RustPatch, Quaternion.Euler(0f, 12f + i * 31f, 0f));
            }
        }

        // ------------------------------------------------------------------ pillar

        private static void BuildPillarDetail(Transform root, Vector3 size, float radius)
        {
            float floorY = -size.y * 0.5f;

            // Three flange rings, four rivet rings.
            Mesh flange = CharacterMeshes.Ring(radius * 0.78f, radius * 1.0f, 0.09f, 8);
            Mesh rivet = CharacterMeshes.Ring(radius * 0.82f, radius * 0.9f, 0.03f, 16);
            for (int i = 0; i < 3; i++)
                MeshPart(root, "Flange Ring", flange, new Vector3(0f, floorY + size.y * (0.2f + i * 0.28f), 0f), WorldMaterials.M_Steel);
            for (int i = 0; i < 4; i++)
                MeshPart(root, "Rivet Ring", rivet, new Vector3(0f, floorY + size.y * (0.12f + i * 0.2f), 0f), WorldMaterials.M_Steel);

            // Amber lamp band at 2.25 m with a floor pool.
            float lampY = Mathf.Min(2.25f, size.y * 0.9f);
            MeshPart(root, "Lamp Band", CharacterMeshes.Ring(radius * 0.78f, radius * 0.95f, 0.14f, 12),
                new Vector3(0f, floorY + lampY, 0f), WorldMaterials.M_LampAmber);
            ShipKit.BuildGraduatedPool(root, new Vector3(0f, floorY, 0f), radius * 2.6f, WorldMaterials.ReefWorkLampAmber * 0.6f);

            // Copper conduit up one side and a gauge box with a lit dial.
            float cx = radius * 0.82f;
            MeshPart(root, "Conduit", CharacterMeshes.Beam(size.y * 0.8f, 0.035f, 0.035f, 8), new Vector3(cx, 0f, 0f), WorldMaterials.M_Copper);
            for (int i = 0; i < 3; i++)
                MeshPart(root, "Conduit Clamp", CollarMesh(0.06f, 0.04f), new Vector3(cx, floorY + size.y * (0.2f + i * 0.28f), 0f),
                    WorldMaterials.M_Steel, Quaternion.identity);
            Vector3 gaugeAt = new Vector3(0f, floorY + 1.2f, -(radius * 0.82f + 0.05f));
            MeshPart(root, "Gauge Box", CharacterMeshes.Bevelled(new Vector3(0.26f, 0.2f, 0.1f), 0.02f), gaugeAt, WorldMaterials.M_MetalDark);
            MeshPart(root, "Gauge Dial", CharacterMeshes.Sphere(8), gaugeAt + new Vector3(0f, 0f, -0.05f), WorldMaterials.M_Circuit_Cyan,
                localScale: Vector3.one * 0.1f);

            // Base: barnacles and five glowing coral nubs (all lower than the floor-prop cap).
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f + 0.3f;
                Barnacle(root, new Vector3(Mathf.Cos(a) * radius * 0.98f, floorY + 0.12f + (i % 2) * 0.1f, Mathf.Sin(a) * radius * 0.98f), 0.05f);
            }
            Mesh nub = CharacterMeshes.Prism(5, 0.035f, 0.012f, FloorPropMaxHeight * 0.9f, 0.1f);
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f + 0.9f;
                MeshPart(root, "Coral Nub", nub, new Vector3(Mathf.Cos(a) * radius * 1.22f, floorY + FloorPropMaxHeight * 0.45f, Mathf.Sin(a) * radius * 1.22f),
                    WorldMaterials.M_BioGlow);
            }
        }

        // ------------------------------------------------------------------ console

        private static void BuildConsoleScreen(Transform root, Vector3 size, float deskTopY)
        {
            // Tilted top (normal leans toward the camera side, -Z) carrying the lit screen content.
            var tilt = new GameObject("Screen Tilt").transform;
            tilt.SetParent(root, false);
            tilt.localPosition = new Vector3(0f, deskTopY + 0.02f, 0f);
            tilt.localRotation = Quaternion.Euler(-22f, 0f, 0f);

            float w = size.x * 0.86f, d = size.z * 0.82f;
            MeshBox(tilt, "Screen Bezel", Vector3.zero, new Vector3(w, 0.04f, d), WorldMaterials.M_MetalDark);
            MeshBox(tilt, "Screen", new Vector3(0f, 0.022f, d * 0.12f), new Vector3(w * 0.9f, 0.012f, d * 0.6f), WorldMaterials.M_MetalDark);

            float sy = 0.032f, sx = w * 0.9f;
            // Waveform: eight thin segments rising and falling.
            Mesh seg = CharacterMeshes.Bevelled(new Vector3(sx * 0.1f, 0.01f, 0.025f), 0.004f);
            for (int i = 0; i < 8; i++)
                MeshPart(tilt, "Wave", seg, new Vector3(-sx * 0.45f + i * sx * 0.1f * 1.05f, sy, d * 0.3f + Mathf.Sin(i * 0.9f) * d * 0.07f),
                    WorldMaterials.M_Circuit_Cyan);
            // Coloured bars.
            Material[] bars = { WorldMaterials.M_KelpGreen, WorldMaterials.M_Hazard, WorldMaterials.M_KelpMagenta, WorldMaterials.M_Circuit_Purple };
            for (int i = 0; i < 4; i++)
                MeshPart(tilt, "Bar", CharacterMeshes.Bevelled(new Vector3(sx * (0.22f + 0.1f * ((i * 3) % 4)), 0.01f, 0.03f), 0.004f),
                    new Vector3(-sx * 0.2f, sy, d * 0.14f - i * 0.055f), bars[i]);
            // Dial.
            MeshPart(tilt, "Dial", CharacterMeshes.Ring(0.03f, 0.065f, 0.012f, 14), new Vector3(sx * 0.34f, sy, d * 0.06f), WorldMaterials.M_Circuit_Cyan);
            MeshPart(tilt, "Dial Needle", CharacterMeshes.Bevelled(new Vector3(0.05f, 0.01f, 0.01f), 0.002f),
                new Vector3(sx * 0.34f + 0.015f, sy + 0.004f, d * 0.06f), WorldMaterials.M_KelpMagenta, Quaternion.Euler(0f, 35f, 0f));

            // Row of six coloured buttons.
            Material[] btn = { WorldMaterials.M_Circuit_Cyan, WorldMaterials.M_KelpGreen, WorldMaterials.M_Hazard,
                WorldMaterials.M_KelpMagenta, WorldMaterials.M_LampAmber, WorldMaterials.M_Circuit_Purple };
            for (int i = 0; i < 6; i++)
                MeshPart(tilt, "Button", CharacterMeshes.Sphere(6), new Vector3(-w * 0.34f + i * w * 0.136f, 0.03f, -d * 0.4f),
                    btn[i], localScale: Vector3.one * 0.05f);
        }

        private static void BuildConsoleExtras(Transform root, Vector3 size)
        {
            float floorY = -size.y * 0.5f;
            // Front vent slats.
            Mesh slat = CharacterMeshes.Bevelled(new Vector3(size.x * 0.6f, 0.025f, 0.03f), 0.006f);
            for (int i = 0; i < 4; i++)
                MeshPart(root, "Vent Slat", slat, new Vector3(0f, floorY + size.y * (0.2f + i * 0.1f), -size.z * 0.5f - 0.012f), WorldMaterials.M_Steel);

            // Cable snaking ~2 m across the floor (a chain of short low segments).
            Mesh cableSeg = CharacterMeshes.Bevelled(new Vector3(0.22f, 0.04f, 0.04f), 0.01f);
            Vector3 start = new Vector3(size.x * 0.5f, floorY + 0.03f, -size.z * 0.2f);
            Vector3 prev = start;
            for (int i = 1; i <= 10; i++)
            {
                Vector3 next = start + new Vector3(i * 0.2f, 0f, Mathf.Sin(i * 0.8f) * 0.25f);
                Vector3 dir = next - prev;
                MeshPart(root, "Cable", cableSeg, (prev + next) * 0.5f, WorldMaterials.M_MetalDark,
                    Quaternion.Euler(0f, -Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg, 0f), new Vector3(dir.magnitude / 0.22f * 1.05f, 1f, 1f));
                prev = next;
            }

            // Faint green pool.
            ShipKit.BuildGraduatedPool(root, new Vector3(0f, floorY, -size.z * 0.4f), Mathf.Max(size.x, size.z) * 1.1f,
                WorldMaterials.ReefKelpGreen * 0.3f);
        }

        // ------------------------------------------------------------------ drum

        private static void BuildDrumDetail(Transform root, Vector3 size, float radius)
        {
            float floorY = -size.y * 0.5f;

            // Hazard-stripe skirt: an orange ring with dark blocks between the stripes.
            MeshPart(root, "Skirt", CharacterMeshes.Lathe(new[]
            {
                new Vector2(radius * 1.0f, 0f), new Vector2(radius * 1.06f, 0.02f),
                new Vector2(radius * 1.06f, size.y * 0.11f), new Vector2(radius * 1.0f, size.y * 0.12f),
            }, 20), new Vector3(0f, floorY, 0f), WorldMaterials.M_Hazard);
            Mesh stripe = CharacterMeshes.Bevelled(new Vector3(radius * 0.2f, size.y * 0.1f, 0.03f), 0.01f);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                MeshPart(root, "Skirt Stripe", stripe, new Vector3(Mathf.Cos(a) * radius * 1.075f, floorY + size.y * 0.06f, Mathf.Sin(a) * radius * 1.075f),
                    WorldMaterials.M_MetalDark, Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f));
            }

            // Two dark cooling collars, each with a thin cyan reactor ring on its face (MV-1364: the collar
            // itself was solid emissive cyan and read as a rough bright rim).
            for (int i = 0; i < 2; i++)
            {
                float ringY = floorY + size.y * (0.32f + i * 0.22f);
                MeshPart(root, "Cooling Ring", CharacterMeshes.Ring(radius * 1.0f, radius * 1.09f, 0.07f, 24),
                    new Vector3(0f, ringY, 0f), WorldMaterials.M_MetalDark);
                MeshPart(root, "Reactor Ring", CharacterMeshes.Ring(radius * 1.085f, radius * 1.1f, 0.015f, 24),
                    new Vector3(0f, ringY, 0f), WorldMaterials.M_Circuit_Cyan);
            }

            // Dark bolted lid.
            float lidY = size.y * 0.5f - size.y * 0.06f;
            MeshPart(root, "Lid", CharacterMeshes.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(radius * 0.86f, 0f), new Vector2(radius * 0.9f, 0.03f), new Vector2(0f, 0.05f),
            }, 20), new Vector3(0f, lidY, 0f), WorldMaterials.M_MetalDark);
            Mesh bolt = CharacterMeshes.Sphere(6);
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                MeshPart(root, "Lid Bolt", bolt, new Vector3(Mathf.Cos(a) * radius * 0.78f, lidY + 0.04f, Mathf.Sin(a) * radius * 0.78f),
                    WorldMaterials.M_Steel, localScale: Vector3.one * 0.06f);
            }

            // Red valve wheel and the "RX-2" label on the lid.
            ValveWheel(root, new Vector3(radius * 0.3f, lidY + 0.09f, radius * 0.25f), radius * 0.22f);
            float px = Mathf.Min(0.05f, radius * 0.05f);
            Stencil(root, "Label RX-2", "RX-2", px, new Vector3(-radius * 0.15f, lidY + 0.058f, -radius * 0.45f), 0f, WorldMaterials.M_StencilWhite);

            // Three copper pipes curving from the drum side into the deck, with floor flanges.
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 35f) * Mathf.Deg2Rad;
                Vector3 outDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float bendR = radius + 0.38f;
                float y0 = floorY + size.y * 0.42f;
                Vector3 p0 = outDir * (radius * 0.98f) + Vector3.up * y0;
                Vector3 p1 = outDir * bendR + Vector3.up * y0;
                Vector3 p2 = outDir * bendR + Vector3.up * (floorY + 0.03f);
                Pipe(root, "Drum Pipe H", p0, p1, 0.05f, WorldMaterials.M_Copper);
                Pipe(root, "Drum Pipe V", p1, p2, 0.05f, WorldMaterials.M_Copper);
                MeshPart(root, "Pipe Elbow", CharacterMeshes.Sphere(8), p1, WorldMaterials.M_Copper, localScale: Vector3.one * 0.11f);
                MeshPart(root, "Floor Flange", CollarMesh(0.13f, 0.04f), new Vector3(p2.x, floorY + 0.02f, p2.z), WorldMaterials.M_Steel);
            }

            ShipKit.BuildGraduatedPool(root, new Vector3(0f, floorY, 0f), radius * 2.6f, WorldMaterials.ReefAccentCyan * 0.5f);
        }

        /// <summary>A beam between two local points.</summary>
        private static GameObject Pipe(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material mat)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return null;
            return MeshPart(parent, name, CharacterMeshes.Beam(len, radius, radius, 8), (a + b) * 0.5f, mat,
                Quaternion.FromToRotation(Vector3.up, d / len));
        }

        // ------------------------------------------------------------------ bulkhead

        private static void BuildBulkheadDressing(Transform root, string pieceId, Vector3 size, bool alongX, float faceLength, float faceDepth)
        {
            float topY = size.y * 0.5f;

            // Hazard-stripe band along the top edge of each long face.
            float bandH = 0.16f;
            Mesh band = HazardStripeMesh(faceLength * 0.98f, bandH);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 n = alongX ? new Vector3(0f, 0f, side) : new Vector3(side, 0f, 0f);
                MeshPart(root, "Hazard Band", band, n * (faceDepth * 0.5f + BulkheadRibProud + 0.012f) + Vector3.up * (topY - bandH * 0.5f - 0.1f),
                    WorldMaterials.M_Hazard, Quaternion.LookRotation(n, Vector3.up));
            }

            // Wide stacks: a side copper pipe (vent grilles come from BuildBulkheadDetails).
            bool wide = faceLength >= 6f;
            if (wide)
            {
                Vector3 n = alongX ? new Vector3(0f, 0f, 1f) : new Vector3(1f, 0f, 0f);
                Quaternion lie = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
                MeshPart(root, "Side Pipe", CharacterMeshes.Beam(faceLength * 0.9f, 0.07f, 0.07f, 8),
                    n * (faceDepth * 0.5f + BulkheadRibProud + 0.08f) + Vector3.up * (-size.y * 0.3f), WorldMaterials.M_Copper, lie);
            }

            // Top: dark grating, brass rail along both long edges, hatch plate.
            float plateTop = topY + BulkheadTopPlateThickness + 0.012f;
            Vector3 gratingSize = alongX
                ? new Vector3(faceLength * 0.9f, 0.012f, faceDepth * 0.7f)
                : new Vector3(faceDepth * 0.7f, 0.012f, faceLength * 0.9f);
            MeshBox(root, "Top Grating", new Vector3(0f, plateTop, 0f), gratingSize, WorldMaterials.M_GratingPatch);

            float railY = plateTop + 0.3f;
            for (int side = -1; side <= 1; side += 2)
            {
                float off = side * (faceDepth * 0.5f - 0.1f);
                Vector3 railPos = alongX ? new Vector3(0f, railY, off) : new Vector3(off, railY, 0f);
                Quaternion lie = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
                MeshPart(root, "Brass Rail", CharacterMeshes.Beam(faceLength * 0.94f, 0.02f, 0.02f, 6), railPos, WorldMaterials.M_Brass, lie);
                int posts = Mathf.Max(2, Mathf.FloorToInt(faceLength / 3f) + 1);
                for (int i = 0; i < posts; i++)
                {
                    float t = (float)i / (posts - 1) - 0.5f;
                    float along = t * faceLength * 0.92f;
                    Vector3 p = alongX ? new Vector3(along, plateTop + 0.15f, off) : new Vector3(off, plateTop + 0.15f, along);
                    MeshPart(root, "Rail Post", CharacterMeshes.Beam(0.3f, 0.015f, 0.015f, 5), p, WorldMaterials.M_Brass);
                }
            }

            MeshBox(root, "Hatch Plate", new Vector3(faceLength * 0.18f * (alongX ? 1f : 0f), plateTop + 0.01f, faceLength * 0.18f * (alongX ? 0f : 1f)),
                new Vector3(0.7f, 0.025f, 0.7f), WorldMaterials.M_MetalDark);
            MeshBox(root, "Hatch Handle", new Vector3(faceLength * 0.18f * (alongX ? 1f : 0f), plateTop + 0.035f, faceLength * 0.18f * (alongX ? 0f : 1f)),
                new Vector3(0.3f, 0.02f, 0.05f), WorldMaterials.M_Brass);

            // One amber lamp with pool on stacks of at least 6 m^2.
            if (size.x * size.z >= 6f)
            {
                Vector3 lampAt = new Vector3(alongX ? -faceLength * 0.25f : 0f, plateTop + 0.12f, alongX ? 0f : -faceLength * 0.25f);
                MeshPart(root, "Top Lamp", CharacterMeshes.Sphere(8), lampAt, WorldMaterials.M_LampAmber, localScale: Vector3.one * 0.18f);
                MeshPart(root, "Top Lamp Post", CharacterMeshes.Beam(0.12f, 0.025f, 0.025f, 6), lampAt + Vector3.down * 0.08f, WorldMaterials.M_MetalDark);
                ShipKit.BuildGraduatedPool(root, new Vector3(lampAt.x, plateTop, lampAt.z), 2.2f, WorldMaterials.ReefWorkLampAmber * 0.5f);
            }
        }

        // ------------------------------------------------------------------ floor pipe

        private static void BuildFloorPipeDetail(Transform root, Vector3 size, bool alongX, float runLength, float radius)
        {
            Quaternion lie = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
            int flanges = Mathf.Max(2, Mathf.FloorToInt(runLength / 1.2f) + 1);
            Mesh collar = CharacterMeshes.Ring(radius * 1.0f, radius * 1.3f, 0.06f, 12);
            for (int i = 0; i < flanges; i++)
            {
                float off = ((float)i / (flanges - 1) - 0.5f) * (runLength - 0.1f);
                Vector3 p = alongX ? new Vector3(off, 0f, 0f) : new Vector3(0f, 0f, off);
                MeshPart(root, "Pipe Flange", collar, p, WorldMaterials.M_Steel, lie);
            }
            ValveWheel(root, new Vector3(0f, radius * 1.05f, 0f), Mathf.Min(radius * 0.9f, 0.3f));
        }
    }
}
