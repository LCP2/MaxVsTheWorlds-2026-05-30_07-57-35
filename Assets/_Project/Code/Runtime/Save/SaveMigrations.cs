using System;
using System.Collections.Generic;

namespace MaxWorlds.Save
{
    /// <summary>
    /// Ordered list of save-schema migration steps, each moving a <see cref="SaveSlotData"/> from one
    /// <see cref="SaveSlotData.SaveVersion"/> to the next (MV-1144 - the City's world insertion needs
    /// every existing save's world-number fields renumbered, and this is the mechanism that will carry
    /// it). <see cref="Apply"/> is the only entry point <see cref="SaveSystem.Load"/> uses; it runs
    /// every step between the data's own version and the target, in list order, then stamps the
    /// result. Public rather than <c>internal</c>: no <c>InternalsVisibleTo</c> back to the EditMode
    /// test assembly exists in this project (see <c>WorldCatalog</c>'s own note on the same point).
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>One step: migrates a save at <see cref="FromVersion"/> to <see cref="FromVersion"/> + 1.</summary>
        public readonly struct Step
        {
            public readonly int FromVersion;
            public readonly Action<SaveSlotData> Migrate;

            public Step(int fromVersion, Action<SaveSlotData> migrate)
            {
                FromVersion = fromVersion;
                Migrate = migrate;
            }
        }

        /// <summary>MV-1176: the index the City took in <see cref="WorldCatalog"/> the moment save v2
        /// shipped — a frozen historical fact, not a live lookup. Must never be re-derived from
        /// <see cref="WorldCatalog.IndexOf"/>: a later world inserted ahead of the City would move that
        /// index forward, which would silently break this already-shipped step's own meaning.</summary>
        private const int CityInsertedAtSaveV2 = 2;

        private static readonly Step[] ShippedSteps =
        {
            // 0 -> 1 (MV-1144): adds SaveVersion itself. Changes no data - a save with no SaveVersion
            // key reads as 0 and is simply stamped forward to 1.
            new Step(0, data => { }),
            // 1 -> 2 (MV-1176): the City lands between the Stormdrain and the Reef - every save whose
            // world-number fields pointed at the Reef (or anything after it) must point one world later.
            new Step(1, data => ShiftWorldIndices(data, CityInsertedAtSaveV2)),
        };

        private static IReadOnlyList<Step> s_steps = ShippedSteps;
        private static int s_currentVersion = SaveSystem.CurrentSaveVersion;

        /// <summary>The version <see cref="SaveSystem.Load"/>/<see cref="SaveSystem.Save"/> migrate up
        /// to and stamp onto a slot - <see cref="SaveSystem.CurrentSaveVersion"/> in shipped code,
        /// overridable by <see cref="UseForTests"/> so a test can exercise a longer step list without
        /// the shipped constant changing underneath it.</summary>
        public static int CurrentVersion => s_currentVersion;

        /// <summary>Runs every step whose <see cref="Step.FromVersion"/> is at least <paramref name="data"/>'s
        /// own <see cref="SaveSlotData.SaveVersion"/> and below <paramref name="toVersion"/>, in list
        /// order, then sets <see cref="SaveSlotData.SaveVersion"/> to <paramref name="toVersion"/>. A
        /// no-op beyond that final stamp if the data is already at or past <paramref name="toVersion"/>.</summary>
        public static void Apply(SaveSlotData data, int toVersion)
        {
            int fromVersion = data.SaveVersion;
            foreach (Step step in s_steps)
            {
                if (step.FromVersion < fromVersion || step.FromVersion >= toVersion) continue;
                step.Migrate(data);
            }
            data.SaveVersion = toVersion;
        }

        /// <summary>Adds 1 to every one of the four world-number fields whose value is at least
        /// <paramref name="insertedAt"/>, leaving an "unknown" -1 alone. The shape a future
        /// world-insertion migration step will call with the City's own index; nothing shipped calls
        /// it yet (MV-1144 proves the mechanism before it is needed).</summary>
        public static void ShiftWorldIndices(SaveSlotData data, int insertedAt)
        {
            if (data.WorldIndex >= insertedAt) data.WorldIndex++;
            if (data.FurthestWorldIndex >= insertedAt) data.FurthestWorldIndex++;
            if (data.CheckpointWorldIndex >= insertedAt) data.CheckpointWorldIndex++;
            if (data.CheckpointRigBoardWorldIndex >= insertedAt) data.CheckpointRigBoardWorldIndex++;
        }

        /// <summary>Test seam: swap in a longer step list and a different "current version" so a test
        /// can exercise a migration the shipped list doesn't need yet. Pair with <see cref="ResetForTests"/>.</summary>
        public static void UseForTests(IReadOnlyList<Step> steps, int currentVersion)
        {
            s_steps = steps;
            s_currentVersion = currentVersion;
        }

        public static void ResetForTests()
        {
            s_steps = ShippedSteps;
            s_currentVersion = SaveSystem.CurrentSaveVersion;
        }
    }
}
