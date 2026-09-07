using System;
using MaxWorlds.Weapons;

namespace MaxWorlds.Save
{
    /// <summary>
    /// One profile's payload (YT-218). A slot is a PLAYER: what persists is the player's identity
    /// and their best result, so two brothers sharing a device each keep their own progress. Since
    /// MV-524 a slot can ALSO carry one paused run — an area-entry checkpoint (below), not a world
    /// snapshot: no robot/pickup/gate state survives, only what THE RIG/wallet/death-count looked
    /// like on entering the checkpointed area. PLAY always starts fresh and clears any checkpoint;
    /// RESUME restores it. <c>[Serializable]</c> and fields-only so <c>JsonUtility</c> can round-trip
    /// it with no custom converter.
    /// </summary>
    [Serializable]
    public sealed class SaveSlotData
    {
        /// <summary>False for an untouched slot — the Home screen shows "Empty" and offers New Game.</summary>
        public bool HasData;

        /// <summary>The profile's own name, shown on its slot card. Defaults to a per-slot label
        /// until a rename UI exists.</summary>
        public string DisplayName = string.Empty;

        /// <summary>Fewest deaths taken across any run this profile has ever finished (Victory) —
        /// the score hooks arrive with YT-209. Replaces the old peak-Domination-%, which stopped
        /// discriminating once a death no longer ends the run (MV-427: every player eventually
        /// reaches 100%). -1 means this profile has never finished a run yet.</summary>
        public int BestDeathsToVictory = -1;

        /// <summary>Which world (<see cref="MaxWorlds.Arena.WorldLibrary.Keys"/>) this profile plays
        /// next, 0-based (MV-687). Advances by one on every Victory, clamped to the last world.</summary>
        public int WorldIndex;

        /// <summary>Which primary this profile is currently equipped with (MV-689) — the RCDA until a
        /// World 1 finale Weapon Core morph flips it to the LPPE for good. Mirrors
        /// <see cref="WeaponCatalog.PrimaryKind"/> exactly so a save round-trips it with no converter.
        /// A pre-existing save with <see cref="WorldIndex"/> &gt;= 1 but this still at
        /// <see cref="WeaponCatalog.PrimaryKind.Rcda"/> is a save from before this field existed — one
        /// whose morph never got the chance to persist.</summary>
        public WeaponCatalog.PrimaryKind PrimaryKind = WeaponCatalog.PrimaryKind.Rcda;

        /// <summary>True once a Weapon Core has been collected but THE RIG hasn't been opened yet to
        /// play the morph (MV-689) — the persisted twin of <see cref="MaxWorlds.Weapons.PendingMorphingModule.WeaponCorePending"/>,
        /// which lives only in memory and would otherwise lose the banked core across an app restart.</summary>
        public bool WeaponCorePending;

        // --- Mid-run checkpoint (MV-557 schema; captured/restored for real as of MV-524 parts 2/3) ---
        // Written by SaveSystem.CaptureActiveCheckpoint (AreaAccumulationDirector.EnterArea and
        // WorldRunner's pause/focus handlers) and read by SaveSystem.RestoreCheckpoint (HomeScreen's
        // RESUME). A save predating this schema has none of these fields in its JSON; JsonUtility
        // leaves them at these defaults, and HasRunInProgress = false is what "no run in progress"
        // reads as.

        /// <summary>True once a mid-run checkpoint has been captured for this slot — distinguishes
        /// "no run in progress" from a checkpoint sitting at literal default values (area 0, no cells).</summary>
        public bool HasRunInProgress;

        /// <summary>The area index (<c>AreaAccumulationDirector.CurrentArea</c>) the run was checkpointed
        /// in — a resume restarts the player at this area's entry, not mid-area.</summary>
        public int CheckpointAreaIndex;

        /// <summary>THE RIG's node ids at the checkpoint, parallel to <see cref="CheckpointRigNodeLevels"/>
        /// — <c>JsonUtility</c> can't serialize a <c>Dictionary</c>, hence the parallel-array split of
        /// <see cref="MaxWorlds.Weapons.RigState.SnapshotLevels"/>.</summary>
        public string[] CheckpointRigNodeIds = Array.Empty<string>();

        /// <summary>THE RIG's node levels at the checkpoint, parallel to <see cref="CheckpointRigNodeIds"/>.</summary>
        public int[] CheckpointRigNodeLevels = Array.Empty<int>();

        /// <summary>Categories unlocked at the checkpoint (<c>RigState.SnapshotUnlockedCategories</c>).</summary>
        public string[] CheckpointUnlockedCategories = Array.Empty<string>();

        /// <summary><c>PickupWallet.PowerCells</c> at the checkpoint.</summary>
        public int CheckpointPowerCells;

        /// <summary><c>PickupWallet.PowerCellsSecondary</c> at the checkpoint (MV-672).</summary>
        public int CheckpointPowerCellsSecondary;

        /// <summary><c>DeathRunState.DeathsTaken</c> at the checkpoint — deaths persist across a resume
        /// by design (MV-524), so this is restored, not zeroed.</summary>
        public int CheckpointDeathsTaken;
    }
}
