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

            List<BackdropPiece> pieces = Build(path.Layout);

            if (!Validate(path.Layout, pieces, out string reason))
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

        /// <summary>The neighbourhood, derived from the arena so it moves when the arena does.</summary>
        public static List<BackdropPiece> Build(in BackyardPathLayout l)
        {
            var pieces = new List<BackdropPiece>(48);

            float lawnEdge = l.LawnHalfWidth + l.WallThickness;     // outer face of the lawn's wall
            float arenaEdge = l.ArenaHalfWidth + l.WallThickness;

            foreach (float side in new[] { -1f, 1f })
            {
                // The neighbours' fence, parallel to ours and a garden's width away. This is the
                // piece that does the most work: two fence lines with a gap between them is what
                // makes the yard read as one of several, rather than as the only place there is.
                Fence(pieces, side * (lawnEdge + Standoff + 2.5f), l.StartZ - 4f, l.GateZ + 2f);
                Fence(pieces, side * (arenaEdge + Standoff + 2f), l.GateZ + 2f, l.ArenaEndZ + 6f);

                // A hedge along it, softening the line. It stops short of the gate: past there the
                // boss arena is 6 m wider than the lawn, and a hedge that ran the full length would
                // have to jog outward to clear it — which is a fence's job, not a hedge's.
                Hedge(pieces, side * (lawnEdge + Standoff + 1.6f), l.StartZ - 2f, l.GateZ - 3f, 2.4f);

                // The neighbours themselves: a wall and a roof, close enough to clear our fence.
                House(pieces, new Vector2(side * (lawnEdge + 11f), 2f), 7.5f, 9f, 6.5f);
                House(pieces, new Vector2(side * (arenaEdge + 11f), 32f), 6.5f, 8f, 7.5f);
            }

            // The far end of the street, over the boss arena's back wall.
            House(pieces, new Vector2(-6f, l.ArenaEndZ + 16f), 9f, 11f, 7f);
            House(pieces, new Vector2(8f, l.ArenaEndZ + 19f), 8f, 9f, 6f);
            Hedge(pieces, 0f, l.ArenaEndZ + 6f, l.ArenaEndZ + 6.6f, 2.2f, alongX: true, length: 44f);

            // Trees, thrown around the ring so the rooflines aren't a wall of boxes. They sit in the
            // haze, so nobody counts them — five is a neighbourhood, twenty is a forest and a frame
            // budget.
            Tree(pieces, new Vector2(-(lawnEdge + 6f), -2f), 7.5f, PropCatalog.TreeDefault);
            Tree(pieces, new Vector2(lawnEdge + 7f, 9f), 8.5f, PropCatalog.TreeOak);
            Tree(pieces, new Vector2(-(arenaEdge + 6.5f), 27f), 9f, PropCatalog.TreeDefault);
            Tree(pieces, new Vector2(arenaEdge + 6f, 39f), 7f, PropCatalog.TreeThin);
            Tree(pieces, new Vector2(-13f, l.ArenaEndZ + 9f), 8f, PropCatalog.TreeOak);
            Tree(pieces, new Vector2(15f, l.ArenaEndZ + 8f), 7.5f, PropCatalog.TreeDefault);

            return pieces;
        }

        private static void Fence(List<BackdropPiece> pieces, float x, float zFrom, float zTo)
        {
            float length = zTo - zFrom;
            if (length <= 0f) return;

            pieces.Add(new BackdropPiece(
                new Vector3(x, 0f, (zFrom + zTo) * 0.5f),
                new Vector3(0.22f, 2.6f, length),
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
        private static void Hedge(List<BackdropPiece> pieces, float at, float from, float to,
                                  float height, bool alongX = false, float length = 0f)
        {
            float run = alongX ? length : to - from;
            if (run <= 0f) return;

            int n = Mathf.Max(2, Mathf.RoundToInt(run / 2.4f));
            float step = run / n;
            float middle = alongX ? (from + to) * 0.5f : from + (to - from) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                float along = -run * 0.5f + step * (i + 0.5f);
                float h = height * (1f + (i % 3 - 1) * 0.12f);   // deterministic, and never a straight top

                Vector3 center = alongX
                    ? new Vector3(at + along, 0f, middle)
                    : new Vector3(at, 0f, middle + along);

                pieces.Add(new BackdropPiece(
                    center,
                    new Vector3(step * 1.5f, h, step * 1.5f),
                    Surface.Foliage,
                    yaw: i * 47f,                                 // no two shrubs the same way round
                    model: Bushes[i % Bushes.Length]));
            }
        }

        private static readonly string[] Bushes =
        {
            PropCatalog.BushDetailed, PropCatalog.Bush, PropCatalog.BushLarge,
        };

        /// <summary>A house: one wall block and a pitched roof. It is seen from one side, at
        /// distance, through haze, over a 3.5 m fence — a box and a wedge is the whole of it, and
        /// anything more is polygons nobody will ever resolve.</summary>
        private static void House(List<BackdropPiece> pieces, Vector2 at, float width, float depth,
                                  float height)
        {
            pieces.Add(new BackdropPiece(new Vector3(at.x, 0f, at.y),
                                         new Vector3(width, height, depth), Surface.HouseWall));

            const float pitch = 30f;
            float slope = width * 0.66f;
            float rise = slope * 0.5f * Mathf.Sin(pitch * Mathf.Deg2Rad);

            // Two tipped slabs meeting over the ridge. Positioned by their centres — see Tipped.
            foreach (float s in new[] { -1f, 1f })
            {
                pieces.Add(new BackdropPiece(
                    new Vector3(at.x + s * width * 0.25f, height + rise, at.y),
                    new Vector3(slope, 0.32f, depth + 0.9f),
                    Surface.Roof,
                    roll: s * pitch));
            }
        }

        private static void Tree(List<BackdropPiece> pieces, Vector2 at, float height, string key)
        {
            pieces.Add(new BackdropPiece(new Vector3(at.x, 0f, at.y),
                                         new Vector3(height * 0.55f, height, height * 0.55f),
                                         Surface.Foliage, model: key));
        }

        // --- the rule --------------------------------------------------------------------------

        /// <summary>True when every piece of the backdrop is genuinely OUTSIDE the arena, by a
        /// margin. Scenery that reaches into the yard is not scenery — it's an obstacle the player
        /// can't shoot, and the one thing worse than a bare yard is one full of things that lie.</summary>
        public static bool Validate(in BackyardPathLayout l, IReadOnlyList<BackdropPiece> pieces,
                                    out string reason)
        {
            Rect[] rooms = Rooms(l);

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
        /// Room by room, and not one rectangle around the lot: the boss arena is 6 m wider than the
        /// lawn, and treating the whole yard as if it were arena-width would push the neighbours'
        /// hedge three metres further from the lawn fence than it needs to be — which, at a camera
        /// that can only see a few metres past the fence, is the difference between a neighbour and
        /// nothing at all.
        /// </summary>
        public static Rect[] Rooms(in BackyardPathLayout l)
        {
            float pad = l.WallThickness + MinClearance;

            return new[]
            {
                Room(l.PatioHalfWidth + pad, l.StartZ - pad, l.LawnStartZ),
                Room(l.LawnHalfWidth + pad, l.LawnStartZ, l.GateZ),
                Room(l.ArenaHalfWidth + pad, l.GateZ, l.ArenaEndZ + pad),
            };
        }

        private static Rect Room(float halfWidth, float zMin, float zMax) =>
            new Rect(-halfWidth, zMin, halfWidth * 2f, zMax - zMin);

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
