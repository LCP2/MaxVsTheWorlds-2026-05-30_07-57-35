using System.IO;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-524 parts 2/3: the plain, EditMode-testable methods behind the two triggers this ticket wires
    /// (<see cref="MaxWorlds.Enemies.AreaAccumulationDirector.EnterArea"/>, <see cref="MaxWorlds.Arena.WorldRunner"/>'s
    /// pause/focus handlers) and the HomeScreen RESUME/PLAY split — <see cref="SaveSystem.CaptureActiveCheckpoint"/>,
    /// <see cref="SaveSystem.ClearCheckpoint"/> and <see cref="WeaponSystemState.RebuildAcquiredFromRigState"/>.
    /// None of these three methods existed on main before this ticket, so every assertion below was
    /// unreachable on the base commit — the same "proven to fail" bar MV-557's own test used for new
    /// functionality (Testing policy v2, Rule 1).
    ///
    /// WorldRunner's own OnApplicationPause/OnApplicationFocus/ResumeCheckpoint and
    /// AreaAccumulationDirector.EnterArea's new call site are thin MonoBehaviour/scene-wiring around
    /// these same methods — the same PlayMode-shaped gap MV427DeathContinuesTests documents for the
    /// rest of WorldRunner's orchestration, deliberately not covered here (CC_AUTONOMY.md forbids
    /// authoring PlayMode tests).
    /// </summary>
    public sealed class MV524ResumeWiringTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-save-tests-mv524");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            PickupWallet.Reset();   // also resets RigState
            DeathRunState.Reset();
            WeaponSystemState.Reset();
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            PickupWallet.Reset();
            DeathRunState.Reset();
            WeaponSystemState.Reset();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void CaptureActiveCheckpoint_ClearCheckpoint_AndAcquiredOrderRebuild_AllBehaveCorrectly()
        {
            // CaptureActiveCheckpoint (AC6's extracted plain method): no active slot must never write
            // anything — the guard a capture/press-kit/perf-capture run (and every EditMode test) relies on.
            SaveSystem.CaptureActiveCheckpoint(3);
            Assert.That(Directory.Exists(_dir), Is.False,
                "with no active slot, CaptureActiveCheckpoint must not write a checkpoint file at all");

            // With an active slot, it captures into THAT slot, at the given area — what EnterArea and
            // the pause/focus handlers both rely on.
            SaveSystem.ActiveSlot = 1;
            PickupWallet.SetPowerCells(12);
            SaveSystem.CaptureActiveCheckpoint(4);

            SaveSlotData captured = SaveSystem.Load(1);
            Assert.That(captured.HasRunInProgress, Is.True, "the EnterArea/pause trigger's own write must land");
            Assert.That(captured.CheckpointAreaIndex, Is.EqualTo(4));
            Assert.That(captured.CheckpointPowerCells, Is.EqualTo(12));

            // ClearCheckpoint (AC4's "PLAY clears the captured run"): wipes the run, leaves identity and
            // personal best untouched.
            SaveSystem.Save(1, new SaveSlotData
            {
                HasData = true,
                DisplayName = "DEXTER",
                BestDeathsToVictory = 5,
                HasRunInProgress = true,
                CheckpointAreaIndex = 4,
                CheckpointPowerCells = 12,
            });

            SaveSystem.ClearCheckpoint(1);

            SaveSlotData cleared = SaveSystem.Load(1);
            Assert.That(cleared.HasRunInProgress, Is.False, "PLAY must clear a slot's captured run");
            Assert.That(cleared.DisplayName, Is.EqualTo("DEXTER"), "clearing a checkpoint must never touch identity");
            Assert.That(cleared.BestDeathsToVictory, Is.EqualTo(5), "clearing a checkpoint must never touch the personal best");

            // RebuildAcquiredFromRigState (part 3's HomeScreen.OnResume step): a resume restores
            // RigState directly (SaveSystem.RestoreCheckpoint -> RigState.RestoreSnapshot), which never
            // touches WeaponSystemState's own acquisition-order list — without the rebuild, an owned
            // ability would read as unacquired on the Weapons screen despite working in combat.
            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Speed), Is.False, "sanity: nothing owned yet");

            RigState.AcquireCap("m_spd");   // simulates what RigState.RestoreSnapshot leaves behind
            Assert.That(WeaponSystemState.IsAcquired(AbilityKind.Speed), Is.True,
                "IsAcquired reads RigState directly, so this is already true before any rebuild");
            Assert.That(WeaponSystemState.Acquired, Is.Empty,
                "but Acquired (the Weapons screen's own ordered list) is NOT populated by RigState alone");

            WeaponSystemState.RebuildAcquiredFromRigState();

            CollectionAssert.Contains(new System.Collections.Generic.List<AbilityKind>(WeaponSystemState.Acquired),
                AbilityKind.Speed, "the rebuild must repopulate Acquired from RigState's live ownership");
        }
    }
}
