using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Arena
{
    public enum CoverShape { Box, Cylinder }

    /// <summary>What a cover piece looks like once the art pass has been through (YT-75). The BOX is
    /// what the game reasons about — it is the collider, and none of these change it. This only says
    /// which kit models get built inside it.</summary>
    public enum CoverDressing { None, Tree, Hedge, Planter }

    /// <summary>One free-standing cover prop in the lawn (YT-68). Sits on the floor by construction:
    /// only its XZ centre is authored, the height follows from the size, so a prop can never be
    /// authored half-buried or floating.</summary>
    [System.Serializable]
    public struct ArenaCover
    {
        public string Name;
        public Vector2 CenterXz;
        public Vector3 Size;      // full world size (a cylinder's X/Z are its diameter)
        public CoverShape Shape;
        public CoverDressing Dressing;

        public ArenaCover(string name, Vector2 centerXz, Vector3 size, CoverShape shape,
                          CoverDressing dressing = CoverDressing.None)
        {
            Name = name; CenterXz = centerXz; Size = size; Shape = shape; Dressing = dressing;
        }

        /// <summary>World centre — Y derived so the prop rests on the ground plane (y=0).</summary>
        public Vector3 Center => new Vector3(CenterXz.x, Size.y * 0.5f, CenterXz.y);

        /// <summary>Footprint in XZ. A cylinder uses its bounding square — conservative, which is
        /// what we want when asking "does this block a spawn / a lane".</summary>
        public Rect Footprint => new Rect(
            CenterXz.x - Size.x * 0.5f, CenterXz.y - Size.z * 0.5f, Size.x, Size.z);

        /// <summary>Shortest distance from the footprint to an XZ point (0 if inside).</summary>
        public float DistanceTo(Vector2 p)
        {
            Rect r = Footprint;
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        public bool Overlaps(ArenaCover other) => Footprint.Overlaps(other.Footprint);
    }

    /// <summary>
    /// The cover set that turns the lawn from an empty box into a fight (YT-68): three props that
    /// break the straight line between Max and the swarm, so there are safe angles to swing through
    /// and loops to kite around instead of one open beeline.
    ///
    /// All three sit OFF the centre line and clear of the shed's spawn ring, so the critical path
    /// (patio → shed → gate) still reads at the fixed ~72° camera and robots never spawn inside a
    /// prop. Those aren't conventions to remember — they're invariants checked by
    /// <see cref="Validate"/> and by the EditMode tests, so a bad number fails the build, not a
    /// playtest.
    /// </summary>
    public static class BackyardCover
    {
        /// <summary>The centre strip kept clear of props, either side of x=0: the mission path.</summary>
        public const float CentreLaneHalfWidth = 3f;

        /// <summary>Cover must keep this clear of the side walls, so nothing forms a concave pocket
        /// a chasing robot can wedge itself into.</summary>
        public const float WallMargin = 2f;

        /// <summary>Narrowest gap the player must always have to run through, at any depth.</summary>
        public const float MinFreeChannel = 6f;

        /// <summary>Clearance a prop must leave OUTSIDE the shed's spawn ring. Merely not touching
        /// the ring isn't enough — a robot has a body (~0.5 m), so a prop tangent to the ring still
        /// spawns robots halfway inside it.</summary>
        public const float SpawnClearance = 0.8f;

        /// <summary>Three props, staggered left/right/left so kiting up the lawn is a slalom rather
        /// than a straight run: a low planter on the right, a tall tree on the left, and a hedge
        /// covering the right-hand approach to the shed.</summary>
        public static ArenaCover[] Default => new[]
        {
            new ArenaCover("Cover Planter", new Vector2(7f, 2f),     new Vector3(3.5f, 1.6f, 3.5f), CoverShape.Box,      CoverDressing.Planter),
            new ArenaCover("Cover Tree",    new Vector2(-6.5f, 8f),  new Vector3(2.4f, 4.4f, 2.4f), CoverShape.Cylinder, CoverDressing.Tree),
            new ArenaCover("Cover Hedge",   new Vector2(6.5f, 11f),  new Vector3(6f, 1.8f, 1.2f),   CoverShape.Box,      CoverDressing.Hedge),
        };

        /// <summary>Widest continuous gap a player can run through at depth <paramref name="z"/>,
        /// across the lawn. Guards against a prop set that walls the room off.</summary>
        public static float FreeChannelAt(BackyardPathLayout layout, IReadOnlyList<ArenaCover> cover, float z)
        {
            float min = -layout.LawnHalfWidth, max = layout.LawnHalfWidth;

            // Blocked x-intervals at this depth, in order.
            var blocked = new List<Vector2>();
            foreach (var c in cover)
            {
                Rect r = c.Footprint;
                if (z < r.yMin || z > r.yMax) continue;
                blocked.Add(new Vector2(Mathf.Max(r.xMin, min), Mathf.Min(r.xMax, max)));
            }
            blocked.Sort((a, b) => a.x.CompareTo(b.x));

            float widest = 0f, cursor = min;
            foreach (var b in blocked)
            {
                if (b.x > cursor) widest = Mathf.Max(widest, b.x - cursor);
                cursor = Mathf.Max(cursor, b.y);
            }
            return Mathf.Max(widest, max - cursor);
        }

        /// <summary>True when every prop is inside the lawn, off the mission line, clear of the
        /// walls, clear of the shed's spawn ring, not intersecting another prop, and leaves a
        /// runnable channel at every depth. <paramref name="reason"/> names the first breach.</summary>
        public static bool Validate(BackyardPathLayout layout, IReadOnlyList<ArenaCover> cover,
                                    float shedZ, float spawnRadius, out string reason)
        {
            var ring = new Vector2(0f, shedZ);

            for (int i = 0; i < cover.Count; i++)
            {
                ArenaCover c = cover[i];
                Rect r = c.Footprint;

                if (r.yMin < layout.LawnStartZ || r.yMax > layout.GateZ)
                { reason = $"{c.Name} is outside the lawn along Z"; return false; }

                if (Mathf.Max(Mathf.Abs(r.xMin), Mathf.Abs(r.xMax)) > layout.LawnHalfWidth - WallMargin)
                { reason = $"{c.Name} is too close to a side wall (pocket a robot could wedge in)"; return false; }

                if (r.xMin < CentreLaneHalfWidth && r.xMax > -CentreLaneHalfWidth)
                { reason = $"{c.Name} blocks the centre line of the mission path"; return false; }

                if (c.DistanceTo(ring) < spawnRadius + SpawnClearance)
                { reason = $"{c.Name} crowds the shed's spawn ring — robots would spawn inside it"; return false; }

                if (c.Size.y < 1f)
                { reason = $"{c.Name} is too short to break a chase"; return false; }

                for (int j = i + 1; j < cover.Count; j++)
                {
                    if (c.Overlaps(cover[j]))
                    { reason = $"{c.Name} overlaps {cover[j].Name}"; return false; }
                }
            }

            // Sweep the room in 0.5 m steps: the player must always have somewhere to run.
            for (float z = layout.LawnStartZ; z <= layout.GateZ; z += 0.5f)
            {
                if (FreeChannelAt(layout, cover, z) < MinFreeChannel)
                { reason = $"cover pinches the lawn shut at z={z}"; return false; }
            }

            reason = null;
            return true;
        }
    }
}
