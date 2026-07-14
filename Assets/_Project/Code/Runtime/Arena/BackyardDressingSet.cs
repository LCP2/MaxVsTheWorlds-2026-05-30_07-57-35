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
    /// stepping-stone path, the woodpile by the shed — everything that turns grey rooms into
    /// somebody's back garden.
    ///
    /// THE ART FOLLOWS THE MAP. Every driver here is a loop over the level's own geometry — over
    /// <see cref="MapGeometry.Faces"/> (both sides of every wall the map derives), over its zones,
    /// over the chain of links from the spawn to the factory. Nothing is hand-listed. That is the
    /// whole of this file's design, and it is not a tidiness argument: the yard used to be written
    /// out as thirteen fence runs along a straight corridor, so the day the map grew a nook and a
    /// shed off the side of the lawn, the fences would have stood along walls that no longer existed
    /// and the new walls would have had none at all. Now a room that appears in the map appears in
    /// the garden, fenced and planted, with nothing to re-author.
    ///
    /// An INNER face (one with a room behind it) gets the paling fence and the planting at its foot;
    /// an OUTER one gets the neighbours' trees. That is the whole seam.
    ///
    /// Pure data, so where every prop lands is decided — and checked — without instantiating
    /// anything; <see cref="BackyardDressing"/> turns it into GameObjects.
    ///
    /// THE RULE THIS FILE EXISTS TO KEEP: dressing is scenery, never geometry. Nothing here carries a
    /// collider, and <see cref="Validate"/> refuses any prop tall enough to matter that stands where
    /// the fight needs the space — the middle of a room, a doorway, the cover, or the ring the
    /// factory spawns robots on. So the yard can be made as busy as we like and the answer to "could
    /// the dressing have broken the fight?" stays no, by construction rather than by playtest.
    /// </summary>
    public static class BackyardDressingSet
    {
        /// <summary>Fixed seed: the yard must lay out identically in the editor, in CI and on the
        /// deployed link, or "it looked fine for me" stops meaning anything.</summary>
        public const int Seed = 7513;

        /// <summary>How far in from a wall a prop may stand. Past this it is in the room, and the
        /// room is the fight.</summary>
        public const float EdgeBand = 1.9f;

        /// <summary>Depth of the doorway passages that no tall prop may reach into. Deliberately the
        /// clear passage, not the door frame: the fence panels that FORM the doorway are supposed to
        /// be there.</summary>
        public const float DoorwayClearance = 2.5f;

        /// <summary>How far inside the spawn ring a factory-zone prop has to stop, so a robot is never
        /// born standing in the woodpile.</summary>
        public const float SpawnClearance = 0.5f;

        private const float PanelWidth = 3f;      // nominal world width of one fence panel
        private const float PanelDepth = 0.28f;
        private const float ShedWallHeight = 2.4f;

        /// <summary>A wall face shorter than this is a corner, a doorway shoulder or a stub. Planting
        /// it out puts a shrub in every crevice of the level and reads as clutter, not as a garden.</summary>
        private const float MinPlantedFace = 5f;

        /// <summary>Planting starts this far in from each end of a wall, so the corners stay swept and
        /// a chasing robot never has a shrub-lined pocket to wedge itself into.</summary>
        private const float CornerInset = 2f;

        // ---------------------------------------------------------------- build

        /// <summary>The whole dressing set for a map. Deterministic.</summary>
        public static List<DressingProp> Build(MapData map)
        {
            var props = new List<DressingProp>(256);
            if (map == null) return props;

            var rng = new System.Random(Seed);
            List<ArenaCover> cover = CoverIn(map);
            var keepout = new Keepout(map, cover);
            List<WallFace> faces = MapGeometry.Faces(map);

            Fence(props, map, faces);
            OutsideFoliage(props, map, faces, rng);
            SteppingStones(props, map, cover, rng);
            EdgePlanting(props, map, faces, keepout, rng);
            FlowerBeds(props, map, keepout);

            // A shed for every factory (YT-92). One machine standing in a plank shed and another
            // standing bare in a room would read as two different things, and they are the same thing.
            foreach (Vector2 factory in Factories(map)) ShedYard(props, factory);

            return props;
        }

        // ---------------------------------------------------------------- fence

        /// <summary>Panels along the inner face of every wall the map derives, tall enough to BE the
        /// wall. The greybox slab stays — it is what stops the player — but what you see is a paling
        /// fence, which is what a back garden is bounded by. Every room gets one, including the ones
        /// that did not exist when this was written.</summary>
        private static void Fence(List<DressingProp> into, MapData map, List<WallFace> faces)
        {
            int gate = GardenGate(map, faces);

            for (int i = 0; i < faces.Count; i++)
            {
                if (!faces[i].FacesRoom) continue;
                FenceRun(into, map, faces[i], gateAtCentre: i == gate);
            }
        }

        /// <summary>The face the garden gate goes in: the inner face of the wall the player has his
        /// back to when the run starts, looking the way he is about to walk. FOUND, not named — move
        /// the spawn to another room and the way in moves with it.</summary>
        private static int GardenGate(MapData map, List<WallFace> faces)
        {
            MapEntity spawn = map.First(EntityKind.PlayerSpawn);
            if (spawn == null) return -1;

            Vector2 at = spawn.CenterXz;
            int best = -1;
            float nearest = float.MaxValue;

            for (int i = 0; i < faces.Count; i++)
            {
                WallFace f = faces[i];

                // Faces up-field (so it is a wall he turns his back on) and stands behind him.
                if (!f.FacesRoom || f.Out.y < 0.5f || f.A.y > at.y) continue;

                float d = (Midpoint(f) - at).sqrMagnitude;
                if (d < nearest) { nearest = d; best = i; }
            }

            return best;
        }

        /// <summary>Panels along a wall's inner face, from <see cref="WallFace.A"/> to
        /// <see cref="WallFace.B"/>. The panel is sunk into the wall behind it — it shows its face and
        /// nothing else, so the fence is never something the player clips into on the way past.</summary>
        private static void FenceRun(List<DressingProp> into, MapData map, in WallFace face,
                                     bool gateAtCentre)
        {
            float length = face.Length;
            if (length < 0.5f) return;

            int n = Mathf.Max(1, Mathf.RoundToInt(length / PanelWidth));
            // An odd panel count puts a panel ON the midpoint, which is where the gate goes. With an
            // even one the gate lands half a panel off-centre and the path no longer runs through it.
            if (gateAtCentre && n % 2 == 0) n++;

            float step = length / n;
            Vector2 dir = face.Direction;
            float yaw = Mathf.Atan2(face.Out.x, face.Out.y) * Mathf.Rad2Deg;

            // 2 cm proud of the wall face: enough never to z-fight the slab behind it, far too little
            // to stand on.
            Vector2 sink = face.Out * (PanelDepth * 0.5f - 0.02f);
            int centreIndex = gateAtCentre ? Mathf.FloorToInt(n / 2f) : -1;

            for (int i = 0; i < n; i++)
            {
                string key = i == centreIndex ? PropCatalog.FenceGate : PropCatalog.FencePanel;
                Vector3 kit = PropCatalog.Size(key);
                var scale = new Vector3(step / kit.x, map.wallHeight / kit.y, PanelDepth / kit.z);

                into.Add(new DressingProp(key, face.A + dir * (step * (i + 0.5f)) - sink, scale, yaw));
            }
        }

        // ---------------------------------------------------------------- beyond the fence

        private static readonly string[] Trees =
        {
            PropCatalog.TreeDefault, PropCatalog.TreeOak, PropCatalog.TreeFat,
            PropCatalog.TreeThin, PropCatalog.TreeSmall,
        };

        /// <summary>Trees and scrub on every face that looks OUT of the level — the neighbours' yards,
        /// the bottom of the garden. They give the fence line something to be in front of, and they
        /// cannot touch the fight, because they are on the far side of a wall.</summary>
        private static void OutsideFoliage(List<DressingProp> into, MapData map, List<WallFace> faces,
                                           System.Random rng)
        {
            foreach (WallFace face in faces)
            {
                if (face.FacesRoom || face.Length < 1f) continue;

                for (float along = 0f; along < face.Length; along += Range(rng, 4.5f, 7f))
                    Grove(into, map, rng, face, along);
            }
        }

        /// <summary>A tree with a bush or two at its foot, so it isn't a lollipop on a lawn. The whole
        /// grove is pushed out from the wall and the undergrowth is jittered along it, never back
        /// across it.</summary>
        private static void Grove(List<DressingProp> into, MapData map, System.Random rng,
                                  WallFace face, float along)
        {
            float depth = Range(rng, 2.2f, 5.5f);
            Vector2 dir = face.Direction;
            Vector2 away = face.Out;
            Vector2 foot = face.A + dir * along;

            Vector2 At(float outward, float slide) => foot + away * outward + dir * slide;

            string tree = Trees[rng.Next(Trees.Length)];
            Plant(into, map, tree, At(depth, 0f),
                  PropCatalog.ScaleToHeight(tree, Range(rng, 3.8f, 6.4f)), Range(rng, 0f, 360f));

            int bushes = rng.Next(0, 3);
            for (int i = 0; i < bushes; i++)
            {
                string bush = rng.Next(2) == 0 ? PropCatalog.BushDetailed : PropCatalog.Bush;
                Vector2 at = At(Mathf.Max(0.8f, depth + Range(rng, -1.2f, 1.6f)), Range(rng, -2f, 2f));
                Plant(into, map, bush, at, PropCatalog.ScaleToHeight(bush, Range(rng, 0.6f, 1f)),
                      Range(rng, 0f, 360f));
            }
        }

        /// <summary>Plants a neighbour's tree only where it is genuinely a neighbour's — clear of every
        /// room, footprint and all. The wall solver grows a wall's ends to close the corners, so a face
        /// that looks OUT of one room can overhang the floor of the next; without this, the shed would
        /// have a wood growing through it.</summary>
        private static void Plant(List<DressingProp> into, MapData map, string key, Vector2 at,
                                  Vector3 scale, float yaw)
        {
            var prop = new DressingProp(key, at, scale, yaw);
            Rect footprint = prop.Footprint;

            foreach (MapZone zone in map.zones)
                if (zone != null && zone.Footprint.Overlaps(footprint)) return;

            into.Add(prop);
        }

        // ---------------------------------------------------------------- ground

        /// <summary>
        /// Stepping stones from where Max starts to the factory he is here to break, through the
        /// middle of every room on the way. Flat, so they are pure paint — but what they paint is the
        /// mission line, which is the one thing a player must never lose.
        ///
        /// The line is no longer a straight run up the lawn: the shed is off to one side and the path
        /// turns to reach it. So the route is WALKED — the shortest chain of links between the two —
        /// rather than typed as a Z from and a Z to.
        ///
        /// Where the route runs behind a piece of cover it skips a stone rather than paving under it.
        /// A stone is flat and could not obstruct anything if it tried, but half a paving slab poking
        /// out of the foot of a hedge is the sort of thing that reads as a bug even when it isn't.
        /// </summary>
        private static void SteppingStones(List<DressingProp> into, MapData map,
                                           List<ArenaCover> cover, System.Random rng)
        {
            List<Vector2> route = Route(map);
            if (route.Count < 2) return;

            const float Spacing = 1.8f;
            const float Start = 1.8f;   // clear of Max's own feet
            const float Stop = 3f;      // and short of the factory: the last stretch is the shed's yard

            Vector3 scale = PropCatalog.ScaleToHeight(PropCatalog.PathStone, 0.06f);
            float total = RouteLength(route);

            for (float d = Start; d < total - Stop; d += Spacing)
            {
                Vector2 at = PointAlong(route, d, out Vector2 dir);
                var sideways = new Vector2(-dir.y, dir.x);

                var stone = new DressingProp(PropCatalog.PathStone,
                                             at + sideways * Range(rng, -0.4f, 0.4f),
                                             scale, Range(rng, -14f, 14f));

                if (!Under(cover, stone)) into.Add(stone);
            }

            into.Add(new DressingProp(PropCatalog.PathStoneCircle, PointAlong(route, 0.7f, out _),
                                      PropCatalog.ScaleToHeight(PropCatalog.PathStoneCircle, 0.06f) * 1.6f));
        }

        private static bool Under(List<ArenaCover> cover, in DressingProp prop)
        {
            Rect footprint = prop.Footprint;

            foreach (ArenaCover c in cover)
                if (c.Footprint.Overlaps(footprint)) return true;

            return false;
        }

        /// <summary>The mission line, as a polyline: the player spawn, the centre of every room between
        /// it and the factory, and the factory itself.</summary>
        public static List<Vector2> Route(MapData map)
        {
            var route = new List<Vector2>();
            if (map == null) return route;

            MapEntity spawn = map.First(EntityKind.PlayerSpawn);
            MapEntity factory = map.First(EntityKind.Factory);
            if (spawn == null || factory == null) return route;

            MapZone from = map.ZoneAt(spawn.x, spawn.z);
            MapZone to = map.ZoneAt(factory.x, factory.z);
            if (from == null || to == null) return route;

            // The same room-by-room route the ROBOTS walk (YT-93). One definition, deliberately: the
            // stones paint the mission line, and if the stones and the robots disagreed about the way
            // through the yard, one of them would be lying to the player.
            List<MapZone> rooms = MapRoutes.Rooms(map, from, to);

            route.Add(spawn.CenterXz);
            for (int i = 1; i < rooms.Count - 1; i++)
                route.Add(new Vector2(rooms[i].x, rooms[i].z));
            route.Add(factory.CenterXz);

            return route;
        }

        private static float RouteLength(List<Vector2> route)
        {
            float total = 0f;
            for (int i = 0; i + 1 < route.Count; i++) total += (route[i + 1] - route[i]).magnitude;
            return total;
        }

        /// <summary>The point <paramref name="distance"/> along the route, and the direction of travel
        /// there. Past the end it clamps to the end, which is what a stone spacing that does not divide
        /// the route neatly needs it to do.</summary>
        private static Vector2 PointAlong(List<Vector2> route, float distance, out Vector2 direction)
        {
            direction = Vector2.up;

            for (int i = 0; i + 1 < route.Count; i++)
            {
                Vector2 leg = route[i + 1] - route[i];
                float length = leg.magnitude;
                if (length < 0.001f) continue;

                direction = leg / length;
                if (distance <= length) return route[i] + direction * distance;
                distance -= length;
            }

            return route[route.Count - 1];
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
        /// out of the fight. Every inner face long enough to read as a wall gets a run.</summary>
        private static void EdgePlanting(List<DressingProp> into, MapData map, List<WallFace> faces,
                                         Keepout keepout, System.Random rng)
        {
            foreach (WallFace face in faces)
            {
                if (!face.FacesRoom || face.Length < MinPlantedFace) continue;

                MapZone room = RoomBehind(map, face);
                if (room != null) EdgeRun(into, keepout, rng, face, room);
            }
        }

        /// <summary>
        /// One run of planting along a wall face, inside the room it looks into.
        ///
        /// The prop is sized FIRST and placed SECOND, from its own footprint: it goes hard against
        /// the fence with a small gap, so however wide the model turns out to be, its inner edge
        /// stays inside the edge band. A prop too wide to fit the band at the height it wanted is
        /// scaled down until it does. That's the difference between an invariant and a convention —
        /// nobody has to remember that pots are three times wider than they are tall.
        ///
        /// The band is measured from the ROOM's edge, not from the face. A party wall — one with a
        /// room on both sides — straddles the line the rooms share, so its face already stands half a
        /// wall inside the room, and a shrub planted the full band's depth beyond THAT is a shrub in
        /// the fight space.
        /// </summary>
        private static void EdgeRun(List<DressingProp> into, Keepout keepout, System.Random rng,
                                    in WallFace face, MapZone room)
        {
            const float Gap = 0.15f;                             // breathing room off the fence face

            float widest = EdgeBand - FaceInset(room, face) - Gap * 2f;
            if (widest < 0.25f) return;                          // no band left to plant in

            Vector2 span = Overlap(room, face);
            Vector2 dir = face.Direction;

            for (float t = span.x + CornerInset; t < span.y - CornerInset; t += Range(rng, 1.8f, 3.4f))
            {
                EdgeItem item = EdgeItems[rng.Next(EdgeItems.Length)];
                float yaw = Range(rng, 0f, 360f);
                Vector3 scale = PropCatalog.ScaleToHeight(item.Key,
                                                          Range(rng, item.MinHeight, item.MaxHeight));

                float width = Across(new DressingProp(item.Key, Vector2.zero, scale, yaw), face);
                if (width > widest)
                {
                    scale *= widest / width;                     // too broad for the band: plant a smaller one
                    width = widest;
                }

                Vector2 at = face.A + dir * t + face.Out * (Gap + width * 0.5f);
                var prop = new DressingProp(item.Key, at, scale, yaw);

                // A room's edge is not always free: a doorway may open through it, a cover block may
                // stand against it. Ask before planting rather than plant and be refused — one shrub
                // that cannot go anywhere must not cost the yard its whole dressing.
                if (keepout.Objection(prop) == null) into.Add(prop);
            }
        }

        /// <summary>The room a face looks into, or null. Read from the geometry (a step off the face,
        /// the way it points) rather than carried around, so it is right even where a wall's capped end
        /// overhangs the room next door.</summary>
        private static MapZone RoomBehind(MapData map, in WallFace face)
        {
            Vector2 inside = Midpoint(face) + face.Out * 0.25f;
            return map.ZoneAt(inside.x, inside.y);
        }

        /// <summary>How far a face stands INSIDE the room it looks into: nothing for an exterior wall,
        /// half a wall for a party wall the two rooms share.</summary>
        private static float FaceInset(MapZone room, in WallFace face)
        {
            if (face.Out.x > 0.5f) return Mathf.Max(0f, face.A.x - room.XMin);
            if (face.Out.x < -0.5f) return Mathf.Max(0f, room.XMax - face.A.x);
            if (face.Out.y > 0.5f) return Mathf.Max(0f, face.A.y - room.ZMin);
            return Mathf.Max(0f, room.ZMax - face.A.y);
        }

        /// <summary>The stretch of a face — as distance from <see cref="WallFace.A"/> — that actually
        /// runs alongside a room. Walls are capped past their ends to close the corners, so a face is
        /// routinely longer than the room behind it.</summary>
        private static Vector2 Overlap(MapZone room, in WallFace face)
        {
            bool alongX = Mathf.Abs(face.Direction.x) > 0.5f;

            float from = alongX ? room.XMin : room.ZMin;
            float to = alongX ? room.XMax : room.ZMax;
            float origin = alongX ? face.A.x : face.A.y;

            return new Vector2(Mathf.Max(0f, from - origin), Mathf.Min(face.Length, to - origin));
        }

        /// <summary>A prop's footprint measured ACROSS a face — the direction the edge band runs in.</summary>
        private static float Across(in DressingProp prop, in WallFace face)
        {
            Rect f = prop.Footprint;
            return Mathf.Abs(face.Direction.x) > 0.5f ? f.height : f.width;
        }

        private static readonly string[] Flowers =
        {
            PropCatalog.FlowerRedA, PropCatalog.FlowerYellowA, PropCatalog.FlowerPurpleA,
            PropCatalog.FlowerRedB, PropCatalog.FlowerPurpleC,
        };

        private const float RowLength = 2.2f;
        private const float BedInset = 1.1f;

        /// <summary>Two tilled beds down the sides of every open room, planted out. The dirt rows are
        /// flat paint; the flowers standing in them are what actually reads at this camera angle.</summary>
        private static void FlowerBeds(List<DressingProp> into, MapData map, Keepout keepout)
        {
            foreach (MapZone zone in map.zones)
            {
                if (zone == null || zone.Kind != ZoneKind.Open) continue;

                Bed(into, keepout, zone, -1f);
                Bed(into, keepout, zone, +1f);
            }
        }

        /// <summary>
        /// A bed against one side of a room, at the first depth along it that clears the fight.
        ///
        /// The beds used to be at two hand-picked depths, which worked only because the lawn was a
        /// corridor with nothing else along its edges. A room with a doorway in its side has to be
        /// ASKED where a bed fits, not told — so the near bed walks up from the near end and the far
        /// bed walks down from the far end, and each takes the first slot it is allowed.
        /// </summary>
        private static void Bed(List<DressingProp> into, Keepout keepout, MapZone room, float side)
        {
            float x = room.x + side * (room.width * 0.5f - BedInset);
            float first = room.ZMin + 2.5f;
            float last = room.ZMax - 2.5f - RowLength;
            if (last < first) return;

            float step = side < 0f ? 0.5f : -0.5f;

            for (float z = side < 0f ? first : last; z >= first && z <= last; z += step)
            {
                if (!Fits(keepout, room, x, z, side)) continue;

                Vector3 kit = PropCatalog.Size(PropCatalog.DirtRow);
                float row = RowLength / kit.x;
                var rowScale = new Vector3(row, 0.09f / kit.y, row);

                for (int i = 0; i < 2; i++)
                {
                    into.Add(new DressingProp(PropCatalog.DirtRow, new Vector2(x, z + i * RowLength),
                                              rowScale, 90f));

                    for (int f = 0; f < 3; f++)
                        into.Add(Flower(x, z, side, i, f));
                }
                return;
            }
        }

        private static bool Fits(Keepout keepout, MapZone room, float x, float z, float side)
        {
            for (int i = 0; i < 2; i++)
            for (int f = 0; f < 3; f++)
            {
                DressingProp flower = Flower(x, z, side, i, f);
                if (!room.Contains(flower.CenterXz.x, flower.CenterXz.y)) return false;
                if (keepout.Objection(flower) != null) return false;
            }
            return true;
        }

        private static DressingProp Flower(float x, float z, float side, int row, int index)
        {
            string key = Flowers[(row * 3 + index) % Flowers.Length];
            var at = new Vector2(x + side * 0.2f,
                                 z + row * RowLength + (index - 1) * RowLength * 0.32f);

            return new DressingProp(key, at, PropCatalog.ScaleToHeight(key, 0.75f), index * 47f);
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
        /// Everything is placed RELATIVE to the factory, because the factory no longer stands on the
        /// centre line — it is off in a room of its own. And everything stays well inside the spawn
        /// ring, which is what guarantees robots emerge in front of the shed and walk away from it,
        /// and never out of a wall.
        /// </summary>
        private static void ShedYard(List<DressingProp> into, Vector2 factory)
        {
            Vector3 panel = PropCatalog.Size(PropCatalog.FencePanel);
            const float HalfWidth = 1.7f;    // just clear of the 3 m hutch body
            const float BackZ = 2f;
            const float WingLength = 1.7f;

            Vector3 Wall(float width) =>
                new Vector3(width / panel.x, ShedWallHeight / panel.y, PanelDepth / panel.z);

            into.Add(new DressingProp(PropCatalog.FencePanel, factory + new Vector2(0f, BackZ),
                                      Wall(HalfWidth * 2f), 180f, DressingZone.Factory));
            into.Add(new DressingProp(PropCatalog.FencePanel,
                                      factory + new Vector2(-HalfWidth, BackZ - WingLength * 0.5f),
                                      Wall(WingLength), 90f, DressingZone.Factory));
            into.Add(new DressingProp(PropCatalog.FencePanel,
                                      factory + new Vector2(HalfWidth, BackZ - WingLength * 0.5f),
                                      Wall(WingLength), -90f, DressingZone.Factory));

            // The woodpile and pots you'd expect beside a garden shed — tucked in behind the corners,
            // where they dress the silhouette without ever standing between the player and the core.
            Clutter(into, PropCatalog.LogStack, factory + new Vector2(-2.2f, 0.8f), 0.5f, 0f);
            Clutter(into, PropCatalog.Stump, factory + new Vector2(-2.05f, 1.4f), 0.35f, 0f);
            Clutter(into, PropCatalog.PotLarge, factory + new Vector2(2.1f, 0.7f), 0.35f, 0f);
            Clutter(into, PropCatalog.PotSmall, factory + new Vector2(2f, 1.3f), 0.42f, 30f);
        }

        private static void Clutter(List<DressingProp> into, string key, Vector2 at, float height, float yaw)
        {
            into.Add(new DressingProp(key, at, PropCatalog.ScaleToHeight(key, height), yaw,
                                      DressingZone.Factory));
        }

        private static float Range(System.Random rng, float a, float b) =>
            a + (float)rng.NextDouble() * (b - a);

        private static Vector2 Midpoint(in WallFace face) => (face.A + face.B) * 0.5f;

        /// <summary>Where the factories stand. A spawn ring is around EACH of these, not around x=0 —
        /// and every one of them is a place no shrub may grow, or a robot is born inside it.</summary>
        private static List<Vector2> Factories(MapData map)
        {
            var at = new List<Vector2>(2);
            foreach (MapEntity e in MapValidation.Kind(map, EntityKind.Factory)) at.Add(e.CenterXz);
            return at;
        }

        private static List<ArenaCover> CoverIn(MapData map)
        {
            var cover = new List<ArenaCover>();
            foreach (MapEntity e in MapValidation.Kind(map, EntityKind.Cover)) cover.Add(e.ToCover());
            return cover;
        }

        // ---------------------------------------------------------------- validation

        /// <summary>
        /// True when no prop can affect the fight; <paramref name="reason"/> names the first breach.
        ///
        /// A yard prop passes if it is flat enough to walk over, or it is beyond the walls, or — being
        /// inside a room — it hugs a wall, stays out of the doorway passages, keeps off the cover, and
        /// keeps off the factory's spawn ring. Factory props are judged the other way round: they must
        /// stay INSIDE the ring, so nothing is ever spawned standing in them.
        /// </summary>
        public static bool Validate(MapData map, IReadOnlyList<DressingProp> props,
                                    IReadOnlyList<ArenaCover> cover, out string reason)
        {
            if (map == null) { reason = "there is no map to dress"; return false; }

            var keepout = new Keepout(map, cover);

            foreach (DressingProp prop in props)
            {
                if (!PropCatalog.Has(prop.Key))
                { reason = $"'{prop.Key}' is not a prop in the kit"; return false; }

                if (prop.Scale.x <= 0f || prop.Scale.y <= 0f || prop.Scale.z <= 0f)
                { reason = $"{prop.Key} has a non-positive scale"; return false; }

                reason = keepout.Objection(prop);
                if (reason != null) return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// Everywhere a prop tall enough to matter may not stand, and why.
        ///
        /// The drivers ask this BEFORE they place anything, and <see cref="Validate"/> asks it again
        /// afterwards. That is deliberate: the rule is stated once, so the yard cannot be generated
        /// against one idea of "clear" and then judged against another — and a shrub with nowhere to
        /// go costs the map a shrub, not its entire dressing.
        /// </summary>
        private sealed class Keepout
        {
            private readonly Rect[] _interiors;
            private readonly Rect[] _doorways;
            private readonly List<ArenaCover> _cover = new List<ArenaCover>();
            private readonly List<Vector2> _rings;

            public Keepout(MapData map, IReadOnlyList<ArenaCover> cover)
            {
                _interiors = Interiors(map);
                _doorways = Doorways(map);
                _rings = Factories(map);

                if (cover != null) _cover.AddRange(cover);
            }

            /// <summary>Why this prop may not stand where it does, or null if it may.</summary>
            public string Objection(in DressingProp prop)
            {
                if (prop.Zone == DressingZone.Factory)
                {
                    // A shed prop belongs to ONE shed — the one it was placed around — and has to stay
                    // wholly inside THAT ring. Judging it against the nearest ring is what lets a
                    // second shed exist without every prop in the first one being measured from a
                    // factory forty metres away and failing.
                    return Nearest(prop.CenterXz, out Vector2 ring) &&
                           prop.FarthestFrom(ring) > MapValidation.SpawnRadius - SpawnClearance
                        ? $"{prop.Key} by the shed reaches the spawn ring — robots would be born inside it"
                        : null;
                }

                if (prop.IsFlat) return null;   // walked over, not around

                Rect footprint = prop.Footprint;

                foreach (Rect room in _interiors)
                    if (room.Overlaps(footprint))
                        return $"{prop.Key} at {prop.CenterXz} stands in the middle of the fight space";

                foreach (Rect door in _doorways)
                    if (door.Overlaps(footprint))
                        return $"{prop.Key} at {prop.CenterXz} stands in a doorway";

                // Every ring: a yard prop is judged against all of them, because there is no such thing
                // as being clear of one factory's spawn ring while standing in another's.
                foreach (Vector2 ring in _rings)
                    if (prop.DistanceTo(ring) < MapValidation.SpawnRadius + SpawnClearance)
                        return $"{prop.Key} at {prop.CenterXz} crowds a shed's spawn ring";

                foreach (ArenaCover c in _cover)
                    if (c.Footprint.Overlaps(footprint))
                        return $"{prop.Key} at {prop.CenterXz} grows through the {c.Name}";

                return null;
            }

            /// <summary>The factory nearest an XZ point. False when the map has no factory at all, in
            /// which case no shed prop can be judged — and none can have been placed either.</summary>
            private bool Nearest(Vector2 to, out Vector2 ring)
            {
                ring = Vector2.zero;
                float best = float.MaxValue;

                foreach (Vector2 r in _rings)
                {
                    float d = (r - to).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    ring = r;
                }

                return best < float.MaxValue;
            }
        }

        /// <summary>Each room of the map minus the band a prop may stand in — i.e. the space the fight
        /// needs. Nothing tall may touch these.</summary>
        public static Rect[] Interiors(MapData map)
        {
            if (map?.zones == null) return new Rect[0];

            var rooms = new List<Rect>(map.zones.Length);
            foreach (MapZone zone in map.zones)
                if (zone != null) rooms.Add(Deflate(zone.Footprint, EdgeBand));

            return rooms.ToArray();
        }

        /// <summary>Every way through, as the passage you walk down: the doorway the map's link cuts,
        /// inset from the frame the fence panels form, and reaching
        /// <see cref="DoorwayClearance"/> either side of the wall line.</summary>
        public static Rect[] Doorways(MapData map)
        {
            const float FrameInset = 0.6f;

            var doors = new List<Rect>();
            if (map?.links == null) return doors.ToArray();

            foreach (MapLink link in map.links)
            {
                if (link == null) continue;
                if (!MapGeometry.Doorway(map, link, out bool alongX, out float coord, out Span hole))
                    continue;

                float half = Mathf.Max(0.5f, hole.Length * 0.5f - FrameInset);

                doors.Add(alongX
                    ? new Rect(hole.Mid - half, coord - DoorwayClearance, half * 2f, DoorwayClearance * 2f)
                    : new Rect(coord - DoorwayClearance, hole.Mid - half, DoorwayClearance * 2f, half * 2f));
            }

            return doors.ToArray();
        }

        private static Rect Deflate(Rect r, float by)
        {
            float w = Mathf.Max(0f, r.width - by * 2f);
            float h = Mathf.Max(0f, r.height - by * 2f);
            return new Rect(r.xMin + by, r.yMin + by, w, h);
        }
    }
}
