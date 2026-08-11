using MaxWorlds.Arena;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Narrows a room's spawn rectangle to the side away from its entry door (MV-323 — feedback, Max
    /// 0.7 doc: robots and a mob were arriving right at the entrance because ambient placement never
    /// looked at where the door into the room actually was). Pure maths, no transforms, so this stays
    /// unit-testable the same way <see cref="EnemyFormation"/>/<see cref="EnemySeparation"/> are.
    /// </summary>
    public static class SpawnBias
    {
        /// <summary>Fraction of the room's span, along the door-to-interior axis, excluded from the
        /// door-adjacent (near) side before a point is sampled. 0.5 keeps candidates to the far half of
        /// the room — enough to read as "away from the door" without squeezing every spawn against the
        /// far wall, which would just trade one cluster for another.</summary>
        public const float NearFractionExcluded = 0.5f;

        /// <summary>
        /// The sub-rectangle of <paramref name="zone"/> (already inset by <paramref name="edgeMargin"/>
        /// on every side, same as the un-biased search) that lies on the far side from the door,
        /// relative to <paramref name="awayFromDoor"/> — the world-space direction from the room entered
        /// just before this one, through the doorway, into this room (see
        /// <see cref="MapRuntime.EntryDirection"/>). Only ever narrows along whichever axis (X or Z) that
        /// direction predominantly runs, matching how every doorway in this map cuts a straight,
        /// axis-aligned wall. A zero (or near-zero) direction — no known entry, e.g. area 1 — returns the
        /// room's full inset bounds unchanged, exactly what the un-biased search used before MV-323.
        /// </summary>
        public static Rect FarSideBounds(MapZone zone, Vector3 awayFromDoor, float edgeMargin)
        {
            float xMin = zone.XMin + edgeMargin, xMax = zone.XMax - edgeMargin;
            float zMin = zone.ZMin + edgeMargin, zMax = zone.ZMax - edgeMargin;

            if (awayFromDoor.sqrMagnitude < 1e-6f)
                return Rect.MinMaxRect(xMin, zMin, xMax, zMax);

            if (Mathf.Abs(awayFromDoor.x) >= Mathf.Abs(awayFromDoor.z))
            {
                float cut = Mathf.Lerp(xMin, xMax, NearFractionExcluded);
                if (awayFromDoor.x >= 0f) xMin = cut; else xMax = cut;
            }
            else
            {
                float cut = Mathf.Lerp(zMin, zMax, NearFractionExcluded);
                if (awayFromDoor.z >= 0f) zMin = cut; else zMax = cut;
            }

            return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
        }

        /// <summary>
        /// Slices <paramref name="bounds"/> (the far-side rectangle from <see cref="FarSideBounds"/>)
        /// into <paramref name="totalBands"/> equal strata along the door-to-interior axis, ordered
        /// nearest-to-the-gate first, and returns the one <paramref name="spawnIndex"/> falls into
        /// (MV-324 — feedback, Max 0.7 doc: robots landing at roughly the same distance from the gate
        /// all closed on Max together, reading as one simultaneous mob instead of a staggered approach).
        /// Cycling <paramref name="spawnIndex"/> through the bands guarantees a spread of distances from
        /// the gate regardless of how the room's per-point RNG happens to land — pure per-point
        /// randomness could still get unlucky and cluster an entire batch in one band by chance.
        /// <paramref name="totalBands"/> &lt;= 1 returns <paramref name="bounds"/> unchanged.
        /// </summary>
        public static Rect StaggerBand(Rect bounds, Vector3 awayFromDoor, int spawnIndex, int totalBands)
        {
            if (totalBands <= 1) return bounds;

            int band = ((spawnIndex % totalBands) + totalBands) % totalBands;
            float t0 = (float)band / totalBands;
            float t1 = (float)(band + 1) / totalBands;

            if (Mathf.Abs(awayFromDoor.x) >= Mathf.Abs(awayFromDoor.z))
            {
                bool increasing = awayFromDoor.x >= 0f;
                float near = increasing ? bounds.xMin : bounds.xMax;
                float far = increasing ? bounds.xMax : bounds.xMin;
                float a = Mathf.Lerp(near, far, t0), b = Mathf.Lerp(near, far, t1);
                return Rect.MinMaxRect(Mathf.Min(a, b), bounds.yMin, Mathf.Max(a, b), bounds.yMax);
            }
            else
            {
                bool increasing = awayFromDoor.z >= 0f;
                float near = increasing ? bounds.yMin : bounds.yMax;
                float far = increasing ? bounds.yMax : bounds.yMin;
                float a = Mathf.Lerp(near, far, t0), b = Mathf.Lerp(near, far, t1);
                return Rect.MinMaxRect(bounds.xMin, Mathf.Min(a, b), bounds.xMax, Mathf.Max(a, b));
            }
        }
    }
}
