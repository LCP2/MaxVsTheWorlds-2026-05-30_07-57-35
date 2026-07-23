using System;
using System.Collections.Generic;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Save
{
    /// <summary>
    /// One save slot's payload (YT-151): plain data, JSON-serialised straight off
    /// <see cref="UpgradeState"/> and <see cref="MaxWorlds.Pickups.PickupWallet"/> plus wherever Max was
    /// standing. <c>[Serializable]</c> and fields-only so <c>JsonUtility</c> can round-trip it with no
    /// custom converter.
    /// </summary>
    [Serializable]
    public sealed class SaveSlotData
    {
        /// <summary>False for an untouched slot — the Home screen shows "Empty" and offers New Game.</summary>
        public bool HasData;

        /// <summary>Which level/area this save resumes into. One value in the current slice
        /// (<see cref="SaveSystem.DefaultLevelId"/>); carried so a save survives a future multi-level build.</summary>
        public string LevelId = SaveSystem.DefaultLevelId;

        public float PosX, PosY, PosZ;
        public float RotY;

        /// <summary>Every part Max has installed, in no particular order (<see cref="UpgradeState.Installed"/>
        /// is a set).</summary>
        public List<PartKind> InstalledParts = new List<PartKind>();

        public int PowerCells;

        /// <summary>Collected but not yet installed, oldest first — matches
        /// <see cref="MaxWorlds.Pickups.PickupWallet.PendingParts"/>' order so a reload installs them
        /// in the same sequence the upgrade screen would have.</summary>
        public List<PartKind> PendingParts = new List<PartKind>();
    }
}
