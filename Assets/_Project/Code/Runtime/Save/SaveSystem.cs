using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
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
        /// <paramref name="areaIndex"/>. Preserves the slot's existing identity/personal-best fields.
        /// Not called from anywhere yet — <see cref="AreaAccumulationDirector.EnterArea"/> and an
        /// <c>OnApplicationPause</c> handler are the trigger wiring, MV-524 parts 2/3.</summary>
        public static void CaptureCheckpoint(int slot, int areaIndex)
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
            data.CheckpointAreaIndex = areaIndex;
            data.CheckpointPowerCells = PickupWallet.PowerCells;
            data.CheckpointPowerCellsSecondary = PickupWallet.PowerCellsSecondary;
            data.CheckpointDeathsTaken = DeathRunState.DeathsTaken;
            data.CheckpointElapsedSeconds = RunProgressState.Elapsed;
            data.CheckpointKills = RunProgressState.Kills;
            data.CheckpointFloodLevel01 = StormdrainFlood.Level01;
            data.CheckpointDestroyedReplicatorIds = FactoryCensus.DestroyedReplicatorIds();
            data.CheckpointDestroyedShedIds = FactoryCensus.DestroyedShedIds();
            data.HasRunInProgress = true;

            Save(slot, data);
        }

        /// <summary>Restore <paramref name="slot"/>'s captured checkpoint (MV-557, part 1 of MV-524)
        /// into the live <see cref="RigState"/>/<see cref="PickupWallet"/>/<see cref="DeathRunState"/>.
        /// Returns false and changes nothing if the slot holds no checkpoint. Re-entering the checkpoint's
        /// area is the caller's job — this ticket does not wire a scene/HomeScreen caller (MV-524 part 3).
        ///
        /// MV-776: also rewinds <see cref="StormdrainFlood"/> to the checkpoint's own level (never the
        /// live one — a resume restores what the gate looked like, not wherever the flood drifted to by
        /// the time the run ended) and re-applies the checkpoint's destroyed-Replicator set onto whichever
        /// currently-registered instances the level's own build has already brought alive by the time
        /// this runs (<see cref="FactoryCensus.ApplyCheckpointDestroyedIds"/>) — both pure-data systems
        /// with no scene dependency of their own, same as every other field this method already
        /// restores.
        ///
        /// MV-922: same treatment for World 1's own factory, the Mower Hutch shed — before this, a
        /// resume restored the area but every shed behind the player came back alive and the
        /// destroyed-factory count restarted from zero (<see cref="FactoryCensus.ApplyCheckpointDestroyedShedIds"/>).</summary>
        public static bool RestoreCheckpoint(int slot)
        {
            SaveSlotData data = Load(slot);
            if (!data.HasRunInProgress) return false;

            var levels = new Dictionary<string, int>();
            int count = Math.Min(data.CheckpointRigNodeIds?.Length ?? 0, data.CheckpointRigNodeLevels?.Length ?? 0);
            for (int i = 0; i < count; i++) levels[data.CheckpointRigNodeIds[i]] = data.CheckpointRigNodeLevels[i];

            RigState.RestoreSnapshot(levels, data.CheckpointUnlockedCategories ?? Array.Empty<string>());
            PickupWallet.SetPowerCells(data.CheckpointPowerCells);
            PickupWallet.SetPowerCellSecondary(data.CheckpointPowerCellsSecondary);
            DeathRunState.RestoreDeathsTaken(data.CheckpointDeathsTaken);
            RunProgressState.Restore(data.CheckpointElapsedSeconds, data.CheckpointKills);
            StormdrainFlood.RestoreLevel01(data.CheckpointFloodLevel01);
            FactoryCensus.ApplyCheckpointDestroyedIds(data.CheckpointDestroyedReplicatorIds);
            FactoryCensus.ApplyCheckpointDestroyedShedIds(data.CheckpointDestroyedShedIds);
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
        public static void CaptureActiveCheckpoint(int areaIndex)
        {
            if (ActiveSlot < 0 || areaIndex <= 0) return;
            CaptureCheckpoint(ActiveSlot, areaIndex);
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
            data.CheckpointRigNodeIds = Array.Empty<string>();
            data.CheckpointRigNodeLevels = Array.Empty<int>();
            data.CheckpointUnlockedCategories = Array.Empty<string>();
            data.CheckpointPowerCells = 0;
            data.CheckpointPowerCellsSecondary = 0;
            data.CheckpointDeathsTaken = 0;
            data.CheckpointElapsedSeconds = 0f;
            data.CheckpointKills = 0;
            data.CheckpointFloodLevel01 = 0f;
            data.CheckpointDestroyedReplicatorIds = Array.Empty<string>();
            data.CheckpointDestroyedShedIds = Array.Empty<string>();
        }

        /// <summary>Test isolation / a fresh process: forget which slot is live and stop pointing at a
        /// scratch directory.</summary>
        public static void ResetForTests()
        {
            ActiveSlot = -1;
            s_directoryOverride = null;
        }
    }
}
