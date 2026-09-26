using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-964 (the one new test, per CC_AUTONOMY's testing policy): a world without an authored
    /// <see cref="WorldTransitions"/> row silently ships with no way into its next world — nothing else
    /// in the game would ever notice (World 2 -> World 3 shipped exactly this way until MV-959/964). This
    /// walks every from-world in <see cref="WorldLibrary"/> against the REAL configs and asserts only
    /// RESOLVED geometry — never an authored table constant re-read against itself (Testing Policy v2,
    /// Tier 1) — so a row that resolves onto a wall too close to a corner, on top of a gate or cover
    /// item, or through another area entirely, fails here, not in a build.
    ///
    /// Fails on base commit 9a3f895, which has no <c>WorldTransitions</c> type at all (CS0246).
    /// </summary>
    public sealed class WorldTransitionCoverageTests
    {
        private const float MinWallEndClearance = 1.5f;
        private const float MinNeighborClearance = 2.0f;

        [Test]
        public void EveryWorldHasADoorAndCorridorIntoTheNext()
        {
            float wallThickness = MapData.DefaultWallThickness;

            for (int i = 0; i < WorldLibrary.Count - 1; i++)
            {
                WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.KeyForIndex(i));
                WorldConfig toCfg = WorldLibrary.Load(WorldLibrary.KeyForIndex(i + 1));
                Assert.IsNotNull(fromCfg, $"world {i} failed to load — see the error log above.");
                Assert.IsNotNull(toCfg, $"world {i + 1} failed to load — see the error log above.");

                WorldTransitionEntry entry = WorldTransitions.For(i);
                Assert.IsNotNull(entry, $"world {i} has no WorldTransitions entry into the next world");

                // a. the resolved exit area IS the config's own final boss area.
                WorldArea exitArea = entry.ExitArea(fromCfg);
                Assert.IsNotNull(exitArea, $"world {i}'s transition resolves no exit area");
                WorldArea finalBossArea = fromCfg.AreaByIndex(fromCfg.dials.areaCount);
                Assert.AreEqual(finalBossArea.id, exitArea.id,
                    $"world {i}'s exit area '{exitArea.id}' is not its final boss area '{finalBossArea.id}'");

                // b. the door mouth clears both wall ends and every gate/cover authored in that area.
                Vector2 doorMouth = entry.ExitDoorMouth(fromCfg);
                AssertClearsWallEnds(doorMouth, exitArea, entry.ExitWall, wallThickness, i);
                AssertClearOfGates(doorMouth, fromCfg, exitArea, i);
                AssertClearOfCover(doorMouth, exitArea, i);

                // c. the corridor's own footprint intersects no area in world i (stub included).
                Rect corridor = entry.CorridorFootprint(fromCfg, wallThickness);
                foreach (WorldArea area in fromCfg.areas)
                {
                    if (area == null) continue;
                    Assert.IsFalse(corridor.Overlaps(area.Footprint),
                        $"world {i}'s corridor overlaps its own area '{area.id}'");
                }

                // d. the arrival shell's footprint intersects no area in world i+1, except sharing the
                // stub's own wall edge (Rect.Overlaps uses strict inequalities, so an exact edge-touch
                // never counts as an overlap).
                Rect arrival = entry.ArrivalFootprint(toCfg, wallThickness);
                foreach (WorldArea area in toCfg.areas)
                {
                    if (area == null) continue;
                    Assert.IsFalse(arrival.Overlaps(area.Footprint),
                        $"world {i + 1}'s arrival shell overlaps area '{area.id}'");
                }
            }

            Assert.IsNull(WorldTransitions.For(WorldLibrary.Count - 1),
                "the last world must have no exit transition — there is nowhere left to advance into");
        }

        private static void AssertClearsWallEnds(Vector2 doorMouth, WorldArea area, Wall wall, float wallThickness, int worldIndex)
        {
            Span span = area.WallSpan(wall);
            float along = area.WallRunsAlongX(wall) ? doorMouth.x : doorMouth.y;
            Assert.GreaterOrEqual(along - span.Min, MinWallEndClearance + wallThickness,
                $"world {worldIndex}'s door sits too close to its wall's start corner");
            Assert.GreaterOrEqual(span.Max - along, MinWallEndClearance + wallThickness,
                $"world {worldIndex}'s door sits too close to its wall's end corner");
        }

        private static void AssertClearOfGates(Vector2 doorMouth, WorldConfig cfg, WorldArea area, int worldIndex)
        {
            foreach (WorldGate gate in cfg.gates)
            {
                if (gate == null) continue;
                CheckGateEndpoint(doorMouth, area, gate.from, worldIndex);
                CheckGateEndpoint(doorMouth, area, gate.to, worldIndex);
            }
        }

        private static void CheckGateEndpoint(Vector2 doorMouth, WorldArea area, WorldGateEndpoint endpoint, int worldIndex)
        {
            if (endpoint == null || endpoint.area != area.id) return;
            if (!WallEnums.TryParse(endpoint.wall, out Wall wall)) return;

            Span span = area.WallSpan(wall);
            float along = span.Min + Mathf.Clamp01(endpoint.pos) * span.Length;
            float wallCoord = area.WallCoord(wall);
            Vector2 gateMouth = area.WallRunsAlongX(wall) ? new Vector2(along, wallCoord) : new Vector2(wallCoord, along);

            float distance = Vector2.Distance(doorMouth, gateMouth);
            Assert.GreaterOrEqual(distance, MinNeighborClearance,
                $"world {worldIndex}'s door mouth is only {distance:0.0} m from a gate mouth in the same area");
        }

        /// <summary>MV-964 §AC1.b: cover is CENTRE-based — <c>{x, z}</c> is the shape's own centre in
        /// world space, so clearance is the door mouth's distance to the nearest point on that
        /// centre ± half-extent box, never the raw centre-to-centre distance.</summary>
        private static void AssertClearOfCover(Vector2 doorMouth, WorldArea area, int worldIndex)
        {
            if (area.cover == null) return;
            foreach (WorldCover cover in area.cover)
            {
                if (cover == null) continue;
                float dx = Mathf.Max(0f, Mathf.Abs(doorMouth.x - cover.x) - cover.width * 0.5f);
                float dz = Mathf.Max(0f, Mathf.Abs(doorMouth.y - cover.z) - cover.depth * 0.5f);
                float clearance = Mathf.Sqrt(dx * dx + dz * dz);
                Assert.GreaterOrEqual(clearance, MinNeighborClearance,
                    $"world {worldIndex}'s door mouth is only {clearance:0.0} m from cover '{cover.id}'");
            }
        }
    }
}
