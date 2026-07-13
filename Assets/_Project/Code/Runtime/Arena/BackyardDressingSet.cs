using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Models;

namespace MaxWorlds.Arena
{
    /// <summary>Which set of rules a prop is judged by. The yard shares space with the fight; the
    /// factory's own dressing shares space with the ring it spawns robots on.</summary>
    public enum DressingZone
    {
        Yard,
        Factory,
    }

    /// <summary>
    /// One placed kit prop (YT-75). Base-pivoted, like every model the kit ships, so authoring is
    /// "what, where on the ground, how big, facing which way" — and a prop cannot be written
    /// half-buried or hovering.
    /// </summary>
    [System.Serializable]
    public struct DressingProp
    {
        public string Key;          // a PropCatalog key
        public Vector2 CenterXz;
        public float YawDeg;
        public Vector3 Scale;       // multiplies the model's kit-unit size
        public DressingZone Zone;

        public DressingProp(string key, Vector2 centerXz, Vector3 scale, float yawDeg = 0f,
                            DressingZone zone = DressingZone.Yard)
        {
            Key = key; CenterXz = centerXz; Scale = scale; YawDeg = yawDeg; Zone = zone;
        }

        /// <summary>World size of the placed prop, before yaw.</summary>
        public Vector3 Size => Vector3.Scale(PropCatalog.Size(Key), Scale);

        public float Height => Size.y;

        /// <summary>Scenery you walk over rather than around — see <see cref="PropCatalog.FlatHeight"/>.</summary>
        public bool IsFlat => Height <= PropCatalog.FlatHeight;

        /// <summary>Where it stands. Y is 0: kit models are pivoted at their base.</summary>
        public Vector3 Position => new Vector3(CenterXz.x, 0f, CenterXz.y);

        /// <summary>Axis-aligned footprint, widened for the yaw — so a fence panel turned 90° is
        /// measured as the 3 m it actually spans, not the 3 m it would have spanned unturned.</summary>
        public Rect Footprint
        {
            get
            {
                Vector3 s = Size;
                float rad = YawDeg * Mathf.Deg2Rad;
                float c = Mathf.Abs(Mathf.Cos(rad));
                float n = Mathf.Abs(Mathf.Sin(rad));
                float w = s.x * c + s.z * n;
                float d = s.x * n + s.z * c;
                return new Rect(CenterXz.x - w * 0.5f, CenterXz.y - d * 0.5f, w, d);
            }
        }

        /// <summary>Distance from the footprint to an XZ point (0 when the point is inside it).</summary>
        public float DistanceTo(Vector2 p)
        {
            Rect r = Footprint;
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dz = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>How far the footprint's farthest corner reaches from an XZ point — the question to
        /// ask of a prop that has to stay wholly inside the factory's spawn ring.</summary>
        public float FarthestFrom(Vector2 p)
        {
            Rect r = Footprint;
            float dx = Mathf.Max(Mathf.Abs(r.xMin - p.x), Mathf.Abs(r.xMax - p.x));
            float dz = Mathf.Max(Mathf.Abs(r.yMin - p.y), Mathf.Abs(r.yMax - p.y));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// The Backyard's set dressing (YT-75): the fence line, the trees over it, the flower beds, the
    /// stepping-stone path, the woodpile by the shed — everything that turns three grey rooms into
    /// somebody's back garden.
    ///
    /// Generated FROM <see cref="BackyardPathLayout"/> rather than hand-listed, so it cannot drift
    /// out of step with the arena: reshape the lawn and the fence reshapes with it. Pure data, so
    /// where every prop lands is decided — and checked — without instantiating anything;
    /// <see cref="BackyardDressing"/> turns it into GameObjects.
    ///
    /// THE RULE THIS FILE EXISTS TO KEEP: dressing is scenery, never geometry. Nothing here carries a
    /// collider, and <see cref="Validate"/> refuses any prop tall enough to matter that stands where
    /// the fight needs the space — the middle of a room, a doorway, or the ring the factory spawns
    /// robots on. So the yard can be made as busy as we like and the answer to "could the dressing
    /// have broken the fight?" stays no, by construction rather than by playtest.
    /// </summary>
    public static class BackyardDressingSet
    {
        /// <summary>Fixed seed: the yard must lay out identically in the editor, in CI and on the
        /// deployed link, or "it looked fine for me" stops meaning anything.</summary>
        public const int Seed = 7513;

        /// <summary>How far in from a wall a prop may stand. Past this it is in the room, and the
        /// room is the fight.</summary>
        public const float EdgeBand = 1.9f;

        /// <summary>Depth of the doorway passages — the gate and the patio mouth — that no tall prop
        /// may reach into. Deliberately the clear passage, not the door frame: the fence panels that
        /// FORM the doorway are supposed to be there.</summary>
        public const float DoorwayClearance = 2.5f;

        /// <summary>How far inside the spawn ring a factory-zone prop has to stop, so a robot is never
        /// born standing in the woodpile.</summary>
        public const float SpawnClearance = 0.5f;

        private const float PanelWidth = 3f;      // nominal world width of one fence panel
        private const float PanelDepth = 0.28f;
        private const float ShedWallHeight = 2.4f;

        // ---------------------------------------------------------------- build

        /// <summary>The whole dressing set for a layout. Deterministic.</summary>
        public static List<DressingProp> Build(BackyardPathLayout layout, float shedZ, float spawnRadius)
        {
            var props = new List<DressingProp>(256);
            var rng = new System.Random(Seed);

            Fence(props, layout);
            OutsideFoliage(props, layout, rng);
            SteppingStones(props, layout, shedZ, rng);
            EdgePlanting(props, layout, rng);
            FlowerBeds(props, layout);
            ShedYard(props, shedZ);

            return props;
        }

        // ---------------------------------------------------------------- fence

        /// <summary>Panels along the inner face of every wall, tall enough to BE the wall. The greybox
        /// slab stays — it is what stops the player — but what you see is a paling fence, which is
        /// what a back garden is bounded by.</summary>
        private static void Fence(List<DressingProp> into, BackyardPathLayout L)
        {
            float t = L.WallThickness;
            float patio = L.PatioHalfWidth, lawn = L.LawnHalfWidth;
            float gate = L.GateHalfWidth, arena = L.ArenaHalfWidth;

            // Patio: the way in. The garden gate goes in the middle of the back wall.
            FenceRun(into, L, new Vector2(-patio, L.StartZ), new Vector2(-patio, L.LawnStartZ), Vector2.right);
            FenceRun(into, L, new Vector2(patio, L.StartZ), new Vector2(patio, L.LawnStartZ), Vector2.left);
            FenceRun(into, L, new Vector2(-patio, L.StartZ), new Vector2(patio, L.StartZ), Vector2.up,
                     gateAtCentre: true);

            // Lawn: the fight room, and the shoulders it opens out through.
            FenceRun(into, L, new Vector2(-lawn, L.LawnStartZ), new Vector2(-lawn, L.GateZ), Vector2.right);
            FenceRun(into, L, new Vector2(lawn, L.LawnStartZ), new Vector2(lawn, L.GateZ), Vector2.left);
            float mouthZ = L.LawnStartZ + t * 0.5f;
            FenceRun(into, L, new Vector2(-lawn, mouthZ), new Vector2(-patio, mouthZ), Vector2.up);
            FenceRun(into, L, new Vector2(patio, mouthZ), new Vector2(lawn, mouthZ), Vector2.up);

            // The gate wall, from both sides: the lawn sees its back, the arena sees its face.
            float lawnSide = L.GateZ - t * 0.5f;
            float arenaSide = L.GateZ + t * 0.5f;
            FenceRun(into, L, new Vector2(-lawn, lawnSide), new Vector2(-gate, lawnSide), Vector2.down);
            FenceRun(into, L, new Vector2(gate, lawnSide), new Vector2(lawn, lawnSide), Vector2.down);
            FenceRun(into, L, new Vector2(-arena, arenaSide), new Vector2(-gate, arenaSide), Vector2.up);
            FenceRun(into, L, new Vector2(gate, arenaSide), new Vector2(arena, arenaSide), Vector2.up);

            // Boss arena: the bottom of the garden.
            FenceRun(into, L, new Vector2(-arena, L.GateZ), new Vector2(-arena, L.ArenaEndZ), Vector2.right);
            FenceRun(into, L, new Vector2(arena, L.GateZ), new Vector2(arena, L.ArenaEndZ), Vector2.left);
            FenceRun(into, L, new Vector2(-arena, L.ArenaEndZ), new Vector2(arena, L.ArenaEndZ), Vector2.down);
        }

        /// <summary>Panels along a wall's inner face from <paramref name="a"/> to <paramref name="b"/>.
        /// <paramref name="inward"/> points into the room, and the panel is sunk into the wall behind
        /// it — it shows its face and nothing else, so the fence is never something the player clips
        /// into on the way past.</summary>
        private static void FenceRun(List<DressingProp> into, BackyardPathLayout L,
                                     Vector2 a, Vector2 b, Vector2 inward, bool gateAtCentre = false)
        {
            Vector2 span = b - a;
            float length = span.magnitude;
            if (length < 0.5f) return;

            int n = Mathf.Max(1, Mathf.RoundToInt(length / PanelWidth));
            // An odd panel count puts a panel ON the midpoint, which is where the gate goes. With an
            // even one the gate lands half a panel off-centre and the path no longer runs through it.
            if (gateAtCentre && n % 2 == 0) n++;

            float step = length / n;
            Vector2 dir = span / length;
            float yaw = Mathf.Atan2(inward.x, inward.y) * Mathf.Rad2Deg;

            // 2 cm proud of the wall face: enough never to z-fight the slab behind it, far too little
            // to stand on.
            Vector2 sink = inward * (PanelDepth * 0.5f - 0.02f);
            int centreIndex = gateAtCentre ? Mathf.FloorToInt(n / 2f) : -1;

            for (int i = 0; i < n; i++)
            {
                string key = i == centreIndex ? PropCatalog.FenceGate : PropCatalog.FencePanel;
                Vector3 kit = PropCatalog.Size(key);
                var scale = new Vector3(step / kit.x, L.WallHeight / kit.y, PanelDepth / kit.z);

                into.Add(new DressingProp(key, a + dir * (step * (i + 0.5f)) - sink, scale, yaw));
            }
        }

        // ---------------------------------------------------------------- beyond the fence

        private static readonly string[] Trees =
        {
            PropCatalog.TreeDefault, PropCatalog.TreeOak, PropCatalog.TreeFat,
            PropCatalog.TreeThin, PropCatalog.TreeSmall,
        };

        /// <summary>Trees and scrub OUTSIDE the fence — the neighbours' yards, the bottom of the
        /// garden. They give the fence line something to be in front of, and they cannot touch the
        /// fight, because they are on the far side of a wall.</summary>
        private static void OutsideFoliage(List<DressingProp> into, BackyardPathLayout L, System.Random rng)
        {
            float t = L.WallThickness;

            SideTrees(into, rng, -1f, L.LawnHalfWidth + t, L.LawnStartZ - 2f, L.GateZ + 2f);
            SideTrees(into, rng, +1f, L.LawnHalfWidth + t, L.LawnStartZ - 2f, L.GateZ + 2f);
            SideTrees(into, rng, -1f, L.ArenaHalfWidth + t, L.GateZ + 2f, L.ArenaEndZ);
            SideTrees(into, rng, +1f, L.ArenaHalfWidth + t, L.GateZ + 2f, L.ArenaEndZ);
            SideTrees(into, rng, -1f, L.PatioHalfWidth + t, L.StartZ - 3f, L.LawnStartZ - 2f);
            SideTrees(into, rng, +1f, L.PatioHalfWidth + t, L.StartZ - 3f, L.LawnStartZ - 2f);

            BackTrees(into, rng, L.ArenaEndZ + t, +1f, -L.ArenaHalfWidth, L.ArenaHalfWidth);
            BackTrees(into, rng, L.StartZ - t, -1f, -L.PatioHalfWidth - 4f, L.PatioHalfWidth + 4f);
        }

        /// <summary>Trees down the outside of one wall. <paramref name="wallOuter"/> is the wall's far
        /// face; everything is planted clear of it, so nothing leans back over the fence.</summary>
        private static void SideTrees(List<DressingProp> into, System.Random rng, float side,
                                      float wallOuter, float zFrom, float zTo)
        {
            for (float z = zFrom; z < zTo; z += Range(rng, 4.5f, 7f))
            {
                float depth = Range(rng, 2.2f, 5.5f);
                Grove(into, rng, side, wallOuter, depth, z, alongZ: true);
            }
        }

        private static void BackTrees(List<DressingProp> into, System.Random rng, float wallOuter,
                                      float side, float xFrom, float xTo)
        {
            for (float x = xFrom; x < xTo; x += Range(rng, 4.5f, 7f))
            {
                float depth = Range(rng, 2.2f, 5f);
                Grove(into, rng, side, wallOuter, depth, x, alongZ: false);
            }
        }

        /// <summary>A tree with a bush or two at its foot, so it isn't a lollipop on a lawn. The whole
        /// grove is pushed out from the wall by <paramref name="depth"/> and the undergrowth is
        /// jittered along the fence, never back across it.</summary>
        private static void Grove(List<DressingProp> into, System.Random rng, float side, float wallOuter,
                                  float depth, float along, bool alongZ)
        {
            Vector2 At(float outward, float slide) => alongZ
                ? new Vector2(side * (wallOuter + outward), along + slide)
                : new Vector2(along + slide, wallOuter + side * outward);

            string tree = Trees[rng.Next(Trees.Length)];
            into.Add(new DressingProp(tree, At(depth, 0f),
                                      PropCatalog.ScaleToHeight(tree, Range(rng, 3.8f, 6.4f)),
                                      Range(rng, 0f, 360f)));

            int bushes = rng.Next(0, 3);
            for (int i = 0; i < bushes; i++)
            {
                string bush = rng.Next(2) == 0 ? PropCatalog.BushDetailed : PropCatalog.Bush;
                Vector2 at = At(Mathf.Max(0.8f, depth + Range(rng, -1.2f, 1.6f)), Range(rng, -2f, 2f));
                into.Add(new DressingProp(bush, at, PropCatalog.ScaleToHeight(bush, Range(rng, 0.6f, 1f)),
                                          Range(rng, 0f, 360f)));
            }
        }

        // ---------------------------------------------------------------- ground

        /// <summary>Stepping stones from the patio door up the lawn. Flat, so they are pure paint —
        /// but what they paint is the mission line, which is the one thing a player must never
        /// lose.</summary>
        private static void SteppingStones(List<DressingProp> into, BackyardPathLayout L, float shedZ,
                                           System.Random rng)
        {
            var scale = PropCatalog.ScaleToHeight(PropCatalog.PathStone, 0.06f);
            for (float z = L.StartZ + 1.8f; z < shedZ - 3f; z += 1.8f)
            {
                into.Add(new DressingProp(PropCatalog.PathStone, new Vector2(Range(rng, -0.4f, 0.4f), z),
                                          scale, Range(rng, -14f, 14f)));
            }

            into.Add(new DressingProp(PropCatalog.PathStoneCircle, new Vector2(0f, L.StartZ + 0.7f),
                                      PropCatalog.ScaleToHeight(PropCatalog.PathStoneCircle, 0.06f) * 1.6f));
        }

        // ---------------------------------------------------------------- planting

        /// <summary>A plantable prop and the range of heights it looks right at. The heights are the
        /// AUTHORED intent; <see cref="EdgeRun"/> still shrinks anything whose footprint would
        /// otherwise reach out of the edge band, so a new entry here can't quietly put a shrub in the
        /// middle of the lawn.</summary>
        private readonly struct EdgeItem
        {
            public readonly string Key;
            public readonly float MinHeight;
            public readonly float MaxHeight;

            public EdgeItem(string key, float minHeight, float maxHeight)
            {
                Key = key; MinHeight = minHeight; MaxHeight = maxHeight;
            }
        }

        private static readonly EdgeItem[] EdgeItems =
        {
            new EdgeItem(PropCatalog.Bush, 0.40f, 0.60f),
            new EdgeItem(PropCatalog.BushDetailed, 0.35f, 0.55f),
            new EdgeItem(PropCatalog.BushLarge, 0.40f, 0.60f),
            new EdgeItem(PropCatalog.BushSmall, 0.30f, 0.50f),
            new EdgeItem(PropCatalog.Grass, 0.40f, 0.65f),
            new EdgeItem(PropCatalog.GrassLarge, 0.40f, 0.65f),
            new EdgeItem(PropCatalog.GrassLeafs, 0.30f, 0.50f),
            new EdgeItem(PropCatalog.RockSmallA, 0.30f, 0.50f),
            new EdgeItem(PropCatalog.RockSmallB, 0.30f, 0.50f),
            new EdgeItem(PropCatalog.RockFlat, 0.15f, 0.25f),
            new EdgeItem(PropCatalog.PotLarge, 0.25f, 0.35f),
            new EdgeItem(PropCatalog.PotSmall, 0.40f, 0.60f),
            new EdgeItem(PropCatalog.Stump, 0.35f, 0.55f),
            new EdgeItem(PropCatalog.Log, 0.25f, 0.35f),
        };

        /// <summary>Planting at the foot of the fence, inside the rooms — hard against the boundary,
        /// out of the fight.</summary>
        private static void EdgePlanting(List<DressingProp> into, BackyardPathLayout L, System.Random rng)
        {
            EdgeRun(into, rng, -1f, L.LawnHalfWidth, L.LawnStartZ + 2f, L.GateZ - 2f);
            EdgeRun(into, rng, +1f, L.LawnHalfWidth, L.LawnStartZ + 2f, L.GateZ - 2f);
            EdgeRun(into, rng, -1f, L.ArenaHalfWidth, L.GateZ + 4f, L.ArenaEndZ - 2f);
            EdgeRun(into, rng, +1f, L.ArenaHalfWidth, L.GateZ + 4f, L.ArenaEndZ - 2f);
            EdgeRun(into, rng, -1f, L.PatioHalfWidth, L.StartZ + 1.5f, L.LawnStartZ - 4f);
            EdgeRun(into, rng, +1f, L.PatioHalfWidth, L.StartZ + 1.5f, L.LawnStartZ - 4f);
        }

        /// <summary>
        /// One run of planting along a wall.
        ///
        /// The prop is sized FIRST and placed SECOND, from its own footprint: it goes hard against
        /// the fence with a small gap, so however wide the model turns out to be, its inner edge
        /// stays inside the edge band. A prop too wide to fit the band at the height it wanted is
        /// scaled down until it does. That's the difference between an invariant and a convention —
        /// nobody has to remember that pots are three times wider than they are tall.
        /// </summary>
        private static void EdgeRun(List<DressingProp> into, System.Random rng, float side,
                                    float wallInner, float zFrom, float zTo)
        {
            const float Gap = 0.15f;                          // breathing room off the fence face
            float widest = EdgeBand - Gap * 2f;               // must fit, footprint and all

            for (float z = zFrom; z < zTo; z += Range(rng, 1.8f, 3.4f))
            {
                EdgeItem item = EdgeItems[rng.Next(EdgeItems.Length)];
                float yaw = Range(rng, 0f, 360f);
                Vector3 scale = PropCatalog.ScaleToHeight(item.Key,
                                                          Range(rng, item.MinHeight, item.MaxHeight));

                float width = new DressingProp(item.Key, Vector2.zero, scale, yaw).Footprint.width;
                if (width > widest)
                {
                    scale *= widest / width;                  // too broad for the band: plant a smaller one
                    width = widest;
                }

                var at = new Vector2(side * (wallInner - Gap - width * 0.5f), z);
                into.Add(new DressingProp(item.Key, at, scale, yaw));
            }
        }

        private static readonly string[] Flowers =
        {
            PropCatalog.FlowerRedA, PropCatalog.FlowerYellowA, PropCatalog.FlowerPurpleA,
            PropCatalog.FlowerRedB, PropCatalog.FlowerPurpleC,
        };

        /// <summary>Two tilled beds down the sides of the lawn, planted out. The dirt rows are flat
        /// paint; the flowers standing in them are what actually reads at this camera angle.</summary>
        private static void FlowerBeds(List<DressingProp> into, BackyardPathLayout L)
        {
            Bed(into, L, -1f, L.LawnStartZ + 3.5f);
            Bed(into, L, +1f, L.GateZ - 7.5f);
        }

        private static void Bed(List<DressingProp> into, BackyardPathLayout L, float side, float zStart)
        {
            const float RowLength = 2.2f;
            const float Inset = 1.1f;

            Vector3 kit = PropCatalog.Size(PropCatalog.DirtRow);
            float row = RowLength / kit.x;
            var rowScale = new Vector3(row, 0.09f / kit.y, row);
            float x = side * (L.LawnHalfWidth - Inset);

            for (int i = 0; i < 2; i++)
            {
                float z = zStart + i * RowLength;
                into.Add(new DressingProp(PropCatalog.DirtRow, new Vector2(x, z), rowScale, 90f));

                for (int f = 0; f < 3; f++)
                {
                    string key = Flowers[(i * 3 + f) % Flowers.Length];
                    var at = new Vector2(x + side * 0.2f, z + (f - 1) * RowLength * 0.32f);
                    into.Add(new DressingProp(key, at, PropCatalog.ScaleToHeight(key, 0.75f), f * 47f));
                }
            }
        }

        // ---------------------------------------------------------------- the shed

        /// <summary>
        /// The Mower Hutch's shed, and the clutter around it.
        ///
        /// The hutch itself is left alone, on purpose. It is the objective — hazard-orange body,
        /// pulsing core, floating label, every bit of that a deliberate fix for players not being
        /// able to FIND the thing they were meant to kill (YT-38 QA) — and it is damageable, so
        /// gameplay owns its colour. Roofing it over would hide it from a camera that looks almost
        /// straight down. So the shed is built AROUND it: plank walls at its back and sides, open to
        /// the front and to the sky, with the machine sitting inside.
        ///
        /// Everything here stays well inside the spawn ring. That is what guarantees robots emerge in
        /// front of the shed and walk away from it, and never out of a wall.
        /// </summary>
        private static void ShedYard(List<DressingProp> into, float shedZ)
        {
            Vector3 panel = PropCatalog.Size(PropCatalog.FencePanel);
            const float HalfWidth = 1.7f;    // just clear of the 3 m hutch body
            const float BackZ = 2f;
            const float WingLength = 1.7f;

            Vector3 Wall(float width) =>
                new Vector3(width / panel.x, ShedWallHeight / panel.y, PanelDepth / panel.z);

            into.Add(new DressingProp(PropCatalog.FencePanel, new Vector2(0f, shedZ + BackZ),
                                      Wall(HalfWidth * 2f), 180f, DressingZone.Factory));
            into.Add(new DressingProp(PropCatalog.FencePanel,
                                      new Vector2(-HalfWidth, shedZ + BackZ - WingLength * 0.5f),
                                      Wall(WingLength), 90f, DressingZone.Factory));
            into.Add(new DressingProp(PropCatalog.FencePanel,
                                      new Vector2(HalfWidth, shedZ + BackZ - WingLength * 0.5f),
                                      Wall(WingLength), -90f, DressingZone.Factory));

            // The woodpile and pots you'd expect beside a garden shed — tucked in behind the corners,
            // where they dress the silhouette without ever standing between the player and the core.
            Clutter(into, PropCatalog.LogStack, new Vector2(-2.2f, shedZ + 0.8f), 0.5f, 0f);
            Clutter(into, PropCatalog.Stump, new Vector2(-2.05f, shedZ + 1.4f), 0.35f, 0f);
            Clutter(into, PropCatalog.PotLarge, new Vector2(2.1f, shedZ + 0.7f), 0.35f, 0f);
            Clutter(into, PropCatalog.PotSmall, new Vector2(2f, shedZ + 1.3f), 0.42f, 30f);
        }

        private static void Clutter(List<DressingProp> into, string key, Vector2 at, float height, float yaw)
        {
            into.Add(new DressingProp(key, at, PropCatalog.ScaleToHeight(key, height), yaw,
                                      DressingZone.Factory));
        }

        private static float Range(System.Random rng, float a, float b) =>
            a + (float)rng.NextDouble() * (b - a);

        // ---------------------------------------------------------------- validation

        /// <summary>
        /// True when no prop can affect the fight; <paramref name="reason"/> names the first breach.
        ///
        /// A yard prop passes if it is flat enough to walk over, or it is beyond the walls, or — being
        /// inside a room — it hugs a wall, stays out of the doorway passages, keeps off the cover, and
        /// keeps off the factory's spawn ring. Factory props are judged the other way round: they must
        /// stay INSIDE the ring, so nothing is ever spawned standing in them.
        /// </summary>
        public static bool Validate(BackyardPathLayout layout, IReadOnlyList<DressingProp> props,
                                    IReadOnlyList<ArenaCover> cover, float shedZ, float spawnRadius,
                                    out string reason)
        {
            var ring = new Vector2(0f, shedZ);
            Rect[] interiors = Interiors(layout);
            Rect[] doorways = Doorways(layout);

            foreach (DressingProp prop in props)
            {
                if (!PropCatalog.Has(prop.Key))
                { reason = $"'{prop.Key}' is not a prop in the kit"; return false; }

                if (prop.Scale.x <= 0f || prop.Scale.y <= 0f || prop.Scale.z <= 0f)
                { reason = $"{prop.Key} has a non-positive scale"; return false; }

                if (prop.Zone == DressingZone.Factory)
                {
                    if (prop.FarthestFrom(ring) > spawnRadius - SpawnClearance)
                    {
                        reason = $"{prop.Key} by the shed reaches the spawn ring — " +
                                 "robots would be born inside it";
                        return false;
                    }
                    continue;
                }

                if (prop.IsFlat) continue;   // walked over, not around

                Rect footprint = prop.Footprint;

                foreach (Rect room in interiors)
                {
                    if (room.Overlaps(footprint))
                    { reason = $"{prop.Key} at {prop.CenterXz} stands in the middle of the fight space"; return false; }
                }

                foreach (Rect door in doorways)
                {
                    if (door.Overlaps(footprint))
                    { reason = $"{prop.Key} at {prop.CenterXz} stands in a doorway"; return false; }
                }

                if (prop.DistanceTo(ring) < spawnRadius + SpawnClearance)
                { reason = $"{prop.Key} at {prop.CenterXz} crowds the shed's spawn ring"; return false; }

                if (cover == null) continue;
                foreach (ArenaCover c in cover)
                {
                    if (c.Footprint.Overlaps(footprint))
                    { reason = $"{prop.Key} at {prop.CenterXz} grows through the {c.Name}"; return false; }
                }
            }

            reason = null;
            return true;
        }

        /// <summary>Each room minus the band a prop may stand in — i.e. the space the fight needs.
        /// Nothing tall may touch these.</summary>
        public static Rect[] Interiors(BackyardPathLayout L)
        {
            return new[]
            {
                Deflate(Room(L.PatioHalfWidth, L.StartZ, L.LawnStartZ), EdgeBand),
                Deflate(Room(L.LawnHalfWidth, L.LawnStartZ, L.GateZ), EdgeBand),
                Deflate(Room(L.ArenaHalfWidth, L.GateZ, L.ArenaEndZ), EdgeBand),
            };
        }

        /// <summary>The two ways through — the patio mouth and the boss gate — as the passages you
        /// walk down, inset from the frames the fence panels form.</summary>
        public static Rect[] Doorways(BackyardPathLayout L)
        {
            const float FrameInset = 0.6f;
            float patio = Mathf.Max(0.5f, L.PatioHalfWidth - FrameInset);
            float gate = Mathf.Max(0.5f, L.GateHalfWidth - FrameInset);

            return new[]
            {
                new Rect(-patio, L.LawnStartZ - DoorwayClearance, patio * 2f, DoorwayClearance * 2f),
                new Rect(-gate, L.GateZ - DoorwayClearance, gate * 2f, DoorwayClearance * 2f),
            };
        }

        private static Rect Room(float halfWidth, float zFrom, float zTo) =>
            new Rect(-halfWidth, zFrom, halfWidth * 2f, zTo - zFrom);

        private static Rect Deflate(Rect r, float by)
        {
            float w = Mathf.Max(0f, r.width - by * 2f);
            float h = Mathf.Max(0f, r.height - by * 2f);
            return new Rect(r.xMin + by, r.yMin + by, w, h);
        }
    }
}
