using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The save/load system underneath the Home screen (YT-151): three slots on disk, capturing
    /// Max's position plus whatever <see cref="UpgradeState"/>/<see cref="PickupWallet"/> hold, and
    /// restoring both cleanly on Continue. <see cref="SaveSystem.DirectoryOverride"/> points every
    /// test at a scratch folder so none of this ever touches a real device's save data.
    /// </summary>
    public sealed class SaveSystemTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            UpgradeState.Reset();
            PickupWallet.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            UpgradeState.Reset();
            PickupWallet.Reset();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void AnUntouchedSlotReadsEmpty()
        {
            SaveSlotData data = SaveSystem.Load(0);
            Assert.That(data.HasData, Is.False, "a slot that was never saved must read as empty");
        }

        [Test]
        public void SaveThenLoad_RoundTripsPositionLevelAndTallies()
        {
            UpgradeState.Install(PartKind.BeamNozzle);
            UpgradeState.Install(PartKind.Hydro);
            PickupWallet.AddPowerCell();
            PickupWallet.AddPowerCell();
            PickupWallet.AddPart(PartKind.PowerNozzle);

            var pos = new Vector3(12.5f, 0f, -8f);
            SaveSlotData written = SaveSystem.Capture(pos, 135f, SaveSystem.DefaultLevelId);
            SaveSystem.Save(1, written);

            SaveSlotData read = SaveSystem.Load(1);

            Assert.That(read.HasData, Is.True);
            Assert.That(read.LevelId, Is.EqualTo(SaveSystem.DefaultLevelId));
            Assert.That(read.PosX, Is.EqualTo(12.5f).Within(1e-4f));
            Assert.That(read.PosY, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(read.PosZ, Is.EqualTo(-8f).Within(1e-4f));
            Assert.That(read.RotY, Is.EqualTo(135f).Within(1e-4f));
            Assert.That(read.InstalledParts, Is.EquivalentTo(new[] { PartKind.BeamNozzle, PartKind.Hydro }));
            Assert.That(read.PowerCells, Is.EqualTo(2));
            Assert.That(read.PendingParts, Is.EqualTo(new[] { PartKind.PowerNozzle }));
        }

        [Test]
        public void Apply_RestoresUpgradesAndWallet_ReplacingWhateverWasLive()
        {
            UpgradeState.Install(PartKind.AugmentationHarness);   // stale live state that must be wiped
            PickupWallet.AddPowerCell();

            var data = new SaveSlotData
            {
                HasData = true,
                InstalledParts = { PartKind.BeamNozzle, PartKind.PowerNozzle },
                PowerCells = 9,
                PendingParts = { PartKind.Hydro },
            };

            SaveSystem.Apply(data);

            Assert.That(UpgradeState.IsInstalled(PartKind.AugmentationHarness), Is.False,
                "Apply must replace the live state, not merge into it");
            Assert.That(UpgradeState.IsInstalled(PartKind.BeamNozzle), Is.True);
            Assert.That(UpgradeState.IsInstalled(PartKind.PowerNozzle), Is.True);
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(9));
            Assert.That(PickupWallet.PartsPending, Is.EqualTo(1));
            Assert.That(PickupWallet.TryPeekPart(out PartKind next), Is.True);
            Assert.That(next, Is.EqualTo(PartKind.Hydro));
        }

        [Test]
        public void ACorruptFileReadsAsEmptyRatherThanThrowing()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "save_slot_2.json"), "{ not json");

            SaveSlotData data = SaveSystem.Load(2);

            Assert.That(data.HasData, Is.False);
        }

        [Test]
        public void DeleteRemovesTheSlotFile()
        {
            SaveSystem.Save(0, SaveSystem.Capture(Vector3.zero, 0f, SaveSystem.DefaultLevelId));
            Assert.That(SaveSystem.Load(0).HasData, Is.True);

            SaveSystem.Delete(0);

            Assert.That(SaveSystem.Load(0).HasData, Is.False);
        }
    }
}
