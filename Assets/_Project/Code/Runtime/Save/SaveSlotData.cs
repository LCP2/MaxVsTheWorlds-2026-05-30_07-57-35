using System;

namespace MaxWorlds.Save
{
    /// <summary>
    /// One profile's payload (YT-218). A slot is a PLAYER, not a paused game: no mid-run state
    /// (position, installed parts, wallet contents) survives between runs — picking a profile
    /// always starts a fresh run. What persists is the player's identity and their best result,
    /// so two brothers sharing a device each keep their own progress. <c>[Serializable]</c> and
    /// fields-only so <c>JsonUtility</c> can round-trip it with no custom converter.
    ///
    /// Seams left deliberately unbuilt for the economy tickets: banked power cells and owned
    /// weapons will live here once those systems exist — don't add fields for them yet.
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

        // --- Mid-run checkpoint (MV-557, part 1 of MV-524) ---
        // Capture/restore only; nothing calls these yet (that's MV-524 parts 2/3 — EnterArea/
        // OnApplicationPause triggers and the HomeScreen RESUME/PLAY split). A save predating this
        // schema has none of these fields in its JSON; JsonUtility leaves them at these defaults,
        // and HasRunInProgress = false is what "no run in progress" reads as.

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

        /// <summary><c>DeathRunState.DeathsTaken</c> at the checkpoint — deaths persist across a resume
        /// by design (MV-524), so this is restored, not zeroed.</summary>
        public int CheckpointDeathsTaken;
    }
}
