using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Where a concealed robot group hides (MV-363). Pure geometry, no transforms, so it stays
    /// unit-testable the same way <see cref="SpawnBias"/> is — this only picks the spot; placing a
    /// robot there and deciding whether it's clear of cover/other robots or on-screen is still
    /// <c>AreaAccumulationDirector</c>'s job, same as ordinary spawns.
    ///
    /// "Behind cover" here means the room's OWN authored cover (<see cref="ArenaCover"/>) — fences,
    /// hedges, planters — not just an empty patch of the far-side band every other spawn already
    /// uses (<see cref="SpawnBias.FarSideBounds"/>). Lee, 15 Aug: "groups of robots are placed
    /// behind fences and shrub rows", which is a claim about a specific prop, not merely "somewhere
    /// far from the door".
    /// </summary>
    public static class ConcealmentBias
    {
        /// <summary>How far past a cover piece's own footprint a concealed robot lands — enough
        /// clearance that it is never placed clipped through the prop's own collider.</summary>
        public const float Standoff = 1.2f;

        /// <summary>Radius candidates are jittered within around the anchor point, so a multi-robot
        /// group doesn't all land on the exact same spot behind the same piece of cover.</summary>
        public const float JitterRadius = 2.2f;

        public static bool InsideZone(MapZone zone, Vector2 xz) =>
            xz.x >= zone.XMin && xz.x <= zone.XMax && xz.y >= zone.ZMin && xz.y <= zone.ZMax;

        /// <summary>
        /// The room's own cover piece that sits deepest along <paramref name="awayFromDoor"/> — the
        /// one farthest from the gate — and a point just behind it, on the far side from the door,
        /// so the prop actually sits between the doorway and the robot (not merely near it). False
        /// when <paramref name="cover"/> has nothing inside <paramref name="zone"/> at all, which the
        /// caller falls back from onto the ordinary far-band placement.
        /// </summary>
        public static bool TryBehindDeepestCover(IReadOnlyList<ArenaCover> cover, MapZone zone,
            Vector3 awayFromDoor, float edgeMargin, out Vector2 point)
        {
            point = default;
            if (cover == null || zone == null) return false;

            bool alongX = Mathf.Abs(awayFromDoor.x) >= Mathf.Abs(awayFromDoor.z);
            float sign = alongX ? Mathf.Sign(awayFromDoor.x) : Mathf.Sign(awayFromDoor.z);
            if (awayFromDoor.sqrMagnitude < 1e-6f) sign = 1f;

            bool found = false;
            ArenaCover best = default;
            float bestDepth = float.NegativeInfinity;

            for (int i = 0; i < cover.Count; i++)
            {
                ArenaCover c = cover[i];
                if (!InsideZone(zone, c.CenterXz)) continue;

                float depth = (alongX ? c.CenterXz.x : c.CenterXz.y) * sign;
                if (!found || depth > bestDepth) { bestDepth = depth; best = c; found = true; }
            }

            if (!found) return false;

            float halfAlongAxis = alongX ? best.Size.x * 0.5f : best.Size.z * 0.5f;
            float push = (halfAlongAxis + Standoff) * sign;
            Vector2 candidate = alongX
                ? best.CenterXz + new Vector2(push, 0f)
                : best.CenterXz + new Vector2(0f, push);

            float xMin = zone.XMin + edgeMargin, xMax = zone.XMax - edgeMargin;
            float zMin = zone.ZMin + edgeMargin, zMax = zone.ZMax - edgeMargin;
            candidate.x = Mathf.Clamp(candidate.x, xMin, xMax);
            candidate.y = Mathf.Clamp(candidate.y, zMin, zMax);

            point = candidate;
            return true;
        }
    }
}
