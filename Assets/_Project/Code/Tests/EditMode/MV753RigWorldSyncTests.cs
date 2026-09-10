using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-753 (the one new test, per CC_AUTONOMY's testing policy — the ticket's own AC list asked for
    /// four separate new tests; Rule 1 caps a ticket at one, so every assertion below folds into this
    /// single scenario instead of being split out). One root cause behind two separately-described
    /// player-reported faults:
    ///
    /// Cause 1 — THE RIG's VIEW (<see cref="RigBoardLayout"/>) never followed its MODEL
    /// (<see cref="RigBoard"/>) off a Weapon Core morph: <c>WeaponsScreen.RebuildBoard</c> was the only
    /// caller of <c>RigBoardLayout.UseWorld</c>, and only the dev capture harness ever called it, so a
    /// real morph left the screen drawing World 1's node ids against World 2's model — World-1-only ids
    /// (e.g. <c>p_flw</c>) read as permanent LOCK, World 2's real ids (<c>p_rof</c>/<c>s_rkt</c>) never
    /// drew at all.
    ///
    /// Cause 2 — <c>PickupDirector</c>'s one-per-run Rack Module drop gates on
    /// <c>RigBoard.ActiveWorldIndex == 1</c>, which only <c>WeaponSystemState.ApplyWeaponCoreMorph</c>
    /// sets, which in normal play only ran lazily on THE RIG's first open — so every Replicator
    /// destroyed before that first open dropped no Rack Module (Lee: "something happened ... but I had
    /// no ability to pick up").
    ///
    /// Fails to compile on current main: <c>BackyardPath.ApplyPendingMorphAtRunStart</c> does not exist
    /// there — the new RUN START entry point this fix adds, called first thing in
    /// <c>BackyardPath.Awake</c>, in place of waiting for THE RIG's first open.
    /// </summary>
    public sealed class MV753RigWorldSyncTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
            PendingMorphingModule.Reset();
            PickupWallet.Reset();
        }

        [Test]
        public void RunStartMorphFixesTheBoardViewAndTheRackModuleDrop_MV753()
        {
            // Arrange: World 1's finale banked a Weapon Core, same as a real Victory leaves it.
            PendingMorphingModule.SetWeaponCore();
            Assert.IsTrue(PendingMorphingModule.WeaponCorePending);

            // Act 1 (Cause 2's fix): RUN START applies the morph — BEFORE THE RIG is ever opened.
            BackyardPath.ApplyPendingMorphAtRunStart(worldIndex: 1);

            Assert.IsFalse(PendingMorphingModule.WeaponCorePending, "run start must consume the banked core");
            Assert.AreEqual(1, RigBoard.ActiveWorldIndex,
                "run start must already be on World 2's board -- the bug left this on World 1 until THE RIG's first open");

            // Assert 1 (Cause 2): the first Replicator destroyed, with THE RIG still never opened, must
            // drop a Rack Module -- the exact player-facing gap ("no ability to pick up") this fixes.
            var directorGo = new GameObject("PickupDirector Test");
            var director = directorGo.AddComponent<PickupDirector>();
            try
            {
                InvokeOnFactoryDestroyed(director, Vector3.zero);
                Assert.IsNotNull(FindLive(director, PickupKind.RackModule),
                    "the first Replicator destroyed in a World 2 run must drop a Rack Module, even though THE RIG was never opened");
            }
            finally
            {
                Object.DestroyImmediate(directorGo);
            }

            // Act 2 (Cause 1's fix): the player finally opens THE RIG, well after the morph.
            var screenGo = new GameObject("WeaponsScreen Test");
            var screen = screenGo.AddComponent<WeaponsScreen>();
            try
            {
                screen.Open();

                // Assert 2 (Cause 1): the screen must draw World 2's real ids and NEVER World 1's stale
                // ones -- pre-fix, RigBoardLayout stayed on World 1 so this was exactly backwards. The
                // full SET named in the ticket's own AC2, not a sample: every World-1-only id absent,
                // every World 2 real id present.
                foreach (string world1OnlyId in new[] { "p_flw", "p_spr", "s_bal", "s_lob", "s_aut", "s_rte" })
                    Assert.IsNull(screen.NodeButton(world1OnlyId),
                        $"{world1OnlyId} is a World-1-only id -- it must never be drawn against World 2's model");
                foreach (string world2Id in new[] { "p_rof", "p_frk", "s_rkt" })
                    Assert.IsNotNull(screen.NodeButton(world2Id), $"{world2Id} is one of World 2's real nodes -- it must be drawn");

                // Assert 3 (Cause 1, AC3): every node on World 2's own board resolves a real Category,
                // and a non-root's Parent names another real node on the SAME board -- the exact failure
                // Cause 1 produced when a stale World-1-only id got queried against World 2's model
                // (RigBoard.Category/.Parent returning null, read by RigState.IsCellUnlockable as
                // permanently locked).
                foreach (string id in RigBoard.AllIds)
                {
                    Assert.IsNotNull(RigBoard.Category(id), $"{id}'s Category must resolve on World 2's board");
                    string parent = RigBoard.Parent(id);
                    if (!string.IsNullOrEmpty(parent))
                        Assert.IsTrue(RigBoard.Exists(parent), $"{id}'s parent '{parent}' must exist on World 2's board");
                }
            }
            finally
            {
                Object.DestroyImmediate(screenGo);
            }
        }

        private static void InvokeOnFactoryDestroyed(PickupDirector director, Vector3 pos) =>
            typeof(PickupDirector).GetMethod("OnFactoryDestroyed", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { pos });

        private static Pickup FindLive(PickupDirector director, PickupKind kind)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            foreach (Pickup p in live)
                if (p.Kind == kind) return p;
            return null;
        }
    }
}
