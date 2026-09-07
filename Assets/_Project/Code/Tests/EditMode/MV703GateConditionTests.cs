using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-703 AC1 — the <c>replicators-destroyed:&lt;list|all&gt;</c> grammar, parsed by
    /// <see cref="GateCondition.TryParse"/> and resolved against a live <see cref="FactoryCensus"/>.
    /// Fails on the MV-697 merge commit (551df50): no <see cref="GateCondition"/> type exists there at
    /// all. Tier 2 (resolved values): asserts <see cref="GateCondition.IsSatisfied"/>'s resolved bool
    /// against the census, never an authored constant.
    /// </summary>
    public sealed class MV703GateConditionTests
    {
        private GameObject _a2Go, _a5Go, _a9Go;
        private Replicator _a2, _a5, _a9;

        [SetUp]
        public void SetUp()
        {
            FactoryCensus.Reset();

            _a2Go = new GameObject("a2_replicator");
            _a5Go = new GameObject("a5_replicator");
            _a9Go = new GameObject("a9_replicator");
            _a2 = _a2Go.AddComponent<Replicator>();
            _a5 = _a5Go.AddComponent<Replicator>();
            _a9 = _a9Go.AddComponent<Replicator>();

            FactoryCensus.RegisterReplicator(_a2, "a2");
            FactoryCensus.RegisterReplicator(_a5, "a5");
            FactoryCensus.RegisterReplicator(_a9, "a9");
        }

        [TearDown]
        public void TearDown()
        {
            FactoryCensus.Reset();
            if (_a2Go != null) Object.DestroyImmediate(_a2Go);
            if (_a5Go != null) Object.DestroyImmediate(_a5Go);
            if (_a9Go != null) Object.DestroyImmediate(_a9Go);
        }

        [Test]
        public void ReplicatorsDestroyed_ListUnlocksOnlyItsOwnAreas_AllWaitsForEveryReplicator()
        {
            Assert.IsTrue(GateCondition.TryParse("replicators-destroyed:a2,a5", out GateCondition list, out string reason),
                $"parse must succeed: {reason}");
            Assert.IsFalse(list.IsSatisfied(null, 0), "a2 and a5 are both still standing — must be locked");

            FactoryCensus.ReportReplicatorDestroyed(_a2);
            Assert.IsFalse(list.IsSatisfied(null, 0), "a5 is still standing — must stay locked");

            FactoryCensus.ReportReplicatorDestroyed(_a5);
            Assert.IsTrue(list.IsSatisfied(null, 0),
                "a2 and a5 are both down — a9 is not named in this list, so it must unlock regardless of a9");

            Assert.IsTrue(GateCondition.TryParse("replicators-destroyed:all", out GateCondition all, out reason),
                $"parse must succeed: {reason}");
            Assert.IsFalse(all.IsSatisfied(null, 0), "a9 is still standing — 'all' must stay locked");

            FactoryCensus.ReportReplicatorDestroyed(_a9);
            Assert.IsTrue(all.IsSatisfied(null, 0), "every registered replicator is now down — 'all' must unlock");
        }
    }
}
