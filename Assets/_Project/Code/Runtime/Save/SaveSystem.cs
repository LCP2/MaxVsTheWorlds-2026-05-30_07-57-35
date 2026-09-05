using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MaxWorlds.Arena;
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
        /// yet" and always loses). No-op for no active profile (e.g. tests driving a run with no Home
        /// screen involved).</summary>
        public static void RecordResult(int slot, int deathsTaken)
        {
            if (slot < 0) return;
            SaveSlotData data = Load(slot);
            if (!data.HasData) data = new SaveSlotData { HasData = true, DisplayName = DefaultDisplayName(slot) };
            if (data.BestDeathsToVictory >= 0 && deathsTaken >= data.BestDeathsToVictory) return;
            data.BestDeathsToVictory = deathsTaken;
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
            data.HasRunInProgress = true;

            Save(slot, data);
        }

        /// <summary>Restore <paramref name="slot"/>'s captured checkpoint (MV-557, part 1 of MV-524)
        /// into the live <see cref="RigState"/>/<see cref="PickupWallet"/>/<see cref="DeathRunState"/>.
        /// Returns false and changes nothing if the slot holds no checkpoint. Re-entering the checkpoint's
        /// area is the caller's job — this ticket does not wire a scene/HomeScreen caller (MV-524 part 3).</summary>
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

            data.HasRunInProgress = false;
            data.CheckpointAreaIndex = 0;
            data.CheckpointRigNodeIds = Array.Empty<string>();
            data.CheckpointRigNodeLevels = Array.Empty<int>();
            data.CheckpointUnlockedCategories = Array.Empty<string>();
            data.CheckpointPowerCells = 0;
            data.CheckpointPowerCellsSecondary = 0;
            data.CheckpointDeathsTaken = 0;
            Save(slot, data);
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
