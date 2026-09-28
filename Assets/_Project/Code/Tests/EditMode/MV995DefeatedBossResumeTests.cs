using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-995: a cold-boot RESUME rebuilds every authored boss fresh and Dormant
    /// (<c>MapRuntime.BuildBoss</c>), with no memory of a prior fight -- so a boss the player had already
    /// beaten in a20 before the MV-993 crash came back alive, HUD bar and all, the moment Max next walked
    /// into its area. Fails on base commit 3254a83: <c>SaveSlotData.CheckpointDefeatedBossAreas</c> and
    /// <c>BossCensus.DefeatedAreaIndices</c>/<c>ApplyCheckpointDefeatedAreas</c> do not exist there at all.
    ///
    /// Tier 2 (resolved values): asserts the saved <see cref="SaveSlotData"/> field, the resolved
    /// <see cref="BossCensus.AnyLivingIn"/>, and a real GameObject's Unity fake-null after
    /// <c>DestroyImmediate</c> -- never an authored constant asserted back at itself.
    /// </summary>
    public sealed class MV995DefeatedBossResumeTests
    {
        private string _dir;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv995-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            BossCensus.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            BossCensus.Reset();
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private BigBermudaBoss NewBoss(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            _spawned.Add(go);
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            return go.AddComponent<BigBermudaBoss>();
        }

        [Test]
        public void ResumeSilentlyRemovesAnAlreadyDefeatedAreaBossAndFiresNoSignal()
        {
            // Arrange: area 20's boss was fought and killed BEFORE the checkpoint at area 21 was captured.
            BigBermudaBoss killedBoss = NewBoss("a20_boss1");
            BossCensus.Register(killedBoss, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 20);
            BossCensus.ReportDefeated(killedBoss);
            Assert.IsFalse(BossCensus.AnyLivingIn(20), "precondition: area 20's boss must already be dead");

            SaveSystem.CaptureCheckpoint(0, areaIndex: 21);

            SaveSlotData saved = SaveSystem.Load(0);
            CollectionAssert.Contains(saved.CheckpointDefeatedBossAreas, 20,
                "a checkpoint captured after area 20's boss died must record area 20 as defeated");

            // Act: a RESUME does a cold scene rebuild -- a fresh BigBermudaBoss stands in area 20 again,
            // registered exactly as a real Wake() would register it once Max walks in (BossCensus has no
            // memory yet that this INSTANCE belongs to a fight already settled -- that's the whole bug).
            BigBermudaBoss freshBoss = NewBoss("a20_boss1_fresh");
            BossCensus.Register(freshBoss, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 20);

            int defeatedSignalCount = 0;
            void OnDefeated() => defeatedSignalCount++;
            HudSignals.BossDefeated += OnDefeated;
            bool restored;
            try
            {
                restored = SaveSystem.RestoreCheckpoint(0);
            }
            finally
            {
                HudSignals.BossDefeated -= OnDefeated;
            }

            Assert.IsTrue(restored);
            Assert.IsFalse(BossCensus.AnyLivingIn(20),
                "a RESUME must never leave area 20's boss re-registered as living");
            Assert.IsTrue(freshBoss == null,
                "the freshly-rebuilt boss for an already-defeated area must be destroyed, not left standing");
            Assert.AreEqual(0, defeatedSignalCount,
                "restoring a checkpoint's already-recorded boss defeat must never re-fire BossDefeated");
        }
    }
}
