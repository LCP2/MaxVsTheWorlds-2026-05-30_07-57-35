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
        /// each run) — dodged along that same ring, never off it, around any authored cover a slot's
        /// own angle would otherwise land it inside (MV-459), and around the area's own shed spawn ring
        /// if it has one (MV-496).</summary>
        public static Vector3[] SeedPositions(WorldArea area, int count)
        {
            if (area == null || count <= 0) return Array.Empty<Vector3>();

            Vector2 center = area.CenterXz;
            float radius = Mathf.Min(area.size.w, area.size.d) * 0.3f;
            WorldShed shed = area.hasShed ? area.shed : null;

            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = ClearOfCover(center, radius, i * (Mathf.PI * 2f / count), area.cover, shed);
                positions[i] = new Vector3(
                    center.x + Mathf.Cos(angle) * radius,
                    0f,
                    center.y + Mathf.Sin(angle) * radius);
            }
            return positions;
        }

        /// <summary>How far apart, in radians, each cover-dodge attempt tries next, alternating either
        /// side of a slot's own authored angle — small enough that a hedge row costs a few degrees, not
        /// a lap of the room.</summary>
        private const float CoverDodgeStep = 2f * Mathf.Deg2Rad;

        /// <summary>How far <see cref="ClearOfCover"/> will search either side of a slot's own angle
        /// before giving up and standing at the authored spot anyway — half the ring, so a dodge can
        /// never cross over and land on the ring's own opposite slot.</summary>
        private const float MaxCoverDodgeOffset = Mathf.PI;

        /// <summary>The ring angle to actually stand at: <paramref name="baseAngle"/> itself, unless
        /// that seeds a robot on top of authored cover (MV-459 — nothing before this checked the
        /// garrison's deterministic ring against the area's own hedge rows, so a Bruiser could be, and
        /// on the shipped world1_config.json WAS, seeded dead inside a shrub across ten of eighteen
        /// areas — stuck on geometry, unreachable, silently starving that area's
        /// <see cref="MaxWorlds.Arena.DeathRunState.TryGrantAreaPart"/>) or inside the area's own shed
        /// spawn ring (MV-496 — the same "stuck on geometry"/starved-part failure mode, just against a
        /// different obstacle: on the shipped config a8's seed#8 landed 1.53 m into its shed's 4.3 m
        /// spawn clearance). Walks outward from the authored angle at the SAME radius, alternating
        /// sides, until clear of both — the radius never changes, so a dodged slot stays exactly as
        /// inset from the walls as the ring formula always promised (and so it never leaves
        /// <see cref="WorldArea.Footprint"/>, which the ring's own 0.3x-of-the-shorter-side inset
        /// already guarantees with room to spare).</summary>
        private static float ClearOfCover(Vector2 center, float radius, float baseAngle, WorldCover[] cover, WorldShed shed)
        {
            if (IsClearOfCover(center, radius, baseAngle, cover, shed)) return baseAngle;

            for (float offset = CoverDodgeStep; offset <= MaxCoverDodgeOffset; offset += CoverDodgeStep)
            {
                if (IsClearOfCover(center, radius, baseAngle + offset, cover, shed)) return baseAngle + offset;
                if (IsClearOfCover(center, radius, baseAngle - offset, cover, shed)) return baseAngle - offset;
            }

            return baseAngle; // every angle on the ring is fouled - stand at the authored spot anyway
        }

        private static bool IsClearOfCover(Vector2 center, float radius, float angle, WorldCover[] cover, WorldShed shed)
        {
            var point = new Vector2(center.x + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius);

            if (cover != null)
            {
                foreach (WorldCover c in cover)
                {
                    if (c == null) continue;

                    ArenaCover body = new MapEntity
                    {
                        x = c.x, z = c.z, width = c.width, height = c.height, depth = c.depth, shape = c.shape,
                    }.ToCover();

                    if (body.DistanceTo(point) < MapValidation.SpawnClearance) return false;
                }
            }

            if (shed != null && Vector2.Distance(point, new Vector2(shed.x, shed.z)) < MapValidation.SpawnRadius + MapValidation.SpawnClearance)
                return false;

            return true;
        }
    }
}
