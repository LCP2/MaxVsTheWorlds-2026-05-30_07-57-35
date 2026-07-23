using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Save
{
    /// <summary>
    /// Keeps the active slot's save current while a run is live (YT-151), so Continue drops the player
    /// back mid-level instead of at the authored spawn. Snapshots Max's position on a short timer and
    /// immediately whenever <see cref="UpgradeState"/> or <see cref="PickupWallet"/> change (installing a
    /// part or banking a cell shouldn't have to wait for the next tick to be safe on disk).
    ///
    /// Self-installing, same idiom as every other system (<see cref="MaxWorlds.Core.SceneInstallers"/>).
    /// A no-op until the Home screen hands off a slot (<see cref="SaveSystem.ActiveSlot"/> &gt;= 0), and
    /// stops the moment Max is dead — a save should resume you back in the fight, not in the exact spot
    /// that killed you.
    /// </summary>
    public sealed class SaveDriver : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<SaveDriver>() != null) return;
            new GameObject("SaveDriver").AddComponent<SaveDriver>();
        }

        private const float AutosaveInterval = 5f;

        private PlayerController _player;
        private PlayerHealth _health;
        private float _timer;

        private void OnEnable()
        {
            UpgradeState.Changed += SaveNow;
            PickupWallet.PowerCellsChanged += OnPowerCellsChanged;
            PickupWallet.PartsChanged += OnPartsChanged;
        }

        private void OnDisable()
        {
            UpgradeState.Changed -= SaveNow;
            PickupWallet.PowerCellsChanged -= OnPowerCellsChanged;
            PickupWallet.PartsChanged -= OnPartsChanged;
        }

        private void OnPowerCellsChanged(int _) => SaveNow();
        private void OnPartsChanged(int _) => SaveNow();

        private void Update()
        {
            if (SaveSystem.ActiveSlot < 0) return;

            // Unscaled: a paused Result/Upgrade screen must not stall the autosave clock indefinitely.
            _timer += Time.unscaledDeltaTime;
            if (_timer < AutosaveInterval) return;
            _timer = 0f;
            SaveNow();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveNow();
        }

        private void OnApplicationQuit() => SaveNow();

        private void SaveNow()
        {
            if (SaveSystem.ActiveSlot < 0) return;

            if (_player == null) _player = FindFirstObjectByType<PlayerController>();
            if (_player == null) return;

            if (_health == null) _health = _player.GetComponent<PlayerHealth>();
            if (_health != null && !_health.IsAlive) return;   // don't checkpoint the spot that killed you

            SaveSystem.CaptureAndSave(SaveSystem.ActiveSlot, SaveSystem.DefaultLevelId);
        }
    }
}
