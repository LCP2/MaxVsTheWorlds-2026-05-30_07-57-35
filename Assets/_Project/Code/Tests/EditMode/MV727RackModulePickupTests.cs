using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-727 (the one new test, per CC_AUTONOMY's testing policy): the Shoulder Rack is a WORLD 2
    /// PICKUP now, not a RIG purchase — MV-694 unlocked SECONDARY the instant the Weapon Core morph
    /// landed (<see cref="WeaponSystemState.ApplyWeaponCoreMorph"/>), making <c>s_rkt</c> immediately
    /// cell-buyable with no shed/Replicator involved. This ticket reverses that: SECONDARY stays
    /// LOCKED for the whole run until the player walks over the Rack Module the first Replicator
    /// drops.
    ///
    /// MV-1090 updated this test's own assertions: collecting the module used to unlock SECONDARY and
    /// grant <c>s_rkt</c> in the SAME instant Collect ran, which let the Shoulder Rack start
    /// auto-firing before the player had ever opened THE RIG to see the reveal — the bug MV-1090 fixes.
    /// Collecting now only BANKS the module (<see cref="PendingMorphingModule.RackModulePending"/>); the
    /// unlock + grant move to THE RIG's next open (<c>MV1090RackModuleBankedUntilRevealTests</c> covers
    /// that half end to end). This test still proves the one thing that belongs at the Collect call
    /// site: the grant does NOT happen there, and nothing is spent banking it.
    ///
    /// Fails on base commit 43c8d6e: <c>PickupKind</c> has no <c>RackModule</c> member at all, so this
    /// does not compile against that commit (CS0117 "'PickupKind' does not contain a definition for
    /// 'RackModule'") — the same class of pre-fix evidence <c>MV694ShoulderRackTests</c>/
    /// <c>MV698WeaponCoreFinaleDropTests</c> document for a brand-new pickup kind. Quoted compiler
    /// output (Unity 6000.4.9f1, cc-verify.bat's [1/6] editor-compile step against 43c8d6e with only
    /// this test file added, nothing else changed — Logs\compile.log):
    /// <code>
    /// Assets\_Project\Code\Tests\EditMode\MV727RackModulePickupTests.cs(60,48): error CS0117:
    /// 'PickupKind' does not contain a definition for 'RackModule'
    /// Assets\_Project\Code\Tests\EditMode\MV727RackModulePickupTests.cs(61,63): error CS0117:
    /// 'PickupKind' does not contain a definition for 'RackModule'
    /// </code>
    /// </summary>
    public sealed class MV727RackModulePickupTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
            PickupWallet.Reset();
            PendingMorphingModule.Reset();
            // World 2's board is where s_rkt actually lives (MV-732 removed it from World 1's file).
            RigBoard.UseWorld(1);
        }

        [Test]
        public void CollectingTheRackModulePickupBanksItWithoutUnlockingOrGrantingAnything_MV727()
        {
            // Arrange: a fresh World 2 run — SECONDARY locked, s_rkt unowned, same as right after a
            // real Weapon Core morph now leaves it (this ticket's own reversal of MV-694).
            Assert.IsFalse(RigState.IsCategoryUnlocked("SECONDARY"),
                "a fresh World 2 run must start with SECONDARY locked -- it is now found, not bought");
            Assert.AreEqual(0, RigState.Level("s_rkt"), "s_rkt must start unowned");

            int cellsBefore = PickupWallet.PowerCells;
            int powerCellsSecondaryBefore = PickupWallet.PowerCellsSecondary;

            var directorGo = new GameObject("PickupDirector Test");
            var director = directorGo.AddComponent<PickupDirector>();

            try
            {
                // Act: drive the exact walk-over collection path a real player takes for a dropped
                // Rack Module, the same private-Collect-via-reflection idiom
                // MV698WeaponCoreFinaleDropTests.InvokeCollect already established for a rare drop.
                SpawnDrop(director, PickupKind.RackModule, Vector3.zero);
                Pickup module = FindLive(director, PickupKind.RackModule);
                InvokeCollect(director, module);

                // Assert: the RESOLVED state -- MV-1090 banks only; nothing unlocks or grants yet, and
                // nothing is spent banking it.
                Assert.IsTrue(PendingMorphingModule.RackModulePending,
                    "MV-1090: collecting the Rack Module must bank it, waiting for THE RIG's next open");
                Assert.IsFalse(RigState.IsCategoryUnlocked("SECONDARY"),
                    "MV-1090: collecting must NOT unlock SECONDARY -- that waits for THE RIG's open-time reveal");
                Assert.AreEqual(0, RigState.Level("s_rkt"),
                    "MV-1090: collecting must NOT grant s_rkt -- that waits for THE RIG's open-time reveal");
                Assert.AreEqual(cellsBefore, PickupWallet.PowerCells,
                    "the grant must cost no Parts");
                Assert.AreEqual(powerCellsSecondaryBefore, PickupWallet.PowerCellsSecondary,
                    "the grant must cost no Power Cells");
            }
            finally
            {
                Object.DestroyImmediate(directorGo);
            }
        }

        private static void SpawnDrop(PickupDirector director, PickupKind kind, Vector3 pos)
        {
            typeof(PickupDirector)
                .GetMethod("SpawnDrop", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { kind, pos, default(MaxWorlds.Upgrades.PartKind), default(AbilityKind) });
        }

        private static Pickup FindLive(PickupDirector director, PickupKind kind)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            foreach (Pickup p in live)
                if (p.Kind == kind) return p;
            Assert.Fail($"no live pickup of kind {kind} found on the director");
            return null;
        }

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }
    }
}
