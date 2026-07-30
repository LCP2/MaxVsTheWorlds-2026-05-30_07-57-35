using System;
using System.IO;
using UnityEngine;

namespace MaxWorlds.Save
{
    /// <summary>
    /// Three player profiles, on disk (YT-218; supersedes the mid-run resume slots from YT-151).
    /// Reads/writes JSON under <c>Application.persistentDataPath</c> (overridable —
    /// <see cref="DirectoryOverride"/> — so tests never touch a real device's save data).
    ///
    /// A profile is an identity plus a personal best, not a paused run: selecting one always drops
    /// the player into a fresh fight. Static, same idiom as <see cref="MaxWorlds.Upgrades.UpgradeState"/>/
    /// <see cref="MaxWorlds.Pickups.PickupWallet"/>: one live game, no reference-threading.
    /// <see cref="ActiveSlot"/> is the process's "which profile did the player pick" flag — -1
    /// means the Home screen hasn't handed off yet, which is also what gates the Home screen
    /// reopening on a Replay-triggered scene reload.
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

        /// <summary>A run on <paramref name="slot"/> just ended (win or lose) having peaked at
        /// <paramref name="peakNormalized"/> Domination — bank it as the profile's personal best if
        /// it beats the existing one. No-op for no active profile (e.g. tests driving a run with no
        /// Home screen involved).</summary>
        public static void RecordResult(int slot, float peakNormalized)
        {
            if (slot < 0) return;
            SaveSlotData data = Load(slot);
            if (!data.HasData) data = new SaveSlotData { HasData = true, DisplayName = DefaultDisplayName(slot) };
            if (peakNormalized <= data.PersonalBestNormalized) return;
            data.PersonalBestNormalized = peakNormalized;
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
