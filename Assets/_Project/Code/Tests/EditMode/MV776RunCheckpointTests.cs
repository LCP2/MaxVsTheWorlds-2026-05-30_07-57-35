using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Arena;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-776: World 2's mid-run checkpoint gains the flood level (rewound to the checkpoint's own
    /// value, not the run's live one) and the destroyed-Replicator set — before this ticket a resume
    /// restored THE RIG/wallet/deaths/area (MV-524/MV-557) but had no concept of either, so a resumed
    /// run always reappeared with the flood at 0 and every Replicator the player had already destroyed
    /// alive again. Fails to COMPILE on base commit 9a5499f: <c>SaveSlotData.CheckpointFloodLevel01</c>/
    /// <c>CheckpointDestroyedReplicatorIds</c>, <c>StormdrainFlood.RestoreLevel01</c>,
    /// <c>Replicator.Id</c>/<c>SetId</c>, and <c>FactoryCensus.ApplyCheckpointDestroyedIds</c> do not
    /// exist there at all.
    ///
    /// Tier 2 (resolved values): every assertion reads a resolved <see cref="StormdrainFlood.Level01"/>,
    /// a live <see cref="Replicator.IsAlive"/>, a resolved <see cref="RigState.Level"/>/
    /// <see cref="PickupWallet"/> balance, or the saved <see cref="SaveSlotData"/> itself — never an
    /// authored constant asserted back at itself.
    /// </summary>
    public sealed class MV776RunCheckpointTests
    {
        private string _dir;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv776-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            PickupWallet.Reset();   // also resets RigState
            DeathRunState.Reset();
            FactoryCensus.Reset();
            StormdrainFlood.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            PickupWallet.Reset();
            DeathRunState.Reset();
            FactoryCensus.Reset();
            StormdrainFlood.Reset();
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private Replicator MakeReplicator(string id, string areaId)
        {
            var go = new GameObject($"replicator_{id}");
            _spawned.Add(go);
            var rep = go.AddComponent<Replicator>();
            rep.Build();
            rep.SetId(id);
            FactoryCensus.RegisterReplicator(rep, areaId);
            return rep;
        }

        [Test]
        public void CheckpointRestoresFloodReplicatorsRigAndWallet_AndWinClearsIt_AndALegacySaveIsUnaffected()
        {
            // AC5 (no-regression guard): a slot with no checkpoint restores exactly as today, and never
            // touches the live flood.
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "DEXTER", WorldIndex = 1 });
            Assert.IsFalse(SaveSystem.RestoreCheckpoint(0), "a slot with no checkpoint must report nothing to restore");
            Assert.AreEqual(0f, StormdrainFlood.Level01, "no checkpoint must never touch the live flood");

            // Arrange a run: one Replicator destroyed BEFORE the gate, rig/wallet state, flood at 40%
            // — then pass the gate (the checkpoint).
            Replicator destroyedBeforeGate = MakeReplicator("a5_replicator1", "a5");
            Replicator destroyedAfterGate = MakeReplicator("a9_replicator1", "a9");

            destroyedBeforeGate.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
            FactoryCensus.ReportReplicatorDestroyed(destroyedBeforeGate);

            RigState.RaiseLevel("p_dmg");   // owned at start level 1 -> level 2
            PickupWallet.SetPowerCells(12);
            PickupWallet.SetPowerCellSecondary(5);
            StormdrainFlood.RestoreLevel01(0.40f);

            SaveSystem.CaptureCheckpoint(0, areaIndex: 9);

            // The run continues past the gate: a second Replicator dies, the flood keeps rising, and
            // the player spends cells — then dies at flood 70%.
            destroyedAfterGate.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
            FactoryCensus.ReportReplicatorDestroyed(destroyedAfterGate);
            StormdrainFlood.RestoreLevel01(0.70f);
            PickupWallet.SetPowerCells(0);

            // Disturb every bit of live state a passing restore must repair, so a pass is provably doing
            // the work rather than reading stale statics.
            PickupWallet.Reset();
            DeathRunState.Reset();

            // A resume rebuilds the level from scratch — fresh Replicator instances, same stable ids,
            // nothing yet knows which of them the checkpoint already destroyed.
            FactoryCensus.Reset();
            Replicator freshBeforeGate = MakeReplicator("a5_replicator1", "a5");
            Replicator freshAfterGate = MakeReplicator("a9_replicator1", "a9");

            bool restored = SaveSystem.RestoreCheckpoint(0);

            Assert.IsTrue(restored);
            Assert.AreEqual(9, SaveSystem.Load(0).CheckpointAreaIndex,
                "restores at the gate's own area, not wherever the death happened");
            Assert.AreEqual(0.40f, StormdrainFlood.Level01, 0.0001f,
                "the flood must rewind to the gate's own level, not the death's 70%");
            Assert.AreEqual(2, RigState.Level("p_dmg"));
            Assert.AreEqual(12, PickupWallet.PowerCells);
            Assert.AreEqual(5, PickupWallet.PowerCellsSecondary);
            Assert.IsFalse(freshBeforeGate.IsAlive,
                "a Replicator destroyed before the checkpoint must still read destroyed after restoring");
            Assert.IsTrue(freshAfterGate.IsAlive,
                "a Replicator destroyed after the checkpoint must be alive again on restore");

            // Winning the world must wipe the checkpoint outright, not just its area index — the
            // ticket's own named bug ("a stale checkpoint into a world already finished").
            SaveSystem.RecordResult(0, deathsTaken: 3);
            SaveSlotData afterVictory = SaveSystem.Load(0);
            Assert.AreEqual(2, afterVictory.WorldIndex, "Victory must still advance WorldIndex");
            Assert.IsFalse(afterVictory.HasRunInProgress,
                "winning the world must clear the checkpoint outright, not just its area index");
            Assert.IsFalse(SaveSystem.RestoreCheckpoint(0),
                "a subsequent load must not resume into the world just finished");
        }
    }
}
