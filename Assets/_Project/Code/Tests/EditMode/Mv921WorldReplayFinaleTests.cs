using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-921 (the one new test, per CC_AUTONOMY's testing policy): World 1's finale pays out even
    /// when replayed on a save whose own <c>WorldIndex</c> has already gone further (World 3). Fails
    /// on base commit ce8628e: <c>BossVictoryPayoff.HasNextWorld</c>, <c>WorldFinaleGate.HasNextWorld</c>
    /// and <c>RunTracker.Seal</c>'s <c>advancesWorld</c> all re-read <c>SaveSlotData.WorldIndex</c>
    /// fresh instead of asking "which world am I actually playing" — with the save already at
    /// WorldIndex 2, every one of those reads <c>3 &gt; 2 + 1</c> as false, so the finale drops no
    /// Weapon Core, the exit gate/results card offer nothing to advance into, and (via
    /// <c>SaveSystem.RecordResult</c>'s old <c>Math.Min(WorldIndex + 1, Count - 1)</c> math, which
    /// clamps straight back to 2) a subsequent THE RIG open morphs into World 3's own weapon
    /// (Undertow) instead of World 1's (LPPE). Also fails to compile on base commit — CS1061 —
    /// against <c>AreaAccumulationDirector.ConfigureWorld(cfg, worldIndex)</c>/<c>.ActiveWorldIndex</c>
    /// and <c>SaveSystem.RecordResult</c>'s third parameter, none of which exist there: that gap is the
    /// fix itself (MV-921's root cause: nothing distinguished "the world being played" from "the save's
    /// furthest-progress marker").
    /// </summary>
    public sealed class Mv921WorldReplayFinaleTests
    {
        private string _dir;
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv921-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            // The save already reached World 3 (index 2) in an earlier session — this run is a REPLAY
            // of World 1, which the AreaAccumulationDirector below configures directly (the same "no
            // scene/BackyardPath needed" idiom MV698/MV915's own tests already use).
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 2 });

            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            Time.timeScale = 1f;

            _root = new GameObject("MV-921 Probe Root");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);

            // Seal() -> ShowResults() builds a real "Result Screen" GameObject (RunTracker's own idiom,
            // untracked by this test's own handles) -- clean it up so it doesn't leak into later tests.
            foreach (var rs in Object.FindObjectsByType<ResultScreen>(FindObjectsSortMode.None))
                Object.DestroyImmediate(rs.gameObject);

            BossCensus.Reset();
            Pickup.ResetRegistry();
            PendingMorphingModule.Reset();
            WeaponSystemState.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;   // Seal() -> ResultScreen.Show() freezes the game; restore it
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // OnEnable isn't reliably invoked for AddComponent outside Play mode (same note
        // MV698WeaponCoreFinaleDropTests/Mv915WorldOneFinaleSequenceTests carry for Awake) -- drive it
        // directly so every listener actually subscribes to HudSignals the way it does for real.
        private static void InvokeOnEnable(Component c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(c, null);

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        private static BigBermudaBoss NewBoss(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            return go.AddComponent<BigBermudaBoss>();
        }

        private static Pickup[] LivePickups() =>
            Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None);

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (System.Collections.IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }

        [Test]
        public void ReplayingWorld1OnAFurtherAlongSave_StillPaysOutAndAdvancesFromTheWorldActuallyPlayed()
        {
            var cfg = new WorldConfig
            {
                dials = new WorldDials { areaCount = 30 },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "a30", index = 30, role = "boss",
                        origin = new WorldAreaOrigin(), size = new WorldAreaSize(),
                    },
                },
            };

            // The active world this run resolves is World 1 (index 0) -- e.g. a dev replay -- even
            // though the save loaded above already carries WorldIndex 2 from a prior session that
            // reached World 3. This is the exact divergence MV-921's root cause describes.
            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg, worldIndex: 0);

            var pickupDirector = _root.AddComponent<PickupDirector>();

            var payoff = new GameObject("BossVictoryPayoff Test").AddComponent<BossVictoryPayoff>();
            InvokeOnEnable(payoff);

            var tracker = new GameObject("RunTracker Test").AddComponent<RunTracker>();
            InvokeOnEnable(tracker);

            // AC3: BackyardExitGate opens on the boss-defeat signal alone -- this synthetic world
            // authors no sheds/factories/robots at all, so a gate that only opens once those are
            // cleared would never open here.
            var exitGate = new GameObject("BackyardExitGate Test").AddComponent<BackyardExitGate>();
            InvokeOnEnable(exitGate);

            BigBermudaBoss boss = NewBoss("a30_boss1");
            BossCensus.Register(boss, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);

            try
            {
                // AC1 (first half) + AC3: the final boss falls.
                InvokeOnDeath(boss);

                Assert.AreEqual(1, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                    "AC1: the finale must drop the Weapon Core for the world actually being played " +
                    "(World 1), even though the save's own WorldIndex (2) is further along");

                Assert.IsTrue(exitGate.IsOpen,
                    "AC3: the exit gate must open on the boss's defeat alone -- no shed, factory or " +
                    "robot prerequisite (this synthetic world authors none of those, and it must still " +
                    "open)");

                // Walk-over collect, then the other two seal conditions land -- same sequence MV698's
                // own test uses.
                Pickup core = LivePickups().First(p => p.Kind == PickupKind.WeaponCore);
                InvokeCollect(pickupDirector, core);
                HudSignals.EmitBossPayoffFinished();
                HudSignals.EmitRunComplete();
                HudSignals.EmitFinaleGateCrossed();

                // AC1 (second half): the results card must offer NEXT WORLD, not NO FURTHER WORLDS.
                var resultScreen = Object.FindFirstObjectByType<ResultScreen>();
                Assert.IsNotNull(resultScreen, "Victory must have sealed and shown the Result screen");
                Button[] buttons = resultScreen.GetComponentsInChildren<Button>(true);
                Button nextWorldBtn = buttons.FirstOrDefault(b => b.gameObject.name == "NEXT WORLD");
                Assert.IsNotNull(nextWorldBtn,
                    "AC1: the results card must offer NEXT WORLD, not NO FURTHER WORLDS, when replaying " +
                    "World 1 on a save whose WorldIndex has already moved on to World 3");
                Assert.IsTrue(nextWorldBtn.interactable, "AC1: NEXT WORLD must be enabled, not a dead button");

                // AC2: the save's furthest-progress marker must not be reduced by this replay.
                SaveSlotData saved = SaveSystem.Load(0);
                Assert.AreEqual(2, saved.FurthestWorldIndex,
                    "AC2: the furthest-progress marker must stay at World 3 (index 2) -- replaying and " +
                    "completing World 1 must not reduce it");
                Assert.AreEqual(1, saved.WorldIndex,
                    "finishing the World 1 replay must offer World 2 (index 1) next, the world right " +
                    "after the one actually played -- not stay parked on World 3");

                // AC4: opening THE RIG after this replay must morph into World 1's own upgrade (LPPE),
                // never a later world's (Undertow) -- WeaponsScreen.Open() resolves the morph target off
                // exactly this (post-Victory) SaveSlotData.WorldIndex via CurrentWorldIndex().
                WeaponSystemState.OpenWeaponCoreMorphIfPending(saved.WorldIndex);
                Assert.AreEqual(WeaponCatalog.PrimaryKind.Lppe, WeaponSystemState.ActivePrimary,
                    "AC4: the active weapon on entering World 1 (via this replay's own finale morph) " +
                    "must resolve to World 1's own upgrade (LPPE), not a later world's weapon (Undertow) " +
                    "carried in off a WorldIndex that never should have advanced past the world played");
            }
            finally
            {
                Object.DestroyImmediate(boss.gameObject);
                Object.DestroyImmediate(payoff.gameObject);
                Object.DestroyImmediate(tracker.gameObject);
                Object.DestroyImmediate(exitGate.gameObject);
            }
        }
    }
}
