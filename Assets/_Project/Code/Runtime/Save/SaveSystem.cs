using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Save
{
    /// <summary>
    /// Three save slots, on disk (YT-151). Reads/writes JSON under <c>Application.persistentDataPath</c>
    /// (overridable — <see cref="DirectoryOverride"/> — so tests never touch a real device's save data),
    /// and knows how to snapshot the live run (<see cref="UpgradeState"/> + <see cref="PickupWallet"/> +
    /// Max's transform) into a <see cref="SaveSlotData"/> and back.
    ///
    /// Static, same idiom as <see cref="UpgradeState"/>/<see cref="PickupWallet"/>: one save game, no
    /// reference-threading. <see cref="ActiveSlot"/> is the process's "which slot did the player pick"
    /// flag — -1 means the Home screen hasn't handed off yet, which is also what gates
    /// <see cref="SaveDriver"/>'s autosave and stops the Home screen reopening on a Replay-triggered
    /// scene reload.
    /// </summary>
    public static class SaveSystem
    {
        public const int SlotCount = 3;

        /// <summary>The only level the current slice has. Carried in every save so the schema survives
        /// a future multi-level build without a migration.</summary>
        public const string DefaultLevelId = "Backyard_Slice";

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

        /// <summary>Read a slot. A missing or corrupt file reads as an empty slot rather than throwing —
        /// a save is a convenience, not something that should be able to brick the Home screen.</summary>
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

        /// <summary>Build a slot payload off the live statics plus a given transform — pure, so it's
        /// testable without a scene.</summary>
        public static SaveSlotData Capture(Vector3 position, float yawDegrees, string levelId)
        {
            return new SaveSlotData
            {
                HasData = true,
                LevelId = levelId,
                PosX = position.x,
                PosY = position.y,
                PosZ = position.z,
                RotY = yawDegrees,
                InstalledParts = new List<PartKind>(UpgradeState.Installed),
                PowerCells = PickupWallet.PowerCells,
                PendingParts = new List<PartKind>(PickupWallet.PendingParts),
            };
        }

        /// <summary>Find Max in the live scene and write his current run straight to <paramref name="slot"/>.
        /// A no-op if there's no player to snapshot (e.g. mid scene-load).</summary>
        public static void CaptureAndSave(int slot, string levelId)
        {
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (player == null) return;
            Vector3 pos = player.transform.position;
            float yaw = player.transform.eulerAngles.y;
            Save(slot, Capture(pos, yaw, levelId));
        }

        /// <summary>Push a loaded slot's upgrades/wallet back into the live statics. Does not move the
        /// player — the caller (the Home screen) places Max once it has decided the slot is live.</summary>
        public static void Apply(SaveSlotData data)
        {
            UpgradeState.Reset();
            foreach (PartKind part in data.InstalledParts) UpgradeState.Install(part);

            PickupWallet.Reset();
            PickupWallet.SetPowerCells(data.PowerCells);
            foreach (PartKind part in data.PendingParts) PickupWallet.AddPart(part);
        }

        /// <summary>Drop Max at a slot's saved position/heading — a teleport, not a walk, so the
        /// CharacterController is disabled around the move (it otherwise keeps its own notion of where
        /// it stood, and fights a direct transform write).</summary>
        public static void PlacePlayer(SaveSlotData data)
        {
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            if (player == null) return;

            var cc = player.GetComponent<CharacterController>();
            bool wasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;

            player.transform.SetPositionAndRotation(
                new Vector3(data.PosX, data.PosY, data.PosZ),
                Quaternion.Euler(0f, data.RotY, 0f));

            if (cc != null) cc.enabled = wasEnabled;
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
