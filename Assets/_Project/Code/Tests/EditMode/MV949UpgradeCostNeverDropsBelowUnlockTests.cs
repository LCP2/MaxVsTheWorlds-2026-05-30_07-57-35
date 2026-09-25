using System.Collections.Generic;
using NUnit.Framework;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-949, Lee: a node's own price must never DROP — before this ticket
    /// <see cref="CellSpend.UpgradeCostFor(int)"/> read 5 for a level 1-&gt;2 upgrade, less than the
    /// 10-cell unlock that always precedes it, so a node's cost tag visibly fell right after unlock
    /// before climbing back past it (unlock 10, then 5, 10, 15, 20...). The fix reads the ladder off
    /// <c>level + 1</c> instead of <c>level</c> (still capped at <see cref="CellSpend.UpgradeCostEscalationCap"/>),
    /// giving unlock 10, then 10, 15, 20, 20... — non-decreasing everywhere. <c>u_slt</c> is excluded —
    /// it has its own MV-623 flat 40-cell price, untouched by this ticket. Testing policy (MV-465): one
    /// new test, proven to fail on pre-fix HEAD — <c>CellSpend.UpgradeCostFor(1)</c> read 5 there, so
    /// the very first per-node sequence assertion below failed with
    /// "Expected: greater than or equal to 10 / But was: 5" (full output quoted in the MV-949 fix
    /// comment), and the pinned-sequence assertion failed with
    /// "Expected: [10, 10, 15, 20] / But was: [10, 5, 10, 15]".
    /// </summary>
    public sealed class MV949UpgradeCostNeverDropsBelowUnlockTests
    {
        [TearDown]
        public void TearDown() => RigBoard.ResetForTests();

        [Test]
        public void EveryNodesOwnCostSequenceIsNonDecreasing_AndWorld1sFirstFourValuesAreExact()
        {
            AssertNonDecreasingAcrossBoard(worldIndex: 0);
            AssertNonDecreasingAcrossBoard(worldIndex: 1);

            // AC1's pinned check: a World 1 node's own [unlock, upgrade(1), upgrade(2), upgrade(3)]
            // must read exactly 10, 10, 15, 20. p_rng is a plain (non-flat-priced, unmultiplied) node.
            RigBoard.UseWorld(0);
            var p_rngSequence = CostSequence("p_rng");
            Assert.That(p_rngSequence.GetRange(0, 4), Is.EqualTo(new List<int> { 10, 10, 15, 20 }),
                $"World 1's p_rng own [unlock, upgrade(1), upgrade(2), upgrade(3)] must read exactly " +
                $"[10, 10, 15, 20], got [{string.Join(", ", p_rngSequence.GetRange(0, 4))}]");
        }

        private static void AssertNonDecreasingAcrossBoard(int worldIndex)
        {
            RigBoard.UseWorld(worldIndex);
            foreach (string id in RigBoard.AllIds)
            {
                if (id == "u_slt") continue; // MV-623's own flat 40-cell price — untouched by this ticket

                List<int> sequence = CostSequence(id);
                for (int i = 1; i < sequence.Count; i++)
                {
                    Assert.That(sequence[i], Is.GreaterThanOrEqualTo(sequence[i - 1]),
                        $"World {worldIndex + 1} node '{id}' own cost sequence must never drop — " +
                        $"step {i} ({sequence[i]}) fell below step {i - 1} ({sequence[i - 1]}); " +
                        $"full sequence [{string.Join(", ", sequence)}]");
                }
            }
        }

        private static List<int> CostSequence(string id)
        {
            var sequence = new List<int> { CellSpend.UnlockCostFor(id) };
            int maxLevel = RigBoard.MaxLevel(id);
            for (int level = 1; level < maxLevel; level++)
                sequence.Add(CellSpend.UpgradeCostFor(id, level));
            return sequence;
        }
    }
}
