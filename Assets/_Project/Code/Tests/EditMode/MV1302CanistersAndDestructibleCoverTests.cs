using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1302: canister and destructible-cover placement across Worlds 3-5 (City/Reef/Mothership).
    /// Loads each shipped config via the runtime loader (<see cref="WorldLibrary"/> +
    /// <see cref="WorldMapLoader.TryLoad"/>, the same pattern <see cref="MV1314DestructibleCoverRuleTests"/>
    /// already uses) and asserts AC1's own checks against all three worlds in a single test method
    /// (testing policy MV-465, Rule 1: ONE new test per ticket): every area index &gt;= 3 that is not
    /// the stub has 2-4 canisters; every world marks destructible cover in &gt;= 20 areas; no
    /// destructible piece exceeds the footprint cap (or the Mothership's hull carve-out), or sits
    /// within the gate clearance. The remaining AC1 clause -- "every other cover piece's fields
    /// equal the base commit" -- is a diff-time property (verified by inspection, quoted in the fix
    /// comment), not a resolved value this engine can read back, so it is not asserted here.
    ///
    /// Fails on 48d926d (the commit this ticket started from): no config authors any "canisters" at
    /// all and no cover piece anywhere in city_config.json/world3_config.json/mothership_config.json
    /// carries hp &gt; 0 yet, so both the canister-count and destructible-area-count assertions below
    /// fail outright. See the fix comment for the captured failure output.
    /// </summary>
    public sealed class MV1302CanistersAndDestructibleCoverTests
    {
        private const int MinCanisters = 2;
        private const int MaxCanisters = 4;
        private const int RequiredDestructibleAreas = 20;

        private static readonly string[] WorldKeys =
        {
            WorldLibrary.City, WorldLibrary.World3, WorldLibrary.Mothership,
        };

        [Test]
        public void CityReefMothership_CanistersAndDestructibleCoverMatchTheRule()
        {
            foreach (string key in WorldKeys)
            {
                WorldConfig cfg = WorldLibrary.Load(key);
                Assert.IsNotNull(cfg, $"'{key}' must load for this test to mean anything");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason),
                    $"'{key}': {reason}");

                var gates = new List<Vector2>();
                foreach (MapEntity e in map.entities)
                    if (e != null && e.Kind == EntityKind.AreaGate) gates.Add(new Vector2(e.x, e.z));

                int areasWithDestructible = 0;

                foreach (WorldArea area in cfg.areas)
                {
                    if (area.id != "stub" && area.index >= 3)
                    {
                        int n = area.canisters?.Length ?? 0;
                        Assert.That(n, Is.InRange(MinCanisters, MaxCanisters),
                            $"'{key}' area '{area.id}' (index {area.index}) has {n} canisters");
                    }

                    bool hasDestructible = false;
                    foreach (WorldCover cover in area.cover)
                    {
                        if (cover == null || cover.hp <= 0f) continue;
                        hasDestructible = true;

                        float footprint = cover.width * cover.depth;
                        Assert.LessOrEqual(footprint, MapValidation.DestructibleCoverFootprintCapSqm,
                            $"'{key}' destructible '{cover.id}' in '{area.id}' is {footprint} sqm");

                        if (MapValidation.IsHullDressing(cover.dressing))
                            Assert.LessOrEqual(footprint, MapValidation.DestructibleCoverHullCarveOutSqm,
                                $"'{key}' destructible hull-dressed '{cover.id}' in '{area.id}' is {footprint} sqm");

                        var p = new Vector2(cover.x, cover.z);
                        foreach (Vector2 g in gates)
                            Assert.GreaterOrEqual(Vector2.Distance(p, g), MapValidation.DestructibleCoverGateClearance,
                                $"'{key}' destructible '{cover.id}' in '{area.id}' sits within " +
                                $"{MapValidation.DestructibleCoverGateClearance} m of a gate");
                    }

                    if (hasDestructible) areasWithDestructible++;
                }

                Assert.GreaterOrEqual(areasWithDestructible, RequiredDestructibleAreas,
                    $"'{key}' marks destructible cover in only {areasWithDestructible} areas, " +
                    $"short of the required {RequiredDestructibleAreas}");
            }
        }
    }
}
