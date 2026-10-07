using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Save
{
    /// <summary>
    /// Three player profiles, on disk (YT-218; supersedes the mid-run resume slots from YT-151).
    /// Reads/writes JSON under <c>Application.persistentDataPath</c> (overridable —
    /// <see cref="DirectoryOverride"/> — so tests never touch a real device's save data).
    ///
    /// A profile is an identity plus a personal best AND, since MV-524, an optional mid-run
    /// checkpoint: an area-entry snapshot (RIG node levels/unlocked categories, power cells, deaths
    /// taken — no world state) captured on area entry and on backgrounding
    /// (<see cref="CaptureActiveCheckpoint"/>, wired from <see cref="MaxWorlds.Enemies.AreaAccumulationDirector.EnterArea"/>
    /// and <see cref="MaxWorlds.Arena.WorldRunner"/>'s pause/focus handlers) and restored by RESUME
    /// on the Home screen (<see cref="RestoreCheckpoint"/>). PLAY still always drops the player into
    /// a fresh fight and clears any captured run (<see cref="ClearCheckpoint"/>) — there is still no
    /// world snapshot and no full-fidelity resume, just an area checkpoint. Static, same idiom as
    /// <see cref="MaxWorlds.Upgrades.UpgradeState"/>/<see cref="MaxWorlds.Pickups.PickupWallet"/>: one
    /// live game, no reference-threading. <see cref="ActiveSlot"/> is the process's "which profile
    /// did the player pick" flag — -1 means the Home screen hasn't handed off yet, which is also what
    /// gates the Home screen reopening on a Replay-triggered scene reload.
    /// </summary>
    public static class SaveSystem
    {
        public const int SlotCount = 3;

        /// <summary>Slot the player picked this process; -1 until the Home screen hands off.</summary>
        public static int ActiveSlot { get; set; } = -1;

        /// <summary>Which slot/world <see cref="MaxWorlds.UI.HomeScreen.OnResume"/> asked for, spanning
        /// the reload it triggers when the checkpoint's world isn't the one already built (MV-985) — same
        /// idiom as <see cref="MaxWorlds.Arena.WorldTransitions.PendingArrivalFrom"/>. Set just before the
        /// reload; <see cref="MaxWorlds.Arena.BackyardPath.ActiveWorldIndex"/> honours it ahead of the
        /// save's own <see cref="SaveSlotData.WorldIndex"/> so the freshly-booted scene builds the
        /// checkpoint's world, not <see cref="ActiveSlot"/>'s. Consumed and cleared by whatever finds it
        /// set on the next <c>Start()</c> (<c>HomeScreen</c>'s own "ActiveSlot already set" path). Null
        /// means "no cross-world resume in flight" — PLAY, RESUME onto the already-built world, a Home
        /// world button and a respawn all boot with this null, exactly as before this ticket.</summary>
        public static PendingResumePlan? PendingResume { get; set; }

        /// <summary>MV-1057: marks that the reload <see cref="PendingResume"/> is carrying should finish
        /// as the Home screen's DEV "FINAL AREA" jump (<see cref="MaxWorlds.UI.HomeScreen.ApplyDevFinaleJump"/>)
        /// rather than an ordinary RESUME (<see cref="MaxWorlds.UI.HomeScreen.ApplyResume"/>) once the
        /// reload's own <c>Start()</c> finds <see cref="PendingResume"/> set — same idiom as
        /// <see cref="PendingResume"/> itself, just the one extra bit a cross-world dev jump needs on top
        /// of it. False (the RESUME path) whenever no dev jump is in flight.</summary>
        public static bool PendingDevFinaleJump { get; set; }

        /// <summary>See <see cref="PendingResume"/>.</summary>
        public readonly struct PendingResumePlan
        {
            public readonly int Slot;
            public readonly int WorldIndex;

            public PendingResumePlan(int slot, int worldIndex)
            {
                Slot = slot;
                WorldIndex = worldIndex;
            }
        }

        private static string s_directoryOverride;

        /// <summary>Where slot files live. Defaults to the device's persistent data path; a test points
        /// this at a scratch folder first so it never reads or writes a real save.</summary>
        public static string DirectoryOverride
        {
            get => s_directoryOverride;
            set => s_directoryOverride = value;
        }

        private static string Directory => s_directoryOverride ?? Application.persistentDataPath;

        private static string PathFor(int slot) => Path.Combine(Directory, $"save_slot_{slot}.json");

        /// <summary>Default identity for a never-played slot — a rename UI is a future seam, not
        /// built here.</summary>
        public static string DefaultDisplayName(int slot) => $"PLAYER {slot + 1}";

        /// <summary>Read a profile. A missing or corrupt file reads as an empty profile rather than
        /// throwing — a save is a convenience, not something that should be able to brick the Home
        /// screen.</summary>
        public static SaveSlotData Load(int slot)
        {
            string path = PathFor(slot);
            if (!File.Exists(path)) return new SaveSlotData();
            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<SaveSlotData>(json) ?? new SaveSlotData();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] slot {slot} failed to load, treating as empty: {e.Message}");
                return new SaveSlotData();
            }
        }

        public static void Save(int slot, SaveSlotData data)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(PathFor(slot), JsonUtility.ToJson(data));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] slot {slot} failed to save: {e.Message}");
            }
        }

        public static void Delete(int slot)
        {
            string path = PathFor(slot);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>First pick of a never-played slot: create its profile with a default name and no
        /// personal best yet. A no-op (returns the existing profile untouched) if the slot already
        /// has data — picking an existing profile must never reset its best.</summary>
        public static SaveSlotData EnsureProfile(int slot)
        {
            SaveSlotData data = Load(slot);
            if (data.HasData) return data;

            data = new SaveSlotData { HasData = true, DisplayName = DefaultDisplayName(slot) };
            Save(slot, data);
            return data;
        }

        /// <summary>A run on <paramref name="slot"/> just finished (Victory — MV-427: death no longer
        /// ends a run) having taken <paramref name="deathsTaken"/> deaths — bank it as the profile's
        /// personal best if it beats the existing one (fewer is better; -1 means "no finished run
        /// yet" and always loses), and advance <see cref="SaveSlotData.WorldIndex"/> to the world right
        /// after <paramref name="playedWorldIndex"/> (MV-921), clamped to the last one, resetting the
        /// mid-run checkpoint area back to 0 — a fresh world starts at its own beginning, not wherever
        /// the previous world's run happened to be checkpointed. Both the personal-best and
        /// world-advance halves always save, unlike the old personal-best-only early-return, since a
        /// save that didn't beat the record must still remember the world advanced. No-op for no
        /// active profile (e.g. tests driving a run with no Home screen involved).
        ///
        /// MV-921: <paramref name="playedWorldIndex"/> is the world actually PLAYED this run — NOT
        /// necessarily <paramref name="data"/>'s own (possibly further-along) <c>WorldIndex</c>. The old
        /// "always <c>WorldIndex + 1</c>" math silently no-opped (clamped straight back to itself) when
        /// an earlier world was replayed on a save that had already gone further, which is the bug this
        /// ticket fixes: finishing World 1 again on a save that reached World 3 must offer World 2 next,
        /// not stay parked on World 3. <see cref="SaveSlotData.FurthestWorldIndex"/> is the profile's
        /// own separate high-water mark, which this can only raise, never lower (self-healing it from
        /// the pre-existing <c>WorldIndex</c> first, for a save saved before this field existed).
        /// Defaults <paramref name="playedWorldIndex"/> to -1, meaning "not specified" — every
        /// pre-existing caller keeps the old "advance off my own WorldIndex" behaviour unchanged.
        ///
        /// MV-776: wipes the WHOLE checkpoint, not just <see cref="SaveSlotData.CheckpointAreaIndex"/> —
        /// the ticket's own named bug, "a stale checkpoint into a world already finished". Before this,
        /// <see cref="SaveSlotData.HasRunInProgress"/> stayed true and every other Checkpoint* field
        /// stayed stale, so a subsequent RESUME would restore a dead run's THE RIG/wallet/flood/
        /// destroyed-Replicator snapshot straight into the world just finished.</summary>
        public static void RecordResult(int slot, int deathsTaken, int playedWorldIndex = -1)
        {
            if (slot < 0) return;
            SaveSlotData data = Load(slot);
            if (!data.HasData) data = new SaveSlotData { HasData = true, DisplayName = DefaultDisplayName(slot) };

            if (data.BestDeathsToVictory < 0 || deathsTaken < data.BestDeathsToVictory)
                data.BestDeathsToVictory = deathsTaken;

            int played = playedWorldIndex >= 0 ? playedWorldIndex : data.WorldIndex;
            data.FurthestWorldIndex = Math.Max(data.FurthestWorldIndex, data.WorldIndex);   // migrate a pre-existing save
            int nextIndex = Math.Min(played + 1, WorldLibrary.Count - 1);
            data.FurthestWorldIndex = Math.Max(data.FurthestWorldIndex, nextIndex);
            data.WorldIndex = nextIndex;
            ResetCheckpointFields(data);

            Save(slot, data);
        }

        /// <summary>Capture a mid-run checkpoint into <paramref name="slot"/>'s save (MV-557, part 1 of
        /// MV-524): snapshots <see cref="RigState"/>'s node levels and unlocked categories,
        /// <see cref="PickupWallet.PowerCells"/> and <see cref="DeathRunState.DeathsTaken"/> at
        /// <paramref name="areaIndex"/>, and (MV-985) <paramref name="worldIndex"/> — the world actually
        /// being played, into <see cref="SaveSlotData.CheckpointWorldIndex"/>. Preserves the slot's
        /// existing identity/personal-best fields.
        /// <see cref="AreaAccumulationDirector.EnterArea"/> and an <c>OnApplicationPause</c> handler are
        /// the trigger wiring, MV-524 parts 2/3.</summary>
        public static void CaptureCheckpoint(int slot, int areaIndex, int worldIndex = -1)
        {
            SaveSlotData data = Load(slot);
            if (!data.HasData) data = new SaveSlotData { HasData = true, DisplayName = DefaultDisplayName(slot) };

            IReadOnlyDictionary<string, int> levels = RigState.SnapshotLevels();
            data.CheckpointRigNodeIds = new string[levels.Count];
            data.CheckpointRigNodeLevels = new int[levels.Count];
            int i = 0;
            foreach (KeyValuePair<string, int> kv in levels)
            {
                data.CheckpointRigNodeIds[i] = kv.Key;
                data.CheckpointRigNodeLevels[i] = kv.Value;
                i++;
            }

            var categories = new List<string>(RigState.SnapshotUnlockedCategories());
            data.CheckpointUnlockedCategories = categories.ToArray();

            // MV-1095: the SUPPORT family's own remembered-levels set — a cold-boot RESUME between a
            // world-entry LOCK and re-earning the family through this world's own cadence must not
            // lose what's owed back.
            IReadOnlyDictionary<string, int> remembered = RigState.SnapshotRememberedSupportLevels();
            data.SupportRememberedIds = new string[remembered.Count];
            data.SupportRememberedLevels = new int[remembered.Count];
            int ri = 0;
            foreach (KeyValuePair<string, int> kv in remembered)
            {
                data.SupportRememberedIds[ri] = kv.Key;
                data.SupportRememberedLevels[ri] = kv.Value;
                ri++;
            }
            // MV-951: a capture must never write an area index lower than the checkpoint it was
            // restored from — WorldRunner.ResumeCheckpoint/Continue land CurrentArea one area BEHIND
            // the checkpoint (standing at the gate looking in), so a pause/focus capture taken before
            // that gate breaks again would otherwise regress the save by one area every single resume.
            data.CheckpointAreaIndex = Math.Max(data.CheckpointAreaIndex, areaIndex);
            // MV-985: which world areaIndex above actually belongs to — the world PLAYED, from the
            // caller's own AreaAccumulationDirector.ActiveWorldIndex, not this slot's WorldIndex (which
            // names the world that plays NEXT and can be a different one already).
            data.CheckpointWorldIndex = worldIndex;
            // MV-1080: THE RIG's own board can already be on the NEXT world (a finale's CLEAN-UP window
            // morphs it the instant the Core is collected, before the played world above advances) — a
            // RESUME must re-apply THIS board's loadout, not the played world's.
            data.CheckpointRigBoardWorldIndex = RigBoard.ActiveWorldIndex;
            data.CheckpointPowerCells = PickupWallet.PowerCells;
            data.CheckpointPowerCellsSecondary = PickupWallet.PowerCellsSecondary;
            data.CheckpointDeathsTaken = DeathRunState.DeathsTaken;
            data.CheckpointElapsedSeconds = RunProgressState.Elapsed;
            data.CheckpointKills = RunProgressState.Kills;
            data.CheckpointDestroyedReplicatorIds = FactoryCensus.DestroyedReplicatorIds();
            data.CheckpointDestroyedShedIds = FactoryCensus.DestroyedShedIds();
            data.CheckpointDefeatedBossAreas = BossCensus.DefeatedAreaIndices();

            // MV-1129: the live finale gate's own resolved state, if this world has reached one yet --
            // null for every world before its own final boss falls, same as a fresh run. Read directly
            // off the scene (unlike every other field above, which reads a static census) because
            // WorldFinaleGate.Awake already runs well before this method ever does, and nothing else
            // here keeps a running tally of its own.
            var finaleGate = MaxWorlds.VFX.WorldFinaleGate.Active;
            data.CheckpointFinaleWeaponGranted = finaleGate != null && finaleGate.WeaponMomentResolved;
            data.CheckpointFinaleExitOpen = finaleGate != null && finaleGate.IsOpen;

            // MV-951: HomeScreen.OnResume wipes AbilityCreditBank/UpgradeState/PendingMorphingModule
            // as part of the same transient-state reset it always did for a fresh PLAY — harmless only
            // once something restores them afterward, which nothing did before this ticket.
            data.CheckpointAbilityCredits = AbilityCreditBank.Banked;
            var installedParts = new List<string>();
            foreach (PartKind kind in UpgradeState.Installed) installedParts.Add(kind.ToString());
            data.CheckpointInstalledParts = installedParts.ToArray();
            data.WeaponCorePending = PendingMorphingModule.WeaponCorePending;
            // MV-1090: same reasoning as WeaponCorePending above — a cold-boot RESUME between collecting
            // a Rack Module and opening THE RIG must not silently lose the banked unlock.
            data.RackModulePending = PendingMorphingModule.RackModulePending;

            // MV-951: MapRuntime.Build unconditionally zeroes the Invasion Level clock on every cold
            // boot (a new scene build) — a resumed run must not appear back at area 5 fighting area-1
            // toughness/spawn-rate.
            data.CheckpointEscalationElapsed = MaxWorlds.Enemies.DifficultyDirector.Elapsed;
            data.CheckpointEscalationShedSkipSeconds = MaxWorlds.Enemies.DifficultyDirector.ShedSkipSeconds;
            data.CheckpointEscalationShedsDestroyed = MaxWorlds.Enemies.DifficultyDirector.ShedsDestroyed;

            data.HasRunInProgress = true;

            Save(slot, data);
        }

        /// <summary>Restore <paramref name="slot"/>'s captured checkpoint (MV-557, part 1 of MV-524)
        /// into the live <see cref="RigState"/>/<see cref="PickupWallet"/>/<see cref="DeathRunState"/>.
        /// Returns false and changes nothing if the slot holds no checkpoint. Re-entering the checkpoint's
        /// area is the caller's job — this ticket does not wire a scene/HomeScreen caller (MV-524 part 3).
        ///
        /// MV-776: also re-applies the checkpoint's destroyed-Replicator set onto whichever
        /// currently-registered instances the level's own build has already brought alive by the time
        /// this runs (<see cref="FactoryCensus.ApplyCheckpointDestroyedIds"/>) — a pure-data system
        /// with no scene dependency of its own, same as every other field this method already
        /// restores.
        ///
        /// MV-922: same treatment for World 1's own factory, the Mower Hutch shed — before this, a
        /// resume restored the area but every shed behind the player came back alive and the
        /// destroyed-factory count restarted from zero (<see cref="FactoryCensus.ApplyCheckpointDestroyedShedIds"/>).
        ///
        /// MV-995: same treatment again for an already-defeated boss — before this, a cold-boot RESUME's
        /// fresh <c>MapRuntime.BuildBoss</c> rebuild brought every authored boss back Dormant with no
        /// memory of a prior fight, so a boss the player had already beaten woke up and fought again the
        /// moment Max walked back into its area (<see cref="BossCensus.ApplyCheckpointDefeatedAreas"/>).</summary>
        public static bool RestoreCheckpoint(int slot)
        {
            SaveSlotData data = Load(slot);
            if (!data.HasRunInProgress) return false;

            var levels = new Dictionary<string, int>();
            int count = Math.Min(data.CheckpointRigNodeIds?.Length ?? 0, data.CheckpointRigNodeLevels?.Length ?? 0);
            for (int i = 0; i < count; i++) levels[data.CheckpointRigNodeIds[i]] = data.CheckpointRigNodeLevels[i];

            RigState.RestoreSnapshot(levels, data.CheckpointUnlockedCategories ?? Array.Empty<string>());

            // MV-1095: the mirror of the capture-side write above.
            var remembered = new Dictionary<string, int>();
            int rcount = Math.Min(data.SupportRememberedIds?.Length ?? 0, data.SupportRememberedLevels?.Length ?? 0);
            for (int ri = 0; ri < rcount; ri++) remembered[data.SupportRememberedIds[ri]] = data.SupportRememberedLevels[ri];
            RigState.RestoreRememberedSupportLevels(remembered);
            PickupWallet.SetPowerCells(data.CheckpointPowerCells);
            PickupWallet.SetPowerCellSecondary(data.CheckpointPowerCellsSecondary);
            DeathRunState.RestoreDeathsTaken(data.CheckpointDeathsTaken);
            RunProgressState.Restore(data.CheckpointElapsedSeconds, data.CheckpointKills);
            FactoryCensus.ApplyCheckpointDestroyedIds(data.CheckpointDestroyedReplicatorIds);
            FactoryCensus.ApplyCheckpointDestroyedShedIds(data.CheckpointDestroyedShedIds);
            BossCensus.ApplyCheckpointDefeatedAreas(data.CheckpointDefeatedBossAreas);

            // MV-951: the mirror of the capture-side write above — AbilityCreditBank/UpgradeState/
            // PendingMorphingModule.WeaponCorePending all get wiped by HomeScreen.OnResume's own
            // transient-state reset right before this runs, same reasoning as RigState/PickupWallet.
            AbilityCreditBank.RestoreBanked(data.CheckpointAbilityCredits);
            var installedParts = new List<PartKind>();
            foreach (string name in data.CheckpointInstalledParts ?? Array.Empty<string>())
                if (Enum.TryParse(name, out PartKind kind)) installedParts.Add(kind);
            UpgradeState.RestoreInstalled(installedParts);
            PendingMorphingModule.RestoreWeaponCorePending(data.WeaponCorePending);
            PendingMorphingModule.RestoreRackModulePending(data.RackModulePending);
            MaxWorlds.Enemies.DifficultyDirector.RestoreClock(
                data.CheckpointEscalationElapsed, data.CheckpointEscalationShedSkipSeconds, data.CheckpointEscalationShedsDestroyed);

            // MV-950: the two Apply* calls above already fixed FactoryCensus.Destroyed/
            // ReplicatorsDestroyed (gameplay state) — but the HUD's ArenaProgress banner and the
            // Result screen's RunStats tally are separate counters that only ever advanced off the
            // live HudSignals.FactoryDestroyed kill signal, deliberately never replayed here (that
            // would re-drop loot/VFX). Without this, both silently read 0/N after a resume even
            // though the gameplay state correctly knew otherwise.
            FactoryCensus.RaiseCheckpointRestored();
            return true;
        }

        /// <summary>Capture a checkpoint for whichever slot is currently active (MV-524 parts 2/3) —
        /// the plain, EditMode-testable method both real triggers call: <see cref="MaxWorlds.Enemies.AreaAccumulationDirector.EnterArea"/>
        /// on area entry, and <see cref="MaxWorlds.Arena.WorldRunner"/>'s <c>OnApplicationPause</c>/
        /// <c>OnApplicationFocus</c> handlers on backgrounding (neither of which Unity ever invokes
        /// outside Play mode, hence extracting the actual write out to here). A no-op with no active
        /// slot (<see cref="ActiveSlot"/> &lt; 0 — a capture/press-kit/perf-capture run, or a test) or
        /// for the empty entry stub (<paramref name="areaIndex"/> &lt;= 0 — nothing worth
        /// checkpointing yet).</summary>
        public static void CaptureActiveCheckpoint(int areaIndex, int worldIndex = -1)
        {
            if (ActiveSlot < 0 || areaIndex <= 0) return;
            CaptureCheckpoint(ActiveSlot, areaIndex, worldIndex);
        }

        /// <summary>What RESUME on <paramref name="slot"/> needs to do (MV-985), given
        /// <paramref name="builtWorldIndex"/> — the world the scene behind the Home screen is already
        /// built as (<see cref="MaxWorlds.Arena.BackyardPath.ResolvedWorldIndex"/>). The target world is
        /// the checkpoint's own <see cref="SaveSlotData.CheckpointWorldIndex"/> when it was recorded,
        /// falling back to <see cref="SaveSlotData.WorldIndex"/> only for a pre-existing save that never
        /// captured one. <see cref="ResumePlan.NeedsReload"/> is false exactly when that target already
        /// matches what's built — the common case, since a checkpoint is almost always in the world
        /// that's already up — so <see cref="MaxWorlds.UI.HomeScreen.OnResume"/> only reloads for the
        /// cross-world case this ticket fixes.</summary>
        public static ResumePlan ResolveResumePlan(int slot, int builtWorldIndex)
        {
            SaveSlotData data = Load(slot);
            int targetWorld = data.CheckpointWorldIndex >= 0 ? data.CheckpointWorldIndex : data.WorldIndex;
            return new ResumePlan(targetWorld != builtWorldIndex, targetWorld, data.CheckpointAreaIndex);
        }

        /// <summary>See <see cref="ResolveResumePlan"/>.</summary>
        public readonly struct ResumePlan
        {
            public readonly bool NeedsReload;
            public readonly int WorldIndex;
            public readonly int AreaIndex;

            public ResumePlan(bool needsReload, int worldIndex, int areaIndex)
            {
                NeedsReload = needsReload;
                WorldIndex = worldIndex;
                AreaIndex = areaIndex;
            }
        }

        /// <summary>Clear <paramref name="slot"/>'s captured run (MV-524 part 3) — what choosing PLAY
        /// on a slot holding one calls, so starting fresh never leaves a stale RESUME behind.
        /// Preserves the slot's identity/personal-best fields; a no-op if the slot carries no run.</summary>
        public static void ClearCheckpoint(int slot)
        {
            SaveSlotData data = Load(slot);
            if (!data.HasRunInProgress) return;

            ResetCheckpointFields(data);
            Save(slot, data);
        }

        /// <summary>Every Checkpoint* field (MV-776) back to "no run in progress" — shared by
        /// <see cref="ClearCheckpoint"/> (PLAY starting fresh) and <see cref="RecordResult"/> (a Victory,
        /// which must wipe the checkpoint outright, not just <see cref="SaveSlotData.CheckpointAreaIndex"/>
        /// as it used to). Mutates <paramref name="data"/> in place; the caller saves it.</summary>
        private static void ResetCheckpointFields(SaveSlotData data)
        {
            data.HasRunInProgress = false;
            data.CheckpointAreaIndex = 0;
            data.CheckpointWorldIndex = -1;
            data.CheckpointRigBoardWorldIndex = -1;
            data.CheckpointRigNodeIds = Array.Empty<string>();
            data.CheckpointRigNodeLevels = Array.Empty<int>();
            data.CheckpointUnlockedCategories = Array.Empty<string>();
            data.SupportRememberedIds = Array.Empty<string>();
            data.SupportRememberedLevels = Array.Empty<int>();
            data.CheckpointPowerCells = 0;
            data.CheckpointPowerCellsSecondary = 0;
            data.CheckpointDeathsTaken = 0;
            data.CheckpointElapsedSeconds = 0f;
            data.CheckpointKills = 0;
            data.CheckpointDestroyedReplicatorIds = Array.Empty<string>();
            data.CheckpointDestroyedShedIds = Array.Empty<string>();
            data.CheckpointDefeatedBossAreas = Array.Empty<int>();
            data.CheckpointFinaleWeaponGranted = false;
            data.CheckpointFinaleExitOpen = false;
            data.CheckpointAbilityCredits = 0;
            data.CheckpointInstalledParts = Array.Empty<string>();
            data.CheckpointEscalationElapsed = 0f;
            data.CheckpointEscalationShedSkipSeconds = 0f;
            data.CheckpointEscalationShedsDestroyed = 0;
        }

        /// <summary>Test isolation / a fresh process: forget which slot is live and stop pointing at a
        /// scratch directory.</summary>
        public static void ResetForTests()
        {
            ActiveSlot = -1;
            s_directoryOverride = null;
            PendingResume = null;
            PendingDevFinaleJump = false;
        }
    }
}
