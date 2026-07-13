using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Dressing
{
    /// <summary>Why a prop is allowed to stand where it stands. Everything else is validated.</summary>
    public enum DressZone
    {
        /// <summary>Loose in the yard: it must be walk-over low, or clear of the walkable interior.</summary>
        Yard,
        /// <summary>Standing on one of the arena's cover blocks (YT-68) — the collider is already there.</summary>
        Cover,
        /// <summary>Part of the shed, on top of / flush against the Mower Hutch's own collider.</summary>
        Shed,
    }

    /// <summary>
    /// One placed piece of set-dressing (YT-75). Pure data — no GameObject, no Unity scene.
    ///
    /// <see cref="Position"/> is the XZ centre and, unless <see cref="AnchorCentre"/>, the BOTTOM in
    /// Y: a prop is authored where it touches the ground, so it can't be authored floating or
    /// half-buried whatever the kit's own pivot happens to be. <see cref="Size"/> is the world size
    /// we want the prop to end up, not a scale factor — the kit is authored at some arbitrary unit
    /// scale and we refuse to care what it is (see <see cref="GardenKit"/>).
    /// </summary>
    public readonly struct DressProp
    {
        public readonly string Model;      // kit model key; null = a plain timber box (the shed)
        public readonly string Paint;      // kit material name — boxes only; models bring their own
        public readonly Vector3 Position;
        public readonly Vector3 Size;      // world target size; 0 on an axis = keep the model's aspect
        public readonly Vector3 Euler;
        public readonly DressZone Zone;
        public readonly bool Sways;        // foliage moves in the wind; timber and stone do not
        public readonly bool AnchorCentre; // Y is the centre, not the base (the shed's pitched roof)

        public DressProp(string model, Vector3 position, Vector3 size, float yaw = 0f,
                         DressZone zone = DressZone.Yard, bool sways = false,
                         string paint = null, Vector3 euler = default, bool anchorCentre = false)
        {
            Model = model;
            Paint = paint;
            Position = position;
            Size = size;
            Euler = euler == default ? new Vector3(0f, yaw, 0f) : euler;
            Zone = zone;
            Sways = sways;
            AnchorCentre = anchorCentre;
        }

        /// <summary>Conservative XZ footprint: the axis-aligned box around the rotated size.</summary>
        public Rect Footprint
        {
            get
            {
                float rad = Euler.y * Mathf.Deg2Rad;
                float c = Mathf.Abs(Mathf.Cos(rad)), s = Mathf.Abs(Mathf.Sin(rad));
                float w = Size.x * c + Size.z * s;
                float d = Size.x * s + Size.z * c;
                return new Rect(Position.x - w * 0.5f, Position.z - d * 0.5f, w, d);
            }
        }

        /// <summary>Lowest point the prop reaches. The pitched roof is rolled, so its height is not
        /// simply Size.y — and "is this above head height" is the question the roof has to pass.</summary>
        public float MinY
        {
            get
            {
                if (!AnchorCentre) return Position.y;
                float roll = Euler.z * Mathf.Deg2Rad;
                float half = 0.5f * (Mathf.Abs(Size.y * Mathf.Cos(roll)) + Mathf.Abs(Size.x * Mathf.Sin(roll)));
                return Position.y - half;
            }
        }

        public float Height => Size.y;
    }

    /// <summary>The kit models the yard is dressed from. Keys are file names under
    /// Resources/GardenKit (Kenney Nature Kit, CC0 — see Art/Kits/KenneyNature/LICENSE.txt).</summary>
    public static class KitModels
    {
        public const string FencePanel = "fence_planks";
        public const string FenceGate = "fence_gate";
        public const string TreeTall = "tree_default";
        public const string TreeOak = "tree_oak";
        public const string TreeSmall = "tree_small";
        public const string Pot = "pot_large";
        public const string PotSmall = "pot_small";
        public const string BedRow = "crops_dirtRow";
        public const string BedEnd = "crops_dirtRowEnd";
        public const string BedPatch = "crops_dirtSingle";
        public const string Stone = "path_stone";
        public const string StoneRound = "path_stoneCircle";
        public const string StoneEnd = "path_stoneEnd";
        public const string Sign = "sign";
        public const string LogStack = "log_stack";
        public const string Log = "log";
        public const string Stump = "stump_round";

        public static readonly string[] Bushes =
            { "plant_bush", "plant_bushDetailed", "plant_bushLarge", "plant_bushSmall" };

        public static readonly string[] Flowers =
        {
            "flower_redA", "flower_redB", "flower_yellowA", "flower_yellowB",
            "flower_purpleA", "flower_purpleB",
        };

        public static readonly string[] Tufts = { "grass", "grass_large", "grass_leafs" };
        public static readonly string[] Rocks = { "rock_smallA", "rock_smallB", "rock_smallC", "stone_smallA" };
    }

    /// <summary>
    /// Where every piece of set-dressing goes (YT-75), as pure maths off the same
    /// <see cref="BackyardPathLayout"/> that builds the arena — so the yard re-dresses itself when
    /// the arena is reshaped, and none of it is authored by hand in a scene file.
    ///
    /// The one rule that matters is that dressing must never take the fight away from the player:
    /// a hundred pretty props are worth nothing if one of them stands in the doorway. So the plan is
    /// checked, not eyeballed. <see cref="Validate"/> holds every prop to it, the EditMode tests run
    /// it, and <see cref="BackyardDressing"/> refuses to build a plan that fails — the same contract
    /// <see cref="BackyardCover"/> already uses for the cover blocks.
    ///
    /// Nothing here carries a collider (see <see cref="GardenKit"/>), so the geometric rules below
    /// are about what the yard LOOKS like — no robot walking waist-deep through a bush, no fence
    /// panel across the mission line — not about what it collides with. Movement is untouched by
    /// construction, which is why this stream can dress the arena without touching gameplay.
    /// </summary>
    public static class DressingPlan
    {
        /// <summary>Short enough to walk over without noticing. A prop this size can stand anywhere,
        /// including the middle of the fight.</summary>
        public const float LowPropMax = 0.45f;

        /// <summary>How far in from a wall the border planting is allowed to reach. Beyond this the
        /// lawn belongs to the fight. Wide enough for a shrub against the fence, narrow enough that
        /// a robot chasing Max down the wall still has a lane that isn't a hedge.</summary>
        public const float BorderBand = 1.9f;

        /// <summary>Clear height a prop must hang above to be allowed over the play space — the
        /// shed's roof passes, and nothing else even tries.</summary>
        public const float Headroom = 2f;

        /// <summary>The width the fence tiles at, before it's evened out to fill a run exactly.</summary>
        public const float FencePanelWidth = 2f;

        public const float FenceThickness = 0.16f;

        /// <summary>Fence sits just proud of the wall it hides, or the two z-fight.</summary>
        public const float FenceInset = 0.08f;

        /// <summary>The Mower Hutch's own box (3 × 3 at the shed), plus the shed posts either side.
        /// Everything the shed dressing adds lives inside this, which is already solid.</summary>
        public const float ShedHalfWidth = 3f;
        public const float ShedHalfDepth = 2.4f;

        /// <summary>Everything the player and the robots actually move through: the three rooms,
        /// inset by the border band, plus the two doorways between them (which the insets would
        /// otherwise leave looking like fair game for a shrub).</summary>
        public static Rect[] WalkableInterior(in BackyardPathLayout l)
        {
            float b = BorderBand;
            return new[]
            {
                Inset(-l.PatioHalfWidth, l.StartZ, l.PatioHalfWidth, l.LawnStartZ, b),
                Inset(-l.LawnHalfWidth, l.LawnStartZ, l.LawnHalfWidth, l.GateZ, b),
                Inset(-l.ArenaHalfWidth, l.GateZ, l.ArenaHalfWidth, l.ArenaEndZ, b),

                // The patio's mouth and the boss gate: narrow, and the only way through. Inset by
                // the fence that lines them, which is part of the wall, not something in the gap.
                FromTo(-l.PatioHalfWidth + 0.4f, l.LawnStartZ - b, l.PatioHalfWidth - 0.4f, l.LawnStartZ + b),
                FromTo(-l.GateHalfWidth + 0.1f, l.GateZ - b, l.GateHalfWidth - 0.1f, l.GateZ + b),
            };
        }

        /// <summary>The whole set, deterministic for a seed.</summary>
        public static List<DressProp> Build(in BackyardPathLayout l, float shedZ,
                                            IReadOnlyList<ArenaCover> cover, int seed = 75)
        {
            var props = new List<DressProp>(256);
            var rng = new Rng((uint)seed);

            Fence(props, l);
            Path(props, l, ref rng);
            Patio(props, l, ref rng);
            Borders(props, l, ref rng);
            Beds(props, l, ref rng);
            Trees(props, l, ref rng);
            Shed(props, shedZ);
            CoverDressing(props, cover, ref rng);
            Tufts(props, l, shedZ, cover, ref rng);

            return props;
        }

        // --- the fence line -------------------------------------------------------------------

        /// <summary>A timber fence on the inside face of every wall, so the yard is bounded by a
        /// fence rather than by a grey box. Panels are sized to fill each run exactly — a run that
        /// ends in a half-panel or a gap is the one thing that reads as "generated".</summary>
        private static void Fence(List<DressProp> props, in BackyardPathLayout l)
        {
            float patio = l.PatioHalfWidth, lawn = l.LawnHalfWidth;
            float gate = l.GateHalfWidth, arena = l.ArenaHalfWidth;
            float i = FenceInset;

            // The side walls are built OUTSIDE the room they bound, so their inner face is the room
            // edge and the fence goes an inset in from it. The two shoulder walls are built ACROSS
            // the boundary instead, centred on it — so their faces are half a wall away, and a fence
            // on the boundary line would be buried inside the wall it's supposed to be hiding.
            float shoulder = l.WallThickness * 0.5f + i;

            // Patio: sides and the back wall Max came in through — with the gate he came through.
            Run(props, new Vector2(-patio + i, l.StartZ), new Vector2(-patio + i, l.LawnStartZ), 90f);
            Run(props, new Vector2(patio - i, l.StartZ), new Vector2(patio - i, l.LawnStartZ), 270f);
            Run(props, new Vector2(-patio, l.StartZ + i), new Vector2(patio, l.StartZ + i), 0f, gateAtCentre: true);

            // The shoulders where the patio opens out into the lawn.
            Run(props, new Vector2(-lawn, l.LawnStartZ + shoulder), new Vector2(-patio, l.LawnStartZ + shoulder), 0f);
            Run(props, new Vector2(patio, l.LawnStartZ + shoulder), new Vector2(lawn, l.LawnStartZ + shoulder), 0f);

            // The lawn — the long sides of the yard.
            Run(props, new Vector2(-lawn + i, l.LawnStartZ), new Vector2(-lawn + i, l.GateZ), 90f);
            Run(props, new Vector2(lawn - i, l.LawnStartZ), new Vector2(lawn - i, l.GateZ), 270f);

            // The gate wall, from both sides. The doorway itself (|x| < GateHalfWidth) stays open.
            Run(props, new Vector2(-lawn, l.GateZ - shoulder), new Vector2(-gate, l.GateZ - shoulder), 180f);
            Run(props, new Vector2(gate, l.GateZ - shoulder), new Vector2(lawn, l.GateZ - shoulder), 180f);
            Run(props, new Vector2(-arena, l.GateZ + shoulder), new Vector2(-gate, l.GateZ + shoulder), 0f);
            Run(props, new Vector2(gate, l.GateZ + shoulder), new Vector2(arena, l.GateZ + shoulder), 0f);

            // The boss arena.
            Run(props, new Vector2(-arena + i, l.GateZ), new Vector2(-arena + i, l.ArenaEndZ), 90f);
            Run(props, new Vector2(arena - i, l.GateZ), new Vector2(arena - i, l.ArenaEndZ), 270f);
            Run(props, new Vector2(-arena, l.ArenaEndZ - i), new Vector2(arena, l.ArenaEndZ - i), 180f);
        }

        private static void Run(List<DressProp> props, Vector2 a, Vector2 b, float yaw,
                                bool gateAtCentre = false)
        {
            float length = Vector2.Distance(a, b);
            if (length < 0.5f) return;

            int n = Mathf.Max(1, Mathf.RoundToInt(length / FencePanelWidth));
            if (gateAtCentre && n % 2 == 0) n++;          // an odd count puts a panel dead centre

            float w = length / n;
            Vector2 dir = (b - a) / length;
            int middle = n / 2;

            for (int k = 0; k < n; k++)
            {
                Vector2 p = a + dir * (w * (k + 0.5f));
                bool isGate = gateAtCentre && k == middle;
                props.Add(new DressProp(
                    isGate ? KitModels.FenceGate : KitModels.FencePanel,
                    new Vector3(p.x, 0f, p.y),
                    new Vector3(w, 3.4f, FenceThickness),
                    yaw));
            }
        }

        // --- the ground the player walks in on -------------------------------------------------

        /// <summary>Stepping stones up the patio and out into the lawn: the critical path, said in
        /// set-dressing instead of in a HUD arrow. They stop where the fight starts.</summary>
        private static void Path(List<DressProp> props, in BackyardPathLayout l, ref Rng rng)
        {
            for (float z = l.StartZ + 1.4f; z < l.LawnStartZ + 4f; z += 1.5f)
            {
                bool fading = z > l.LawnStartZ;              // the last few, swallowed by the grass
                string model = fading ? KitModels.StoneEnd
                             : rng.Next() < 0.3f ? KitModels.StoneRound : KitModels.Stone;

                props.Add(new DressProp(
                    model,
                    new Vector3(rng.Range(-0.45f, 0.45f), 0.01f, z),
                    new Vector3(fading ? 0.9f : 1.5f, 0.08f, fading ? 0.8f : 1.1f),
                    rng.Range(-12f, 12f)));
            }
        }

        private static void Patio(List<DressProp> props, in BackyardPathLayout l, ref Rng rng)
        {
            float x = l.PatioHalfWidth - 0.85f;

            // A planted pot either side of the back gate, so the entry reads as somebody's yard.
            foreach (float side in new[] { -1f, 1f })
            {
                var at = new Vector3(side * x, 0f, l.StartZ + 1.6f);
                props.Add(new DressProp(KitModels.Pot, at, new Vector3(1.1f, 0.5f, 1.1f),
                                        rng.Range(0f, 360f)));
                props.Add(new DressProp(rng.Pick(KitModels.Bushes),
                                        at + new Vector3(0f, 0.48f, 0f),
                                        new Vector3(0.9f, 0.75f, 0.9f), rng.Range(0f, 360f),
                                        sways: true));
            }

            props.Add(new DressProp(KitModels.Sign, new Vector3(-x, 0f, l.StartZ + 4.2f),
                                    new Vector3(0.8f, 1.1f, 0.12f), 205f));
            props.Add(new DressProp(KitModels.LogStack, new Vector3(x, 0f, l.StartZ + 4.6f),
                                    new Vector3(1.2f, 1f, 2f), 0f));
            // Kept back from the mouth of the patio: that gap is the way out, and the way out is
            // the one place in the yard nothing gets to stand.
            props.Add(new DressProp(KitModels.Stump, new Vector3(-x + 0.2f, 0f, l.LawnStartZ - 3.2f),
                                    new Vector3(0.8f, 0.55f, 0.9f), rng.Range(0f, 360f)));
            props.Add(new DressProp(KitModels.PotSmall, new Vector3(x - 0.1f, 0f, l.LawnStartZ - 3.5f),
                                    new Vector3(0.7f, 0.6f, 0.65f), rng.Range(0f, 360f)));
        }

        // --- planting -------------------------------------------------------------------------

        /// <summary>Planting hugging the fence, all the way round: the yard is full at the edges and
        /// empty where the fight is, which is exactly the shape a fight room wants anyway.</summary>
        private static void Borders(List<DressProp> props, in BackyardPathLayout l, ref Rng rng)
        {
            BorderRun(props, ref rng, l.LawnHalfWidth, l.LawnStartZ + 1.5f, l.GateZ - 1.5f, 2.5f);
            BorderRun(props, ref rng, l.ArenaHalfWidth, l.GateZ + 2f, l.ArenaEndZ - 2f, 3.2f);
        }

        private static void BorderRun(List<DressProp> props, ref Rng rng, float halfWidth,
                                      float zFrom, float zTo, float step)
        {
            for (float z = zFrom; z <= zTo; z += step)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    if (rng.Next() < 0.12f) continue;                 // a gap here and there

                    float x = side * (halfWidth - rng.Range(0.7f, 1f));
                    float zz = z + rng.Range(-0.5f, 0.5f);
                    float roll = rng.Next();

                    if (roll < 0.42f)
                    {
                        // Kept deliberately modest: a shrub against the fence is planting, and a
                        // shrub big enough for a robot to disappear into is a bug report.
                        float h = rng.Range(0.7f, 1.15f);
                        props.Add(new DressProp(rng.Pick(KitModels.Bushes), new Vector3(x, 0f, zz),
                                                new Vector3(h * 1.15f, h, h * 1.15f),
                                                rng.Range(0f, 360f), sways: true));
                    }
                    else if (roll < 0.68f)
                    {
                        Flowers(props, ref rng, new Vector2(x, zz), rng.RangeInt(2, 5), 0.55f);
                    }
                    else if (roll < 0.85f)
                    {
                        float h = rng.Range(0.3f, 0.42f);
                        props.Add(new DressProp(rng.Pick(KitModels.Tufts), new Vector3(x, 0f, zz),
                                                new Vector3(h * 1.6f, h, h * 1.6f),
                                                rng.Range(0f, 360f), sways: true));
                    }
                    else
                    {
                        float h = rng.Range(0.3f, 0.45f);
                        props.Add(new DressProp(rng.Pick(KitModels.Rocks), new Vector3(x, 0f, zz),
                                                new Vector3(h * 1.9f, h, h * 1.9f), rng.Range(0f, 360f)));
                    }
                }
            }
        }

        private static void Flowers(List<DressProp> props, ref Rng rng, Vector2 at, int count,
                                    float spread)
        {
            for (int k = 0; k < count; k++)
            {
                float h = rng.Range(0.32f, 0.44f);
                props.Add(new DressProp(
                    rng.Pick(KitModels.Flowers),
                    new Vector3(at.x + rng.Range(-spread, spread), 0f, at.y + rng.Range(-spread, spread)),
                    new Vector3(h * 0.7f, h, h * 0.7f),
                    rng.Range(0f, 360f), sways: true));
            }
        }

        /// <summary>Two dug flower beds along the lawn fence — the yard has been gardened, which is
        /// the whole reason a robot mower factory in it is funny.</summary>
        private static void Beds(List<DressProp> props, in BackyardPathLayout l, ref Rng rng)
        {
            Bed(props, ref rng, new Vector2(-(l.LawnHalfWidth - 1f), 4.5f));
            Bed(props, ref rng, new Vector2(l.LawnHalfWidth - 1f, 16.5f));
        }

        private static void Bed(List<DressProp> props, ref Rng rng, Vector2 at)
        {
            const float rowLength = 1.7f;
            for (int k = -1; k <= 1; k++)
            {
                float z = at.y + k * rowLength;
                bool end = k != 0;
                props.Add(new DressProp(end ? KitModels.BedEnd : KitModels.BedRow,
                                        new Vector3(at.x, 0.02f, z),
                                        new Vector3(rowLength, 0.12f, 1.5f),
                                        k < 0 ? 270f : 90f));
                Flowers(props, ref rng, new Vector2(at.x, z), 3, 0.55f);
            }
        }

        private static void Trees(List<DressProp> props, in BackyardPathLayout l, ref Rng rng)
        {
            // Behind the fence, where they can be as big as they like: they give the yard a horizon
            // over the fence line and cost nothing to walk around, because you can't walk there.
            Backdrop(props, ref rng, new Vector2(-(l.LawnHalfWidth + 2.2f), 3f), 6.4f, KitModels.TreeTall);
            Backdrop(props, ref rng, new Vector2(l.LawnHalfWidth + 2.3f, 12f), 5.6f, KitModels.TreeOak);
            Backdrop(props, ref rng, new Vector2(-9.5f, l.StartZ + 4f), 5.2f, KitModels.TreeOak);
            Backdrop(props, ref rng, new Vector2(9.8f, l.StartZ + 2.5f), 4.6f, KitModels.TreeSmall);
            Backdrop(props, ref rng, new Vector2(-(l.ArenaHalfWidth + 1.6f), 30f), 6.8f, KitModels.TreeTall);
            Backdrop(props, ref rng, new Vector2(l.ArenaHalfWidth + 1.7f, 37f), 6.2f, KitModels.TreeOak);
        }

        private static void Backdrop(List<DressProp> props, ref Rng rng, Vector2 at, float height,
                                     string model)
        {
            props.Add(new DressProp(model, new Vector3(at.x, 0f, at.y),
                                    new Vector3(height * 0.55f, height, height * 0.55f),
                                    rng.Range(0f, 360f), sways: true));
        }

        /// <summary>
        /// The Mower Hutch becomes a shed: corner posts, and a pitched timber roof over the BACK of
        /// the box — an open-fronted hutch, which is what the robots pour out of anyway (YT-70).
        ///
        /// The open front is not a style choice. The hutch is a target: gameplay tints it to show
        /// damage, and at a 72° camera you read a box mostly by its top. A roof over the whole thing
        /// would look like a shed and play like a factory whose health you can't see. So the roof
        /// takes the back, the front stays bare, and the shed costs the fight nothing.
        ///
        /// Everything here stands on the hutch's own footprint — which is already solid — or above
        /// head height, so none of it takes a step away from anyone.
        /// </summary>
        private static void Shed(List<DressProp> props, float shedZ)
        {
            const float pitch = 28f;
            const float slope = 1.95f;     // a roof, not a pair of wings: barely wider than the box
            const float depth = 2f;        // the back two-thirds of the 3 m hutch, plus a lip
            float roofZ = shedZ + 0.8f;

            // The eave board the roof sits on. It also closes the gap between the flat top of the
            // hutch (y = 2) and the underside of the pitch, which you would otherwise see straight
            // into from a camera looking down at 72°.
            props.Add(new DressProp(
                null, new Vector3(0f, 2f, roofZ), new Vector3(3.2f, 0.22f, depth),
                zone: DressZone.Shed, paint: "woodBarkDark"));

            // The pitch. Every part of it clears head height, so Max can stand against the hutch
            // without his head inside a roof — which is why the eaves start above 2 m and not at
            // whatever height happened to look right.
            foreach (float side in new[] { -1f, 1f })
            {
                props.Add(new DressProp(
                    null, new Vector3(side * 0.86f, 2.72f, roofZ), new Vector3(slope, 0.16f, depth),
                    zone: DressZone.Shed, paint: "woodDark",
                    euler: new Vector3(0f, 0f, side * pitch), anchorCentre: true));
            }

            // Corner posts, flush with the hutch's corners. The two at the back carry the roof; the
            // two at the front frame the opening the robots come out of.
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                props.Add(new DressProp(
                    null, new Vector3(sx * 1.62f, 0f, shedZ + sz * 1.62f),
                    new Vector3(0.22f, 2.05f, 0.22f),
                    zone: DressZone.Shed, paint: "woodDark"));
            }
        }

        /// <summary>
        /// The lawn's three cover blocks (YT-68), given the thing they were always named after: a
        /// tree, a planter, a hedge. The gameplay stream authored those blocks as a fight — the
        /// angles to swing through, the loops to kite around — so the dressing reads their shapes
        /// rather than re-authoring them. Re-tune a cover block over there and the prop that stands
        /// on it follows, instead of drifting into a hedge that's a metre off its own collider.
        ///
        /// The block itself keeps its collider and loses only its renderer, so the fight is
        /// unchanged: what you can hide behind is exactly what you could hide behind before.
        /// </summary>
        private static void CoverDressing(List<DressProp> props, IReadOnlyList<ArenaCover> cover,
                                          ref Rng rng)
        {
            if (cover == null) return;

            foreach (var c in cover)
            {
                Vector3 s = c.Size;
                var at = new Vector2(c.CenterXz.x, c.CenterXz.y);

                if (c.Shape == CoverShape.Cylinder)
                {
                    // Tall and round: a tree. The canopy is allowed to be a little wider than the
                    // trunk you're actually hiding behind — that's what a tree looks like.
                    props.Add(new DressProp(KitModels.TreeTall, new Vector3(at.x, 0f, at.y),
                                            new Vector3(s.x * 1.15f, s.y, s.z * 1.15f),
                                            rng.Range(0f, 360f), DressZone.Cover, sways: true));
                    continue;
                }

                if (s.x >= s.z * 2.5f || s.z >= s.x * 2.5f)
                {
                    Hedge(props, ref rng, c);
                    continue;
                }

                Planter(props, ref rng, c);
            }
        }

        private static void Hedge(List<DressProp> props, ref Rng rng, in ArenaCover c)
        {
            bool alongX = c.Size.x >= c.Size.z;
            float length = alongX ? c.Size.x : c.Size.z;
            float thickness = alongX ? c.Size.z : c.Size.x;

            int n = Mathf.Max(2, Mathf.RoundToInt(length / 1.25f));
            float step = length / n;

            for (int k = 0; k < n; k++)
            {
                float along = -length * 0.5f + step * (k + 0.5f);
                float h = c.Size.y * rng.Range(0.95f, 1.08f);
                float w = Mathf.Max(step * 1.35f, thickness * 1.1f);

                var at = alongX
                    ? new Vector2(c.CenterXz.x + along, c.CenterXz.y)
                    : new Vector2(c.CenterXz.x, c.CenterXz.y + along);

                props.Add(new DressProp(rng.Pick(KitModels.Bushes), new Vector3(at.x, 0f, at.y),
                                        new Vector3(w, h, w), rng.Range(0f, 360f),
                                        DressZone.Cover, sways: true));
            }
        }

        private static void Planter(List<DressProp> props, ref Rng rng, in ArenaCover c)
        {
            float top = c.Size.y * 0.92f;

            props.Add(new DressProp(KitModels.Pot, new Vector3(c.CenterXz.x, 0f, c.CenterXz.y),
                                    new Vector3(c.Size.x * 0.98f, top, c.Size.z * 0.98f),
                                    0f, DressZone.Cover));

            // Planted, not empty: a shrub and a scatter of flowers standing on the soil.
            props.Add(new DressProp(rng.Pick(KitModels.Bushes),
                                    new Vector3(c.CenterXz.x, top, c.CenterXz.y),
                                    new Vector3(1.3f, 0.95f, 1.3f), rng.Range(0f, 360f),
                                    DressZone.Cover, sways: true));

            for (int k = 0; k < 4; k++)
            {
                float h = rng.Range(0.3f, 0.42f);
                props.Add(new DressProp(
                    rng.Pick(KitModels.Flowers),
                    new Vector3(c.CenterXz.x + rng.Range(-1f, 1f), top,
                                c.CenterXz.y + rng.Range(-1f, 1f)),
                    new Vector3(h * 0.7f, h, h * 0.7f), rng.Range(0f, 360f),
                    DressZone.Cover, sways: true));
            }
        }

        /// <summary>Tufts loose in the lawn. Ankle-high, so they read as an unkempt lawn from the
        /// camera and are nothing at all to run through.</summary>
        private static void Tufts(List<DressProp> props, in BackyardPathLayout l, float shedZ,
                                  IReadOnlyList<ArenaCover> cover, ref Rng rng)
        {
            int placed = 0;
            for (int attempt = 0; attempt < 200 && placed < 44; attempt++)
            {
                float x = rng.Range(-l.LawnHalfWidth + 1f, l.LawnHalfWidth - 1f);
                float z = rng.Range(l.LawnStartZ + 1f, l.ArenaEndZ - 2f);

                // Not inside the shed and not inside a cover block: a tuft in there is a tuft
                // nobody will ever see, drawn every frame.
                if (Vector2.Distance(new Vector2(x, z), new Vector2(0f, shedZ)) < 2.6f) continue;
                if (InsideCover(new Vector2(x, z), cover)) continue;

                float h = rng.Range(0.16f, 0.3f);
                props.Add(new DressProp(rng.Pick(KitModels.Tufts), new Vector3(x, 0f, z),
                                        new Vector3(h * 1.7f, h, h * 1.7f),
                                        rng.Range(0f, 360f), sways: true));
                placed++;
            }
        }

        private static bool InsideCover(Vector2 p, IReadOnlyList<ArenaCover> cover)
        {
            if (cover == null) return false;
            foreach (var c in cover)
                if (c.DistanceTo(p) < 0.5f) return true;
            return false;
        }

        // --- the rules ------------------------------------------------------------------------

        /// <summary>
        /// True when no prop in the plan stands where the game is played. A prop passes if it is
        /// ankle-high (walk over it), hangs above head height (the shed roof), stands on a collider
        /// that is already there (a cover block, the hutch), or keeps out of the walkable interior
        /// altogether (the fence, the borders, the trees behind the fence).
        ///
        /// <paramref name="reason"/> names the first prop that breaks it.
        /// </summary>
        public static bool Validate(in BackyardPathLayout l, IReadOnlyList<DressProp> props,
                                    float shedZ, IReadOnlyList<ArenaCover> cover, out string reason)
        {
            Rect[] interior = WalkableInterior(l);

            foreach (var p in props)
            {
                Rect f = p.Footprint;

                if (!OnGround(f, l))
                {
                    reason = $"{p.Model ?? p.Paint} at {p.Position} stands off the edge of the yard";
                    return false;
                }

                if (p.Height <= LowPropMax) continue;          // walk-over
                if (p.MinY >= Headroom) continue;              // over your head

                if (p.Zone == DressZone.Shed)
                {
                    if (Mathf.Abs(f.center.x) <= ShedHalfWidth &&
                        Mathf.Abs(f.center.y - shedZ) <= ShedHalfDepth) continue;
                    reason = $"shed dressing at {p.Position} is off the hutch it's supposed to be part of";
                    return false;
                }

                if (p.Zone == DressZone.Cover)
                {
                    if (OnCover(f, cover)) continue;
                    reason = $"{p.Model} at {p.Position} claims to stand on cover, but no cover block is there";
                    return false;
                }

                foreach (var room in interior)
                {
                    if (!room.Overlaps(f)) continue;
                    reason = $"{p.Model ?? p.Paint} at {p.Position} ({p.Height:0.0} m tall) " +
                             "stands in the space the fight is played in";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>The floor the arena actually has: the 30 m scene plane, plus the boss arena's
        /// own floor slab. A prop past both is standing on nothing.</summary>
        private static bool OnGround(Rect f, in BackyardPathLayout l)
        {
            bool onPlane = f.center.x >= -15f && f.center.x <= 15f
                        && f.center.y >= -15f && f.center.y <= 15f;
            bool onSlab = Mathf.Abs(f.center.x) <= l.ArenaHalfWidth + 2f
                       && f.center.y >= 15f && f.center.y <= l.ArenaEndZ + 2f;
            return onPlane || onSlab;
        }

        /// <summary>Is this prop standing on that cover block? The prop is centred on the block, and
        /// allowed to overhang it — a tree is wider than the trunk you hide behind, and a shrub is
        /// wider than the hedge box. What this rules out is a prop that has drifted off its block
        /// and is now free-standing in the middle of the fight.</summary>
        private static bool OnCover(Rect f, IReadOnlyList<ArenaCover> cover)
        {
            if (cover == null) return false;

            const float centred = 0.6f;    // how far the prop's centre may sit off the block
            const float overhang = 1.3f;   // how far its canopy may spill past it

            foreach (var c in cover)
            {
                Rect r = c.Footprint;
                if (!Expand(r, centred).Contains(f.center)) continue;

                Rect limit = Expand(r, overhang);
                if (limit.Contains(f.min) && limit.Contains(f.max)) return true;
            }
            return false;
        }

        private static Rect Expand(Rect r, float by) =>
            new Rect(r.xMin - by, r.yMin - by, r.width + by * 2f, r.height + by * 2f);

        private static Rect Inset(float xMin, float zMin, float xMax, float zMax, float by)
            => FromTo(xMin + by, zMin + by, xMax - by, zMax - by);

        private static Rect FromTo(float xMin, float zMin, float xMax, float zMax)
            => new Rect(xMin, zMin, Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, zMax - zMin));

        /// <summary>A tiny xorshift, so a seed gives the same yard on every machine and in every
        /// build. UnityEngine.Random would work, but it's global state — and the plan is a pure
        /// function of (layout, seed) or it isn't testable.</summary>
        private struct Rng
        {
            private uint _s;

            public Rng(uint seed) { _s = seed == 0u ? 1u : seed; }

            public float Next()
            {
                _s ^= _s << 13;
                _s ^= _s >> 17;
                _s ^= _s << 5;
                return (_s & 0xFFFFFFu) / 16777216f;
            }

            public float Range(float a, float b) => a + (b - a) * Next();

            public int RangeInt(int a, int b) => a + Mathf.Min((int)(Next() * (b - a)), b - a - 1);

            public T Pick<T>(T[] set) => set[RangeInt(0, set.Length)];
        }
    }
}
