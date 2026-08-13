using NUnit.Framework;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-375: cell and part drops used to be a flat per-kill rate, so a run's total for an area rode
    /// straight up the enemy population's exponential growth (<see cref="MaxWorlds.Enemies.DifficultyEngine.TargetBudget"/>)
    /// instead of an authored curve. <see cref="CellEconomyTuning.CellsForArea"/> and
    /// <see cref="CellEconomyTuning.PartsForArea"/> are now a designed straight line — this pins that
    /// shape: monotonic, and non-exponential (a constant step between areas, not a growing one).
    /// </summary>
    public sealed class CellEconomyTuningAreaCurveTests
    {
        private const int AreasToCheck = 10;

        [Test]
        public void CellsForArea_RisesMonotonically()
        {
            for (int area = 2; area <= AreasToCheck; area++)
            {
                Assert.That(CellEconomyTuning.CellsForArea(area), Is.GreaterThanOrEqualTo(CellEconomyTuning.CellsForArea(area - 1)),
                    $"area {area}'s cell budget must never fall below area {area - 1}'s");
            }
        }

        [Test]
        public void PartsForArea_RisesMonotonically()
        {
            for (int area = 2; area <= AreasToCheck; area++)
            {
                Assert.That(CellEconomyTuning.PartsForArea(area), Is.GreaterThanOrEqualTo(CellEconomyTuning.PartsForArea(area - 1)),
                    $"area {area}'s part budget must never fall below area {area - 1}'s");
            }
        }

        [Test]
        public void CellsForArea_IsLinearNotExponential()
        {
            // A straight line has a constant step between consecutive areas. An exponential curve's
            // step grows every area — that growing-step shape is exactly what this ticket removes.
            float firstStep = CellEconomyTuning.CellsForArea(2) - CellEconomyTuning.CellsForArea(1);
            for (int area = 3; area <= AreasToCheck; area++)
            {
                float step = CellEconomyTuning.CellsForArea(area) - CellEconomyTuning.CellsForArea(area - 1);
                Assert.That(step, Is.EqualTo(firstStep).Within(0.001f),
                    $"the cell curve's step into area {area} must match area 1→2's step — a straight line, not a compounding one");
            }
        }

        [Test]
        public void PartsForArea_IsLinearNotExponential()
        {
            float firstStep = CellEconomyTuning.PartsForArea(2) - CellEconomyTuning.PartsForArea(1);
            for (int area = 3; area <= AreasToCheck; area++)
            {
                float step = CellEconomyTuning.PartsForArea(area) - CellEconomyTuning.PartsForArea(area - 1);
                Assert.That(step, Is.EqualTo(firstStep).Within(0.001f),
                    $"the part curve's step into area {area} must match area 1→2's step — a straight line, not a compounding one");
            }
        }

        [Test]
        public void PartsForArea_RisesGentlerThanCells_LateAreaFloodIsCutHardestForParts()
        {
            // The ticket calls out parts specifically as the currency the late-arena flood must be cut
            // hardest from — pin that the authored part slope is shallower than the cell slope, so a
            // full run's part total climbs less steeply than its cell total by the final area.
            float cellGrowth = CellEconomyTuning.CellsForArea(AreasToCheck) - CellEconomyTuning.CellsForArea(1);
            float partGrowth = CellEconomyTuning.PartsForArea(AreasToCheck) - CellEconomyTuning.PartsForArea(1);
            Assert.That(partGrowth, Is.LessThan(cellGrowth),
                "parts must rise more gently across areas than cells — the scarce currency stays scarce");
        }
    }
}
