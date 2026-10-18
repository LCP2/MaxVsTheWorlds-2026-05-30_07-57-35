using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Pure maths + map-aware validity for where a Skitter lands (MV-1224). Reuses
    /// <see cref="BlinkerTeleport"/>'s own "rotate the line to the target, pick a side" idiom per the
    /// ticket's own "the Blinker already teleports; reuse its pattern" — but the Skitter zig-zags
    /// FORWARD along the line to Max rather than flanking to the side of it, and — unlike
    /// <see cref="BlinkerTeleport"/>, which never checks geometry at all (<see cref="RobotEnemy.TickChase"/>'s
    /// Blinker gate hands its flank point straight to <c>CharacterControllerSafety.SafeReposition</c>,
    /// which only guards PhysX crash conditions) — a hop must never land inside cover, inside or beyond
    /// a wall, or outside the current area (the ticket's own "never through a wall ... never on or
    /// inside cover ... never outside the map"). A map's walls are derived entirely from its zones'
    /// own edges (<see cref="MapZone"/>'s own doc comment), so "never through/beyond a wall" and
    /// "never outside the map" are both the SAME test: the landing point must stay inside the zone
    /// <paramref name="from"/> is already standing in, inset by the mover's own radius so its body
    /// never clips the wall it's inset from.
    /// </summary>
    public static class SkitterHop
    {
        /// <summary>The ticket's own "reappear 2.5-3.5 m further on".</summary>
        public const float MinDistance = 2.5f;
        public const float MaxDistance = 3.5f;

        /// <summary>The ticket's own "±35° off the line to Max".</summary>
        public const float AngleOffsetDeg = 35f;

        /// <summary>How far back toward <c>from</c> each failed candidate steps before trying again —
        /// the ticket's own "if no valid spot exists it hops a shorter distance".</summary>
        private const float SearchStep = 0.1f;

        /// <summary>The zig-zag direction: the line from <paramref name="from"/> to
        /// <paramref name="targetPos"/>, rotated <see cref="AngleOffsetDeg"/> to one side —
        /// <paramref name="sign"/> picks which, and the caller alternates it hop to hop so the path
        /// actually zig-zags rather than curving to one side.</summary>
        public static Vector3 HopDirection(Vector3 from, Vector3 targetPos, float sign)
        {
            Vector3 toTarget = targetPos - from;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-4f) toTarget = Vector3.forward;
            toTarget.Normalize();

            Quaternion rot = Quaternion.AngleAxis(AngleOffsetDeg * Mathf.Sign(sign), Vector3.up);
            return (rot * toTarget).normalized;
        }

        /// <summary>Resolves a valid landing point: starts at a random distance in
        /// [<see cref="MinDistance"/>, <see cref="MaxDistance"/>] along <see cref="HopDirection"/> and
        /// steps back toward <paramref name="from"/> in <see cref="SearchStep"/> increments until
        /// <see cref="IsClearLanding"/> passes. False (the ticket's own "skips the hop") if nothing
        /// clears all the way back to <paramref name="from"/> itself.</summary>
        public static bool TryResolveLanding(MapData map, Vector3 from, Vector3 targetPos, float sign,
            float clearRadius, out Vector3 landing)
        {
            landing = from;
            if (map == null) return false;

            Vector3 dir = HopDirection(from, targetPos, sign);
            float start = Random.Range(MinDistance, MaxDistance);

            for (float dist = start; dist > 0f; dist -= SearchStep)
            {
                Vector3 candidate = from + dir * dist;
                candidate.y = from.y;
                if (IsClearLanding(map, from, candidate, clearRadius))
                {
                    landing = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether <paramref name="point"/> is a legal Skitter landing from
        /// <paramref name="from"/>'s own area — inside the SAME zone <paramref name="from"/> is
        /// standing in, inset by <paramref name="clearRadius"/> from every edge, and clear of every
        /// authored cover piece by that same radius.</summary>
        public static bool IsClearLanding(MapData map, Vector3 from, Vector3 point, float clearRadius)
        {
            MapZone zone = map.ZoneAt(from.x, from.y, from.z);
            if (zone == null) return false;

            if (point.x < zone.XMin + clearRadius || point.x > zone.XMax - clearRadius) return false;
            if (point.z < zone.ZMin + clearRadius || point.z > zone.ZMax - clearRadius) return false;

            if (map.entities == null) return true;

            var p = new Vector2(point.x, point.z);
            foreach (MapEntity e in map.entities)
            {
                if (e == null || e.Kind != EntityKind.Cover) continue;
                if (e.ToCover().DistanceTo(p) < clearRadius) return false;
            }

            return true;
        }
    }
}
