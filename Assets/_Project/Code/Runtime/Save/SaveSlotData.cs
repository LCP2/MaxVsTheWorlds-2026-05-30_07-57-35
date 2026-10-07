using System;

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
        /// next, 0-based (MV-687). Advances to one past whichever world was actually PLAYED on every
        /// Victory (MV-921), clamped to the last world — replaying an earlier world than
        /// <see cref="FurthestWorldIndex"/> and winning moves this to the world right after the one
        /// just played, not past the profile's own furthest reach.</summary>
        public int WorldIndex;

        /// <summary>The highest <see cref="WorldIndex"/> this profile has ever reached, 0-based
        /// (MV-921) — separate from <see cref="WorldIndex"/> itself, which names which world plays NEXT
        /// and can legitimately move backward relative to this when an earlier world is replayed (e.g.
        /// a dev-tool jump, or a future world-select). Never reduced. Defaults to 0 for a pre-existing
        /// save with no explicit value yet; <c>SaveSystem.RecordResult</c> self-heals it from
        /// <see cref="WorldIndex"/> on its first call for such a save.</summary>
        public int FurthestWorldIndex;

        /// <summary>True once a Weapon Core has been collected but THE RIG hasn't been opened yet to
        /// play the morph (MV-689) — the persisted twin of <see cref="MaxWorlds.Weapons.PendingMorphingModule.WeaponCorePending"/>,
        /// which lives only in memory and would otherwise lose the banked core across an app restart.</summary>
        public bool WeaponCorePending;

        /// <summary>True once a Rack Module has been collected but THE RIG hasn't been opened yet to
        /// unlock SECONDARY and grant <c>s_rkt</c> (MV-1090) — the persisted twin of
        /// <see cref="MaxWorlds.Weapons.PendingMorphingModule.RackModulePending"/>, same reasoning as
        /// <see cref="WeaponCorePending"/>.</summary>
        public bool RackModulePending;

        /// <summary>SUPPORT family node ids remembered from the last time it was LOCKED on a new-world
        /// arrival (MV-1095), parallel to <see cref="SupportRememberedLevels"/> — the persisted twin of
        /// <see cref="MaxWorlds.Weapons.RigState.SnapshotRememberedSupportLevels"/>; <c>JsonUtility</c>
        /// can't serialize a <c>Dictionary</c>, hence the parallel-array split, same idiom as
        /// <see cref="CheckpointRigNodeIds"/>.</summary>
        public string[] SupportRememberedIds = Array.Empty<string>();

        /// <summary>SUPPORT family node levels remembered from the last lock, parallel to
        /// <see cref="SupportRememberedIds"/>.</summary>
        public int[] SupportRememberedLevels = Array.Empty<int>();

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

        /// <summary>MV-1096: the id of the gate Max last entered through when this checkpoint was
        /// captured — fixes both the area AND the level/visit, which <see cref="CheckpointAreaIndex"/>
        /// alone cannot for a two-level area (World 2's a10/a11/a12, each visited through two different
        /// gates, floor then deck). Null/empty for a save captured before this field existed — a resume
        /// then falls back to <see cref="CheckpointAreaIndex"/> alone, exactly as it always has.</summary>
        public string CheckpointGateId;

        /// <summary>The 0-based world (<c>AreaAccumulationDirector.ActiveWorldIndex</c>) the checkpoint
        /// above was captured IN (MV-985) — -1 means unknown, either a save from before this field
        /// existed or a slot with no checkpoint. Without this, a RESUME after <c>RunFlow.QuitToMenu</c>
        /// (which always rebuilds World 1 behind the Home screen — see <c>BackyardPath.ActiveWorldIndex</c>)
        /// replayed a World 2 checkpoint's area index against World 1's own areas. Resume's target world
        /// is this field when it is set, falling back to <see cref="WorldIndex"/> only for a pre-existing
        /// save that never recorded it (<c>SaveSystem.ResolveResumePlan</c>).</summary>
        public int CheckpointWorldIndex = -1;

        /// <summary>The world (<see cref="MaxWorlds.Weapons.RigBoard.ActiveWorldIndex"/>) THE RIG's own
        /// board was reading from at the moment this checkpoint was captured (MV-1080) — can differ from
        /// <see cref="CheckpointWorldIndex"/> (the world actually PLAYED) during a finale's CLEAN-UP
        /// window: <c>WorldFinaleGate.BeginCleanup</c> morphs the board onto the NEXT world's the instant
        /// the Weapon Core is collected, well before the played world itself advances past its own final
        /// area. -1 means unknown — either a save from before this field existed, or no checkpoint — and
        /// reads as <see cref="CheckpointWorldIndex"/> (<c>HomeScreen.ApplyResumeState</c>'s own
        /// fallback), same as a pre-MV-985 save falls back to <see cref="WorldIndex"/>.</summary>
        public int CheckpointRigBoardWorldIndex = -1;

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

        /// <summary><c>RunProgressState.Elapsed</c> at the checkpoint (MV-841) — restored the same
        /// way <see cref="CheckpointDeathsTaken"/> is, so a resumed run's whole-world clock doesn't
        /// silently reset to zero.</summary>
        public float CheckpointElapsedSeconds;

        /// <summary><c>RunProgressState.Kills</c> at the checkpoint (MV-841), same contract as
        /// <see cref="CheckpointElapsedSeconds"/>.</summary>
        public int CheckpointKills;

        /// <summary>Stable ids (<c>Replicator.Id</c>) of every Replicator destroyed by the time this
        /// checkpoint was captured (MV-776) — restored on RESUME by silently re-destroying whichever of
        /// the freshly-rebuilt level's Replicators carry a matching id, so a resume never resurrects a
        /// factory the player already paid to destroy.</summary>
        public string[] CheckpointDestroyedReplicatorIds = Array.Empty<string>();

        /// <summary>Stable ids (<c>MowerHutch.Id</c>) of every World 1 shed destroyed by the time this
        /// checkpoint was captured (MV-922) — the World 1 equivalent of
        /// <see cref="CheckpointDestroyedReplicatorIds"/>, restored the same way: silently re-destroying
        /// whichever of the freshly-rebuilt level's sheds carry a matching id, so a resume never
        /// resurrects a shed the player already destroyed, and <c>FactoryCensus.Destroyed</c> restores
        /// intact instead of restarting from zero. That gameplay count was never the whole story: until
        /// MV-950, the HUD's FACTORIES/REPLICATORS banner and the Result screen's tally each kept their
        /// own separate counter fed only by the live destruction signal, so both still read 0/N after a
        /// resume even though <c>FactoryCensus</c> itself was correct. MV-950 seeds those two off
        /// <c>FactoryCensus.CheckpointRestored</c> so every counter now agrees.</summary>
        public string[] CheckpointDestroyedShedIds = Array.Empty<string>();

        /// <summary>Area indices whose boss(es) were ALL defeated by the time this checkpoint was
        /// captured (MV-995) — restored on RESUME by silently removing whichever of the freshly-rebuilt
        /// level's bosses stand in one of these areas, so a resume never resurrects a boss the player
        /// already beat (<c>BossCensus.ApplyCheckpointDefeatedAreas</c>). Empty for a save predating this
        /// field, or one with no defeated boss yet — a RESUME then behaves exactly as before this ticket.</summary>
        public int[] CheckpointDefeatedBossAreas = Array.Empty<int>();

        /// <summary>True once this checkpoint's own world finale had already moved past its weapon
        /// moment — the Core collected and, if there is a next world, its morph already applied — into
        /// clean-up or later, at the moment this checkpoint was captured (MV-1129). The persisted twin of
        /// <see cref="MaxWorlds.VFX.WorldFinaleGate.WeaponMomentResolved"/>, which lives only in the live
        /// gate instance a cold-boot RESUME rebuilds fresh with no memory of it. False for a save
        /// predating this field, or one whose finale never reached this point — a RESUME behaves exactly
        /// as before this ticket (the Weapon Core, if its own area is recorded defeated, simply never
        /// reappears).</summary>
        public bool CheckpointFinaleWeaponGranted;

        /// <summary>True once this checkpoint's own world finale's exit was already open at the moment
        /// this checkpoint was captured (MV-1129) — the persisted twin of
        /// <see cref="MaxWorlds.VFX.WorldFinaleGate.IsOpen"/>.</summary>
        public bool CheckpointFinaleExitOpen;

        /// <summary><c>AbilityCreditBank.Banked</c> at the checkpoint (MV-951) — restored the same way
        /// <see cref="CheckpointDeathsTaken"/> is, so a cold-boot RESUME doesn't lose a banked-but-unspent
        /// ability credit to <c>HomeScreen.OnResume</c>'s own transient-state wipe.</summary>
        public int CheckpointAbilityCredits;

        /// <summary><c>UpgradeState</c>'s installed set at the checkpoint (MV-951), by
        /// <c>PartKind</c> name — same string-array idiom as <see cref="CheckpointDestroyedReplicatorIds"/>
        /// since <c>JsonUtility</c> can't serialize a <c>HashSet</c>.</summary>
        public string[] CheckpointInstalledParts = Array.Empty<string>();

        /// <summary><c>DifficultyDirector.Elapsed</c> at the checkpoint (MV-951) — the Invasion Level's
        /// real-time clock. <c>MapRuntime.Build</c> always resets it to zero on every cold-boot scene
        /// build, before a RESUME even knows there's a checkpoint to land in, so a resumed run must
        /// restore it explicitly rather than left at zero.</summary>
        public float CheckpointEscalationElapsed;

        /// <summary><c>DifficultyDirector</c>'s accumulated shed skip-ahead at the checkpoint (MV-951) —
        /// paired with <see cref="CheckpointEscalationElapsed"/> so the Invasion Level resumes at exactly
        /// the value it was captured at, not just the real-time portion of it.</summary>
        public float CheckpointEscalationShedSkipSeconds;

        /// <summary><c>DifficultyDirector.ShedsDestroyed</c> at the checkpoint (MV-951).</summary>
        public int CheckpointEscalationShedsDestroyed;
    }
}
