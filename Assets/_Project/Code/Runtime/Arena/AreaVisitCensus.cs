using System.Collections.Generic;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Every combat area id Max's own position has ever physically crossed into this run (MV-829) —
    /// what an <c>area-entered:&lt;id&gt;</c> <see cref="GateCondition"/> asks. Same "one place that
    /// knows" idiom <see cref="MaxWorlds.Factories.FactoryCensus"/> already uses for
    /// replicators-destroyed, kept separate from it because this tracks area ids, not factories.
    ///
    /// Fed by <see cref="WorldRunner"/>, which already owns the real physical-crossing signal
    /// (<see cref="MaxWorlds.Enemies.AreaAccumulationDirector.PlayerCrossedIntoArea"/>) — this class
    /// itself reads no live scene state.
    /// </summary>
    public static class AreaVisitCensus
    {
        private static readonly HashSet<string> Entered = new HashSet<string>();

        /// <summary>Wipe the census. Called when a level starts building (the map engine), so a scene
        /// loaded a second time — in the game or in a test run — starts with no area-entered history
        /// left over from the previous level's (or the previous test's) run.</summary>
        public static void Reset() => Entered.Clear();

        public static void MarkEntered(string areaId)
        {
            if (!string.IsNullOrEmpty(areaId)) Entered.Add(areaId);
        }

        public static bool HasEntered(string areaId) =>
            !string.IsNullOrEmpty(areaId) && Entered.Contains(areaId);
    }
}
