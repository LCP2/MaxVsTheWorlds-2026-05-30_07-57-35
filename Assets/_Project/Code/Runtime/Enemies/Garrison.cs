using System;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Origination — garrison seeding (World &amp; Difficulty Framework, Confluence MVW 34439170 §6,
    /// MV-269): each area is seeded with a pre-placed group on first entry, at authored positions,
    /// robots already there rather than popping in. Pure and unit-testable, same idiom as
    /// <see cref="DifficultyEngine"/> — takes a <see cref="WorldConfig"/> explicitly, reads no live
    /// run state, and owns no timing (a caller decides WHEN "first entry" spawns these).
    /// </summary>
    public static class Garrison
    {
        // The area's garrisonDensity dial (spec §7/§8.8) as the SHARE of its solved threat-budget
        // composition that's pre-placed on first entry — the rest streams in later via reinforcements
        // (SupplyLineNetwork) or the area's own spawner. An explicit, tunable interpretation (the spec
        // names the dial but not a formula), the same footing as ThreatValues' own placeholder numbers
        // until a playtest recalibrates it (ticket 4/MV-270).
        public const float NoneShare = 0f;
        public const float LightShare = 0.35f;
        public const float NormalShare = 0.6f;
        public const float HeavyShare = 0.85f;

        public static float DensityShare(string garrisonDensity) => garrisonDensity?.Trim().ToLowerInvariant() switch
        {
            "light" => LightShare,
            "normal" => NormalShare,
            "heavy" => HeavyShare,
            _ => NoneShare,
        };

        /// <summary>How many robots area <paramref name="areaIndex"/> is seeded with on first entry:
        /// its solved composition's total count (<see cref="WorldConfig.SolveComposition"/>) scaled by
        /// its own <c>garrisonDensity</c> share.</summary>
        public static int SeedCount(int areaIndex, WorldConfig cfg)
        {
            WorldArea area = cfg?.AreaByIndex(areaIndex);
            if (area == null) return 0;

            int total = cfg.SolveComposition(areaIndex).TotalCount;
            return Mathf.RoundToInt(total * DensityShare(area.garrisonDensity));
        }

        /// <summary>Deterministic, authored-not-random placement for <paramref name="count"/> robots
        /// inside <paramref name="area"/>: an evenly-spaced ring inset from the walls, so the same area
        /// and count always produce the same positions (robots already there, not popping in at random
        /// each run).</summary>
        public static Vector3[] SeedPositions(WorldArea area, int count)
        {
            if (area == null || count <= 0) return Array.Empty<Vector3>();

            Vector2 center = area.CenterXz;
            float radius = Mathf.Min(area.size.w, area.size.d) * 0.3f;

            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i * (Mathf.PI * 2f / count);
                positions[i] = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    0f,
                    center.y + Mathf.Sin(angle) * radius);
            }
            return positions;
        }
    }
}
