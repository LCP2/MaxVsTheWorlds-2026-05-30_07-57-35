using System;

namespace MaxWorlds.Save
{
    /// <summary>
    /// MV-1291: one device-wide flag, outside any slot — persisted in its own file
    /// (<c>profile.json</c>, written/read by <see cref="SaveSystem.LoadProfile"/>/
    /// <see cref="SaveSystem.SaveProfile"/>), separate from every per-slot <c>save_slot_N.json</c>. A
    /// slot RESET (<see cref="SaveSystem.Delete"/>) only ever removes that slot's own file, so it can
    /// never clear this.
    /// </summary>
    [Serializable]
    public sealed class ProfileData
    {
        /// <summary>True once any slot has reached the real ending (<see cref="SaveSystem.MarkEndingSeen"/>)
        /// — while true, every slot's owned PRIMARY/SECONDARY set
        /// (<see cref="MaxWorlds.Arena.WorldCatalog.OwnedPrimariesThroughWorld"/>/
        /// <see cref="MaxWorlds.Arena.WorldCatalog.OwnedSecondariesThroughWorld"/>) is every weapon in
        /// every world, from World 1 of a brand-new or just-RESET slot on. Never cleared once set.</summary>
        public bool AllWeaponsUnlocked;
    }
}
