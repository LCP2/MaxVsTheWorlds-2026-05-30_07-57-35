using NUnit.Framework;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-375: cell drops used to be a flat per-kill rate, so a run's total for an area rode
    /// straight up the enemy population's exponential growth (<see cref="MaxWorlds.Enemies.DifficultyEngine.TargetBudget"/>)
    /// instead of an authored curve. <see cref="CellEconomyTuning.CellsForArea"/> is now a designed
    /// straight line — this pins that shape: monotonic, and non-exponential (a constant step between
    /// areas, not a growing one). MV-459: the matching <c>PartsForArea</c> curve and its tests were
    /// removed — MV-401 replaced the periodic part drop it fed with a one-per-arena grant that reads
    /// no curve at all.
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
    }
}
