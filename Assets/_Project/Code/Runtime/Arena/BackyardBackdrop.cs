using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Models;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// The neighbourhood (YT-76): what you can see over the fence.
    ///
    /// The yard is a walled rectangle, and until now the world stopped at its fence — grass, then
    /// nothing. That is the difference between a garden and a diorama: a real backyard has a
    /// somewhere it is IN. So beyond the fence there are now the neighbours' houses, their fence,
    /// their hedges and their trees, receding into haze.
    ///
    /// Three rules, all of them consequences of the fixed ~72° camera:
    ///
    ///  * IT ONLY EXISTS AT THE EDGES OF FRAME. The camera looks down, not out — it can see maybe
    ///    six or eight metres past the fence and no further. So the backdrop is a close ring, not a
    ///    landscape, and the houses are close enough to be seen over a 3.5 m fence at all.
    ///
    ///  * IT IS SCENERY, NOT LEVEL. No colliders, nothing inside the arena, nothing that a robot or
    ///    a shot could ever reach. <see cref="Validate"/> holds it to that, and the tests run it.
    ///
    ///  * IT IS CHEAP. Boxes and a handful of kit trees, batched, no shadows cast from anything the
    ///    player will never stand next to. This ships to WebGL on a mobile tier.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardBackdrop : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BackyardBackdrop>() != null) return;
            if (FindFirstObjectByType<BackyardPath>() == null) return;   // not the Backyard
            new GameObject("BackyardBackdrop").AddComponent<BackyardBackdrop>();
        }

        /// <summary>How far past the arena's own walls the backdrop starts. Any closer and it reads
        /// as part of the yard; any further and the camera never sees it.</summary>
        public const float Standoff = 3.5f;

        /// <summary>Nothing in the backdrop may come within this of the arena — it is the guarantee
        /// that scenery can never be mistaken for cover, or block a shot.</summary>
        public const float MinClearance = 2f;

        private Transform _root;

        public int PieceCount { get; private set; }

        private void Awake()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null) return;

            MapData map = path.Map;
            if (map == null)
            {
                Debug.LogWarning("[BackyardBackdrop] no map loaded — nothing to stand behind.");
                return;
            }

            List<BackdropPiece> pieces = Build(map);

            if (!Validate(map, pieces, out string reason))
            {
                Debug.LogWarning($"[BackyardBackdrop] not built: {reason}");
                return;
            }

            _root = new GameObject("Neighbourhood").transform;
            _root.SetParent(transform, false);
            _root.gameObject.AddComponent<KeepsOwnMaterial>();

            foreach (BackdropPiece p in pieces) Place(p);

            StaticBatchingUtility.Combine(_root.gameObject);
            Debug.Log($"[BackyardBackdrop] {PieceCount} pieces beyond the fence.");
        }

        // --- what's out there -------------------------------------------------------------------

        public enum Surface { HouseWall, Roof, Hedge, Foliage }

        /// <summary>One piece of scenery: a box, or a kit model if <see cref="Model"/> is set.
        /// <see cref="Center"/>.y is the BASE for a box that stands on the ground, and the centre
        /// for one that's tipped (a roof) — <see cref="Tipped"/> says which.</summary>
        public readonly struct BackdropPiece
        {
            public readonly string Model;      // a kit prop key, or null for a box
            public readonly Surface Paint;
            public readonly Vector3 Center;
            public readonly Vector3 Size;
            public readonly float Yaw;
            public readonly float Roll;        // a roof's pitch

            public BackdropPiece(Vector3 center, Vector3 size, Surface paint, float yaw = 0f,
                                 string model = null, float roll = 0f)
            {
                Center = center; Size = size; Paint = paint; Yaw = yaw; Model = model; Roll = roll;
            }

            /// <summary>A tipped piece is positioned by its centre; everything else stands on the
            /// ground. A roof has no "base" to stand on, so asking it to have one only produces a
            /// roof buried in a house.</summary>
            public bool Tipped => Mathf.Abs(Roll) > 0.01f;

            /// <summary>Conservative XZ footprint, rotation included.</summary>
            public Rect Footprint
            {
                get
                {
                    float rad = Yaw * Mathf.Deg2Rad;
                    float c = Mathf.Abs(Mathf.Cos(rad)), s = Mathf.Abs(Mathf.Sin(rad));
                    float w = Size.x * c + Size.z * s;
                    float d = Size.x * s + Size.z * c;
                    return new Rect(Center.x - w * 0.5f, Center.z - d * 0.5f, w, d);
                }
            }
        }

        /// <summary>
        /// The neighbourhood, wrapped around the MAP's bounds so it moves when the level does.
        ///
        /// It used to be threaded down the sides of a straight corridor, band by band — which is a
        /// thing you can only write once you know the level is a corridor. The yard turns now, and it
        /// has rooms hanging off it, so the backdrop goes round the rectangle the whole map sits in.
        /// The four sides get the same pieces they always got: the neighbours' hedge, their fence
        /// beyond it, their houses beyond that, and trees to break up the rooflines.
        ///
        /// Everything is positioned by how far its INNER FACE stands off the yard, never by its
        /// centre. Place a house by its centre and it grows into the arena the day somebody makes it
        /// bigger; place it by its near edge and it cannot.
        /// </summary>
        public static List<BackdropPiece> Build(MapData map)
        {
            var pieces = new List<BackdropPiece>(160);
            if (map == null) return pieces;

            Rect b = map.Bounds();

            // The four lines the yard ends on, and which way is "away" from each.
            var sides = new[]
            {
                new Side(alongX: false, at: b.xMin, outward: -1f, from: b.yMin, to: b.yMax),
                new Side(alongX: false, at: b.xMax, outward: +1f, from: b.yMin, to: b.yMax),
                new Side(alongX: true, at: b.yMin, outward: -1f, from: b.xMin, to: b.xMax),
                new Side(alongX: true, at: b.yMax, outward: +1f, from: b.xMin, to: b.xMax),
            };

            foreach (Side side in sides)
            {
                // A hedge first, closest in, softening the line our own fence draws.
                Hedge(pieces, side, 2.4f);

                // Then the neighbours' fence, parallel to ours and a garden's width away. This is the
                // piece that does the most work: two fence lines with a gap between them is what makes
                // the yard read as one of several, rather than as the only place there is.
                Fence(pieces, side);
            }

            // The neighbours themselves: a wall and a roof each, close enough to clear our fence, and
            // spread round the ring so no two rooflines line up.
            House(pieces, sides[0], along: 0.25f, width: 7.5f, depth: 9f, height: 6.5f);
            House(pieces, sides[0], along: 0.75f, width: 6.5f, depth: 8f, height: 7.5f);
            House(pieces, sides[1], along: 0.30f, width: 7f, depth: 9f, height: 7f);
            House(pieces, sides[1], along: 0.80f, width: 8f, depth: 8f, height: 6f);
            House(pieces, sides[2], along: 0.5f, width: 9f, depth: 8f, height: 6f);
            House(pieces, sides[3], along: 0.35f, width: 9f, depth: 11f, height: 7f);
            House(pieces, sides[3], along: 0.7f, width: 8f, depth: 9f, height: 6f);

            // Trees, thrown around the ring so the rooflines aren't a wall of boxes. They sit in the
            // haze, so nobody counts them — a handful is a neighbourhood, twenty is a forest and a
            // frame budget.
            Tree(pieces, sides[0], along: 0.15f, height: 7.5f, key: PropCatalog.TreeDefault);
            Tree(pieces, sides[0], along: 0.55f, height: 9f, key: PropCatalog.TreeOak);
            Tree(pieces, sides[1], along: 0.2f, height: 8.5f, key: PropCatalog.TreeOak);
            Tree(pieces, sides[1], along: 0.6f, height: 7f, key: PropCatalog.TreeThin);
            Tree(pieces, sides[2], along: 0.25f, height: 8f, key: PropCatalog.TreeDefault);
            Tree(pieces, sides[3], along: 0.15f, height: 8f, key: PropCatalog.TreeOak);
            Tree(pieces, sides[3], along: 0.85f, height: 7.5f, key: PropCatalog.TreeDefault);

            return pieces;
        }

        /// <summary>One edge of the map's bounding rectangle, and which way is out of the yard from
        /// it. Every backdrop piece is placed in these terms — how far along the edge, how far beyond
        /// it — so a piece's clearance from the arena is a property of where it is asked to go, not
        /// something a coordinate has to be checked for afterwards.</summary>
        private readonly struct Side
        {
            public readonly bool AlongX;    // the edge runs along X (it is the near or far end)
            public readonly float At;       // its coordinate on the other axis
            public readonly float Outward;  // ±1: which way is away from the yard
            public readonly float From;
            public readonly float To;

            public Side(bool alongX, float at, float outward, float from, float to)
            {
                AlongX = alongX; At = at; Outward = outward; From = from; To = to;
            }

            public float Length => To - From;

            /// <summary>A point <paramref name="along"/> the edge (0..1) and <paramref name="beyond"/>
            /// metres past it, measured from the arena's own wall.</summary>
            public Vector2 Point(float along, float beyond)
            {
                float slide = From + Length * along;
                float off = At + Outward * (Standoff + beyond);
                return AlongX ? new Vector2(slide, off) : new Vector2(off, slide);
            }
        }

        /// <summary>Places a piece by its inner face: <paramref name="clear"/> is the gap between the
        /// arena's standoff and the nearest bit of it, whatever size it turns out to be.</summary>
        private static Vector2 Beyond(in Side side, float along, float clear, float size) =>
            side.Point(along, clear + size * 0.5f);

        private static void Fence(List<BackdropPiece> pieces, in Side side)
        {
            const float Thickness = 0.22f;
            const float Clear = 5f;         // out past the hedge

            // Long enough to close the corners — a fence line that stops at the bounds leaves a gap
            // you can see the void through from a camera that is looking slightly sideways.
            float length = side.Length + 12f;
            Vector2 at = Beyond(side, 0.5f, Clear, Thickness);

            pieces.Add(new BackdropPiece(
                new Vector3(at.x, 0f, at.y),
                side.AlongX ? new Vector3(length, 2.6f, Thickness) : new Vector3(Thickness, 2.6f, length),
                Surface.HouseWall));
        }

        /// <summary>
        /// A hedge, made of shrubs.
        ///
        /// It was a long green box, and from a camera looking straight down a long green box is not
        /// a hedge — it is a canal. Foliage reads as foliage because its top is broken: light
        /// catches the near leaves and misses the far ones, and the shadow it throws has a ragged
        /// edge. So the neighbours' hedges are the same kit shrubs the yard's own hedge is made of,
        /// grown big and set in a line, and they cost the same as the box did once they batch.
        /// </summary>
        private static void Hedge(List<BackdropPiece> pieces, in Side side, float height)
        {
            const float Clear = 1f;

            float run = side.Length + 8f;
            int n = Mathf.Max(2, Mathf.RoundToInt(run / 2.4f));
            float step = run / n;
            float size = step * 1.5f;

            for (int i = 0; i < n; i++)
            {
                float along = (step * (i + 0.5f) - 4f) / side.Length;
                float h = height * (1f + (i % 3 - 1) * 0.12f);   // deterministic, and never a straight top

                Vector2 at = Beyond(side, along, Clear, size);

                pieces.Add(new BackdropPiece(
                    new Vector3(at.x, 0f, at.y),
                    new Vector3(size, h, size),
                    Surface.Foliage,
                    yaw: i * 47f,                                 // no two shrubs the same way round
                    model: Bushes[i % Bushes.Length]));
            }
        }

        private static readonly string[] Bushes =
        {
            PropCatalog.BushDetailed, PropCatalog.Bush, PropCatalog.BushLarge,
        };

        /// <summary>A house: one wall block and a pitched roof, stood beside one edge of the yard with
        /// its front to us. It is seen from one side, at distance, through haze, over a 3.5 m fence —
        /// a box and a wedge is the whole of it, and anything more is polygons nobody will ever
        /// resolve.</summary>
        private static void House(List<BackdropPiece> pieces, in Side side, float along,
                                  float width, float depth, float height)
        {
            const float Clear = 3f;
            const float Pitch = 30f;

            // The roof overhangs the walls, so the roof is what the standoff is measured from.
            float across = depth + 0.9f;
            float yaw = side.AlongX ? 0f : 90f;      // face the yard, whichever edge we're on
            Vector2 at = Beyond(side, along, Clear, across);

            pieces.Add(new BackdropPiece(new Vector3(at.x, 0f, at.y),
                                         new Vector3(width, height, depth), Surface.HouseWall, yaw));

            float slope = width * 0.66f;
            float rise = slope * 0.5f * Mathf.Sin(Pitch * Mathf.Deg2Rad);
            Vector2 street = side.AlongX ? Vector2.right : Vector2.up;   // along the row of houses

            // Two tipped slabs meeting over the ridge. Positioned by their centres — see Tipped.
            foreach (float s in new[] { -1f, 1f })
            {
                Vector2 ridge = at + street * (s * width * 0.25f);

                pieces.Add(new BackdropPiece(
                    new Vector3(ridge.x, height + rise, ridge.y),
                    new Vector3(slope, 0.32f, across),
                    Surface.Roof,
                    yaw,
                    roll: s * Pitch));
            }
        }

        private static void Tree(List<BackdropPiece> pieces, in Side side, float along, float height,
                                 string key)
        {
            const float Clear = 1.5f;

            float spread = height * 0.55f;
            Vector2 at = Beyond(side, along, Clear, spread);

            pieces.Add(new BackdropPiece(new Vector3(at.x, 0f, at.y),
                                         new Vector3(spread, height, spread),
                                         Surface.Foliage, model: key));
        }

        // --- the rule --------------------------------------------------------------------------

        /// <summary>True when every piece of the backdrop is genuinely OUTSIDE the arena, by a
        /// margin. Scenery that reaches into the yard is not scenery — it's an obstacle the player
        /// can't shoot, and the one thing worse than a bare yard is one full of things that lie.</summary>
        public static bool Validate(MapData map, IReadOnlyList<BackdropPiece> pieces, out string reason)
        {
            Rect[] rooms = Rooms(map);

            foreach (BackdropPiece p in pieces)
            {
                Rect f = p.Footprint;

                foreach (Rect room in rooms)
                {
                    if (!room.Overlaps(f)) continue;
                    reason = $"{p.Paint} at {p.Center} reaches into the arena";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// The arena, room by room, each grown by its own wall and the clearance nothing may cross.
        ///
        /// Room by room, and not one rectangle around the lot: the boss arena is wider than the lawn
        /// and the shed hangs off its side, and treating the whole yard as if it were one block would
        /// push the neighbours' hedge metres further from the fence than it needs to be — which, at a
        /// camera that can only see a few metres past the fence, is the difference between a
        /// neighbour and nothing at all.
        /// </summary>
        public static Rect[] Rooms(MapData map)
        {
            if (map?.zones == null) return new Rect[0];

            float pad = map.wallThickness + MinClearance;
            var rooms = new List<Rect>(map.zones.Length);

            foreach (MapZone zone in map.zones)
            {
                if (zone == null) continue;

                Rect f = zone.Footprint;
                rooms.Add(new Rect(f.xMin - pad, f.yMin - pad, f.width + pad * 2f, f.height + pad * 2f));
            }

            return rooms.ToArray();
        }

        // --- building it -------------------------------------------------------------------------

        private void Place(BackdropPiece p)
        {
            GameObject go = p.Model != null
                ? ModelLibrary.Instantiate(PropCatalog.ResourceKey(p.Model), _root)
                : null;

            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(_root, false);
                go.transform.localScale = p.Size;
                go.transform.localRotation = Quaternion.Euler(0f, p.Yaw, p.Roll);
                go.transform.localPosition = p.Tipped
                    ? p.Center
                    : p.Center + new Vector3(0f, p.Size.y * 0.5f, 0f);

                go.GetComponent<MeshRenderer>().sharedMaterial = Paint(p.Paint);
            }
            else
            {
                go.transform.localScale = PropCatalog.ScaleToHeight(p.Model, p.Size.y);
                go.transform.localPosition = new Vector3(p.Center.x, 0f, p.Center.z);
                go.transform.localRotation = Quaternion.Euler(0f, p.Yaw, 0f);
            }

            go.name = p.Model ?? p.Paint.ToString();
            go.isStatic = true;

            // Nothing out here casts a shadow. The shadow map is a fixed budget spent on a fixed
            // distance, and a neighbour's roof eating it so it can throw a shadow onto a lawn it is
            // twenty metres from is a bad trade — the yard's own props need those texels.
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);

            PieceCount++;
        }

        /// <summary>Backdrop materials. Deliberately duller than anything in the yard: distance is a
        /// colour, and a neighbour's roof that reads as loudly as Max does is a neighbour's roof the
        /// player will keep looking at.</summary>
        private static Material Paint(Surface kind)
        {
            switch (kind)
            {
                case Surface.HouseWall: return Flat("backdrop_wall", new Color(0.36f, 0.33f, 0.29f));
                case Surface.Roof: return Flat("backdrop_roof", new Color(0.28f, 0.19f, 0.16f));

                // Duller and greyer than any green in the yard. A hedge out here is a flat slab
                // several metres across, and a slab takes the sun all at once — paint it the same
                // green as the shrubs the player is standing next to and it lights up like a
                // billboard at the edge of frame. Distance desaturates; so does this.
                default: return Flat("backdrop_hedge", new Color(0.14f, 0.20f, 0.12f));
            }
        }

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        private static Material Flat(string key, Color c)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = key,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.02f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);

            Cache[key] = m;
            return m;
        }
    }
}
