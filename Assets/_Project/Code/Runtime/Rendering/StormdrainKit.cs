using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// World 2's Stormdrain environment kit — the buildable pieces that give the drain a silhouette.
    ///
    /// World 2 shipped with a colour palette and nothing else (MV-690's documented scope cut, quoted
    /// in <see cref="ReefKit"/>'s own header). MV-750 then correctly removed the borrowed garden
    /// props, which left a recoloured greybox: a flat floor, thin walls, grey cover blocks. This is
    /// the kit those two tickets never landed.
    ///
    /// Direction is the Stormdrain key art: a dark, low-value wet floor; ONE saturated hero colour
    /// (acid-green sludge) carrying the eye; rust/amber metal as the only other saturated family;
    /// pooled warm light with real darkness between; heavy horizontal pipe runs and kerbs breaking
    /// every wall foot. Read the frame as SHAPE first — that is what the current build has none of.
    ///
    /// PERFORMANCE CONTRACT — this kit adds NO real-time lights. Every "lamp" is an unlit emissive
    /// quad plus a flat additive pool on the floor. URP Forward on iOS pays per additional light per
    /// pixel, and a drain with forty wall lamps in it would cost more than the whole rest of the
    /// world. Fog and emissive fakes buy the same read for nothing.
    ///
    /// Nothing built here carries a collider — same contract every
    /// <see cref="MaxWorlds.Arena.BackyardDressing"/> and <see cref="ReefDressing"/> prop keeps: the
    /// map's own wall boxes and each cover block's own box remain the only things that stop anyone.
    /// </summary>
    public static class StormdrainKit
    {
        // ---------------------------------------------------------------- palette

        /// <summary>Rusted iron — pipes, collars, standpipes, valve wheels. W2 RUST #985025.
        /// MV-783: desaturated and darkened alongside <see cref="BiomePalette.Stormdrain"/>'s own
        /// <c>Metal</c> — rust is an accent against a cool concrete base now, not one of the world's
        /// only two colours.</summary>
        public static readonly Color Rust = new Color(0.560f, 0.330f, 0.215f);

        /// <summary>Darker rust for collars and flanges, so a pipe run has joints in it.</summary>
        public static readonly Color RustDark = new Color(0.38f, 0.20f, 0.10f);

        /// <summary>Wet concrete, one step darker than the wall so a kerb reads against it. MV-783:
        /// retoned onto the approved cool base alongside <see cref="BiomePalette.Stormdrain"/>'s
        /// <c>Foliage</c> (the algae-stain family) — still deliberately between the floor and wall
        /// tiers.</summary>
        public static readonly Color KerbConcrete = new Color(0.305f, 0.345f, 0.275f);

        /// <summary>The soffit's underside — the shadow that says "roof". MV-783: retoned onto the
        /// approved cool base; still the darkest neutral in the room, one step below the floor.</summary>
        public static readonly Color Soffit = new Color(0.135f, 0.150f, 0.170f);

        /// <summary>Lamp glass. Warm amber, deliberately over 1.0 in no channel — the emissive
        /// material is unlit, so its albedo IS its output and clipping it just loses the colour.</summary>
        public static readonly Color LampAmber = new Color(0.98f, 0.72f, 0.34f);

        /// <summary>The floor pool under a lamp. Same hue, far lower value: it is added onto whatever
        /// the floor already is, so a bright pool blows out to white paper.</summary>
        public static readonly Color LampPool = new Color(0.42f, 0.28f, 0.11f);

        /// <summary>Algae creep — the streak below a lamp and the crust on a silt bin.</summary>
        public static readonly Color Algae = new Color(0.26f, 0.36f, 0.14f);

        /// <summary>Acid sludge — the world's one saturated hero colour (MV-783: pulled darker so it
        /// still reads as the brightest, most saturated thing in the room against the now-lit-up
        /// neutrals, rather than losing its pop). MV-785 retoned <c>MapRuntime.SludgeColor</c> to match
        /// this exact value, closing the divergence MV-783 left open. Duplicated as a constant rather
        /// than referenced because Rendering must not depend on Arena.</summary>
        public static readonly Color Sludge = new Color(0.200f, 0.300f, 0.115f);

        /// <summary>The brighter lip around a sludge tile's edge (MV-785: narrowed to one per bank) —
        /// the brightest thing in World 2, and the only place full-strength green is allowed.</summary>
        public static readonly Color SludgeBright = new Color(0.520f, 0.720f, 0.250f);

        /// <summary>A flow band's brighter alternate tone (MV-785) — alternates with <see cref="Sludge"/>
        /// across the ten bands so the channel reads as banded flow, not a flat fill.</summary>
        public static readonly Color SludgeBand = new Color(0.300f, 0.430f, 0.150f);

        /// <summary>The flow chevrons' own mid tone (MV-785) — between <see cref="Sludge"/> and
        /// <see cref="SludgeBright"/>, so the direction-of-travel marks read as part of the flow rather
        /// than as bright as the lip.</summary>
        public static readonly Color SludgeChevronMid = new Color(0.390f, 0.540f, 0.190f);

        /// <summary>Hazard stripe yellow — the one place World 2 is allowed a pure warning colour.</summary>
        public static readonly Color Hazard = new Color(0.85f, 0.65f, 0.15f);

        /// <summary>Cyan status lamps on machinery. The cold counterpoint to all that rust.</summary>
        public static readonly Color Status = new Color(0.35f, 0.85f, 0.95f);

        /// <summary>The recessed panel-joint line (MV-781, change 2) — sitting deliberately between the
        /// floor and <see cref="Soffit"/>. MV-783: retoned onto the approved cool base.</summary>
        public static readonly Color PanelJoint = new Color(0.150f, 0.170f, 0.195f);

        /// <summary>A bay crack (MV-784, change 3) — darker than <see cref="PanelJoint"/> so a hairline
        /// reads as damage IN the slab rather than another joint line.</summary>
        public static readonly Color Crack = new Color(PanelJoint.r * 0.55f, PanelJoint.g * 0.55f, PanelJoint.b * 0.55f);

        /// <summary>The three cast-bay tones (MV-784, change 1) — duplicated as constants rather than
        /// read off <see cref="MaterialLibrary.Palette"/> for the same reason <see cref="Sludge"/> is
        /// (Rendering must not depend on which palette happens to be active when this kit builds), kept
        /// bit-identical to <see cref="BiomePalette.Stormdrain"/>'s own GroundDry/GroundBase/GroundAccent
        /// so the bay grid and the ground shader agree on what "the floor" looks like.</summary>
        public static readonly Color GroundDry = new Color(0.205f, 0.230f, 0.260f);
        public static readonly Color GroundBase = new Color(0.255f, 0.285f, 0.315f);
        public static readonly Color GroundAccent = new Color(0.300f, 0.330f, 0.360f);

        /// <summary>Silt (MV-781, change 3) — warm-neutral, the only warm ground tone in the room.
        /// MV-783: retoned onto the approved cool base, matching <see cref="BiomePalette.Stormdrain"/>'s
        /// own <c>Dirt</c>.</summary>
        public static readonly Color Silt = new Color(0.360f, 0.330f, 0.280f);

        /// <summary>Standing water (MV-781, change 3) — green-cool-shifted so it reads as a pool rather
        /// than a stain. MV-783: retoned onto the approved cool base.</summary>
        public static readonly Color StandingWater = new Color(0.185f, 0.240f, 0.245f);

        // ---------------------------------------------------------------- dimensions

        /// <summary>A grate's built footprint (MV-781) — matches <c>WorldGrate</c>'s own authored 1x1
        /// tile (the same tile <c>MapValidation.WorldLurkerGrates</c> already treats a Lurker as
        /// occupying anywhere within).</summary>
        public const float GrateSize = 1f;
        public const float GrateFrameOuter = 1.0f;
        public const float GrateFrameInner = 0.86f;
        public const float GrateFrameHeight = 0.10f;
        public const int GrateGrilleBarCount = 7;
        public const float GrateGrilleBarWidth = 0.055f;
        public const float GrateGrilleBarHeight = 0.06f;
        public const float GrateGrilleBarLength = 0.86f;

        /// <summary>MV-784, change 2: narrowed from 0.10/0.06 so the line reads as a cut, not a rib.</summary>
        public const float PanelJointWidth = 0.07f;
        public const float PanelJointThickness = 0.05f;
        public const float PanelJointSunk = 0.02f;

        /// <summary>MV-784, change 1: each cast bay is this much short of the grid pitch, so the joint
        /// gap shows all the way round it.</summary>
        public const float BayInset = 0.12f;
        public const float BayThickness = 0.10f;

        /// <summary>MV-784, change 3: the crack blob's own base radius before the (1.35, 1, 0.035)
        /// squash-and-stretch turns it into a long hairline (a number this ticket doesn't fix, since
        /// only the SHAPE ratio is specified — chosen so the finished sliver reads at bay scale rather
        /// than swallowing one whole or vanishing).</summary>
        public const float CrackBaseRadius = 0.55f;
        public const float CrackLift = 0.003f;

        /// <summary>MV-784, change 4: mean core radius range for a silt/water stain, and silt's halo
        /// as a multiple of its own core (the ticket's own 1.6x). Both stains stack a wider under-layer
        /// with a denser layer on top — that two-step edge is what makes a smear read as soaked INTO the
        /// floor rather than cut out of it.</summary>
        public const float StainCoreRadiusMin = 0.9f;
        public const float StainCoreRadiusMax = 1.6f;
        public const float SiltHaloScale = 1.6f;
        public const int SiltSegments = 11;
        public const float StainSegmentMinT = 0.70f;
        public const float StainSegmentMaxT = 1.30f;
        public const int WaterSegments = 13;
        public const float WaterMeniscusWidth = 0.02f;
        public const float WaterMeniscusLumaScale = 1.4f;
        public const float StainLift = 0.004f;
        public const float StainLayerGap = 0.002f;

        /// <summary>MV-785 "Stormdrain Surface Kit" review, approved by Lee 2026-09-12 — the ticket's own
        /// numbers for the sludge channel's flow dressing. One lip per bank, 5.5 cm wide; ten flow bands
        /// 3.0-6.4 m long and 0.62-1.32 m deep, scrolling at 0.35 m/s; twelve chevron pairs scrolling with
        /// the bands; nine foam clumps 0.11-0.22 m radius, scrolling at 0.12 m/s.</summary>
        public const float SludgeLipWidth = 0.055f;
        private const float SludgeLipY = 0.075f;

        public const int SludgeBandCount = 10;
        public const float SludgeBandLengthMin = 3.0f;
        public const float SludgeBandLengthMax = 6.4f;
        public const float SludgeBandDepthMin = 0.62f;
        public const float SludgeBandDepthMax = 1.32f;
        public const float SludgeFlowSpeed = 0.35f;
        private const float SludgeBandY = 0.09f;

        public const int SludgeChevronPairs = 12;
        private const float SludgeChevronY = 0.09f;

        public const int SludgeFoamCount = 9;
        public const float SludgeFoamRadiusMin = 0.11f;
        public const float SludgeFoamRadiusMax = 0.22f;
        public const int SludgeFoamSegments = 7;
        public const float SludgeFoamSpeed = 0.12f;
        private const float SludgeFoamY = 0.06f;

        public const float KerbHeight = 0.45f;
        public const float KerbDepth = 0.32f;

        /// <summary>Height of the lower pipe in a wall's pipe bank; the upper sits 0.55 m above it.</summary>
        public const float PipeLowY = 1.15f;
        public const float PipeRise = 0.55f;
        public const float PipeRadius = 0.16f;

        /// <summary>Metres between pipe collars. Short enough that a long gallery has rhythm in it.</summary>
        public const float CollarSpacing = 4.0f;

        /// <summary>Metres between wall lamps. A drain wants pools of light with dark between them,
        /// not an evenly lit corridor — 7 m at this camera is roughly one pool per screen-third.</summary>
        public const float LampSpacing = 7.0f;
        public const float LampHeight = 2.35f;
        public const float LampPoolRadius = 2.6f;

        /// <summary>How far the soffit band overhangs inward from the top of a wall.</summary>
        public const float SoffitOverhang = 1.2f;
        public const float SoffitThickness = 0.35f;

        // ---------------------------------------------------------------- primitives

        /// <summary>A collider-free box in a flat tinted material. Every piece below is made of these
        /// and cylinders, for the same reason the rest of the game is: generated geometry needs no
        /// authored asset and survives a fresh clone.</summary>
        public static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size,
                                     Color tone, SurfaceKind kind = SurfaceKind.Stone)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            // MV-778: the mesh now carries the box's true size — a chamfered box, not a flat-sided
            // primitive — so the transform's own scale stays at 1 rather than stretching it again.
            go.transform.localScale = Vector3.one;
            go.GetComponent<MeshFilter>().sharedMesh = CharacterMeshes.Bevelled(size, CharacterMeshes.DefaultBevel(size));

            Strip(go);
            Paint(go, kind, tone);
            return go;
        }

        /// <summary>A collider-free pipe — a lathed barrel with a raised collar ring at each end
        /// (MV-779), not a bare cylinder: a cylinder has no end, and an end is what makes a pipe
        /// read as a pipe rather than as a stick. The mesh's own local Y runs 0..length (bottom to
        /// top), so the GameObject is offset back by half that so <paramref name="localPos"/> keeps
        /// meaning "the pipe's own centre", exactly as every existing call site already assumes.</summary>
        public static GameObject Tube(Transform parent, string name, Vector3 localPos,
                                      float radius, float length, Quaternion rot, Color tone)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localRotation = rot;
            go.transform.localPosition = localPos - rot * Vector3.up * (length * 0.5f);
            go.transform.localScale = Vector3.one;
            go.GetComponent<MeshFilter>().sharedMesh = LathedPipeMesh(radius, length);
            Strip(go);
            Paint(go, SurfaceKind.Metal, tone);
            return go;
        }

        /// <summary>The lathed pipe profile MV-779 replaces a bare cylinder with: a plain barrel of
        /// <paramref name="r"/> radius and <paramref name="l"/> length, with a collar ring standing
        /// proud (1.18x radius) in the last/first 5% of the length at each end.</summary>
        private static Mesh LathedPipeMesh(float r, float l)
        {
            var profile = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(r * 1.18f, 0f),
                new Vector2(r * 1.18f, l * 0.05f),
                new Vector2(r, l * 0.05f),
                new Vector2(r, l * 0.95f),
                new Vector2(r * 1.18f, l * 0.95f),
                new Vector2(r * 1.18f, l),
                new Vector2(0f, l),
            };
            return CharacterMeshes.Lathe(profile, 16);
        }

        /// <summary>An unlit emissive quad — a lamp lens, a status light, a flow chevron. Unlit so it
        /// keeps its colour in the dark, which is the entire point of an emissive in a world whose key
        /// light is deliberately dim.</summary>
        public static GameObject Glow(Transform parent, string name, Vector3 localPos, Vector3 size,
                                      Quaternion rot, Color tone)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.transform.localScale = size;
            Strip(go);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = Unlit(tone, name);
            return go;
        }

        // ---------------------------------------------------------------- wall pieces

        /// <summary>
        /// Dresses one inner wall face: a kerb along its foot, a two-pipe bank with collars, and a
        /// lamp every <see cref="LampSpacing"/> metres with an algae streak under it.
        ///
        /// <paramref name="a"/> and <paramref name="b"/> are the face's endpoints in XZ (world), and
        /// <paramref name="outward"/> points INTO the room. Everything is offset along that normal so
        /// no piece ever intersects the wall it hangs on.
        /// </summary>
        public static void DressWallFace(Transform parent, Vector2 a, Vector2 b, Vector2 outward,
                                         float wallHeight, int seed)
        {
            float length = (b - a).magnitude;
            if (length < 1.2f) return;   // a stub between two doorways; dressing it just makes clutter

            Vector2 dir = (b - a) / length;
            Vector3 mid = new Vector3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);
            Vector3 n = new Vector3(outward.x, 0f, outward.y);
            Vector3 along = new Vector3(dir.x, 0f, dir.y);
            Quaternion lie = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);

            // Kerb — the single highest-value piece here. A wall meeting a floor on a hard line reads
            // as a drawn edge; a wall meeting a floor through a kerb reads as built.
            Box(parent, "Kerb",
                mid + n * (KerbDepth * 0.5f) + Vector3.up * (KerbHeight * 0.5f),
                new Vector3(length, KerbHeight, KerbDepth).Oriented(dir),
                KerbConcrete);

            // Pipe bank. Two runs, the upper one shorter and offset, so the wall has a diagonal in it.
            // Verticals are proportional to wallHeight (MV-765) — the authored constants assumed a
            // ~3.5 m wall (World 1's fence); every world's wall is actually 1.5 m.
            float pipeLowY = 0.62f * wallHeight;
            float pipeOut = PipeRadius + 0.08f;
            Tube(parent, "Pipe Low", mid + n * pipeOut + Vector3.up * pipeLowY,
                 PipeRadius, length, lie, Rust);

            // On a low wall, one run with collars is the right amount of detail — a second run just
            // floats above the wall with nothing behind it.
            if (length > 5f && wallHeight >= 2.2f)
            {
                float upperLen = length * 0.62f;
                Vector3 upperMid = mid + along * (length * 0.14f);
                Tube(parent, "Pipe High", upperMid + n * pipeOut + Vector3.up * (pipeLowY + PipeRise),
                     PipeRadius * 0.75f, upperLen, lie, RustDark);
            }

            // Collars: the joints that stop a pipe reading as an extruded line.
            int collars = Mathf.Max(1, Mathf.FloorToInt(length / CollarSpacing));
            for (int i = 0; i < collars; i++)
            {
                float t = (i + 0.5f) / collars;
                Vector3 at = new Vector3(Mathf.Lerp(a.x, b.x, t), pipeLowY, Mathf.Lerp(a.y, b.y, t));
                Tube(parent, $"Collar{i}", at + n * pipeOut, PipeRadius * 1.35f, 0.22f, lie, RustDark);
            }

            // Lamps. Deterministic phase off the seed so two adjacent walls don't line their lamps up
            // into a grid — a grid is exactly what makes generated dressing read as generated. Height
            // is proportional to wallHeight (MV-765): the proportion replaces the old clamp against
            // wallHeight - 0.4f.
            int lamps = Mathf.FloorToInt(length / LampSpacing);
            float phase = Frac(seed * 0.6180339887f);
            float lampHeight = 0.75f * wallHeight;
            for (int i = 0; i < lamps; i++)
            {
                float t = (i + 0.25f + phase * 0.5f) / Mathf.Max(1, lamps);
                if (t <= 0.02f || t >= 0.98f) continue;
                Vector3 at = new Vector3(Mathf.Lerp(a.x, b.x, t), 0f, Mathf.Lerp(a.y, b.y, t));
                BuildWallLamp(parent, at, n, along, lampHeight);
            }
        }

        /// <summary>A wall lamp: a dark hood, an amber lens facing into the room, an algae streak down
        /// the wall under it, and a flat additive pool on the floor. No Light component — see the
        /// performance contract in this class's summary.</summary>
        public static void BuildWallLamp(Transform parent, Vector3 groundAt, Vector3 inward,
                                         Vector3 along, float height)
        {
            var root = new GameObject("Wall Lamp");
            root.transform.SetParent(parent, false);
            root.transform.position = groundAt;

            Quaternion faceIn = Quaternion.LookRotation(-inward, Vector3.up);

            Box(root.transform, "Hood", inward * 0.16f + Vector3.up * height,
                new Vector3(0.34f, 0.20f, 0.30f), Soffit, SurfaceKind.Metal);

            Glow(root.transform, "Lens", inward * 0.30f + Vector3.up * (height - 0.08f),
                 new Vector3(0.26f, 0.20f, 1f), faceIn, LampAmber);

            // Streak: the wall stains under a lamp because that is where the condensation runs.
            Glow(root.transform, "Algae Streak", inward * 0.03f + Vector3.up * (height * 0.45f),
                 new Vector3(0.5f, height * 0.85f, 1f), faceIn, Algae);

            // Floor pool, laid flat and pushed 1 cm up so it never z-fights the slab.
            Glow(root.transform, "Light Pool", inward * (LampPoolRadius * 0.55f) + Vector3.up * 0.012f,
                 new Vector3(LampPoolRadius * 2f, LampPoolRadius * 2f, 1f),
                 Quaternion.Euler(90f, 0f, 0f), LampPool);
        }

        /// <summary>How thick a coping cap is, and how far it overhangs each side of the wall it caps
        /// — the low-wall (&lt;2.2 m) substitute for <see cref="BuildSoffit"/>'s 1.2 m overhang, which
        /// on a 1.5 m wall hung a slab into the room at waist height (MV-765).</summary>
        public const float CopingThickness = 0.18f;
        public const float CopingOverhang = 0.25f;

        /// <summary>The soffit band — a slab overhanging the top of a wall, inward, so the room edge
        /// falls into shadow and the drain reads as a roofed tunnel with its roof cut away for the
        /// camera. This is the cheapest single change that stops World 2 reading as outdoors.
        ///
        /// Below a 2.2 m wall the 1.2 m overhang reaches too far into playable space (MV-765): a
        /// low wall instead gets a coping cap, flush on top and overhanging only 0.25 m each side —
        /// it caps the wall and darkens its top edge without reaching into the room. A future taller
        /// world still gets the full soffit.</summary>
        public static void BuildSoffit(Transform parent, Vector2 a, Vector2 b, Vector2 outward,
                                       float wallHeight)
        {
            float length = (b - a).magnitude;
            if (length < 1.2f) return;

            Vector2 dir = (b - a) / length;
            Vector3 mid = new Vector3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);
            Vector3 n = new Vector3(outward.x, 0f, outward.y);

            if (wallHeight < 2.2f)
            {
                Box(parent, "Soffit",
                    mid + Vector3.up * (wallHeight + CopingThickness * 0.5f),
                    new Vector3(length, CopingThickness, CopingOverhang * 2f).Oriented(dir),
                    Soffit);
                return;
            }

            Box(parent, "Soffit",
                mid + n * (SoffitOverhang * 0.5f) + Vector3.up * (wallHeight - SoffitThickness * 0.5f),
                new Vector3(length, SoffitThickness, SoffitOverhang).Oriented(dir),
                Soffit);
        }

        // ---------------------------------------------------------------- cover reskins

        /// <summary>Standpipe — replaces a Tree. A vertical rust column with a flange base and a valve
        /// wheel on top, the drain's answer to a tree's tall vertical silhouette. Clamped to 1.6x-2.6x
        /// <paramref name="wallHeight"/> (MV-765) so it reads as a deliberate silhouette rising above
        /// a low wall rather than an accident of an unrelated cover block's authored size.</summary>
        public static GameObject BuildStandpipe(Transform parent, Vector3 at, float height, float wallHeight)
        {
            height = Mathf.Clamp(height, 1.6f * wallHeight, 2.6f * wallHeight);

            var root = new GameObject("Standpipe");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            const float r = 0.22f;   // the column's own radius, per MV-779's flare/bell profiles
            Tube(root.transform, "Column", Vector3.up * (height * 0.5f), r, height,
                 Quaternion.identity, Rust);
            Tube(root.transform, "Flange", Vector3.up * 0.12f, 0.42f, 0.24f,
                 Quaternion.identity, RustDark);
            Tube(root.transform, "Collar", Vector3.up * (height * 0.55f), 0.30f, 0.18f,
                 Quaternion.identity, RustDark);

            // MV-779: a flared base at the floor and a bell top at the pipe's own top — the column
            // reads as a fitted standpipe rather than a straight length of stock.
            AddMeshPart(root.transform, "Flared Base", CharacterMeshes.Lathe(new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(r * 1.9f, 0f),
                    new Vector2(r * 1.9f, r * 0.5f),
                    new Vector2(r * 1.15f, r * 0.9f),
                    new Vector2(r, r * 1.1f),
                }, 18),
                Vector3.zero, SurfaceKind.Metal, RustDark);

            AddMeshPart(root.transform, "Bell Top", CharacterMeshes.Lathe(new[]
                {
                    new Vector2(r, 0f),
                    new Vector2(r * 1.1f, r * 0.3f),
                    new Vector2(r * 1.7f, r * 0.85f),
                    new Vector2(r * 1.62f, r * 1.0f),
                    new Vector2(0f, r * 1.0f),
                }, 18),
                Vector3.up * height, SurfaceKind.Metal, RustDark);

            // Valve wheel: four spokes and a rim. A torus would be one draw call cheaper if Unity had
            // a torus primitive; it does not, and four thin boxes read as a wheel from this camera.
            var wheel = new GameObject("Valve Wheel");
            wheel.transform.SetParent(root.transform, false);
            wheel.transform.localPosition = Vector3.up * (height + 0.06f);
            for (int i = 0; i < 4; i++)
            {
                Box(wheel.transform, $"Spoke{i}", Vector3.zero, new Vector3(0.72f, 0.06f, 0.09f),
                    Hazard, SurfaceKind.Metal).transform.localRotation = Quaternion.Euler(0f, i * 45f, 0f);
            }
            return root;
        }

        /// <summary>Debris rake — replaces a Hedge. The hedge is the ONE see-through cover class
        /// (<c>reference_cover_sight_and_wake</c>: only hedges let sight and shots pass), so its
        /// replacement must be visually see-through too or the player will read it as solid and take
        /// the wrong fight. Five thin bars with air between them, not a panel.</summary>
        public static GameObject BuildDebrisRake(Transform parent, Vector3 at, Vector3 size)
        {
            var root = new GameObject("Debris Rake");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            bool alongX = size.x >= size.z;
            float span = alongX ? size.x : size.z;
            float h = Mathf.Max(0.9f, size.y);

            for (int i = 0; i < 5; i++)
            {
                float t = (i + 0.5f) / 5f - 0.5f;
                Vector3 p = alongX ? new Vector3(t * span, h * 0.5f, 0f) : new Vector3(0f, h * 0.5f, t * span);
                Vector3 s = alongX ? new Vector3(0.09f, h, 0.14f) : new Vector3(0.14f, h, 0.09f);
                Box(root.transform, $"Bar{i}", p, s, RustDark, SurfaceKind.Metal);
            }

            // Caught rubbish along the bottom — what a rake is FOR, and the thing that tells the
            // player the sludge in this room flows that way.
            Vector3 sill = alongX ? new Vector3(span, 0.22f, 0.30f) : new Vector3(0.30f, 0.22f, span);
            Box(root.transform, "Caught Silt", Vector3.up * 0.11f, sill, Algae);
            return root;
        }

        /// <summary>Silt bin — replaces a Planter. A concrete kerb-box with a sludge-crusted top and
        /// hazard corner posts: waist-high, solid, obviously a thing to hide behind.</summary>
        public static GameObject BuildSiltBin(Transform parent, Vector3 at, Vector3 size)
        {
            var root = new GameObject("Silt Bin");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            float h = Mathf.Max(0.8f, size.y);
            Box(root.transform, "Shell", Vector3.up * (h * 0.5f), new Vector3(size.x, h, size.z), KerbConcrete);
            Box(root.transform, "Rim", Vector3.up * (h + 0.05f),
                new Vector3(size.x * 1.06f, 0.12f, size.z * 1.06f), Rust, SurfaceKind.Metal);
            Glow(root.transform, "Crust", Vector3.up * (h + 0.13f),
                 new Vector3(size.x * 0.86f, size.z * 0.86f, 1f), Quaternion.Euler(90f, 0f, 0f), Sludge);

            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 3) ? -1f : 1f;
                float sz = (i < 2) ? -1f : 1f;
                Box(root.transform, $"Post{i}",
                    new Vector3(sx * size.x * 0.5f, h * 0.62f, sz * size.z * 0.5f),
                    new Vector3(0.12f, h * 1.24f, 0.12f), Hazard, SurfaceKind.Metal);
            }
            return root;
        }

        /// <summary>Pump housing — replaces a Shed or Machinery cover piece. A ribbed machine box with
        /// a hazard band and a cyan status light: the drain's only cold colour, so it reads as the one
        /// thing in the room that is still switched on.</summary>
        public static GameObject BuildPumpHousing(Transform parent, Vector3 at, Vector3 size)
        {
            var root = new GameObject("Pump Housing");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            float h = Mathf.Max(1.4f, size.y);
            // MV-779: the footprint was rectangular (size.x x size.z) for the old stacked-box Shell,
            // but Body/Cap/Flange below are all radially symmetric — rBase is the housing's own
            // single base radius, sized off the footprint's narrower axis.
            float rBase = Mathf.Min(size.x, size.z) * 0.5f;

            // Body — a tapered six-sided housing. This is the silhouette that says "machine"; it and
            // Cap below replace the old Shell + three raised roof Ribs entirely.
            AddMeshPart(root.transform, "Body", CharacterMeshes.Prism(6, rBase, rBase * 0.82f, h, 0.10f, twistDegrees: 0f),
                Vector3.up * (h * 0.5f), SurfaceKind.Metal, new Color(0.30f, 0.32f, 0.31f));

            // Cap — a lathed dome over the body.
            float capH = h * 0.30f;
            AddMeshPart(root.transform, "Cap", CharacterMeshes.Lathe(new[]
                {
                    new Vector2(rBase * 0.86f, 0f),
                    new Vector2(rBase * 0.86f, capH * 0.15f),
                    new Vector2(rBase * 0.7f, capH * 0.55f),
                    new Vector2(rBase * 0.34f, capH * 0.85f),
                    new Vector2(0f, capH),
                }, 20),
                Vector3.up * h, SurfaceKind.Metal, RustDark);

            // Base flange — a lathed ring the body sits on.
            float fh = h * 0.06f;
            AddMeshPart(root.transform, "Base Flange", CharacterMeshes.Lathe(new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(rBase * 1.22f, 0f),
                    new Vector2(rBase * 1.22f, fh),
                    new Vector2(rBase * 1.05f, fh),
                }, 20),
                Vector3.zero, SurfaceKind.Metal, RustDark);

            // Four bolts around the flange — fabrication, the thing that tells a machine apart from a box.
            float boltScale = h * 0.035f;
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                Vector3 boltAt = new Vector3(Mathf.Cos(a) * rBase * 1.10f, fh * 0.5f, Mathf.Sin(a) * rBase * 1.10f);
                var bolt = AddMeshPart(root.transform, $"Bolt{i}", CharacterMeshes.Sphere(8),
                    boltAt, SurfaceKind.Metal, Rust);
                bolt.transform.localScale = Vector3.one * boltScale;
            }

            Box(root.transform, "Hazard Band", Vector3.up * (h * 0.28f),
                new Vector3(size.x * 1.03f, 0.18f, size.z * 1.03f), Hazard, SurfaceKind.Metal);

            Tube(root.transform, "Outlet", new Vector3(size.x * 0.5f, h * 0.62f, 0f), 0.14f, 0.9f,
                 Quaternion.Euler(0f, 0f, 90f), Rust);

            Glow(root.transform, "Status", new Vector3(0f, h * 0.72f, -size.z * 0.5f - 0.02f),
                 new Vector3(0.18f, 0.18f, 1f), Quaternion.identity, Status);
            return root;
        }

        /// <summary>Silt sacks — replaces a bare crate (<c>CoverDressing.None</c>). Three staggered,
        /// slightly rotated sacks: the cheapest way to make a cover block stop being a cube.</summary>
        public static GameObject BuildSiltSacks(Transform parent, Vector3 at, Vector3 size, int seed)
        {
            var root = new GameObject("Silt Sacks");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            float h = Mathf.Max(0.7f, size.y);
            for (int i = 0; i < 3; i++)
            {
                float jx = (Frac(seed * 0.7548f + i * 0.31f) - 0.5f) * size.x * 0.30f;
                float jz = (Frac(seed * 0.5698f + i * 0.53f) - 0.5f) * size.z * 0.30f;
                float layerH = h / 3f;
                var sack = Box(root.transform, $"Sack{i}",
                    new Vector3(jx, layerH * (i + 0.5f), jz),
                    new Vector3(size.x * 0.92f, layerH * 0.94f, size.z * 0.82f),
                    i == 1 ? Algae : new Color(0.30f, 0.27f, 0.21f), SurfaceKind.Dirt);
                sack.transform.localRotation = Quaternion.Euler(0f, (Frac(seed + i * 0.17f) - 0.5f) * 18f, 0f);
            }
            return root;
        }

        // ---------------------------------------------------------------- floor pieces (MV-781)

        /// <summary>Builds one authored grate: a recessed rust frame, a 7-bar soffit-dark grille and
        /// an unlit void beneath it, flush with the floor at <paramref name="floorTopY"/>.
        /// <paramref name="cornerXZ"/> is the tile's own MIN corner (WorldGrate's authored point,
        /// matching MapValidation's [x, x+1] x [z, z+1] convention) — the geometry itself is built
        /// centred on the tile, at cornerXZ + (0.5, 0.5), while the caller (MapRuntime) keeps the
        /// entity's own recorded position at the authored corner.</summary>
        public static GameObject BuildGrate(Transform parent, string name, Vector2 cornerXZ, float floorTopY = 0f)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(cornerXZ.x + GrateSize * 0.5f, floorTopY, cornerXZ.y + GrateSize * 0.5f);
            // MV-781: built directly under the map root, not under the "Stormdrain Dressing" host that
            // already carries one — this needs its own, or WorldMaterials' generic shape sweep would
            // repaint every rust/soffit tone here back to a flat biome colour.
            root.AddComponent<KeepsOwnMaterial>();

            float thickness = (GrateFrameOuter - GrateFrameInner) * 0.5f;
            float ringOffset = GrateFrameInner * 0.5f + thickness * 0.5f;
            float frameY = -GrateFrameHeight * 0.5f;

            Box(root.transform, "Frame N", new Vector3(0f, frameY, ringOffset),
                new Vector3(GrateFrameOuter, GrateFrameHeight, thickness), Rust, SurfaceKind.Metal);
            Box(root.transform, "Frame S", new Vector3(0f, frameY, -ringOffset),
                new Vector3(GrateFrameOuter, GrateFrameHeight, thickness), Rust, SurfaceKind.Metal);
            Box(root.transform, "Frame E", new Vector3(ringOffset, frameY, 0f),
                new Vector3(thickness, GrateFrameHeight, GrateFrameInner), Rust, SurfaceKind.Metal);
            Box(root.transform, "Frame W", new Vector3(-ringOffset, frameY, 0f),
                new Vector3(thickness, GrateFrameHeight, GrateFrameInner), Rust, SurfaceKind.Metal);

            var bars = new Transform[GrateGrilleBarCount];
            for (int i = 0; i < GrateGrilleBarCount; i++)
            {
                float t = (i + 0.5f) / GrateGrilleBarCount - 0.5f;
                GameObject bar = Box(root.transform, $"Grille Bar{i}",
                    new Vector3(t * GrateFrameInner, -GrateGrilleBarHeight * 0.5f, 0f),
                    new Vector3(GrateGrilleBarWidth, GrateGrilleBarHeight, GrateGrilleBarLength), Soffit, SurfaceKind.Metal);
                bars[i] = bar.transform;
            }

            Glow(root.transform, "Void", new Vector3(0f, -GrateFrameHeight * 0.85f, 0f),
                 new Vector3(GrateFrameInner, GrateFrameInner, 1f), Quaternion.Euler(90f, 0f, 0f), Soffit);

            // MV-773: the bars' own short vertical shudder as a garrisoned Lurker rises through them —
            // triggered by RobotEnemy.OnLurkerPhaseChanged via GrateShudder.TriggerNear, keyed on this
            // same authored MIN corner.
            var shudder = root.AddComponent<GrateShudder>();
            shudder.Configure(bars);
            GrateShudder.Register(cornerXZ, shudder);

            return root;
        }

        /// <summary>A recessed panel-joint strip (MV-781, change 2; narrowed MV-784, change 2) — sunk
        /// <see cref="PanelJointSunk"/> below the floor's own top so it reads as a cut line, not a
        /// raised rib.</summary>
        public static GameObject BuildPanelJoint(Transform parent, Rect worldRect, float floorTopY = 0f)
        {
            Vector3 center = new Vector3(worldRect.center.x,
                floorTopY - PanelJointSunk - PanelJointThickness * 0.5f, worldRect.center.y);
            GameObject go = Box(parent, "Panel Joint", center,
                new Vector3(worldRect.width, PanelJointThickness, worldRect.height), PanelJoint);
            ZeroOutline(go);
            return go;
        }

        /// <summary>A cast bay slab (MV-784, change 1) — one <see cref="CharacterMeshes.Bevelled"/> box
        /// per grid cell, inset <see cref="BayInset"/> short of the grid pitch so the joint gap shows all
        /// the way round it, in whichever of the three bay tones <paramref name="tone"/> resolves the
        /// bay's own hash to.</summary>
        public static GameObject BuildBay(Transform parent, Rect worldRect, Color tone, float floorTopY = 0f)
        {
            Vector3 center = new Vector3(worldRect.center.x, floorTopY - BayThickness * 0.5f, worldRect.center.y);
            GameObject go = Box(parent, "Bay", center,
                new Vector3(worldRect.width, BayThickness, worldRect.height), tone);
            ZeroOutline(go);
            return go;
        }

        /// <summary>A hairline crack in one bay (MV-784, change 3) — a <see cref="BuildBlobMesh"/> blob
        /// squashed and stretched into a long sliver, rotated by the same hash that decided the bay gets
        /// one at all, so a level always cracks the same bays the same way.</summary>
        public static GameObject BuildCrack(Transform parent, Vector3 worldCenter, float hash, float floorTopY = 0f)
        {
            var go = new GameObject("Crack");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(worldCenter.x, floorTopY + CrackLift, worldCenter.z);
            go.transform.localRotation = Quaternion.Euler(0f, hash * 360f, 0f);
            go.transform.localScale = new Vector3(1.35f, 1f, 0.035f);

            Mesh mesh = BuildBlobMesh(CrackBaseRadius, 7, StainSegmentMinT, StainSegmentMaxT, hash * 97.13f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            Paint(go, SurfaceKind.Dirt, Crack);
            ZeroOutline(go);
            return go;
        }

        /// <summary>A silt drift (MV-784, change 4) — two stacked <see cref="BuildBlobMesh"/> blobs: a
        /// wide soft halo halfway in tone between the floor and <see cref="Silt"/>, and a denser core at
        /// full <see cref="Silt"/> inside it. The two-step edge is what makes this read as soaked into
        /// the floor rather than cut out of it.</summary>
        public static GameObject BuildSiltStain(Transform parent, Vector3 worldCenter, float coreRadius,
                                                float seed, float floorTopY = 0f)
        {
            var root = new GameObject("Silt");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(worldCenter.x, floorTopY + StainLift, worldCenter.z);

            Color haloTone = Color.Lerp(GroundBase, Silt, 0.5f);
            BuildStainLayer(root.transform, "Halo", coreRadius * SiltHaloScale, SiltSegments, seed, 0f,
                SurfaceKind.Dirt, haloTone);
            BuildStainLayer(root.transform, "Core", coreRadius, SiltSegments, seed + 11f, StainLayerGap,
                SurfaceKind.Dirt, Silt);

            return root;
        }

        /// <summary>A standing-water pool (MV-784, change 4) — a dark, cool <see cref="StandingWater"/>
        /// core ringed by a <see cref="WaterMeniscusWidth"/> meniscus at roughly
        /// <see cref="WaterMeniscusLumaScale"/>x the floor's luminance, built the same "wider layer under
        /// a denser one" way <see cref="BuildSiltStain"/> is.</summary>
        public static GameObject BuildWaterStain(Transform parent, Vector3 worldCenter, float coreRadius,
                                                 float seed, float floorTopY = 0f)
        {
            var root = new GameObject("Standing Water");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(worldCenter.x, floorTopY + StainLift, worldCenter.z);

            Color meniscusTone = GroundBase * WaterMeniscusLumaScale;
            BuildStainLayer(root.transform, "Meniscus", coreRadius + WaterMeniscusWidth, WaterSegments, seed, 0f,
                SurfaceKind.Prop, meniscusTone);
            GameObject core = BuildStainLayer(root.transform, "Water", coreRadius, WaterSegments, seed + 13f,
                StainLayerGap, SurfaceKind.Prop, StandingWater);

            Material mat = core.GetComponent<Renderer>()?.sharedMaterial;
            if (mat != null && mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", Mathf.Max(mat.GetFloat("_Smoothness"), 0.55f));

            return root;
        }

        /// <summary>One flat blob layer, shared by <see cref="BuildSiltStain"/> and
        /// <see cref="BuildWaterStain"/> — both stack a wider under-layer with a denser layer this same
        /// height above it.</summary>
        private static GameObject BuildStainLayer(Transform parent, string name, float radius, int segments,
                                                   float seed, float liftY, SurfaceKind kind, Color tone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, liftY, 0f);
            go.AddComponent<MeshFilter>().sharedMesh =
                BuildBlobMesh(radius, segments, StainSegmentMinT, StainSegmentMaxT, seed);
            go.AddComponent<MeshRenderer>();
            Paint(go, kind, tone);
            ZeroOutline(go);
            return go;
        }

        /// <summary>The flat, per-segment-jittered "Blob" every irregular MV-784 shape (crack, silt
        /// halo/core, water core/meniscus) is built from: a centre vertex plus <paramref name="segments"/>
        /// rim vertices, each at <c>radius * (radiusMinT + Frac(...) * (radiusMaxT - radiusMinT))</c> —
        /// deterministic from <paramref name="seed"/>, never <see cref="Random"/>. Same
        /// "pick the winding so the triangle's own normal faces up" idiom as
        /// <see cref="MaxWorlds.Enemies.SludgePuddle.BuildFanMesh"/>, so this always renders face-up
        /// regardless of which way the angle sweep runs.</summary>
        private static Mesh BuildBlobMesh(float radius, int segments, float radiusMinT, float radiusMaxT, float seed)
        {
            segments = Mathf.Max(3, segments);
            var vertices = new Vector3[segments + 1];
            vertices[0] = Vector3.zero;

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float t = radiusMinT + Frac(seed * 1.317f + i * 0.4177f) * (radiusMaxT - radiusMinT);
                float r = radius * t;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
            }

            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                int b = i + 1;
                int c = (i + 1) % segments + 1;
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[0], vertices[c] - vertices[0]);
                int t = i * 3;
                if (normal.y >= 0f) { triangles[t] = 0; triangles[t + 1] = b; triangles[t + 2] = c; }
                else { triangles[t] = 0; triangles[t + 1] = c; triangles[t + 2] = b; }
            }

            var mesh = new Mesh { name = "StormdrainBlob", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: false);
            return mesh;
        }

        /// <summary>MV-784, change 5: nothing at floor level carries the world's inverted-hull outline —
        /// on a floor stain it draws an ink border and turns the floor into sticker art. Reaches back
        /// into the resolved material the same way the water smoothness override already does, rather
        /// than adding a per-kind branch to <see cref="MaterialLibrary.Build"/> that every other surface
        /// would have to keep not tripping.</summary>
        private static void ZeroOutline(GameObject go)
        {
            Material mat = go.GetComponent<Renderer>()?.sharedMaterial;
            if (mat != null && mat.HasProperty("_OutlineOn")) mat.SetFloat("_OutlineOn", 0f);
        }

        // ---------------------------------------------------------------- sludge dressing

        /// <summary>
        /// Dresses one sludge tile (MV-785, "Stormdrain Surface Kit", approved by Lee 2026-09-12): one
        /// bright lip per bank, ten flow bands and twelve chevron pairs scrolling downstream, nine foam
        /// clumps collecting along the banks, and reed clusters along its long sides. Returns the
        /// <see cref="SludgeFlowRig"/> driving the scroll so a caller (or a test) can <c>Tick</c> it
        /// directly with no scene running.
        ///
        /// The sludge is the world's one saturated colour. In the key art it is the thing the eye lands
        /// on first, and the thing that tells the player which way is downstream — that is what the
        /// chevrons and bands are for, and why this is dressing and not decoration.
        /// </summary>
        public static SludgeFlowRig DressSludgeTile(Transform parent, Vector3 center, float width, float depth,
                                                     Vector3 flowDirection, int seed)
        {
            var root = new GameObject("Sludge Dressing");
            root.transform.SetParent(parent, false);
            root.transform.position = center;

            Vector3 flow = flowDirection.sqrMagnitude < 0.001f ? Vector3.forward : flowDirection.normalized;
            flow.y = 0f;
            flow.Normalize();
            Vector3 across = new Vector3(flow.z, 0f, -flow.x);
            Quaternion yawRot = Quaternion.LookRotation(flow, Vector3.up);
            bool alongZ = Mathf.Abs(Vector3.Dot(flow, Vector3.forward)) > 0.5f;
            float run = alongZ ? depth : width;
            float span = alongZ ? width : depth;

            // One bright lip per bank (MV-785 narrows this from all four edges to the two parallel to
            // the flow) — the brightest thing in the tile, and the only full-strength green allowed.
            if (alongZ)
            {
                Box(root.transform, "Lip Bank A", new Vector3(width * 0.5f, SludgeLipY, 0f),
                    new Vector3(SludgeLipWidth, 0.06f, depth), SludgeBright, SurfaceKind.Foliage);
                Box(root.transform, "Lip Bank B", new Vector3(-width * 0.5f, SludgeLipY, 0f),
                    new Vector3(SludgeLipWidth, 0.06f, depth), SludgeBright, SurfaceKind.Foliage);
            }
            else
            {
                Box(root.transform, "Lip Bank A", new Vector3(0f, SludgeLipY, depth * 0.5f),
                    new Vector3(width, 0.06f, SludgeLipWidth), SludgeBright, SurfaceKind.Foliage);
                Box(root.transform, "Lip Bank B", new Vector3(0f, SludgeLipY, -depth * 0.5f),
                    new Vector3(width, 0.06f, SludgeLipWidth), SludgeBright, SurfaceKind.Foliage);
            }

            // Phases are seeded from the rect's own world position (MV-785's own requirement), not from
            // the caller's incrementing tile counter, so two adjacent channels are never in step even
            // when built back-to-back with consecutive seeds.
            float globalPhase = Frac(center.x * 0.4127f + center.z * 0.6180339887f) * run;

            // Ten flow bands, alternating tones, jittered across the channel's width, scrolling with the
            // chevrons along the flow axis.
            var bandsGroup = new GameObject("Bands").transform;
            bandsGroup.SetParent(root.transform, false);
            var bandT = new Transform[SludgeBandCount];
            var bandExtra = new Vector3[SludgeBandCount];
            var bandPhase = new float[SludgeBandCount];
            for (int i = 0; i < SludgeBandCount; i++)
            {
                float hLen = Frac(seed * 1.91f + i * 3.17f);
                float hDepth = Frac(seed * 2.53f + i * 4.71f);
                float hCross = Frac(seed * 0.77f + i * 1.33f);
                float length = Mathf.Lerp(SludgeBandLengthMin, SludgeBandLengthMax, hLen);
                float crossDepth = Mathf.Lerp(SludgeBandDepthMin, SludgeBandDepthMax, hDepth);
                float crossOffset = (hCross - 0.5f) * Mathf.Max(0f, span - crossDepth);
                float phase = Mathf.Repeat((i + 0.5f) / SludgeBandCount * run + globalPhase, run);
                Color tone = (i & 1) == 0 ? Sludge : SludgeBand;

                GameObject band = Box(bandsGroup, $"Band{i}", Vector3.zero,
                    new Vector3(crossDepth, 0.05f, length), tone, SurfaceKind.Foliage);
                band.transform.localRotation = yawRot;
                Vector3 extra = across * crossOffset + Vector3.up * SludgeBandY;
                band.transform.localPosition = extra;
                bandT[i] = band.transform;
                bandExtra[i] = extra;
                bandPhase[i] = phase;
            }

            // Twelve chevron pairs (24 strips) scrolling with the bands, in their own mid tone.
            var chevronsGroup = new GameObject("Chevrons").transform;
            chevronsGroup.SetParent(root.transform, false);
            var chevronT = new Transform[SludgeChevronPairs * 2];
            var chevronExtra = new Vector3[SludgeChevronPairs * 2];
            var chevronPhase = new float[SludgeChevronPairs * 2];
            for (int i = 0; i < SludgeChevronPairs; i++)
            {
                float phase = Mathf.Repeat((i + 0.5f) / SludgeChevronPairs * run + globalPhase, run);
                Vector3 pivotUp = Vector3.up * SludgeChevronY;

                GameObject l = Box(chevronsGroup, $"Chevron{i}L", Vector3.zero,
                    new Vector3(0.95f, 0.05f, 0.16f), SludgeChevronMid, SurfaceKind.Foliage);
                l.transform.localRotation = yawRot * Quaternion.Euler(0f, 45f, 0f);
                Vector3 extraL = yawRot * new Vector3(-0.30f, 0f, -0.30f) + pivotUp;
                l.transform.localPosition = extraL;
                int idxL = i * 2;
                chevronT[idxL] = l.transform;
                chevronExtra[idxL] = extraL;
                chevronPhase[idxL] = phase;

                GameObject r = Box(chevronsGroup, $"Chevron{i}R", Vector3.zero,
                    new Vector3(0.95f, 0.05f, 0.16f), SludgeChevronMid, SurfaceKind.Foliage);
                r.transform.localRotation = yawRot * Quaternion.Euler(0f, -45f, 0f);
                Vector3 extraR = yawRot * new Vector3(0.30f, 0f, -0.30f) + pivotUp;
                r.transform.localPosition = extraR;
                int idxR = idxL + 1;
                chevronT[idxR] = r.transform;
                chevronExtra[idxR] = extraR;
                chevronPhase[idxR] = phase;
            }

            // Nine foam clumps collecting along both banks, scrolling slower than the bands — the
            // differential is what sells the sludge as fluid rather than a conveyor. No explicit tone is
            // given in the ticket for foam; it reuses SludgeBright, the brightest established tone,
            // consistent with foam reading as the lightest froth in the channel.
            var foamGroup = new GameObject("Foam").transform;
            foamGroup.SetParent(root.transform, false);
            var foamT = new Transform[SludgeFoamCount];
            var foamExtra = new Vector3[SludgeFoamCount];
            var foamPhase = new float[SludgeFoamCount];
            for (int i = 0; i < SludgeFoamCount; i++)
            {
                float hRadius = Frac(seed * 3.71f + i * 5.13f);
                float radius = Mathf.Lerp(SludgeFoamRadiusMin, SludgeFoamRadiusMax, hRadius);
                float hInset = Frac(seed * 4.29f + i * 2.77f);
                float edgeOffset = Mathf.Max(0f, span * 0.5f - radius * 1.3f - hInset * 0.15f);
                float crossOffset = (i & 1) == 0 ? edgeOffset : -edgeOffset;
                float hPhase = Frac(seed * 6.10f + i * 1.91f + globalPhase);
                float phase = hPhase * run;

                GameObject clump = BuildFoamClump(foamGroup, $"Foam{i}", Vector3.zero, radius,
                    seed * 0.001f + i * 0.777f, SludgeBright);
                Vector3 extra = across * crossOffset + Vector3.up * SludgeFoamY;
                clump.transform.localPosition = extra;
                foamT[i] = clump.transform;
                foamExtra[i] = extra;
                foamPhase[i] = phase;
            }

            // Bands and chevrons scroll together at SludgeFlowSpeed; foam scrolls slower on its own timer.
            var fastT = new Transform[bandT.Length + chevronT.Length];
            var fastExtra = new Vector3[fastT.Length];
            var fastPhase = new float[fastT.Length];
            System.Array.Copy(bandT, 0, fastT, 0, bandT.Length);
            System.Array.Copy(chevronT, 0, fastT, bandT.Length, chevronT.Length);
            System.Array.Copy(bandExtra, 0, fastExtra, 0, bandExtra.Length);
            System.Array.Copy(chevronExtra, 0, fastExtra, bandExtra.Length, chevronExtra.Length);
            System.Array.Copy(bandPhase, 0, fastPhase, 0, bandPhase.Length);
            System.Array.Copy(chevronPhase, 0, fastPhase, bandPhase.Length, chevronPhase.Length);

            var rig = root.AddComponent<SludgeFlowRig>();
            rig.Configure(flow, run, fastT, fastExtra, fastPhase,
                          flow, run, foamT, foamExtra, foamPhase);

            // Reeds at the edges — the key art's one piece of organic silhouette, and the only thing
            // in the drain that breaks a horizon line.
            int clusters = Mathf.Clamp(Mathf.FloorToInt((width + depth) / 5f), 2, 8);
            for (int i = 0; i < clusters; i++)
            {
                bool onLongSide = (i & 1) == 0;
                float t = Frac(seed * 0.3803f + i * 0.618f) - 0.5f;
                Vector3 at = onLongSide
                    ? new Vector3(t * width, 0f, (i % 4 < 2 ? 1f : -1f) * depth * 0.5f)
                    : new Vector3((i % 4 < 2 ? 1f : -1f) * width * 0.5f, 0f, t * depth);
                BuildReedCluster(root.transform, at, seed + i);
            }

            return rig;
        }

        /// <summary>A foam clump (MV-785) — the same flat, per-segment-jittered "Blob" every other
        /// irregular MV-784 shape is built from, small and round rather than squashed into a sliver or
        /// stain. Collects along the sludge tile's banks and scrolls slower than the flow bands, which is
        /// what sells it as fluid rather than a conveyor.</summary>
        public static GameObject BuildFoamClump(Transform parent, string name, Vector3 localPos, float radius,
                                                float seed, Color tone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh =
                BuildBlobMesh(radius, SludgeFoamSegments, StainSegmentMinT, StainSegmentMaxT, seed);
            go.AddComponent<MeshRenderer>();
            Paint(go, SurfaceKind.Foliage, tone);
            ZeroOutline(go);
            return go;
        }

        /// <summary>Three or four thin cones standing out of the sludge, splayed. Cheap, and the only
        /// vertical the sludge lane has.</summary>
        public static void BuildReedCluster(Transform parent, Vector3 localAt, int seed)
        {
            var root = new GameObject("Reeds");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localAt;

            int n = 3 + (seed & 1);
            for (int i = 0; i < n; i++)
            {
                float h = 0.85f + Frac(seed * 0.221f + i * 0.377f) * 0.65f;
                var reed = Tube(root.transform, $"Reed{i}",
                    new Vector3((Frac(seed * 0.91f + i) - 0.5f) * 0.5f, h * 0.5f,
                                (Frac(seed * 0.44f + i * 0.7f) - 0.5f) * 0.5f),
                    0.045f, h, Quaternion.identity, SludgeBright);
                reed.transform.localRotation =
                    Quaternion.Euler((Frac(seed + i * 0.13f) - 0.5f) * 26f, i * 47f,
                                     (Frac(seed * 1.7f + i) - 0.5f) * 26f);
            }
        }

        // ---------------------------------------------------------------- internals

        /// <summary>Dressing is scenery — never a thing Max, a robot or the boss can collide with.
        /// Same idiom as <see cref="MaxWorlds.Arena.BackyardDressing"/>'s own <c>Strip</c> and
        /// <see cref="ReefKit"/>'s.</summary>
        public static void Strip(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
        }

        private static void Paint(GameObject go, SurfaceKind kind, Color tone)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = MaterialLibrary.Tinted(kind, tone);
        }

        /// <summary>A collider-free part built directly from a <see cref="CharacterMeshes"/> mesh
        /// (MV-779) — the turned-form counterpart to <see cref="Box"/>, for pieces that are lathed or
        /// prism-shaped rather than a chamfered cube. <paramref name="localPos"/> is the part's own
        /// pivot in <paramref name="mesh"/>'s own local space; the caller sets <c>localScale</c>
        /// afterwards if it needs one (e.g. a <see cref="CharacterMeshes.Sphere"/> bolt).</summary>
        public static GameObject AddMeshPart(Transform parent, string name, Mesh mesh, Vector3 localPos,
                                             SurfaceKind kind, Color tone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            Paint(go, kind, tone);
            return go;
        }

        /// <summary>An unlit flat-colour material, cached per colour. Unlit because these pieces are
        /// the light in a world whose key is deliberately dim — a lit lamp lens in a dark room is a
        /// dark lamp lens.</summary>
        private static readonly System.Collections.Generic.Dictionary<Color, Material> _unlit = new();

        public static Material Unlit(Color tone, string name)
        {
            if (_unlit.TryGetValue(tone, out Material cached) && cached != null) return cached;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null || !shader.isSupported) shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var mat = new Material(shader) { name = $"Stormdrain_{name}", hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tone);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tone);
            _unlit[tone] = mat;
            return mat;
        }

        /// <summary>Deterministic 0..1 from a float. Not Random: an EditMode test that counts or
        /// measures dressing has to get the same answer twice.</summary>
        private static float Frac(float v) => Mathf.Abs(v - Mathf.Floor(v));

        /// <summary>Clears the cached unlit materials. Mirrors <see cref="MaterialLibrary.Clear"/> so a
        /// test run that switches worlds does not inherit the previous one's instances.</summary>
        public static void Clear()
        {
            foreach (var m in _unlit.Values)
                if (m != null) { if (Application.isPlaying) Object.Destroy(m); else Object.DestroyImmediate(m); }
            _unlit.Clear();
        }
    }

    internal static class SizeOrientation
    {
        /// <summary>Re-orders a (length, height, depth) size so its length runs along
        /// <paramref name="dir"/> in world XZ. Wall faces are axis-aligned by construction
        /// (<c>MapGeometry.Walls</c> only ever emits along-X and along-Z runs), so this is a swap,
        /// not a rotation — which keeps every dressing piece axis-aligned and batchable.</summary>
        public static Vector3 Oriented(this Vector3 size, Vector2 dir)
            => Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
                ? size
                : new Vector3(size.z, size.y, size.x);
    }
}
