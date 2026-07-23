using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Save;
using MaxWorlds.Player;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Keeps the active slot current while a run is live (YT-151): installing a part or banking a
    /// cell checkpoints immediately rather than waiting for the autosave timer, and nothing is
    /// written before a slot is picked or once Max is dead.
    /// </summary>
    public sealed class SaveDriverPlayTests
    {
        private GameObject _driverGo;
        private GameObject _playerGo;
        private string _dir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-driver-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            UpgradeState.Reset();
            PickupWallet.Reset();

            foreach (var d in Object.FindObjectsByType<SaveDriver>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);

            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.AddComponent<PlayerController>();   // RequireComponent brings the CharacterController

            _driverGo = new GameObject("SaveDriver");
            _driverGo.AddComponent<SaveDriver>();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_driverGo != null) Object.Destroy(_driverGo);
            if (_playerGo != null) Object.Destroy(_playerGo);

            SaveSystem.ResetForTests();
            UpgradeState.Reset();
            PickupWallet.Reset();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NoActiveSlot_InstallingAPartWritesNothing()
        {
            UpgradeState.Install(PartKind.BeamNozzle);
            yield return null;

            Assert.That(SaveSystem.Load(0).HasData, Is.False, "nothing should save before a slot is picked");
        }

        [UnityTest]
        public IEnumerator InstallingAPart_SavesTheActiveSlotImmediately()
        {
            SaveSystem.ActiveSlot = 2;
            yield return null;

            UpgradeState.Install(PartKind.Hydro);
            yield return null;

            SaveSlotData data = SaveSystem.Load(2);
            Assert.That(data.HasData, Is.True, "installing a part must checkpoint immediately, not wait for the timer");
            Assert.That(data.InstalledParts, Does.Contain(PartKind.Hydro));
        }

        [UnityTest]
        public IEnumerator BankingACell_SavesTheActiveSlotImmediately()
        {
            SaveSystem.ActiveSlot = 0;
            yield return null;

            PickupWallet.AddPowerCell();
            yield return null;

            Assert.That(SaveSystem.Load(0).PowerCells, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ADeadPlayer_IsNotCheckpointed()
        {
            SaveSystem.ActiveSlot = 0;
            var health = _playerGo.AddComponent<PlayerHealth>();
            yield return null;

            health.TakeDamage(new MaxWorlds.Core.DamageInfo(9999f, Vector3.zero, Vector3.forward,
                MaxWorlds.Core.Team.Enemy));
            PickupWallet.AddPowerCell();   // would otherwise trigger an immediate save
            yield return null;

            Assert.That(SaveSystem.Load(0).HasData, Is.False,
                "a dead Max must not be checkpointed — the resume spot would be the thing that killed him");
        }
    }
}
