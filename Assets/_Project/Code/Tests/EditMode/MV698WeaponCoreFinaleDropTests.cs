using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-698 (the one new test, per CC_AUTONOMY's testing policy): World 1's finale. Defeating BOTH
    /// bosses in a synthetic world's final boss area must drop exactly one Weapon Core (never a
    /// Device), and Victory must not seal until it is collected. Fails on base commit 5314c85 (MV-701):
    /// <c>BossVictoryPayoff.OnDefeated</c> never spawned a Weapon Core and <c>RunTracker</c> had no
    /// third wait condition for one — <c>PickupKind.WeaponCore</c>/<c>PendingMorphingModule</c>/
    /// <c>WeaponSystemState</c>'s morph plumbing already existed (MV-689) but nothing ever triggered
    /// the drop or gated the seal on it.
    /// </summary>
    public sealed class MV698WeaponCoreFinaleDropTests
    {
        private string _dir;
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv698-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 0 });

            BossCensus.Reset();
            Pickup.ResetRegistry();
            MaxWorlds.Weapons.PendingMorphingModule.Reset();
            Time.timeScale = 1f;

            _root = new GameObject("MV-698 Probe Root");
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
            MaxWorlds.Weapons.PendingMorphingModule.Reset();
            SaveSystem.ResetForTests();
            Time.timeScale = 1f;   // Seal() -> ResultScreen.Show() freezes the game; restore it
            ModalFrameRateGate.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        // OnEnable isn't reliably invoked for AddComponent outside Play mode (same note
        // MV625CrossAreaBossDeathTests/MV613BossRigScaleTests carry for Awake) -- drive it directly so
        // BossVictoryPayoff/RunTracker actually subscribe to HudSignals the way they do for real.
        private static void InvokeOnEnable(Component c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(c, null);

        // BigBermudaBoss.OnDeath is what a real boss's own health-depleted callback calls; driving it
        // directly is the same idiom BossTests.cs already uses for BossCensus.
        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        // Same "no stray collider beside the required CharacterController" construction BossTests.cs
        // uses (BossPrimitive_HasNoColliderBesidesTheCharacterController).
        private static BigBermudaBoss NewBoss(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            return go.AddComponent<BigBermudaBoss>();
        }

        // BossVictoryPayoffPlayTests.PartCount() uses the same live scene scan rather than
        // Pickup.Active -- that registry is populated from Pickup.OnEnable, which (like every other
        // OnEnable in this suite) isn't reliable for a fresh AddComponent outside Play mode.
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
        public void FinalBossAreaDefeat_DropsExactlyOneWeaponCore_AndVictorySealsOnlyAfterCollection()
        {
            // A synthetic world whose only authored area is a30 -- the final boss area (role "boss",
            // index == dials.areaCount), with a next world to advance into.
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

            var areaDirector = _root.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg);

            var pickupDirector = _root.AddComponent<PickupDirector>();

            var payoff = new GameObject("BossVictoryPayoff Test").AddComponent<BossVictoryPayoff>();
            InvokeOnEnable(payoff);

            var tracker = new GameObject("RunTracker Test").AddComponent<RunTracker>();
            InvokeOnEnable(tracker);

            BigBermudaBoss boss1 = NewBoss("a30_boss1");
            BigBermudaBoss boss2 = NewBoss("a30_boss2");
            BossCensus.Register(boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);
            BossCensus.Register(boss2, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 30);

            try
            {
                // a30's first boss falling must not drop anything -- boss2 is still up.
                InvokeOnDeath(boss1);
                Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                    "a30's first boss dying must not drop the core -- boss2 is still up");

                // a30's LAST boss falling clears the area -- exactly one Weapon Core, never a Device.
                InvokeOnDeath(boss2);
                Assert.AreEqual(1, LivePickups().Count(p => p.Kind == PickupKind.WeaponCore),
                    "a30's last boss dying must drop exactly one Weapon Core");
                Assert.AreEqual(0, LivePickups().Count(p => p.Kind == PickupKind.Device),
                    "the World 1 finale drop must never be a Device");

                // The other two seal conditions land (the walk-out beat finishing, the final area
                // itself clearing) -- neither alone, nor both together, may seal Victory while the
                // dropped core is still sitting uncollected.
                HudSignals.EmitBossPayoffFinished();
                HudSignals.EmitRunComplete();

                Assert.AreEqual(0, SaveSystem.Load(0).WorldIndex,
                    "Victory must not seal (WorldIndex must not advance) while the Weapon Core is still uncollected");

                // Walk-over collect -- the same PickupDirector.Collect path a real player takes.
                Pickup core = LivePickups().First(p => p.Kind == PickupKind.WeaponCore);
                InvokeCollect(pickupDirector, core);

                Assert.AreEqual(1, SaveSystem.Load(0).WorldIndex,
                    "collecting the core must let Victory seal and advance WorldIndex into World 2");
            }
            finally
            {
                Object.DestroyImmediate(boss1.gameObject);
                Object.DestroyImmediate(boss2.gameObject);
                Object.DestroyImmediate(payoff.gameObject);
                Object.DestroyImmediate(tracker.gameObject);
            }
        }
    }
}
