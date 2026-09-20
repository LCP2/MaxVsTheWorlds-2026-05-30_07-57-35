using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-855 AC1 — the map's Replicator marker state (Steady/Blinking/DestroyedOutline), resolved by
    /// <see cref="ReplicatorMapModel"/> against the real, shipped World 2 config and a live
    /// <see cref="FactoryCensus"/>. Fails on base commit 34ff72a: <see cref="ReplicatorMapModel"/> and
    /// <see cref="ReplicatorMarkerState"/> do not exist there at all, so this does not compile. Tier 2
    /// (resolved values): asserts the model's resolved state, never an authored constant.
    /// </summary>
    public sealed class MV855ReplicatorMapMarkerTests
    {
        // MV-865 (World 2 re-author) renumbered every area in play order; these are the door's new ids
        // for the same physical Replicator areas (old a2/a3/a5/a8/a9/a10/a11/a18/a17/a16/a6).
        private static readonly string[] DoorAreas =
        {
            "a2", "a3", "a4", "a6", "a7", "a8", "a9", "a10", "a11", "a12", "a13",
        };

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Replicator> _doorReplicators = new List<Replicator>();

        [SetUp]
        public void SetUp() => FactoryCensus.Reset();

        [TearDown]
        public void TearDown()
        {
            FactoryCensus.Reset();
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            _doorReplicators.Clear();
        }

        [Test]
        public void RequiredReplicatorsBlink_UntilTheirGateOpens_ThenDestroyedReadsOutline()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsNotNull(cfg.Area("a14"), "setup failure: a14 not found");

            // Register a live Replicator for every area the a14 door names — the same "register by area
            // id" WorldRunner.Configure does. a14's own Replicator is deliberately left unregistered: this
            // isolates the a14 door's own condition from g24 (World 2's boss door, "replicators-destroyed:
            // all"), which this fixture's census never reaches regardless (nothing here is ever "every
            // Replicator this run has").
            foreach (string areaId in DoorAreas)
            {
                var go = new GameObject($"{areaId}_replicator");
                var rep = go.AddComponent<Replicator>();
                FactoryCensus.RegisterReplicator(rep, areaId);
                _spawned.Add(go);
                _doorReplicators.Add(rep);
            }

            HashSet<string> required = ReplicatorMapModel.RequiredAreaIds(cfg);
            Assert.IsTrue(required.Contains("a2"), "a2 is named in the still-closed a14 door's condition — must be required");
            Assert.IsFalse(required.Contains("a14"), "a14 is not named in its own door's condition — must not be required while the door is closed");

            Assert.AreEqual(ReplicatorMarkerState.Blinking, ReplicatorMapModel.Resolve("a2", alive: true, required),
                "a2's Replicator is required and alive — must resolve Blinking");
            Assert.AreEqual(ReplicatorMarkerState.Steady, ReplicatorMapModel.Resolve("a14", alive: true, required),
                "a Replicator in a14 is not named in the door condition — must resolve Steady");

            // Destroy every Replicator the door names — the gate is now satisfied.
            foreach (Replicator r in _doorReplicators) FactoryCensus.ReportReplicatorDestroyed(r);

            HashSet<string> requiredAfter = ReplicatorMapModel.RequiredAreaIds(cfg);
            Assert.AreEqual(ReplicatorMarkerState.DestroyedOutline, ReplicatorMapModel.Resolve("a2", alive: false, requiredAfter),
                "a destroyed Replicator must resolve DestroyedOutline");
            foreach (string areaId in DoorAreas)
                Assert.AreNotEqual(ReplicatorMarkerState.Blinking, ReplicatorMapModel.Resolve(areaId, alive: false, requiredAfter),
                    $"{areaId}'s gate is now open — its Replicators must not resolve Blinking");
        }
    }
}
