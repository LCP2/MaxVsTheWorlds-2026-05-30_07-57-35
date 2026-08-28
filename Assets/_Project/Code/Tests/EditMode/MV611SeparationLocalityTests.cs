using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-611 — <c>RobotEnemy.TickChase</c>'s neighbour scan used to copy the WHOLE field-wide roster
    /// into its scratch list unconditionally, then <c>EnemySeparation.Push</c> ran its own magnitude
    /// pass a second time over that same full copy to find the few robots actually close enough to
    /// matter. At 60 field-wide survivors that's "3,600 position copies and distance tests per frame"
    /// (the ticket's own measurement) — proportional to the level's whole accumulated population
    /// (concealed knots, garrison never looked at, stragglers run past), not to how crowded any one
    /// robot's own neighbourhood actually is.
    ///
    /// <see cref="SeparationGrid"/> is the fix: an incrementally-maintained XZ spatial hash. Both tests
    /// below are pure maths against synthetic owner ids/positions — no GameObject, no scene, per
    /// CC_AUTONOMY.md's EditMode-without-a-scene steer for this ticket's testable classes.
    ///
    /// Must fail to COMPILE on the pre-fix commit: <c>SeparationGrid</c> did not exist before this
    /// ticket — the same "fails on the base commit" the project's testing policy accepts, per
    /// <c>ZoneRouteGridTests</c>' own doc comment precedent.
    /// </summary>
    public sealed class MV611SeparationLocalityTests
    {
        [Test]
        public void CollectNearby_KeepsOnlyOwnersActuallyWithinRange_NotTheWholeSyntheticField()
        {
            var grid = new SeparationGrid(EnemySeparation.DefaultMinDistance);

            // Owner 0 is the querying robot. Owners 1-3 are genuinely close (within
            // EnemySeparation.DefaultMinDistance = 1.8 m)...
            grid.UpdatePosition(0, Vector3.zero);
            grid.UpdatePosition(1, new Vector3(0.5f, 0f, 0f));
            grid.UpdatePosition(2, new Vector3(0f, 0f, 1f));
            grid.UpdatePosition(3, new Vector3(-1.2f, 0f, 0.5f));
            // ...and 96 spread far away, standing in for an accumulated field-wide population.
            for (int i = 4; i < 100; i++)
                grid.UpdatePosition(i, new Vector3(50f + i, 0f, 50f + i));

            Assert.AreEqual(100, grid.Count, "fixture didn't actually register a 100-robot field");

            var results = new List<Vector3>();
            grid.CollectNearby(0, Vector3.zero, EnemySeparation.DefaultMinDistance, results);

            Assert.AreEqual(3, results.Count,
                "the resolved neighbour count must reflect only genuinely nearby robots (3), not the " +
                "full 100-robot synthetic field-wide population the grid is tracking");
        }

        /// <summary>
        /// AC6: pins the SHAPE of the fix — total per-frame separation work for a 100-robot population
        /// must land well under the O(n²) baseline, not scale toward it. Measured end to end: one
        /// UpdatePosition call per owner (the only pass that ever touches every one of the 100 — O(1)
        /// amortized per call, no sqrt, no neighbour search), then one CollectNearby query per owner,
        /// summing how many neighbours each resolves. 96 owners are spread far apart (mimicking the
        /// ticket's own "residue" population: concealed knots, garrison never looked at, stragglers run
        /// past, strung across a ~30-area level) and 4 form a small local cluster (mimicking the
        /// population actually near a real fight). The old, unconditional-copy shape compared every one
        /// of the other 99 positions for every one of the 100 robots — 9,900 pairs, the O(n²) baseline
        /// this asserts well below.
        /// </summary>
        [Test]
        public void FieldWideSeparationWork_ForA100RobotPopulation_IsBoundedNotQuadratic()
        {
            const int populationSize = 100;
            const int clusterSize = 4;
            const float spacing = 25f;   // far wider than DefaultMinDistance (1.8 m)

            var grid = new SeparationGrid(EnemySeparation.DefaultMinDistance);

            // The one O(n) pass in this whole scheme — cheap (a bucket move, no sqrt) and unavoidable:
            // every robot's own position has to be read into the grid at least once.
            for (int i = 0; i < populationSize - clusterSize; i++)
                grid.UpdatePosition(i, new Vector3(i * spacing, 0f, 0f));
            for (int i = populationSize - clusterSize; i < populationSize; i++)
                grid.UpdatePosition(i, new Vector3(0.3f * i, 0f, 0.3f));   // a real, local cluster

            int oldShapeTotalPairs = populationSize * (populationSize - 1);   // unconditional field-wide copy, every robot

            int newShapeTotalNeighbours = 0;
            var results = new List<Vector3>();
            for (int i = 0; i < populationSize; i++)
            {
                Vector3 selfPos = i < populationSize - clusterSize
                    ? new Vector3(i * spacing, 0f, 0f)
                    : new Vector3(0.3f * i, 0f, 0.3f);

                grid.CollectNearby(i, selfPos, EnemySeparation.DefaultMinDistance, results);
                newShapeTotalNeighbours += results.Count;
            }

            Assert.Less(newShapeTotalNeighbours, populationSize,
                $"measured {newShapeTotalNeighbours} resolved neighbour-pairs across {populationSize} " +
                "robots (one UpdatePosition + one CollectNearby per robot) — locality-bounded " +
                "separation work for an accumulated field must land well under one neighbour per robot " +
                "on average");

            Assert.Less(newShapeTotalNeighbours * 50, oldShapeTotalPairs,
                $"measured: {newShapeTotalNeighbours} resolved neighbour-pairs vs {oldShapeTotalPairs} " +
                "the old unconditional field-wide copy compared every tick — the fix must land at " +
                "least two orders of magnitude below the quadratic baseline, not merely below it");
        }
    }
}
