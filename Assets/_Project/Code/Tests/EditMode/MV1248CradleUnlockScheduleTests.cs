using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1248 (one new test, MV-465 Rule 1): <c>AssemblyCradle.OnDestroyed</c> only ever placed
    /// pickups — MV-1223's own unlock schedule (2nd cradle banks Secondary/Orbitals, 4th banks
    /// Sentinel/SUPPORT) and the "destroyed cradles survive a resume" save path were never built.
    ///
    /// Fails to COMPILE on base commit 6e7e7d9: <c>PendingMorphingModule.SecondaryFamilyPending</c>/
    /// <c>SentinelFamilyPending</c>, <c>AssemblyCradle.Id</c>/<c>SetId</c>/<c>ApplyCheckpointDestroyed</c>,
    /// and <c>FactoryCensus.ApplyCheckpointDestroyedCradleIds</c> do not exist there at all.
    ///
    /// Tier 2 (resolved values): every assertion reads a live <see cref="PendingMorphingModule"/> flag,
    /// a live <see cref="AssemblyCradle.IsAlive"/>, a live <see cref="FactoryCensus.CradlesDestroyed"/>,
    /// or a live <see cref="Pickup"/> count — never an authored constant asserted back at itself.
    /// </summary>
    public sealed class MV1248CradleUnlockScheduleTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1248-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            FactoryCensus.Reset();
            PendingMorphingModule.Reset();
            AssemblyCradle.ResetRegistry();
            Pickup.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            FactoryCensus.Reset();
            PendingMorphingModule.Reset();
            AssemblyCradle.ResetRegistry();
            Pickup.ResetRegistry();
            foreach (var c in Object.FindObjectsByType<AssemblyCradle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(c.gameObject);
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static AssemblyCradle MakeCradle(string id)
        {
            var go = new GameObject($"cradle_{id}");
            var cradle = go.AddComponent<AssemblyCradle>();
            cradle.Build();   // AddComponent's own Awake never runs outside Play mode
            cradle.SetId(id);
            return cradle;
        }

        private static void Destroy(AssemblyCradle cradle) =>
            cradle.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));

        [Test]
        public void DestroyingFourCradlesBanksSecondaryThenSentinel_AndSurvivesAResumeWithNoSecondPayout()
        {
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST" });

            AssemblyCradle c1 = MakeCradle("a1_cradle1");
            AssemblyCradle c2 = MakeCradle("a1_cradle2");
            AssemblyCradle c3 = MakeCradle("a1_cradle3");
            AssemblyCradle c4 = MakeCradle("a1_cradle4");
            AssemblyCradle untouched = MakeCradle("a1_cradle5");

            Destroy(c1);
            Assert.AreEqual(1, FactoryCensus.CradlesDestroyed);
            Assert.IsFalse(PendingMorphingModule.SecondaryFamilyPending, "the 1st cradle must not bank anything yet");

            Destroy(c2);
            Assert.AreEqual(2, FactoryCensus.CradlesDestroyed);
            Assert.IsTrue(PendingMorphingModule.SecondaryFamilyPending, "the 2nd cradle must bank the Secondary (Orbitals) family");
            Assert.IsFalse(PendingMorphingModule.SentinelFamilyPending, "the Sentinel family must not bank yet");

            Destroy(c3);
            Assert.AreEqual(3, FactoryCensus.CradlesDestroyed);
            Assert.IsFalse(PendingMorphingModule.SentinelFamilyPending, "the 3rd cradle must not bank the Sentinel family");

            Destroy(c4);
            Assert.AreEqual(4, FactoryCensus.CradlesDestroyed);
            Assert.IsTrue(PendingMorphingModule.SentinelFamilyPending, "the 4th cradle must bank the Sentinel (SUPPORT) family");

            SaveSystem.CaptureCheckpoint(0, areaIndex: 1);

            // Cold-boot RESUME: the level rebuilds from scratch -- fresh cradle instances, same stable
            // ids, nothing yet knows the checkpoint's history. Disturb every bit of live state a passing
            // restore must repair, so a pass is provably doing the work rather than reading stale statics.
            Object.DestroyImmediate(c1.gameObject);
            Object.DestroyImmediate(c2.gameObject);
            Object.DestroyImmediate(c3.gameObject);
            Object.DestroyImmediate(c4.gameObject);
            Object.DestroyImmediate(untouched.gameObject);
            FactoryCensus.Reset();
            PendingMorphingModule.Reset();   // same wipe HomeScreen.OnResume always runs before a restore

            AssemblyCradle freshC1 = MakeCradle("a1_cradle1");
            AssemblyCradle freshC2 = MakeCradle("a1_cradle2");
            AssemblyCradle freshC3 = MakeCradle("a1_cradle3");
            AssemblyCradle freshC4 = MakeCradle("a1_cradle4");
            AssemblyCradle freshUntouched = MakeCradle("a1_cradle5");

            int pickupsBeforeRestore = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None).Length;

            bool restored = SaveSystem.RestoreCheckpoint(0);

            Assert.IsTrue(restored, "a captured checkpoint must report as restored");
            Assert.IsFalse(freshC1.IsAlive, "cradle 1, destroyed before the checkpoint, must still read destroyed after restoring");
            Assert.IsFalse(freshC2.IsAlive, "cradle 2 must still read destroyed after restoring");
            Assert.IsFalse(freshC3.IsAlive, "cradle 3 must still read destroyed after restoring");
            Assert.IsFalse(freshC4.IsAlive, "cradle 4 must still read destroyed after restoring");
            Assert.IsTrue(freshUntouched.IsAlive, "a cradle never destroyed must stay alive after restoring");
            Assert.AreEqual(4, FactoryCensus.CradlesDestroyed, "the restored census must read exactly the 4 destroyed before the checkpoint");
            Assert.IsTrue(PendingMorphingModule.SecondaryFamilyPending, "the banked Secondary family reveal must survive a resume");
            Assert.IsTrue(PendingMorphingModule.SentinelFamilyPending, "the banked Sentinel family reveal must survive a resume");

            int pickupsAfterRestore = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None).Length;
            Assert.AreEqual(pickupsBeforeRestore, pickupsAfterRestore,
                "restoring a checkpoint's already-recorded cradle destruction must never drop a second payout");
        }
    }
}
