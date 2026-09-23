using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Save;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-922: World 1's mid-run checkpoint gains the destroyed-shed set, the World 1 equivalent of what
    /// MV-776 already gave World 2's Replicator — before this ticket a resume restored the area
    /// (MV-524/MV-557) but every Mower Hutch shed behind the player came back alive and the
    /// destroyed-factory count restarted from zero, so a player had to replay World 1 from the very
    /// start to reach 17/17. Fails to COMPILE on the base commit: <c>MowerHutch.Id</c>/<c>SetId</c>/
    /// <c>ApplyCheckpointDestroyed</c>, <c>FactoryCensus.DestroyedShedIds</c>/
    /// <c>ApplyCheckpointDestroyedShedIds</c>, and <c>SaveSlotData.CheckpointDestroyedShedIds</c> do not
    /// exist there at all.
    ///
    /// Tier 2 (resolved values): every assertion reads a live <see cref="MowerHutch.IsAlive"/>, the
    /// resolved <see cref="FactoryCensus.Destroyed"/> count, or the saved <see cref="SaveSlotData"/>
    /// itself — never an authored constant asserted back at itself.
    /// </summary>
    public sealed class MV922ShedCheckpointTests
    {
        private string _dir;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv922-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            FactoryCensus.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            FactoryCensus.Reset();
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private MowerHutch MakeHutch(string id)
        {
            var go = new GameObject($"hutch_{id}");
            _spawned.Add(go);
            var hutch = go.AddComponent<MowerHutch>();   // RequireComponent brings EnemySpawner
            hutch.Build();                               // Build() registers it with FactoryCensus
            hutch.SetId(id);
            return hutch;
        }

        [Test]
        public void CheckpointRestoresDestroyedShedsAndTheirCount_AndSpareShedsStayAlive()
        {
            // Arrange a World 1 run: two sheds destroyed BEFORE the gate, one destroyed AFTER — then
            // pass the gate (the checkpoint).
            MowerHutch destroyedBeforeGateA = MakeHutch("a2_shed1");
            MowerHutch destroyedBeforeGateB = MakeHutch("a5_shed1");
            MowerHutch destroyedAfterGate = MakeHutch("a9_shed1");

            destroyedBeforeGateA.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
            destroyedBeforeGateB.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
            Assert.AreEqual(2, FactoryCensus.Destroyed, "two sheds must be down before the checkpoint is captured");

            SaveSystem.CaptureCheckpoint(0, areaIndex: 9);

            // The run continues past the gate: a third shed dies too.
            destroyedAfterGate.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
            Assert.AreEqual(3, FactoryCensus.Destroyed, "the run's live state now has all three sheds down");

            // A resume rebuilds the level from scratch — fresh MowerHutch instances, same stable ids,
            // nothing yet knows which of them the checkpoint already destroyed.
            FactoryCensus.Reset();
            MowerHutch freshA = MakeHutch("a2_shed1");
            MowerHutch freshB = MakeHutch("a5_shed1");
            MowerHutch freshC = MakeHutch("a9_shed1");

            bool restored = SaveSystem.RestoreCheckpoint(0);

            Assert.IsTrue(restored);
            Assert.IsFalse(freshA.IsAlive, "a shed destroyed before the checkpoint must still read destroyed after restoring");
            Assert.IsFalse(freshB.IsAlive, "a shed destroyed before the checkpoint must still read destroyed after restoring");
            Assert.IsTrue(freshC.IsAlive, "a shed destroyed AFTER the checkpoint must be alive again on restore");
            Assert.AreEqual(2, FactoryCensus.Destroyed,
                "the destroyed-factory count after restore must equal the count at checkpoint time, not zero and not the run's later total");
        }
    }
}
