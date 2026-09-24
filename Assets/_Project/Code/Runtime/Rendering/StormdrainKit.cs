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

        /// <summary>MV-803, "Stormdrain Pass 4" review, approved by Lee 2026-09-15 ("the diagonal
        /// striped outlines should be bright enough to lift the overall design"). The banding's own
        /// bright yellow — deliberately its own tone, not <see cref="Hazard"/>: this is the striped
        /// paint on a backing plate, brighter and carrying real emission, while <see cref="Hazard"/>
        /// stays the flat warning tone used elsewhere (the light kit's tone list).</summary>
        public static readonly Color HazardStripeColor = new Color(0.980f, 0.800f, 0.140f);

        /// <summary>The banding's backing plate — near-black, so the yellow stripes read as paint on a
        /// dark ground rather than as a bright shape floating in the room.</summary>
        public static readonly Color HazardStripeBacking = new Color(0.050f, 0.048f, 0.045f);

        /// <summary>The ticket's own approved figure — do not re-raise (dimmer "to sit back" was
        /// explicitly rejected; Lee asked for the banding bright enough to lift the design).</summary>
        public const float HazardStripeEmissive = 0.22f;

        /// <summary>The ticket's own approved figures — do not re-raise.</summary>
        public const float HazardStripeLeanDeg = 34f;
        public const float HazardStripePitch = 0.42f;

        /// <summary>A stripe's own width, measured across its long axis before the lean is applied.</summary>
        public const float HazardStripeThickness = 0.16f;

        /// <summary>"Proud of the backing face by a hair" (the ticket's own words) — just enough that a
        /// stripe reads as painted onto the plate rather than flush with (or sunk into) it, on both
        /// faces of the plate at once.</summary>
        public const float HazardStripeProud = 0.006f;

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

        /// <summary>MV-791: was 0.003 — 3 mm of separation from the bay top face is inside depth-buffer
        /// precision at this world's ~26 m camera distance on the WebGL target, and read on device as
        /// left-to-right banding. Raised to a margin the depth buffer can actually resolve.</summary>
        public const float CrackLift = 0.012f;

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

        /// <summary>MV-791: was 0.004 — same depth-precision trap as <see cref="CrackLift"/>, raised the
        /// same way.</summary>
        public const float StainLift = 0.020f;
        public const float StainLayerGap = 0.002f;

        /// <summary>MV-791: the water meniscus ring's own lift above <see cref="StainLift"/> (i.e. above
        /// the pool floor plane), replacing the old shared <see cref="StainLayerGap"/> it used to stack
        /// on — that gap is <see cref="BuildSiltStain"/>'s halo/core spacing, not a number this ticket
        /// touches, so the meniscus gets its own named constant rather than overloading that one.</summary>
        public const float WaterMeniscusLift = 0.006f;

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

        /// <summary>How far the soffit band overhangs inward from the top of a wall.</summary>
        public const float SoffitOverhang = 1.2f;
        public const float SoffitThickness = 0.35f;

        /// <summary>MV-802, "Pipes as structure" (design approved by Lee 2026-09-15, "Stormdrain Pass 4"
        /// review). The camera is a fixed 60 degree top-down rig at 26.02 m, so anything overhead sits
        /// ON the gameplay at that angle — an overhead main hugs the wall line and crosses a room only
        /// at its far end, never over the playable middle. These numbers are the review's own; gated
        /// behind the same <c>wallHeight &gt;= 2.2 m</c> threshold the existing "Pipe High" run already
        /// uses (MV-765) — a wall too short to carry the run at this fixed height must not carry it at
        /// all, the same low-wall invariant that ticket established.
        ///
        /// MV-819: the shipped MV-802 numbers (Y 2.35, inset 0.95) sat the main's whole cross-section
        /// under <see cref="BuildSoffit"/>'s 1.2 m overhang (y 2.65-3.0), which read as no pipes at all
        /// from the actual gameplay camera. Retuned per that ticket's own worked example — drop the main
        /// below the soffit's underside and bring it out past the soffit's own inward edge, rather than
        /// shrinking the soffit (the soffit is what sells "roofed tunnel" on every OTHER wall foot, so
        /// narrowing it globally would trade one readability problem for another).</summary>
        public const float OverheadMainRadius = 0.32f;
        public const float OverheadMainY = 2.0f;
        public const float OverheadMainInset = 1.65f;
        public const float OverheadCrossRadius = 0.29f;

        /// <summary>MV-819: same drop as <see cref="OverheadMainY"/>, so a cross-main also clears the
        /// soffit's 2.65 m underside on every wall it runs near.</summary>
        public const float OverheadCrossY = 2.0f;

        /// <summary>MV-819, change 2: how much further into the room a cross-main's own line sits than
        /// <see cref="OverheadMainInset"/> — without it, a cross-main's line coincided almost exactly
        /// with its own far wall's hugging main (both offset by the same inset, from the zone edge and
        /// the wall face respectively, which differ by only half a wall thickness), so the two pipes'
        /// bounds intersected. AC1 only requires the two renderers' <c>Bounds</c> not intersect — the
        /// lathed pipe mesh's own end-collar flare inflates a tube's rendered cross-section to
        /// <c>radius * 1.18</c> (confirmed empirically: <see cref="OverheadMainRadius"/>'s 0.32 m radius
        /// renders a 0.76 m wide <see cref="Renderer.bounds"/>), so the minimum gap that actually clears
        /// the two AABBs is the two flared half-widths, not the "2x main radius" figure from the
        /// ticket's own prose describing the earlier, larger clearance. Sized to that flared minimum
        /// (~0.72 m) plus a small margin, and kept low enough that
        /// <see cref="OverheadMainInset"/> + this stays comfortably under the 2.5 m "near a zone end"
        /// allowance <c>MV802PipeStructureTests</c> gates overhead structure on.</summary>
        public const float OverheadCrossClearance = 0.8f;

        /// <summary>Overhead collars land roughly every this fraction of a run's own length (the
        /// ticket's own "at roughly 0.24 of its length") rather than a fixed metre spacing — a run's
        /// collars always divide it the same way regardless of how long the wall face is.
        ///
        /// MV-819: a run below <see cref="OverheadShortRunLength"/> instead gets
        /// <see cref="OverheadCollarCount"/>'s halved count — the full division packs four collars
        /// (each ~0.9 m wide once the lathed flare is counted) into a short run tightly enough that a
        /// 10-sample visibility sweep reliably lands on one, self-occluding the very main the collar
        /// rides on.</summary>
        public const float OverheadCollarFraction = 0.24f;

        /// <summary>MV-819: below this run length, <see cref="OverheadCollarCount"/> halves the collar
        /// count — short enough that the full 4-collar division (spacing ~0.24 * length) starts packing
        /// collars closer together than the AC1 visibility sweep's own 1/10-of-length sample pitch.</summary>
        public const float OverheadShortRunLength = 10f;

        public const float OverheadBracketSpacingMin = 4.0f;
        public const float OverheadBracketSpacingMax = 6.0f;

        /// <summary>MV-802, change 3: the floor-level main a "pipe"-dressed cover piece resolves to.</summary>
        public const float FloorMainRadius = 0.40f;

        /// <summary>MV-863: no more than this far apart along a pipe main's own length — the same
        /// "never more than the limit" spacing rule <c>MapRuntime.DeckPostSpacing</c> already uses for
        /// deck support posts.</summary>
        public const float PipeSupportSpacing = 2.0f;

        private const float PipeSupportSize = 0.12f;

        /// <summary>MV-801, "Stormdrain Pass 4" review, approved by Lee 2026-09-15 — a channel-eligible
        /// sludge rect's own numbers. The trough floor sits <see cref="ChannelTroughDepth"/> below the
        /// pre-ticket floor (not deeper: 0.62 m was tested and rejected for hiding the ooze at the 60
        /// degree camera); the ooze SURFACE itself only drops <see cref="ChannelOozeDrop"/>, so there is
        /// visible headroom between the ooze and the trough floor beneath it.</summary>
        public const float ChannelTroughDepth = 0.45f;
        public const float ChannelOozeDrop = 0.18f;
        private const float ChannelWallThickness = 0.10f;
        private const float ChannelLipHeight = 0.10f;
        private const float ChannelKerbWidth = 0.44f;
        private const float ChannelKerbHeight = 0.20f;

        /// <summary>MV-803: both drop edges of a channel get hazard banding, full length, this tall —
        /// the ticket's own figure.</summary>
        private const float ChannelHazardBandHeight = 0.22f;

        /// <summary>MV-822: the pump band's own offset from the housing's centre must clear the hex
        /// prism's forward CORNER (a real vertex — <see cref="CharacterMeshes.Prism"/>'s <c>+ PI/sides</c>
        /// rotation lands one square on the forward axis), not just its flat-face radius, or the band's
        /// middle sits inside the body. The ticket's own figure: 1.0*r plus this clearance.</summary>
        private const float PumpHazardBandRadialClearance = 0.02f;

        private const float ChannelCrossingSpacingMin = 8f;
        private const float ChannelCrossingSpacingMax = 12f;
        private const float ChannelCrossingWidth = 1.5f;
        private const float ChannelCrossingOverhang = 0.6f;
        private const float ChannelCrossingSlatPitch = 0.34f;
        private const float ChannelCrossingPostHeight = 0.75f;

        /// <summary>MV-906: above this channel run length, its crossing spacing (MV-801 change 5) widens
        /// by <see cref="LongChannelSpacingScale"/> — comfortably above every ordinary World 2 channel
        /// run (world2_config.json's own sludge rects top out well under this) and comfortably below
        /// a16's own 106 m channel, the one run this exists to thin. a16 (Gantry Run, level 1, no floor
        /// zone beneath it) carries no ordinary room walls at all (<see cref="MapGeometry.Walls"/> skips
        /// a <c>level &gt; 0</c> zone) — its own dominant dressing cost is this ONE channel's hazard
        /// banding and crossings, not wall dressing.
        ///
        /// The hazard-stripe PITCH itself is never touched here — MV-803's own approved 0.42 m pitch is
        /// an exact figure <see cref="MV803HazardBandingTests"/> checks on every banding instance in the
        /// map, a16's included, so a16's own banding is thinned by covering LESS of the run
        /// (<see cref="LongChannelHazardSegmentLength"/>/<see cref="LongChannelHazardSegmentSpacing"/>,
        /// see <see cref="BuildLongChannelHazardBanding"/>) rather than by spacing its stripes wider.
        /// </summary>
        private const float LongChannelRunLength = 40f;
        private const float LongChannelSpacingScale = 4f;

        /// <summary>MV-906: each hazard-banding segment a long channel gets, in metres — short enough
        /// that every segment still reads as a normal MV-803 band (same pitch, same lean) rather than a
        /// stretched-out one.</summary>
        private const float LongChannelHazardSegmentLength = 8f;

        /// <summary>MV-906: distance between segment CENTRES along a long channel's run — comfortably
        /// wider than <see cref="LongChannelHazardSegmentLength"/> so most of the run is genuinely
        /// undressed between segments, with a segment anchored at (or within half a spacing of) each end
        /// so the corridor never reads unmarked right at its own drop-off.</summary>
        private const float LongChannelHazardSegmentSpacing = 24f;

        private static float LongChannelScale(float run) => run > LongChannelRunLength ? LongChannelSpacingScale : 1f;

        /// <summary>MV-906: a long channel's hazard banding along one drop edge, built as several
        /// <see cref="LongChannelHazardSegmentLength"/> m segments every
        /// <see cref="LongChannelHazardSegmentSpacing"/> m along <paramref name="run"/> (first and last
        /// segment centred within half a spacing of each end, so the corridor is dressed at both ends
        /// and at regular intervals, per the ticket's own AC) instead of one continuous band. Each
        /// segment is a normal <see cref="BuildHazardBanding"/> call — same approved 0.42 m stripe pitch,
        /// same 34-degree lean — so <see cref="MV803HazardBandingTests"/>'s per-instance pitch check
        /// still holds; only the total banded LENGTH (and so total stripe count) drops.</summary>
        private static void BuildLongChannelHazardBanding(Transform parent, Vector3 edgeCentre, float run,
                                                            float height, bool alongX, float depth)
        {
            int segments = Mathf.Max(2, Mathf.RoundToInt(run / LongChannelHazardSegmentSpacing) + 1);
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            float span = run - LongChannelHazardSegmentLength;

            for (int i = 0; i < segments; i++)
            {
                float t = (i / (float)(segments - 1)) - 0.5f;
                Vector3 segCentre = edgeCentre + along * (t * span);
                BuildHazardBanding(parent, segCentre, LongChannelHazardSegmentLength, height, alongX, depth);
            }
        }

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
        /// Dresses one inner wall face: a kerb along its foot with a cyan kerb strip running its
        /// length, a two-pipe bank with collars, and a bulkhead lamp every 6-9 m
        /// (<see cref="StormdrainLightKit.MinLampSpacing"/>-<see cref="StormdrainLightKit.MaxLampSpacing"/>,
        /// MV-787).
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

            // Kerb strip (MV-787, change 2) — a continuous cyan guide-rail along the kerb's own line,
            // not a fitting: it never gets a pool, and it does not count against the fitting budget.
            StormdrainLightKit.BuildKerbStrip(parent, a, b, n);

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

            // Bulkhead lamps (MV-787, "Stormdrain Surface Kit" lighting pass, change 1) — amber caged
            // fittings every 6-9 m, the spread itself seeded off this face's own seed so two adjacent
            // walls never line their lamps up into a grid (a grid is exactly what makes generated
            // dressing read as generated). Replaces the old fixed-7 m single-lens "Wall Lamp".
            // MV-787: "one every 6-9 m along each wall run" still means a run SHORTER than one full
            // spacing gets its own lamp — a dark run just because it never reached a whole 6 m span
            // would be exactly the kind of unlit dead patch this ticket exists to remove — so this is a
            // floor of 1, not floor(length/spacing) alone.
            float faceLampSpacing = Mathf.Lerp(StormdrainLightKit.MinLampSpacing, StormdrainLightKit.MaxLampSpacing,
                Frac(seed * 0.7548776662f));
            int lamps = Mathf.Max(1, Mathf.FloorToInt(length / faceLampSpacing));
            float phase = Frac(seed * 0.6180339887f);
            float mountHeight = Mathf.Min(StormdrainLightKit.BulkheadHeight, wallHeight * 0.9f);
            for (int i = 0; i < lamps; i++)
            {
                float t = (i + 0.25f + phase * 0.5f) / Mathf.Max(1, lamps);
                if (t <= 0.02f || t >= 0.98f) continue;
                Vector3 at = new Vector3(Mathf.Lerp(a.x, b.x, t), 0f, Mathf.Lerp(a.y, b.y, t));
                StormdrainLightKit.BuildBulkheadLamp(parent, "Bulkhead Lamp", at, n, along,
                    StormdrainLightKit.Amber, pulsing: false, mountHeight);
            }
        }

        // ---------------------------------------------------------------- overhead structure (MV-802)

        /// <summary>MV-819: how many collars one overhead run (main or cross-main) of
        /// <paramref name="length"/> gets — the full <see cref="OverheadCollarFraction"/> division above
        /// <see cref="OverheadShortRunLength"/>, 3 below it. See <see cref="OverheadCollarFraction"/>'s
        /// own doc for why a short run needs fewer collars at all; 3, specifically, not the naive half
        /// (2), because 2 collars land at t = 0.25/0.75 of the run — which is EXACTLY the AC1 sweep's own
        /// 3rd/8th sample centre (samples sit at deciles 0.05, 0.15, ... 0.95, and 0.25/0.75 are both
        /// exact decile centres). That is not a near-miss the AABB approximation exaggerates; it is a
        /// guaranteed hit, empirically confirmed (a real run at length ~8.3 m failed AC1 at exactly 50%,
        /// with both halved collars logged as the occluder of their own exactly-aligned sample). 3
        /// collars land at t = 1/6, 1/2, 5/6 — none a decile centre — confirmed clear on every run this
        /// ticket's own World 2 config builds.</summary>
        private static int OverheadCollarCount(float length)
        {
            int full = Mathf.Max(1, Mathf.RoundToInt(1f / OverheadCollarFraction));
            return length < OverheadShortRunLength ? 3 : full;
        }

        /// <summary>MV-819: an overhead main below this length never builds at all. A wall face this
        /// short is inherently close to BOTH its own corners at once (there is no "middle" of the run far
        /// from either end) — and at a corner the perpendicular wall's own main/soffit/bracket sit at the
        /// same <see cref="OverheadMainY"/>, inset by the same <see cref="OverheadMainInset"/> formula
        /// from THEIR OWN wall, converging close to this run's own line right where the two walls meet.
        /// Trimming such a run's own ends back from the corner (tried first) only made this worse: the
        /// same fixed-size collars and bracket then landed on a much shorter remaining line, so they
        /// covered proportionally MORE of it, not less. Skipping the run entirely is what the ticket's
        /// own "a junction box already marks the corner visually" note licenses — that corner reads as
        /// piped from the OTHER (longer) wall's own main and the junction box, not from this stub.</summary>
        public const float OverheadMinRunLength = 5f;

        /// <summary>The overhead main that hugs one wall face's own line (MV-802, change 1): a
        /// <see cref="OverheadMainRadius"/> rust main the face's full length, inset
        /// <see cref="OverheadMainInset"/> from the wall at <see cref="OverheadMainY"/>, with collars
        /// dividing it every <see cref="OverheadCollarFraction"/> of its own length and a bracket
        /// (change 2) tying it back to the wall every <see cref="OverheadBracketSpacingMin"/>-
        /// <see cref="OverheadBracketSpacingMax"/> m. No-ops below <see cref="OverheadMinRunLength"/>
        /// (MV-819: a short face is corner-adjacent on both ends at once — see that constant's own doc)
        /// or below the wall-height threshold the run needs to clear the room without a MV-765 regression
        /// (a wall too short to carry it at a fixed 2.35 m must not carry it at all).</summary>
        public static void DressOverheadRun(Transform parent, Vector2 a, Vector2 b, Vector2 outward,
                                            float wallHeight, int seed)
        {
            float length = (b - a).magnitude;
            if (length < OverheadMinRunLength || wallHeight < 2.2f) return;

            Vector2 dir = (b - a) / length;
            Vector3 mid = new Vector3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);
            Vector3 n = new Vector3(outward.x, 0f, outward.y);
            Vector3 along = new Vector3(dir.x, 0f, dir.y);
            Quaternion lie = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);

            Tube(parent, "Overhead Main", mid + n * OverheadMainInset + Vector3.up * OverheadMainY,
                 OverheadMainRadius, length, lie, Rust);

            int collars = OverheadCollarCount(length);
            for (int i = 0; i < collars; i++)
            {
                float t = (i + 0.5f) / collars;
                Vector3 at = new Vector3(Mathf.Lerp(a.x, b.x, t), OverheadMainY, Mathf.Lerp(a.y, b.y, t))
                             + n * OverheadMainInset;
                Tube(parent, $"Overhead Collar{i}", at, OverheadMainRadius * 1.2f, 0.20f, lie, RustDark);
            }

            float spacing = Mathf.Lerp(OverheadBracketSpacingMin, OverheadBracketSpacingMax,
                Frac(seed * 0.7548776662f + 5f));
            // MV-819: a run below OverheadShortRunLength skips its wall-tie bracket entirely, not just
            // halves it — a bracket's own "Bracket Collar" ring is a THIRD ~0.9 m-wide fitting (on top of
            // the run's own, already-halved overhead collars) competing for the same handful of AC1
            // sample points on a short line; the ticket's own visibility budget matters more here than a
            // short run's structural tie-back.
            if (length >= OverheadShortRunLength)
            {
                int brackets = Mathf.Max(1, Mathf.FloorToInt(length / spacing));
                float phase = Frac(seed * 0.6180339887f + 5f);
                for (int i = 0; i < brackets; i++)
                {
                    float t = (i + 0.5f + phase * 0.5f) / brackets;
                    if (t <= 0.02f || t >= 0.98f) continue;
                    Vector3 wallAt = new Vector3(Mathf.Lerp(a.x, b.x, t), OverheadMainY, Mathf.Lerp(a.y, b.y, t));
                    BuildOverheadBracket(parent, wallAt + n * OverheadMainInset, n, lie);
                }
            }
        }

        /// <summary>One bracket (MV-802, change 2): a beam dropping from a point lower on the wall up to
        /// the overhead main, plus a collar ring at the main itself — the same "arm plus collar" idiom
        /// <see cref="StormdrainLightKit.BuildBulkheadLamp"/>'s own bracket already uses.</summary>
        private static void BuildOverheadBracket(Transform parent, Vector3 pipeAt, Vector3 n, Quaternion pipeLie)
        {
            Vector3 wallPoint = pipeAt - n * OverheadMainInset + Vector3.up * -0.35f;
            Vector3 d = pipeAt - wallPoint;
            float len = d.magnitude;
            if (len < 0.01f) return;

            var beam = AddMeshPart(parent, "Bracket Beam", CharacterMeshes.Beam(len, 0.045f, 0.035f),
                (wallPoint + pipeAt) * 0.5f, SurfaceKind.Metal, RustDark);
            beam.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);

            Tube(parent, "Bracket Collar", pipeAt, OverheadMainRadius * 1.2f, 0.16f, pipeLie, RustDark);
        }

        /// <summary>The one cross-main a room gets (MV-802, change 1): spans <paramref name="span"/>
        /// along <paramref name="acrossDir"/>, centred on <paramref name="center"/> — the caller
        /// (<see cref="MaxWorlds.Arena.StormdrainDressing"/>) has already resolved where that is: inset
        /// from the room's own far wall, on the end furthest from its entry, never over the middle.</summary>
        public static void BuildOverheadCrossMain(Transform parent, Vector3 center, float span, Vector3 acrossDir)
        {
            if (span < 0.5f) return;
            Quaternion lie = Quaternion.LookRotation(acrossDir, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            Tube(parent, "Overhead Cross Main", center, OverheadCrossRadius, span, lie, Rust);

            int collars = OverheadCollarCount(span);
            for (int i = 0; i < collars; i++)
            {
                float t = (i + 0.5f) / collars - 0.5f;
                Vector3 at = center + acrossDir * (t * span);
                Tube(parent, $"Overhead Cross Collar{i}", at, OverheadCrossRadius * 1.2f, 0.20f, lie, RustDark);
            }
        }

        /// <summary>A junction box where two overhead runs meet (MV-802, change 4): a hexagonal steel
        /// body, a rust valve wheel with four spokes on top, and one <see cref="StormdrainLightKit.BuildLedPanel"/>
        /// face. Dressing only, same as everything else in this kit. <paramref name="at"/> is the
        /// junction's own world position, already resolved by the caller (the shared corner of the two
        /// wall faces, offset out to <see cref="OverheadMainInset"/> and up to <see cref="OverheadMainY"/>).</summary>
        public static GameObject BuildOverheadJunctionBox(Transform parent, Vector3 at)
        {
            var root = new GameObject("Overhead Junction");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            float bodyR = OverheadMainRadius * 1.7f;
            float bodyH = OverheadMainRadius * 1.3f;
            AddMeshPart(root.transform, "Body", CharacterMeshes.Prism(6, bodyR, bodyR, bodyH),
                Vector3.zero, SurfaceKind.Metal, RustDark);

            float wheelR = bodyR * 0.55f;
            float wheelY = bodyH * 0.5f + 0.02f;
            AddMeshPart(root.transform, "Valve Wheel", CharacterMeshes.Ring(wheelR * 0.55f, wheelR, 0.03f),
                Vector3.up * wheelY, SurfaceKind.Metal, Rust);

            for (int i = 0; i < 4; i++)
            {
                var spoke = AddMeshPart(root.transform, $"Spoke{i}", CharacterMeshes.Beam(wheelR * 1.8f, 0.02f, 0.02f),
                    Vector3.up * wheelY, SurfaceKind.Metal, Rust);
                spoke.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f) * Quaternion.Euler(0f, 0f, 90f);
            }

            StormdrainLightKit.BuildLedPanel(root.transform, new Vector3(0f, 0f, -bodyR * 0.9f));

            // MV-803: one hazard band, low on the front face — a junction box is something you must
            // act on (it feeds the overhead structure), not decoration.
            BuildHazardBanding(root.transform, new Vector3(0f, -bodyH * 0.28f, -bodyR * 0.9f),
                bodyR * 1.05f, bodyH * 0.32f, alongX: true, depth: 0.02f);

            return root;
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

        /// <summary>MV-818: the fraction of a cover collider's own XZ footprint every dressing form's
        /// combined renderer bounds must fill. The ticket's own rule is "at least 90%, never more than
        /// 0.15 m of overrun" — targeting a footprint slightly INSIDE the collider (93%) satisfies both
        /// halves at once (0 overrun, comfortably above the 90% floor) regardless of the collider's own
        /// absolute size, rather than computing a per-piece margin.</summary>
        public const float CoverFootprintCoverage = 0.93f;

        /// <summary>MV-818, change 4: every dressing form's combined renderer bounds must stand at
        /// least this tall — the collider itself is 1.6 m and blocks shots, so anything shorter reads
        /// as something Max could shoot over that he actually can't.</summary>
        public const float CoverMinVisibleHeight = 1.0f;

        /// <summary>MV-818, change 3: at and above this long/short aspect ratio, a cover piece's own
        /// whole-object yaw (<c>StormdrainDressing.DeterministicYaw</c>) must never be applied — a wide
        /// yaw swings a long piece's own ends outside its collider footprint (the ticket's own observed
        /// failure, up to ~1.1 m on <see cref="BuildBurstMain"/>).</summary>
        public const float NoYawAspectThreshold = 1.5f;

        /// <summary>MV-818, change 2: at and above this long/short aspect ratio, a cover piece must be
        /// built as a repeated run of modules along its own long axis rather than one scaled prop.</summary>
        public const float ModularRunAspectThreshold = 2.0f;

        /// <summary>MV-818, change 3: the maximum per-module yaw jitter a modular run may still apply —
        /// small enough that a jittered module never pushes outside the footprint rule above.</summary>
        public const float ModuleJitterMaxDeg = 5f;

        /// <summary>Standpipe cluster — replaces a Tree (MV-786). Five lathed pipes of varied radius
        /// and height, jittered on a ring across the cover's own footprint, each collared at
        /// mid-height, rising off one shared lathed base plate. Deterministic from world position —
        /// never <see cref="UnityEngine.Random"/> — so the same map always clusters the same pipes the
        /// same way.</summary>
        public static GameObject BuildStandpipe(Transform parent, Vector3 at, Vector3 size)
        {
            var root = new GameObject("Standpipe");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            float footprint = Mathf.Min(size.x, size.z);
            float h = size.y;
            float baseR = footprint * 0.5f + 0.08f;

            AddMeshPart(root.transform, "Base Plate", CharacterMeshes.Lathe(new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(baseR, 0f),
                    new Vector2(baseR, 0.06f),
                    new Vector2(baseR * 0.88f, 0.09f),
                }, 20),
                Vector3.zero, SurfaceKind.Metal, RustDark);

            for (int i = 0; i < 5; i++)
            {
                float hash = at.x * 12.9898f + at.z * 78.233f + i * 37.719f;
                float radius = Mathf.Lerp(0.105f, 0.155f, Frac(hash * 0.6180339887f));
                float height = h * Mathf.Lerp(0.85f, 1.60f, Frac(hash * 0.3247179572f + 11f));
                float ringT = Mathf.Lerp(0.13f, 0.29f, Frac(hash * 0.1284f + 23f));
                float ringR = footprint * ringT;
                float ang = (i / 5f + Frac(hash * 0.918f)) * Mathf.PI * 2f;
                Vector3 pipeAt = new Vector3(Mathf.Cos(ang) * ringR, 0.09f, Mathf.Sin(ang) * ringR);

                Tube(root.transform, $"Pipe{i}", pipeAt + Vector3.up * (height * 0.5f), radius, height,
                     Quaternion.identity, Rust);
                Tube(root.transform, $"Collar{i}", pipeAt + Vector3.up * (height * 0.5f), radius * 1.3f, 0.12f,
                     Quaternion.identity, RustDark);
            }

            return root;
        }

        /// <summary>Collapsed grating — replaces a Hedge (MV-786). Two frame rails and nine cross bars
        /// pitched at 22 degrees, propped at one corner by a hexagonal stub — a fallen grate, not a
        /// hedge. The hedge is the ONE see-through cover class (only hedges let sight and shots pass),
        /// and a lattice of bars with air between them keeps that true by construction, not by a
        /// special case.</summary>
        public static GameObject BuildCollapsedGrating(Transform parent, Vector3 at, Vector3 size)
        {
            var root = new GameObject("Collapsed Grating");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            bool alongX = size.x >= size.z;
            float span = alongX ? size.x : size.z;
            float crossSpan = Mathf.Max(0.5f, alongX ? size.z : size.x);
            const float pitchDeg = 22f;

            // MV-818: rails/bars used to always lie along local Z/X regardless of alongX — an X-long
            // hedge's rails ran ACROSS its own length, not along it (every one of World 2's four
            // hedges is X-long). spanDir/crossDir make both the geometry and the pitch rotation's own
            // axis (which must be the rails' own axis, or a Z-long piece never tilts at all) follow
            // the piece's own long axis instead of assuming one.
            Vector3 spanDir = alongX ? Vector3.right : Vector3.forward;
            Vector3 crossDir = alongX ? Vector3.forward : Vector3.right;
            float lift = crossSpan * 0.5f * Mathf.Sin(pitchDeg * Mathf.Deg2Rad) + 0.04f;

            // Pitched about the rails' OWN long axis, not across it — a 16 m hedge run's rails must
            // tilt as one rigid, uniformly-raised plane, never stretch into a ramp along their own
            // length the way rotating about the cross axis would.
            var panel = new GameObject("Panel").transform;
            panel.SetParent(root.transform, false);
            panel.localPosition = Vector3.up * lift;
            panel.localRotation = Quaternion.AngleAxis(pitchDeg, spanDir);

            for (int i = 0; i < 2; i++)
            {
                float t = i == 0 ? -0.5f : 0.5f;
                var rail = AddMeshPart(panel, $"Rail{i}", CharacterMeshes.Beam(span, 0.045f, 0.045f),
                    crossDir * (t * crossSpan * 0.88f), SurfaceKind.Metal, RustDark);
                rail.transform.localRotation = Quaternion.FromToRotation(Vector3.up, spanDir);
            }

            for (int i = 0; i < 9; i++)
            {
                float t = (i + 0.5f) / 9f - 0.5f;
                var bar = AddMeshPart(panel, $"Bar{i}", CharacterMeshes.Beam(crossSpan * 0.9f, 0.03f, 0.03f),
                    spanDir * (t * span), SurfaceKind.Metal, RustDark);
                bar.transform.localRotation = Quaternion.FromToRotation(Vector3.up, crossDir);
            }

            // MV-818, change 4: the pitched panel alone tops out well under 1 m once crossSpan (the
            // hedge's own short side) is narrow — a 1 m-deep hedge only lifts ~0.4 m. One corner still
            // stands, propped near-vertical against whatever the grate fell from, so the COMBINED
            // bounds clear the collider's own 1.6 m even though most of the grate reads as fallen flat.
            float postHeight = Mathf.Max(lift * 2f, CoverMinVisibleHeight * 1.08f);
            Vector3 cornerLocal = crossDir * (crossSpan * 0.42f) + spanDir * (span * 0.42f);
            AddMeshPart(root.transform, "Prop Stub", CharacterMeshes.Prism(6, 0.07f, 0.055f, postHeight),
                cornerLocal + Vector3.up * (postHeight * 0.5f), SurfaceKind.Metal, RustDark);

            return root;
        }

        /// <summary>Silt hopper — replaces a Planter (MV-786). A turned bin: an outer flared body, a
        /// darker bore nested inside it, a proud rust rim at the mouth, three leg braces, and a silt
        /// spill pooling at the foot.</summary>
        public static GameObject BuildSiltHopper(Transform parent, Vector3 at, Vector3 size)
        {
            var root = new GameObject("Silt Hopper");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            float r = Mathf.Min(size.x, size.z) * 0.5f;
            float h = size.y;
            float outerH = h * 0.84f;
            float innerH = h * 0.80f;

            AddMeshPart(root.transform, "Outer", CharacterMeshes.Prism(8, r * 0.58f, r * 0.98f, outerH),
                Vector3.up * (outerH * 0.5f), SurfaceKind.Stone, KerbConcrete);

            AddMeshPart(root.transform, "Bore", CharacterMeshes.Prism(8, r * 0.46f, r * 0.84f, innerH),
                Vector3.up * (innerH * 0.5f), SurfaceKind.Stone, Soffit);

            AddMeshPart(root.transform, "Rim", CharacterMeshes.Ring(r * 0.86f, r * 1.06f, 0.05f),
                Vector3.up * outerH, SurfaceKind.Metal, Rust);

            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                float legH = h * 0.40f;
                Vector3 legAt = new Vector3(Mathf.Cos(a) * r * 0.78f, legH * 0.5f, Mathf.Sin(a) * r * 0.78f);
                AddMeshPart(root.transform, $"Leg{i}", CharacterMeshes.Beam(legH, r * 0.10f, r * 0.07f),
                    legAt, SurfaceKind.Metal, RustDark);
            }

            float spillSeed = at.x * 0.371f + at.z * 0.593f;
            AddMeshPart(root.transform, "Silt Spill",
                BuildBlobMesh(r * 0.95f, 9, StainSegmentMinT, StainSegmentMaxT, spillSeed),
                Vector3.up * 0.01f, SurfaceKind.Dirt, Silt);

            return root;
        }

        /// <summary>Pump set — replaces a Shed or Machinery cover piece (MV-786). A tapered six-sided
        /// housing, a lathed dome cap, a lathed base flange, four bolts around the flange, and an LED
        /// panel — the drain's only cold colour, so it still reads as the one machine still switched
        /// on.</summary>
        public static GameObject BuildPumpHousing(Transform parent, Vector3 at, Vector3 size)
        {
            var root = new GameObject("Pump Housing");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            float r = Mathf.Min(size.x, size.z) * 0.5f;
            float h = size.y;
            float bodyH = h * 0.78f;

            AddMeshPart(root.transform, "Body", CharacterMeshes.Prism(6, r, r * 0.84f, bodyH),
                Vector3.up * (bodyH * 0.5f), SurfaceKind.Metal, new Color(0.30f, 0.32f, 0.31f));

            float capH = h - bodyH;
            AddMeshPart(root.transform, "Cap", CharacterMeshes.Lathe(new[]
                {
                    new Vector2(r * 0.84f, 0f),
                    new Vector2(r * 0.84f, capH * 0.15f),
                    new Vector2(r * 0.58f, capH * 0.55f),
                    new Vector2(r * 0.26f, capH * 0.85f),
                    new Vector2(0f, capH),
                }, 20),
                Vector3.up * bodyH, SurfaceKind.Metal, RustDark);

            float fh = h * 0.06f;
            AddMeshPart(root.transform, "Base Flange", CharacterMeshes.Lathe(new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(r * 1.18f, 0f),
                    new Vector2(r * 1.18f, fh),
                    new Vector2(r, fh),
                }, 20),
                Vector3.zero, SurfaceKind.Metal, RustDark);

            float boltScale = h * 0.045f;
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                Vector3 boltAt = new Vector3(Mathf.Cos(a) * r * 1.05f, fh + boltScale * 0.5f, Mathf.Sin(a) * r * 1.05f);
                var bolt = AddMeshPart(root.transform, $"Bolt{i}", CharacterMeshes.Sphere(8),
                    boltAt, SurfaceKind.Metal, Rust);
                bolt.transform.localScale = Vector3.one * boltScale;
            }

            // MV-787, change 2: the single tinted "LED panel" box MV-786 built here is now a real
            // 5x3-cell fitting with its own pool (StormdrainLightKit.BuildLedPanel) — machinery is one
            // of the two places the ticket's table puts an LED panel.
            StormdrainLightKit.BuildLedPanel(root.transform, new Vector3(0f, bodyH * 0.72f, -r * 0.84f - 0.02f));

            // MV-822: one hazard band on the pump intake — the front face, just above the base flange,
            // clear of the housing's own forward corner (see PumpHazardBandRadialClearance's own doc).
            BuildHazardBanding(root.transform, new Vector3(0f, fh + h * 0.10f, -(r + PumpHazardBandRadialClearance)),
                r * 1.1f, h * 0.16f, alongX: true, depth: 0.02f);

            return root;
        }

        /// <summary>Burst main — replaces a bare crate (<c>CoverDressing.None</c>) (MV-786). A lathed
        /// tube on its side with a darker bore lathed into one end and a raised collar at that
        /// opening — the cheapest way to make a cover block read as failed plumbing instead of a bare
        /// crate.</summary>
        public static GameObject BuildBurstMain(Transform parent, Vector3 at, Vector3 size)
        {
            var root = new GameObject("Burst Main");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            float footprint = Mathf.Min(size.x, size.z);
            float h = size.y;
            float outerR = Mathf.Min(h * 0.52f, footprint * 0.46f);
            bool longX = size.x >= size.z;
            float longSide = longX ? size.x : size.z;
            float length = longSide * 0.92f;
            Vector3 along = longX ? Vector3.right : Vector3.forward;
            Quaternion lie = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);

            Tube(root.transform, "Outer", Vector3.up * outerR, outerR, length, lie, Rust);

            float boreR = outerR * 0.6f;
            float boreLen = length * 0.30f;
            Vector3 boreAt = along * (length * 0.5f - boreLen * 0.45f) + Vector3.up * outerR;
            Tube(root.transform, "Bore", boreAt, boreR, boreLen, lie, Soffit);

            var collar = AddMeshPart(root.transform, "Collar",
                CharacterMeshes.Ring(outerR * 1.02f, outerR * 1.24f, 0.10f),
                along * (length * 0.5f) + Vector3.up * outerR, SurfaceKind.Metal, RustDark);
            collar.transform.localRotation = lie;

            return root;
        }

        /// <summary>Pipe main (MV-802, "Pipes as structure", change 3) — replaces a cover class flagged
        /// <see cref="CoverDressing.Pipe"/> with a floor-level main, not a freestanding placement: a
        /// single <see cref="FloorMainRadius"/> lathed main lying along the cover piece's own longer XZ
        /// axis, capped at each end by the same <see cref="Tube"/> profile every other pipe in this kit
        /// already uses (its collar-ring end profile IS the "end caps" MV-863 asks for — nothing extra
        /// to build there). Stays square — this is structure, not loose debris, the same reasoning
        /// <see cref="BuildPumpHousing"/>'s own "stays square" already gives Shed/Machinery — so unlike
        /// <see cref="BuildBurstMain"/> it is never yawed by <see cref="MaxWorlds.Arena.StormdrainDressing.BuildFor"/>.
        /// Uses the cover piece's own existing collider and footprint: no collider is added, moved or
        /// resized here.
        ///
        /// MV-863: a support leg every <see cref="PipeSupportSpacing"/> along the run, floor to the
        /// pipe's own underside — the fix for the ticket's other reported fault, a long pipe reading as
        /// a row of stubs (that was actually <c>StormdrainDressing.BuildFor</c> routing anything at
        /// aspect &gt;= 2 through <see cref="MaxWorlds.Arena.StormdrainDressing"/>'s modular-run split,
        /// now excluded for Pipe — this ONE main was always continuous). Without a support a long main
        /// reads as floating; spaced the same "never more than the limit" way
        /// <c>MapRuntime.AddEdgePosts</c> already spaces deck posts.</summary>
        public static GameObject BuildPipeMain(Transform parent, Vector3 at, Vector3 size)
        {
            var root = new GameObject("Pipe Main");
            root.transform.SetParent(parent, false);
            root.transform.position = at;

            bool longX = size.x >= size.z;
            float length = (longX ? size.x : size.z) * 0.92f;
            Vector3 along = longX ? Vector3.right : Vector3.forward;
            Quaternion lie = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);

            Tube(root.transform, "Main", Vector3.up * FloorMainRadius, FloorMainRadius, length, lie, Rust);

            int supportCount = Mathf.Max(2, Mathf.CeilToInt(length / PipeSupportSpacing) + 1);
            for (int i = 0; i < supportCount; i++)
            {
                float t = supportCount > 1 ? (i / (float)(supportCount - 1)) - 0.5f : 0f;
                Vector3 supportAt = along * (t * length) + Vector3.up * (FloorMainRadius * 0.5f);
                Box(root.transform, $"Support {i}", supportAt,
                    new Vector3(PipeSupportSize, FloorMainRadius, PipeSupportSize), RustDark);
            }

            return root;
        }

        // ---------------------------------------------------------------- wall panels (MV-786, change 2)

        /// <summary>MV-786, change 2: a wall run stops being one long slab. Panels every 4 m at 93% of
        /// the segment length and 88% of the wall height, three recessed ribs per panel at quarter
        /// points, a hexagonal pilaster at every panel joint including both ends, a coping along the
        /// top of the whole run and a kerb at its foot. <paramref name="wallMaterial"/> is the wall's
        /// own already-resolved material, reused directly on the panels/coping/kerb so they read as the
        /// SAME wall broken into forms, not a new tint guess.</summary>
        public static void BuildWallPanels(Transform parent, Vector3 center, Vector3 size, bool alongX,
                                           Material wallMaterial)
        {
            float length = alongX ? size.x : size.z;
            float thickness = alongX ? size.z : size.x;
            float height = size.y;
            if (length < 0.01f) return;

            int panelCount = Mathf.Max(1, Mathf.RoundToInt(length / 4f));
            float segLen = length / panelCount;
            float panelLen = segLen * 0.93f;
            float panelHeight = height * 0.88f;

            var run = new GameObject("Wall Run").transform;
            run.SetParent(parent, false);
            run.localPosition = center;

            Vector3 along = alongX ? Vector3.right : Vector3.forward;

            for (int i = 0; i < panelCount; i++)
            {
                float offset = -length * 0.5f + (i + 0.5f) * segLen;
                Vector3 panelCenter = along * offset;
                Vector3 panelSize = alongX
                    ? new Vector3(panelLen, panelHeight, thickness)
                    : new Vector3(thickness, panelHeight, panelLen);

                BevelledPart(run, $"Panel{i}", panelCenter, panelSize, wallMaterial);

                for (int rib = 0; rib < 3; rib++)
                {
                    float rt = (rib + 1) / 4f - 0.5f;
                    Vector3 ribCenter = panelCenter + along * (rt * panelLen);
                    Vector3 ribSize = alongX
                        ? new Vector3(panelLen * 0.05f, panelHeight * 0.9f, thickness * 1.02f)
                        : new Vector3(thickness * 1.02f, panelHeight * 0.9f, panelLen * 0.05f);
                    Box(run, $"Panel{i} Rib{rib}", ribCenter, ribSize, GroundDry, SurfaceKind.Ground);
                }
            }

            for (int i = 0; i <= panelCount; i++)
            {
                float offset = -length * 0.5f + i * segLen;
                AddMeshPart(run, $"Pilaster{i}", CharacterMeshes.Prism(6, 0.30f, 0.24f, height * 0.94f),
                    along * offset, SurfaceKind.Metal, RustDark);
            }

            Vector3 copingSize = alongX
                ? new Vector3(length, 0.16f, 0.44f)
                : new Vector3(0.44f, 0.16f, length);
            BevelledPart(run, "Coping", Vector3.up * (height * 0.5f + 0.08f), copingSize, wallMaterial);

            Vector3 kerbSize = alongX
                ? new Vector3(length, 0.24f, 0.40f)
                : new Vector3(0.40f, 0.24f, length);
            BevelledPart(run, "Kerb", Vector3.up * (-height * 0.5f + 0.12f), kerbSize, wallMaterial);
        }

        /// <summary>A collider-free chamfered box painted with an explicit material rather than a
        /// resolved tone (MV-786) — for a piece that must read as exactly the same surface as something
        /// already built (a wall run's own panels/coping/kerb, reusing the wall's own resolved
        /// material) rather than a new tint guess.</summary>
        private static GameObject BevelledPart(Transform parent, string name, Vector3 localPos, Vector3 size,
                                               Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one;
            go.GetComponent<MeshFilter>().sharedMesh = CharacterMeshes.Bevelled(size, CharacterMeshes.DefaultBevel(size));
            Strip(go);
            var rend = go.GetComponent<Renderer>();
            if (rend != null && material != null) rend.sharedMaterial = material;
            return go;
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
        public static GameObject BuildCrack(Transform parent, Vector3 worldCenter, float hash, float floorTopY = 0f,
                                            float toneScale = 1f)
        {
            var go = new GameObject("Crack");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(worldCenter.x, floorTopY + CrackLift, worldCenter.z);
            go.transform.localRotation = Quaternion.Euler(0f, hash * 360f, 0f);
            go.transform.localScale = new Vector3(1.35f, 1f, 0.035f);

            Mesh mesh = BuildBlobMesh(CrackBaseRadius, 7, StainSegmentMinT, StainSegmentMaxT, hash * 97.13f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            // MV-799: a crack never floats brighter than the bay it sits on, so it carries that bay's
            // own lit-ground multiplier (1 = unlit, the pre-MV-799 behaviour).
            Paint(go, SurfaceKind.Dirt, Crack * toneScale);
            ZeroOutline(go);
            return go;
        }

        /// <summary>A silt drift (MV-784, change 4) — two stacked <see cref="BuildBlobMesh"/> blobs: a
        /// wide soft halo halfway in tone between the floor and <see cref="Silt"/>, and a denser core at
        /// full <see cref="Silt"/> inside it. The two-step edge is what makes this read as soaked into
        /// the floor rather than cut out of it.</summary>
        public static GameObject BuildSiltStain(Transform parent, Vector3 worldCenter, float coreRadius,
                                                float seed, float floorTopY = 0f, float toneScale = 1f)
        {
            var root = new GameObject("Silt");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(worldCenter.x, floorTopY + StainLift, worldCenter.z);

            // MV-799: same lit-ground multiplier as the bay underneath, so a silt drift never floats
            // brighter than the floor it sits on (1 = unlit, the pre-MV-799 behaviour).
            Color haloTone = Color.Lerp(GroundBase, Silt, 0.5f) * toneScale;
            BuildStainLayer(root.transform, "Halo", coreRadius * SiltHaloScale, SiltSegments, seed, 0f,
                SurfaceKind.Dirt, haloTone);
            BuildStainLayer(root.transform, "Core", coreRadius, SiltSegments, seed + 11f, StainLayerGap,
                SurfaceKind.Dirt, Silt * toneScale);

            return root;
        }

        /// <summary>A standing-water pool (MV-784, change 4) — a dark, cool <see cref="StandingWater"/>
        /// core ringed by a <see cref="WaterMeniscusWidth"/> meniscus at roughly
        /// <see cref="WaterMeniscusLumaScale"/>x the floor's luminance, built the same "wider layer under
        /// a denser one" way <see cref="BuildSiltStain"/> is — except (MV-791) the meniscus sits ABOVE
        /// the pool by its own <see cref="WaterMeniscusLift"/>, not below it, since a meniscus is the
        /// rim curling up at the water's edge.</summary>
        public static GameObject BuildWaterStain(Transform parent, Vector3 worldCenter, float coreRadius,
                                                 float seed, float floorTopY = 0f, float toneScale = 1f)
        {
            var root = new GameObject("Standing Water");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(worldCenter.x, floorTopY + StainLift, worldCenter.z);

            // MV-799: same lit-ground multiplier as the bay underneath (1 = unlit, the pre-MV-799
            // behaviour).
            Color meniscusTone = GroundBase * WaterMeniscusLumaScale * toneScale;
            BuildStainLayer(root.transform, "Meniscus", coreRadius + WaterMeniscusWidth, WaterSegments, seed,
                WaterMeniscusLift, SurfaceKind.Prop, meniscusTone);
            GameObject core = BuildStainLayer(root.transform, "Water", coreRadius, WaterSegments, seed + 13f,
                0f, SurfaceKind.Prop, StandingWater * toneScale);

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
        /// bright lip per bank, a flow surface reading as ten bands and twelve chevron pairs scrolling
        /// downstream with nine foam clumps collecting along the banks, and reed clusters along its long
        /// sides. Returns the root GameObject.
        ///
        /// MV-938: the bands/chevrons/foam used to be 43 separately animated Transforms, ticked every
        /// frame by <c>SludgeFlowRig</c> — at World 2's a10 alone, ~2,000 renderers that could never be
        /// mesh-combined (MV-934). <see cref="BuildSludgeFlowSurface"/> now builds that whole pattern as
        /// ONE static quad with the <c>MaxWorlds/SludgeChannelFlow</c> shader reproducing it (same
        /// colours, same counts, same two scroll speeds) entirely on the GPU via <c>_Time.y</c> — no
        /// driver script, no distance gate, nothing left for either to gate.
        ///
        /// The sludge is the world's one saturated colour. In the key art it is the thing the eye lands
        /// on first, and the thing that tells the player which way is downstream — that is what the
        /// chevrons and bands are for, and why this is dressing and not decoration.
        /// </summary>
        public static GameObject DressSludgeTile(Transform parent, Vector3 center, float width, float depth,
                                                  Vector3 flowDirection, int seed, bool isChannel = false)
        {
            var root = new GameObject("Sludge Dressing");
            root.transform.SetParent(parent, false);
            root.transform.position = center;

            Vector3 flow = flowDirection.sqrMagnitude < 0.001f ? Vector3.forward : flowDirection.normalized;
            flow.y = 0f;
            flow.Normalize();
            Vector3 across = new Vector3(flow.z, 0f, -flow.x);
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
            // when built back-to-back with consecutive seeds. Two independently-salted phases — bands/
            // chevrons (fast) and foam (slow) — so the two layers never lock in step, the same reasoning
            // the old per-piece phases used.
            float fastPhase = Frac(center.x * 0.4127f + center.z * 0.6180339887f) * run;
            float slowPhase = Frac(center.x * 0.8123f + center.z * 0.2718281f + 11.3f) * run;

            // The bands/chevrons/foam fill, as ONE static mesh + shader (MV-938) — see
            // BuildSludgeFlowSurface's own doc for why this replaces the old 43-Transform build.
            BuildSludgeFlowSurface(root.transform, flow, across, run, span, fastPhase, slowPhase);

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

            // MV-801: the trough is cut and dressed at the tile's own pre-ticket floor level — it must
            // NOT ride down with the ooze surface below, or the trough floor and the ooze would end up
            // at the same depth. Built as a CHILD of `root` (MV-755's own per-tile chevron check walks
            // "Sludge"'s direct children expecting each one to be a whole dressed tile, so a sibling
            // object here would read as a tile with no chevron) with a local Y offset that cancels the
            // shift `root` itself is about to take below — its resolved WORLD position ends up exactly
            // where it would have been had `root` never moved.
            if (isChannel)
            {
                BuildChannelTrough(root.transform, width, depth, flow, seed);
                root.transform.position += Vector3.down * ChannelOozeDrop;
            }

            return root;
        }

        // ---------------------------------------------------------------- sludge flow surface (MV-938)

        /// <summary>Builds the bands/chevrons/foam fill as ONE static quad, wired to the
        /// <c>MaxWorlds/SludgeChannelFlow</c> shader (see that shader's own header) — the replacement for
        /// the old per-piece build's ten band, twenty-four chevron-leg and nine foam Transforms, all
        /// ticked every frame by the now-removed <c>SludgeFlowRig</c>. The quad's own vertices are built
        /// directly from <paramref name="flow"/>/<paramref name="across"/> rather than a rotated
        /// Transform (the same convention the old per-piece build used for its own local offsets), and
        /// its UV0 carries the tile-local coordinate in METRES — the shader reads that directly against
        /// <c>_Run</c>/<c>_BandCount</c>/etc, so no separate tiling-scale property is needed.</summary>
        private static void BuildSludgeFlowSurface(Transform parent, Vector3 flow, Vector3 across,
                                                    float run, float span, float fastPhase, float slowPhase)
        {
            var go = new GameObject("Flow");
            go.transform.SetParent(parent, false);

            go.AddComponent<MeshFilter>().sharedMesh = BuildSludgeFlowQuad(flow, across, run, span);
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SludgeFlowMaterial(run, span, fastPhase, slowPhase);

            // Every tile's material is already unique (its own run/span/phase), so it is already the
            // minimum one renderer this fill can ever be — MapStaticBatchRoot.CombineZoneGeometry
            // folding a one-renderer bucket into a "Combined ..." mesh buys nothing (still one draw
            // call) but WOULD relocate it out from under "Stormdrain Dressing" to the map root, which
            // the dressing census tests (MV-755, MV-906) rely on it staying under. This marker keeps it
            // out of that bucketing — see MapRuntime.HasAnimatedAncestor's own doc.
            go.AddComponent<SludgeFlowSurfaceMarker>();
        }

        private static Mesh BuildSludgeFlowQuad(Vector3 flow, Vector3 across, float run, float span)
        {
            float halfRun = run * 0.5f;
            float halfSpan = span * 0.5f;
            Vector3 up = Vector3.up * SludgeBandY;

            var verts = new[]
            {
                -flow * halfRun - across * halfSpan + up,
                 flow * halfRun - across * halfSpan + up,
                 flow * halfRun + across * halfSpan + up,
                -flow * halfRun + across * halfSpan + up,
            };
            var uvs = new[]
            {
                new Vector2(-halfRun, -halfSpan),
                new Vector2( halfRun, -halfSpan),
                new Vector2( halfRun,  halfSpan),
                new Vector2(-halfRun,  halfSpan),
            };
            var normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            var tris = new[] { 0, 1, 2, 0, 2, 3 };

            var mesh = new Mesh { name = "SludgeFlowQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Name of the hand-written sludge-flow shader (MV-938) — <c>Shader.Find</c>-only, so it
        /// lives in Always Included Shaders or the build strips it, same contract as every other
        /// hand-written shader <see cref="MaterialLibrary"/> resolves this way.</summary>
        public const string SludgeFlowShaderName = "MaxWorlds/SludgeChannelFlow";

        private static Shader _sludgeFlowShader;
        private static bool _sludgeFlowShaderResolved;

        /// <summary>A fresh material instance per tile — <paramref name="run"/>/<paramref name="span"/>/
        /// phase all vary per tile, so one shared instance can't serve every tile the way
        /// <see cref="Unlit"/>'s per-colour cache does (the same reason World 2's base sludge slab
        /// already takes a per-tile <see cref="MaterialLibrary.Tinted"/> instance rather than a shared
        /// one). Every instance shares the same shader, so the SRP Batcher still batches them — this is
        /// exactly its target case, distinct from static/dynamic batching's "identical material" need.
        /// Degrades to a flat unlit tile rather than magenta if the shader isn't in the build (the same
        /// "look regression, never a broken one" contract <see cref="MaterialLibrary.Build"/> keeps).</summary>
        private static Material SludgeFlowMaterial(float run, float span, float fastPhase, float slowPhase)
        {
            if (!_sludgeFlowShaderResolved)
            {
                _sludgeFlowShaderResolved = true;
                Shader sh = Shader.Find(SludgeFlowShaderName);
                _sludgeFlowShader = sh != null && sh.isSupported ? sh : null;
                if (_sludgeFlowShader == null)
                    Debug.LogWarning($"[StormdrainKit] '{SludgeFlowShaderName}' unavailable; " +
                                      "sludge flow falls back to a flat unlit tile (no bands/chevrons/foam).");
            }

            if (_sludgeFlowShader == null) return Unlit(Sludge, "SludgeFlowFallback");

            var m = new Material(_sludgeFlowShader)
            {
                name = "Stormdrain_SludgeFlow",
                hideFlags = HideFlags.HideAndDontSave,
            };
            m.SetColor("_BaseColor", Sludge);
            m.SetColor("_BandColor", SludgeBand);
            m.SetColor("_ChevronColor", SludgeChevronMid);
            m.SetColor("_FoamColor", SludgeBright);
            m.SetFloat("_Run", run);
            m.SetFloat("_Span", span);
            m.SetFloat("_BandCount", SludgeBandCount);
            m.SetFloat("_ChevronPairs", SludgeChevronPairs);
            m.SetFloat("_FoamCount", SludgeFoamCount);
            m.SetFloat("_FastSpeed", SludgeFlowSpeed);
            m.SetFloat("_SlowSpeed", SludgeFoamSpeed);
            m.SetFloat("_FastPhase", fastPhase);
            m.SetFloat("_SlowPhase", slowPhase);
            return m;
        }

        // ---------------------------------------------------------------- channel trough (MV-801)

        /// <summary>The trough a channel-eligible sludge rect gets (MV-801, change 2, 4 and 5): a sunk
        /// floor slab, two banks (a wall down to it capped by a dark lip just under the old floor line),
        /// a kerb outboard of each lip, and grate-plank crossings along the dry route across it. Parented
        /// under the tile's own <paramref name="root"/> at local Y = <see cref="ChannelOozeDrop"/> — the
        /// caller drops <paramref name="root"/> by that same amount immediately after this call returns,
        /// so this structure's resolved WORLD position lands back on the tile's pre-ticket floor datum,
        /// never offset by the ooze surface built above it in <see cref="DressSludgeTile"/>.</summary>
        private static void BuildChannelTrough(Transform root, float width, float depth, Vector3 flow, int seed)
        {
            bool alongZ = Mathf.Abs(Vector3.Dot(flow, Vector3.forward)) > 0.5f;

            // MV-906: the hazard band's own run is the channel's LONG axis (width when not alongZ, depth
            // when alongZ) — the same "run" BuildChannelCrossings already derives below.
            float run = alongZ ? depth : width;
            bool longChannel = run > LongChannelRunLength;

            var trough = new GameObject("Channel Trough").transform;
            trough.SetParent(root, false);
            trough.localPosition = Vector3.up * ChannelOozeDrop;
            root = trough;

            Box(root, "Trough Floor", Vector3.up * (-ChannelTroughDepth - 0.05f),
                new Vector3(width, 0.10f, depth), GroundDry);

            float wallY = -ChannelTroughDepth * 0.5f;
            float lipY = -ChannelLipHeight * 0.5f;
            float kerbY = ChannelKerbHeight * 0.5f;

            if (alongZ)
            {
                Box(root, "Trough Wall A", new Vector3(width * 0.5f, wallY, 0f),
                    new Vector3(ChannelWallThickness, ChannelTroughDepth, depth), GroundDry);
                Box(root, "Trough Wall B", new Vector3(-width * 0.5f, wallY, 0f),
                    new Vector3(ChannelWallThickness, ChannelTroughDepth, depth), GroundDry);

                // MV-803: hazard banding on both drop edges, flush with the cut at the top of each wall
                // (the lip's own line) — the run is along Z here, so alongX is false.
                Vector3 edgeA = new Vector3(width * 0.5f, -ChannelHazardBandHeight * 0.5f, 0f);
                Vector3 edgeB = new Vector3(-width * 0.5f, -ChannelHazardBandHeight * 0.5f, 0f);
                if (longChannel)
                {
                    BuildLongChannelHazardBanding(root, edgeA, depth, ChannelHazardBandHeight, alongX: false, ChannelWallThickness);
                    BuildLongChannelHazardBanding(root, edgeB, depth, ChannelHazardBandHeight, alongX: false, ChannelWallThickness);
                }
                else
                {
                    BuildHazardBanding(root, edgeA, depth, ChannelHazardBandHeight, alongX: false, ChannelWallThickness);
                    BuildHazardBanding(root, edgeB, depth, ChannelHazardBandHeight, alongX: false, ChannelWallThickness);
                }

                Box(root, "Trough Lip A", new Vector3(width * 0.5f, lipY, 0f),
                    new Vector3(ChannelWallThickness * 1.3f, ChannelLipHeight, depth), Soffit);
                Box(root, "Trough Lip B", new Vector3(-width * 0.5f, lipY, 0f),
                    new Vector3(ChannelWallThickness * 1.3f, ChannelLipHeight, depth), Soffit);

                Box(root, "Channel Kerb A", new Vector3(width * 0.5f + ChannelKerbWidth * 0.5f, kerbY, 0f),
                    new Vector3(ChannelKerbWidth, ChannelKerbHeight, depth), KerbConcrete);
                Box(root, "Channel Kerb B", new Vector3(-width * 0.5f - ChannelKerbWidth * 0.5f, kerbY, 0f),
                    new Vector3(ChannelKerbWidth, ChannelKerbHeight, depth), KerbConcrete);
            }
            else
            {
                Box(root, "Trough Wall A", new Vector3(0f, wallY, depth * 0.5f),
                    new Vector3(width, ChannelTroughDepth, ChannelWallThickness), GroundDry);
                Box(root, "Trough Wall B", new Vector3(0f, wallY, -depth * 0.5f),
                    new Vector3(width, ChannelTroughDepth, ChannelWallThickness), GroundDry);

                // MV-803: hazard banding on both drop edges — the run is along X here.
                Vector3 edgeA = new Vector3(0f, -ChannelHazardBandHeight * 0.5f, depth * 0.5f);
                Vector3 edgeB = new Vector3(0f, -ChannelHazardBandHeight * 0.5f, -depth * 0.5f);
                if (longChannel)
                {
                    BuildLongChannelHazardBanding(root, edgeA, width, ChannelHazardBandHeight, alongX: true, ChannelWallThickness);
                    BuildLongChannelHazardBanding(root, edgeB, width, ChannelHazardBandHeight, alongX: true, ChannelWallThickness);
                }
                else
                {
                    BuildHazardBanding(root, edgeA, width, ChannelHazardBandHeight, alongX: true, ChannelWallThickness);
                    BuildHazardBanding(root, edgeB, width, ChannelHazardBandHeight, alongX: true, ChannelWallThickness);
                }

                Box(root, "Trough Lip A", new Vector3(0f, lipY, depth * 0.5f),
                    new Vector3(width, ChannelLipHeight, ChannelWallThickness * 1.3f), Soffit);
                Box(root, "Trough Lip B", new Vector3(0f, lipY, -depth * 0.5f),
                    new Vector3(width, ChannelLipHeight, ChannelWallThickness * 1.3f), Soffit);

                Box(root, "Channel Kerb A", new Vector3(0f, kerbY, depth * 0.5f + ChannelKerbWidth * 0.5f),
                    new Vector3(width, ChannelKerbHeight, ChannelKerbWidth), KerbConcrete);
                Box(root, "Channel Kerb B", new Vector3(0f, kerbY, -depth * 0.5f - ChannelKerbWidth * 0.5f),
                    new Vector3(width, ChannelKerbHeight, ChannelKerbWidth), KerbConcrete);
            }

            BuildChannelCrossings(root, width, depth, alongZ, seed);
        }

        /// <summary>Grate-plank crossings (MV-801, change 5) — every
        /// <see cref="ChannelCrossingSpacingMin"/>-<see cref="ChannelCrossingSpacingMax"/> m along the
        /// channel, and at least one however short the channel is (a floor of 1, same idiom
        /// <see cref="DressWallFace"/>'s own bulkhead-lamp count already uses). Each plank is
        /// <see cref="ChannelCrossingWidth"/> wide along the direction of travel, spans the channel's
        /// full width plus <see cref="ChannelCrossingOverhang"/> each side, slatted crosswise at
        /// <see cref="ChannelCrossingSlatPitch"/>, with a rust handrail post at each of its four
        /// corners. Dressing only — no collider, same contract every other piece in this kit keeps, so
        /// this never changes where anything can walk.</summary>
        private static void BuildChannelCrossings(Transform root, float width, float depth, bool alongZ, int seed)
        {
            float run = alongZ ? depth : width;
            float span = alongZ ? width : depth;
            float crossSpan = span + ChannelCrossingOverhang * 2f;

            // MV-906: same long-channel widening BuildChannelTrough applies to its own hazard banding —
            // a16's own 106 m channel otherwise gets a crossing (8 renderers each) every 8-12 m end to end.
            float spacing = Mathf.Lerp(ChannelCrossingSpacingMin, ChannelCrossingSpacingMax,
                Frac(seed * 0.8123f)) * LongChannelScale(run);
            int count = Mathf.Max(1, Mathf.FloorToInt(run / spacing));

            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                float along = (t - 0.5f) * run;

                var plank = new GameObject($"Crossing{i}").transform;
                plank.SetParent(root, false);
                plank.localPosition = alongZ ? new Vector3(0f, 0f, along) : new Vector3(along, 0f, 0f);

                int slats = Mathf.Max(1, Mathf.RoundToInt(ChannelCrossingWidth / ChannelCrossingSlatPitch));
                for (int s = 0; s < slats; s++)
                {
                    float slatOffset = ((s + 0.5f) / slats - 0.5f) * ChannelCrossingWidth;
                    Vector3 slatLocal = alongZ ? new Vector3(0f, 0f, slatOffset) : new Vector3(slatOffset, 0f, 0f);
                    Vector3 slatSize = alongZ
                        ? new Vector3(crossSpan, 0.05f, ChannelCrossingSlatPitch * 0.8f)
                        : new Vector3(ChannelCrossingSlatPitch * 0.8f, 0.05f, crossSpan);
                    Box(plank, $"Slat{s}", slatLocal, slatSize, RustDark, SurfaceKind.Metal);
                }

                for (int corner = 0; corner < 4; corner++)
                {
                    float alongSign = corner < 2 ? -1f : 1f;
                    float crossSign = corner % 2 == 0 ? -1f : 1f;
                    float postAlong = alongSign * ChannelCrossingWidth * 0.5f;
                    float postCross = crossSign * crossSpan * 0.5f;
                    Vector3 postLocal = alongZ
                        ? new Vector3(postCross, ChannelCrossingPostHeight * 0.5f, postAlong)
                        : new Vector3(postAlong, ChannelCrossingPostHeight * 0.5f, postCross);
                    Box(plank, $"Post{corner}", postLocal,
                        new Vector3(0.08f, ChannelCrossingPostHeight, 0.08f), Rust, SurfaceKind.Metal);
                }
            }
        }

        // ---------------------------------------------------------------- hazard banding (MV-803)

        /// <summary>MV-803, "Stormdrain Pass 4" review, approved by Lee 2026-09-15 ("the diagonal
        /// striped outlines should be bright enough to lift the overall design"). A backing plate in
        /// <see cref="HazardStripeBacking"/> plus a run of diagonal yellow stripes painted flat on its
        /// face — every <see cref="HazardStripePitch"/> along the run, leaning
        /// <see cref="HazardStripeLeanDeg"/> off the run's own axis, at <see cref="HazardStripeEmissive"/>
        /// so it holds up unlit. Stripes are proud of the backing face by <see cref="HazardStripeProud"/>
        /// (a hair, on both faces at once) rather than standing off it — proud geometry read as teeth in
        /// review and was rejected.
        ///
        /// This is HAZARD marking, not decoration (the ticket's own rule) — every call site anchors it
        /// to something that can hurt you or that you must act on: a channel drop edge, a gate jamb, a
        /// Replicator housing, a pump intake, or a junction box base. Nothing else in World 2 gets it.
        ///
        /// <paramref name="centre"/> is the band's own centre in the parent's local space;
        /// <paramref name="length"/> runs along local X when <paramref name="alongX"/>, local Z
        /// otherwise — the same convention <see cref="BuildWallPanels"/> already uses. A stripe whose
        /// own footprint would poke past either end of the plate is dropped rather than drawn
        /// overhanging ("clipped at both ends", the ticket's own words).</summary>
        public static GameObject BuildHazardBanding(Transform parent, Vector3 centre, float length, float height,
                                                     bool alongX, float depth)
        {
            var root = new GameObject("Hazard Banding");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = centre;

            Vector3 plateSize = alongX
                ? new Vector3(length, height, depth)
                : new Vector3(depth, height, length);
            Box(root.transform, "Plate", Vector3.zero, plateSize, HazardStripeBacking, SurfaceKind.Metal);

            if (length < HazardStripePitch * 0.5f) return root;   // too short for even one stripe

            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            float stripeDepth = depth + HazardStripeProud * 2f;

            // The stripe's own long-axis length before the lean: long enough that, once tilted
            // HazardStripeLeanDeg off horizontal, its vertical (Y) span still reaches the band's full
            // height. Sin, not cos: the long axis starts along the RUN axis (0 degrees off it) and
            // leans toward vertical, so it is the SINE of the lean angle that recovers the height.
            float leanLength = height / Mathf.Sin(HazardStripeLeanDeg * Mathf.Deg2Rad);

            // The stripe's own footprint along the run axis, after the lean — what "clipped at both
            // ends" is measured against, so an end stripe that would poke past the plate is dropped
            // rather than drawn overhanging it.
            float footprint = leanLength * Mathf.Cos(HazardStripeLeanDeg * Mathf.Deg2Rad)
                             + HazardStripeThickness * Mathf.Sin(HazardStripeLeanDeg * Mathf.Deg2Rad);

            // Exact pitch, centred on the plate — never stretched to fill the run (a stretch-to-fit
            // spacing, as the kerb strip's own segments use, drifts arbitrarily far from the ticket's
            // approved 0.42 m on a short run; a fixed pitch holds it exactly regardless of length).
            int slots = Mathf.Max(1, Mathf.FloorToInt(length / HazardStripePitch));
            float firstOffset = -(slots - 1) * 0.5f * HazardStripePitch;
            for (int i = 0; i < slots; i++)
            {
                float offset = firstOffset + i * HazardStripePitch;
                if (offset - footprint * 0.5f < -length * 0.5f || offset + footprint * 0.5f > length * 0.5f)
                    continue;   // clipped at the plate's own end

                Vector3 stripeSize = alongX
                    ? new Vector3(leanLength, HazardStripeThickness, stripeDepth)
                    : new Vector3(stripeDepth, HazardStripeThickness, leanLength);
                Quaternion stripeRot = alongX
                    ? Quaternion.Euler(0f, 0f, HazardStripeLeanDeg)
                    : Quaternion.Euler(HazardStripeLeanDeg, 0f, 0f);

                GameObject stripe = BevelledPart(root.transform, $"Stripe{i}", along * offset, stripeSize,
                    HazardStripeMaterial());
                stripe.transform.localRotation = stripeRot;
            }

            return root;
        }

        private static Material _hazardStripeMaterial;

        /// <summary>The banding's own yellow, cached once — a real lit material (not
        /// <see cref="Unlit"/>) carrying an actual <c>_EmissionColor</c>, the same "reads as LIT, not
        /// just coloured" idiom <see cref="WorldMaterials"/>'s own hazard/circuit materials use, since a
        /// stripe has to hold up inside an unlit bay on its own emission rather than a boosted albedo.
        /// </summary>
        private static Material HazardStripeMaterial()
        {
            if (_hazardStripeMaterial != null) return _hazardStripeMaterial;

            Shader shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = "Stormdrain_HazardStripe",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true, // MV-882: shared across every hazard band/parapet in a level
            };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", HazardStripeColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", HazardStripeColor);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", HazardStripeColor * HazardStripeEmissive);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            _hazardStripeMaterial = m;
            return m;
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

            var mat = new Material(shader)
            {
                name = $"Stormdrain_{name}",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true, // MV-882: shared per colour across every lamp lens/void it paints
            };
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

            if (_hazardStripeMaterial != null)
            {
                if (Application.isPlaying) Object.Destroy(_hazardStripeMaterial);
                else Object.DestroyImmediate(_hazardStripeMaterial);
                _hazardStripeMaterial = null;
            }

            StormdrainLightKit.ClearCache();
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
