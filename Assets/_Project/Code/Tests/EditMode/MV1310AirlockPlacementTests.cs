using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1310 (one new EditMode test, MV-465 Rule 1) — the Mothership's "airlocks[]" placements,
    /// added by <c>Tools/World5/add_airlock_placements.py</c>: a1-a3 carry one each, a30 carries
    /// exactly two (MV-1313 needs both), every other area carries one or two. Every placement must
    /// sit ON the area's own outline, >= 4 m from every resolved gate doorway, >= 6 m from every
    /// Assembly Cradle in the whole map, and clear a 12 m open-floor cone in front (apex at the
    /// door, widening to the door's own 3 m built width by 12 m out, matching
    /// <see cref="WorldMapLoader.AirlockWidth"/>) of every SOLID cover piece in its own area.
    ///
    /// Fails on base commit (the commit before this ticket): every area's <c>airlocks</c> defaults
    /// empty (<c>WorldArea.airlocks = Array.Empty&lt;WorldAirlock&gt;()</c>, its own doc comment:
    /// "placement is MV-1310's own"), so the very first per-area count assertion (a1 must carry 1-2)
    /// fails with an actual count of 0.
    ///
    /// Tier 2 (resolved values), testing policy v2: per-area airlock COUNT is the authored content
    /// itself (same "the list length IS the content" idiom <c>MV1233MothershipLayoutTests</c> already
    /// uses for cradle/garrison counts), but every placement's position/facing is read off the
    /// LOADED <see cref="MapData"/>'s own resolved <see cref="MapEntity"/> (post
    /// <see cref="WorldMapLoader.TryLoad"/>) rather than the raw JSON — including every gate's own
    /// resolved doorway position, which the engine computes from a wall + fraction, never
    /// re-derived here. The cone-clear geometry is reimplemented independently of the Python
    /// generator's own copy (generate-then-verify) rather than imported from it.
    /// </summary>
    public sealed class MV1310AirlockPlacementTests
    {
        private const int ExpectedAreaCount = 30;
        private const float OutlineEpsilon = 0.05f;
        private const float MinGateGap = 4f;
        private const float MinCradleGap = 6f;
        private const float ConeRange = 12f;
        private const float ConeHalfWidthAtRange = 1.5f; // WorldMapLoader.AirlockWidth / 2
        private const float DistanceTolerance = 1e-3f;

        private static readonly Dictionary<string, Vector2> FacingDir = new Dictionary<string, Vector2>
        {
            ["N"] = new Vector2(0f, 1f),
            ["E"] = new Vector2(1f, 0f),
            ["S"] = new Vector2(0f, -1f),
            ["W"] = new Vector2(-1f, 0f),
        };

        [Test]
        public void AllThirtyAreas_AirlocksOnOutline_ClearOfGatesCradlesAndCoverCone()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            var combatAreas = new List<WorldArea>();
            for (int i = 1; i <= ExpectedAreaCount; i++)
            {
                WorldArea a = cfg.Area($"a{i}");
                Assert.IsNotNull(a, $"area 'a{i}' must exist");
                combatAreas.Add(a);
            }

            var gatePoints = map.entities
                .Where(e => e != null && e.Kind == EntityKind.AreaGate)
                .Select(e => new Vector2(e.x, e.z))
                .ToList();
            Assert.IsTrue(gatePoints.Count > 0, "the loaded map must resolve at least one gate");

            var cradlePoints = combatAreas
                .SelectMany(a => a.cradles ?? System.Array.Empty<WorldCradle>())
                .Select(cr => new Vector2(cr.x, cr.z))
                .ToList();
            Assert.IsTrue(cradlePoints.Count > 0, "the loaded map must author at least one cradle");

            var failures = new List<string>();

            for (int i = 0; i < ExpectedAreaCount; i++)
            {
                WorldArea area = combatAreas[i];
                WorldAirlock[] airlocks = area.airlocks ?? System.Array.Empty<WorldAirlock>();
                int count = airlocks.Length;

                if (area.index == 30)
                    Assert.AreEqual(2, count, "a30 must carry exactly 2 airlocks (MV-1313 needs both)");
                else
                    Assert.IsTrue(count == 1 || count == 2, $"area '{area.id}' must carry 1-2 airlocks, found {count}");

                foreach (WorldAirlock al in airlocks)
                {
                    MapEntity entity = map.entities.FirstOrDefault(e => e != null && e.Kind == EntityKind.Airlock && e.id == al.id);
                    Assert.IsNotNull(entity, $"airlock '{al.id}' in '{area.id}' never resolved onto the loaded map");

                    float px = entity.x, pz = entity.z;

                    // --- on the area's own outline ---
                    bool onVerticalWall = Mathf.Abs(px - area.XMin) <= OutlineEpsilon || Mathf.Abs(px - area.XMax) <= OutlineEpsilon;
                    bool onHorizontalWall = Mathf.Abs(pz - area.ZMin) <= OutlineEpsilon || Mathf.Abs(pz - area.ZMax) <= OutlineEpsilon;
                    bool withinX = px >= area.XMin - OutlineEpsilon && px <= area.XMax + OutlineEpsilon;
                    bool withinZ = pz >= area.ZMin - OutlineEpsilon && pz <= area.ZMax + OutlineEpsilon;
                    if (!((onVerticalWall && withinZ) || (onHorizontalWall && withinX)))
                        failures.Add($"'{al.id}' in '{area.id}' at ({px:0.00},{pz:0.00}) is not on the area's own outline");

                    // --- >= 4 m from every resolved gate ---
                    foreach (Vector2 g in gatePoints)
                    {
                        float dist = Vector2.Distance(new Vector2(px, pz), g);
                        if (dist < MinGateGap - DistanceTolerance)
                            failures.Add($"'{al.id}' in '{area.id}' is {dist:0.00} m from a gate (floor {MinGateGap} m)");
                    }

                    // --- >= 6 m from every Assembly Cradle ---
                    foreach (Vector2 c in cradlePoints)
                    {
                        float dist = Vector2.Distance(new Vector2(px, pz), c);
                        if (dist < MinCradleGap - DistanceTolerance)
                            failures.Add($"'{al.id}' in '{area.id}' is {dist:0.00} m from a cradle (floor {MinCradleGap} m)");
                    }

                    // --- a 12 m open-floor cone ahead, clear of every SOLID cover piece in this area ---
                    if (!FacingDir.TryGetValue(entity.facing, out Vector2 dir))
                    {
                        failures.Add($"'{al.id}' in '{area.id}' has an unrecognised facing '{entity.facing}'");
                        continue;
                    }

                    foreach (WorldCover c in area.cover ?? System.Array.Empty<WorldCover>())
                    {
                        if (c.coverKind != "solid") continue;
                        float halfW = c.width / 2f, halfD = c.depth / 2f;
                        var corners = new[]
                        {
                            new Vector2(c.x - halfW, c.z - halfD), new Vector2(c.x + halfW, c.z - halfD),
                            new Vector2(c.x - halfW, c.z + halfD), new Vector2(c.x + halfW, c.z + halfD),
                            new Vector2(c.x, c.z),
                        };
                        foreach (Vector2 corner in corners)
                        {
                            if (PointInCone(corner, new Vector2(px, pz), dir))
                            {
                                failures.Add($"'{al.id}' in '{area.id}': cover '{c.id}' intrudes on its 12 m open-floor cone");
                                break;
                            }
                        }
                    }
                }
            }

            Assert.IsTrue(failures.Count == 0, "MV-1310 airlock placement failures:\n" + string.Join("\n", failures));
        }

        private static bool PointInCone(Vector2 q, Vector2 p, Vector2 dir)
        {
            Vector2 delta = q - p;
            float depth = Vector2.Dot(delta, dir);
            if (depth <= 0f || depth > ConeRange) return false;
            Vector2 perp = delta - depth * dir;
            float maxLateral = depth * (ConeHalfWidthAtRange / ConeRange);
            return perp.magnitude <= maxLateral;
        }
    }
}
