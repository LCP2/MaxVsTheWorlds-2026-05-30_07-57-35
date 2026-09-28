using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Save;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-985 (the one new test, per CC_AUTONOMY's testing policy): RESUME after playing from World 1
    /// into World 2 used to always resume in World 1. <c>RunFlow.QuitToMenu</c> drops
    /// <c>SaveSystem.ActiveSlot</c> to -1 and reloads, and <c>BackyardPath.ActiveWorldIndex()</c> always
    /// resolves World 0 with no active slot — so the scene behind the Home screen is always World 1, and
    /// the old <c>HomeScreen.OnResume</c> restored the checkpoint onto whichever world was already built,
    /// with no reload, because <c>SaveSlotData</c> never recorded which world the checkpoint was captured
    /// in.
    ///
    /// Fails to compile on base commit 8c86feb: <c>SaveSlotData.CheckpointWorldIndex</c>,
    /// <c>SaveSystem.ResolveResumePlan</c>/<c>ResumePlan</c>/<c>PendingResume</c>/<c>PendingResumePlan</c>
    /// don't exist there, and <c>BackyardPath.ActiveWorldIndex()</c> is private — none of which this test
    /// can reach; that gap is the fix itself.
    /// </summary>
    public sealed class MV985ResumeWorldCheckpointTests
    {
        private string _dir;
        private GameObject _directorGo;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv985-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            PickupWallet.Reset();   // also resets RigState
            DeathRunState.Reset();

            // A profile whose own WorldIndex (2 — playing World 3 next) is deliberately NOT the world
            // the checkpoint below is captured in (World 2, index 1) — a replayed earlier world, same
            // shape MV-921 already distinguishes, so a regression that reads WorldIndex instead of the
            // checkpoint's own world fails loudly rather than by a coincidental match.
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 2 });
        }

        [TearDown]
        public void TearDown()
        {
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            SaveSystem.ResetForTests();
            PickupWallet.Reset();
            DeathRunState.Reset();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void CheckpointRecordsItsWorld_ResumePlanReloads_AndBackyardPathHonoursPendingResume()
        {
            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(null, worldIndex: 1);   // World 2 — the world actually being played

            SaveSystem.CaptureCheckpoint(0, areaIndex: 16, worldIndex: director.ActiveWorldIndex);

            SaveSlotData saved = SaveSystem.Load(0);
            Assert.AreEqual(1, saved.CheckpointWorldIndex,
                "the checkpoint must record the world actually PLAYED, not the profile's own WorldIndex");
            Assert.AreEqual(16, saved.CheckpointAreaIndex);

            // A scene already built as World 1 (index 0) — exactly what RunFlow.QuitToMenu leaves
            // resolved behind the Home screen — must resolve a reload into World 2, area 16.
            SaveSystem.ResumePlan plan = SaveSystem.ResolveResumePlan(0, builtWorldIndex: 0);
            Assert.IsTrue(plan.NeedsReload, "a checkpoint in a different world than what's built must reload");
            Assert.AreEqual(1, plan.WorldIndex, "the resume plan must target the checkpoint's own world");
            Assert.AreEqual(16, plan.AreaIndex);

            // What HomeScreen.OnResume sets right before triggering that reload — BackyardPath's world
            // resolution on the reloaded scene's Awake must honour it ahead of SaveSlotData.WorldIndex.
            SaveSystem.PendingResume = new SaveSystem.PendingResumePlan(0, plan.WorldIndex);
            Assert.AreEqual(1, BackyardPath.ActiveWorldIndex(),
                "BackyardPath must resolve the pending resume's world, not the profile's own WorldIndex (2)");
        }
    }
}
