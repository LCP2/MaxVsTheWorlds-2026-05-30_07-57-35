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
        /// MV-777: pushed to the rust/rail accent tier (~95-115 resolved luma), clearly above the
        /// floor/wall/kerb value tiers below it.</summary>
        public static readonly Color Rust = new Color(0.685f, 0.355f, 0.17f);

        /// <summary>Darker rust for collars and flanges, so a pipe run has joints in it.</summary>
        public static readonly Color RustDark = new Color(0.38f, 0.20f, 0.10f);

        /// <summary>Wet concrete, one step darker than the wall so a kerb reads against it. MV-777:
        /// previously rendered within half a luma of <see cref="BiomePalette.Stormdrain"/>'s own floor
        /// tone — a kerb that reads as the floor it sits on defeats the one job its doc comment
        /// describes. Now sits deliberately between the floor and wall tiers.</summary>
        public static readonly Color KerbConcrete = new Color(0.16f, 0.175f, 0.147f);

        /// <summary>The soffit's underside — near black. This is the shadow that says "roof". MV-777:
        /// darker than the floor tier, not just darker than the wall.</summary>
        public static readonly Color Soffit = new Color(0.044f, 0.048f, 0.044f);

        /// <summary>Lamp glass. Warm amber, deliberately over 1.0 in no channel — the emissive
        /// material is unlit, so its albedo IS its output and clipping it just loses the colour.</summary>
        public static readonly Color LampAmber = new Color(0.98f, 0.72f, 0.34f);

        /// <summary>The floor pool under a lamp. Same hue, far lower value: it is added onto whatever
        /// the floor already is, so a bright pool blows out to white paper.</summary>
        public static readonly Color LampPool = new Color(0.42f, 0.28f, 0.11f);

        /// <summary>Algae creep — the streak below a lamp and the crust on a silt bin.</summary>
        public static readonly Color Algae = new Color(0.26f, 0.36f, 0.14f);

        /// <summary>Acid sludge, matching <c>MapRuntime.SludgeColor</c> exactly. Duplicated as a
        /// constant rather than referenced because Rendering must not depend on Arena.</summary>
        public static readonly Color Sludge = new Color(0.55f, 0.85f, 0.15f);

        /// <summary>The brighter lip around a sludge tile's edge, and the flow chevrons on it.</summary>
        public static readonly Color SludgeBright = new Color(0.72f, 1.0f, 0.30f);

        /// <summary>Hazard stripe yellow — the one place World 2 is allowed a pure warning colour.</summary>
        public static readonly Color Hazard = new Color(0.85f, 0.65f, 0.15f);

        /// <summary>Cyan status lamps on machinery. The cold counterpoint to all that rust.</summary>
        public static readonly Color Status = new Color(0.35f, 0.85f, 0.95f);

        /// <summary>The recessed panel-joint line (MV-781, change 2) — roughly half the floor's own
        /// resolved luminance, sitting deliberately between the floor and <see cref="Soffit"/>.</summary>
        public static readonly Color PanelJoint = new Color(0.028f, 0.033f, 0.029f);

        /// <summary>Silt (MV-781, change 3) — warm-neutral, about 1.5x the floor's luminance.</summary>
        public static readonly Color Silt = new Color(0.13f, 0.12f, 0.10f);

        /// <summary>Standing water (MV-781, change 3) — green-shifted toward <see cref="Sludge"/>'s
        /// hue, about 2.2x the floor's luminance so it reads as the brightest thing on the ground.</summary>
        public static readonly Color StandingWater = new Color(0.50f, 0.68f, 0.24f);

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

        public const float PanelJointWidth = 0.10f;
        public const float PanelJointThickness = 0.06f;
        public const float PanelJointSunk = 0.02f;

        public const float FloorPatchLift = 0.015f;
        public const float FloorPatchThickness = 0.03f;

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

            for (int i = 0; i < GrateGrilleBarCount; i++)
            {
                float t = (i + 0.5f) / GrateGrilleBarCount - 0.5f;
                Box(root.transform, $"Grille Bar{i}",
                    new Vector3(t * GrateFrameInner, -GrateGrilleBarHeight * 0.5f, 0f),
                    new Vector3(GrateGrilleBarWidth, GrateGrilleBarHeight, GrateGrilleBarLength), Soffit, SurfaceKind.Metal);
            }

            Glow(root.transform, "Void", new Vector3(0f, -GrateFrameHeight * 0.85f, 0f),
                 new Vector3(GrateFrameInner, GrateFrameInner, 1f), Quaternion.Euler(90f, 0f, 0f), Soffit);

            return root;
        }

        /// <summary>A recessed panel-joint strip (MV-781, change 2) — sunk <see cref="PanelJointSunk"/>
        /// below the floor's own top so it reads as a cut line, not a raised rib.</summary>
        public static GameObject BuildPanelJoint(Transform parent, Rect worldRect, float floorTopY = 0f)
        {
            Vector3 center = new Vector3(worldRect.center.x,
                floorTopY - PanelJointSunk - PanelJointThickness * 0.5f, worldRect.center.y);
            return Box(parent, "Panel Joint", center,
                new Vector3(worldRect.width, PanelJointThickness, worldRect.height), PanelJoint);
        }

        /// <summary>A silt or standing-water patch (MV-781, change 3) — a thin flat slab lifted
        /// <see cref="FloorPatchLift"/> above the floor. Standing water gets a raised smoothness on
        /// its own material instance so the key catches it, per the ticket's own wording.</summary>
        public static GameObject BuildFloorPatch(Transform parent, Rect worldRect, bool isWater, float floorTopY = 0f)
        {
            Color tone = isWater ? StandingWater : Silt;
            Vector3 center = new Vector3(worldRect.center.x,
                floorTopY + FloorPatchLift + FloorPatchThickness * 0.5f, worldRect.center.y);
            GameObject go = Box(parent, isWater ? "Standing Water" : "Silt", center,
                new Vector3(worldRect.width, FloorPatchThickness, worldRect.height), tone,
                isWater ? SurfaceKind.Prop : SurfaceKind.Dirt);

            if (isWater)
            {
                Material mat = go.GetComponent<Renderer>()?.sharedMaterial;
                if (mat != null && mat.HasProperty("_Smoothness"))
                    mat.SetFloat("_Smoothness", Mathf.Max(mat.GetFloat("_Smoothness"), 0.55f));
            }

            return go;
        }

        // ---------------------------------------------------------------- sludge dressing

        /// <summary>
        /// Dresses one sludge tile: a bright lip around its edge, flow chevrons across it pointing the
        /// way the drain runs, and reed clusters along its long sides.
        ///
        /// The sludge is the world's one saturated colour and currently renders as a flat green
        /// rectangle. In the key art it is the thing the eye lands on first, and it is the thing that
        /// tells the player which way is downstream — that is what the chevrons are for, and they are
        /// the reason this is dressing and not decoration.
        /// </summary>
        public static void DressSludgeTile(Transform parent, Vector3 center, float width, float depth,
                                           Vector3 flowDirection, int seed)
        {
            var root = new GameObject("Sludge Dressing");
            root.transform.SetParent(parent, false);
            root.transform.position = center;

            const float lipY = 0.075f;   // just above SludgeThickness (0.05) — never z-fights the tile
            const float lipW = 0.22f;

            Box(root.transform, "Lip N", new Vector3(0f, lipY, depth * 0.5f),
                new Vector3(width, 0.06f, lipW), SludgeBright, SurfaceKind.Foliage);
            Box(root.transform, "Lip S", new Vector3(0f, lipY, -depth * 0.5f),
                new Vector3(width, 0.06f, lipW), SludgeBright, SurfaceKind.Foliage);
            Box(root.transform, "Lip E", new Vector3(width * 0.5f, lipY, 0f),
                new Vector3(lipW, 0.06f, depth), SludgeBright, SurfaceKind.Foliage);
            Box(root.transform, "Lip W", new Vector3(-width * 0.5f, lipY, 0f),
                new Vector3(lipW, 0.06f, depth), SludgeBright, SurfaceKind.Foliage);

            // Chevrons along the flow axis. Two bars at 45 degrees make an arrowhead; laid flat and
            // unlit, they hold their colour in the dark exactly like the key art's.
            Vector3 flow = flowDirection.sqrMagnitude < 0.001f ? Vector3.forward : flowDirection.normalized;
            float run = Mathf.Abs(Vector3.Dot(flow, Vector3.forward)) > 0.5f ? depth : width;
            int chevrons = Mathf.Clamp(Mathf.FloorToInt(run / 3.5f), 1, 6);
            float yaw = Mathf.Atan2(flow.x, flow.z) * Mathf.Rad2Deg;

            for (int i = 0; i < chevrons; i++)
            {
                float t = (i + 0.5f) / chevrons - 0.5f;
                var chev = new GameObject($"Chevron{i}");
                chev.transform.SetParent(root.transform, false);
                chev.transform.localPosition = flow * (t * run) + Vector3.up * 0.09f;
                chev.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                Box(chev.transform, "L", new Vector3(-0.30f, 0f, -0.30f), new Vector3(0.95f, 0.05f, 0.16f),
                    SludgeBright, SurfaceKind.Foliage).transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                Box(chev.transform, "R", new Vector3(0.30f, 0f, -0.30f), new Vector3(0.95f, 0.05f, 0.16f),
                    SludgeBright, SurfaceKind.Foliage).transform.localRotation = Quaternion.Euler(0f, -45f, 0f);
            }

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
