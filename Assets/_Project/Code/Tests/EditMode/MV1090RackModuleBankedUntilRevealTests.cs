using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1090 (the one new test, per CC_AUTONOMY's testing policy): a new ability family must not
    /// start working before the player has opened THE RIG and seen it revealed. MV-727's Rack Module
    /// used to unlock SECONDARY and grant <c>s_rkt</c> in the SAME instant <c>PickupDirector.Collect</c>
    /// ran, so the Shoulder Rack began auto-firing immediately — Lee's own device report ("the ability
    /// applies itself and starts working before you've actually been in to THE RIG"). This walks the
    /// whole fix end to end through real entry points: a real World 2 entry
    /// (<see cref="WeaponSystemState.ApplyWeaponCoreMorph"/>), <see cref="PickupDirector"/>'s own
    /// walk-over Collect path, a real <see cref="ShoulderRack.Tick"/> loop against a real in-range
    /// <see cref="RobotEnemy"/>, a real <see cref="SaveSystem"/> checkpoint/restore round trip
    /// simulating a cold-boot RESUME between the pickup and THE RIG's open, and a real
    /// <see cref="WeaponsScreen.Open"/>/<see cref="WeaponsScreen.ApplyCeremonyTiming"/> driving the
    /// reveal ceremony to completion.
    ///
    /// Fails on base commit d30d293: <c>PendingMorphingModule</c> has no <c>RackModulePending</c>/
    /// <c>SetRackModule</c>/<c>TakeRackModule</c> members, <c>SaveSlotData</c> has no
    /// <c>RackModulePending</c> field, and <c>WeaponsScreen.Open</c> never checks for one — this does
    /// not compile against that commit (CS1061 "'PendingMorphingModule' does not contain a definition
    /// for 'RackModulePending'"), the same class of pre-fix evidence this suite's own neighbours
    /// (<c>MV689WeaponCoreMorphTests</c>, <c>MV727RackModulePickupTests</c>) document.
    /// </summary>
    public sealed class MV1090RackModuleBankedUntilRevealTests
    {
        private const float Dt = 1f / 60f;

        // MV-1028: EditMode's single-tick test body never pumps the editor loop far enough for
        // AddComponent<RobotEnemy>()'s OnEnable to run synchronously — same reflection idiom
        // MV694ShoulderRackTests already established for seeding RobotEnemy.Active directly.
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1090-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;

            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
            PendingMorphingModule.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            WeaponSystemState.Reset();
            PendingMorphingModule.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RobotEnemy.ResetRegistry();
            RigBoard.ResetForTests();
            Time.timeScale = 1f;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void CollectingTheRackModuleBanksItUntilTheRigRevealGrantsAndUnlocksIt_MV1090()
        {
            // ---------------------------------------------------------------- Arrange: a real World 2 entry
            // The exact transition a real Weapon Core morph performs -- World 2's LPPE/Shoulder Rack
            // loadout, SECONDARY left locked (MV-727's own reversal), nothing owned yet.
            WeaponSystemState.ApplyWeaponCoreMorph(1);
            Assert.IsFalse(RigState.IsCategoryUnlocked("SECONDARY"), "fixture: a fresh World 2 run starts with SECONDARY locked");
            Assert.AreEqual(0, RigState.Level("s_rkt"), "fixture: s_rkt starts unowned");
            Assert.AreEqual(SecondaryKind.ShoulderRack, WeaponSystemState.SecondaryKind, "fixture: World 2 fires the Shoulder Rack");

            var maxGo = new GameObject("Max");
            var rack = maxGo.AddComponent<ShoulderRack>();
            RobotEnemy rusher = NewEnemy(EnemyArchetype.Rusher, new Vector3(6f, 0f, 0f));
            PickupWallet.SetPowerCellSecondary(10);

            var directorGo = new GameObject("PickupDirector Test");
            var director = directorGo.AddComponent<PickupDirector>();

            GameObject screenGo = null;
            try
            {
                Physics.SyncTransforms();

                // ---------------------------------------------------------------- Act 1: the real walk-over collect path
                SpawnDrop(director, PickupKind.RackModule, Vector3.zero);
                Pickup module = FindLive(director, PickupKind.RackModule);
                InvokeCollect(director, module);

                Assert.IsTrue(PendingMorphingModule.RackModulePending, "collecting the Rack Module must bank it");
                Assert.IsFalse(RigState.IsCategoryUnlocked("SECONDARY"), "AC1: collecting alone must not unlock SECONDARY");
                Assert.AreEqual(0, RigState.Level("s_rkt"), "AC1: collecting alone must not grant s_rkt");

                // ---------------------------------------------------------------- Act 2: 10 simulated seconds, zero rockets
                Advance(rack, 10f);
                Assert.AreEqual(0, PlayerRocket.Active.Count,
                    "AC1: zero rockets must fire in 10s with SECONDARY still locked, whatever's in range");
                Assert.IsFalse(rack.IsBought,
                    "AC1: the rack's own HUD control (the mount mesh/reload ring MaxRig reads off this) must stay hidden -- s_rkt is still unowned");

                // ---------------------------------------------------------------- Sub-check: cold-boot resume between pickup and RIG open
                SaveSystem.ActiveSlot = 0;
                SaveSystem.CaptureCheckpoint(0, areaIndex: 1, worldIndex: 1);

                // Simulate the process restart losing every in-memory static a real cold boot would.
                PendingMorphingModule.Reset();
                RigState.Reset();

                bool restored = SaveSystem.RestoreCheckpoint(0);
                Assert.IsTrue(restored, "fixture: the checkpoint captured above must restore");

                Assert.IsTrue(PendingMorphingModule.RackModulePending,
                    "sub-check: a cold-boot RESUME between the pickup and THE RIG's open must not lose the banked unlock");
                Assert.IsFalse(RigState.IsCategoryUnlocked("SECONDARY"), "sub-check: still locked after the resume");
                Assert.AreEqual(0, RigState.Level("s_rkt"), "sub-check: still unowned after the resume");

                Advance(rack, 10f);
                Assert.AreEqual(0, PlayerRocket.Active.Count, "sub-check: still inert after the resume -- no rockets fire");

                // ---------------------------------------------------------------- Act 3: open THE RIG and tick the ceremony to completion
                screenGo = new GameObject("WeaponsScreen");
                var screen = screenGo.AddComponent<WeaponsScreen>();
                screen.Open();

                Assert.IsFalse(PendingMorphingModule.RackModulePending, "opening THE RIG must consume the banked module");
                Assert.IsTrue(screen.IsCeremonyActive, "opening THE RIG with a Rack Module pending must start its reveal ceremony");
                Assert.IsTrue(RigState.IsCategoryUnlocked("SECONDARY"), "the grant lands at ceremony start, same as every other draft path");
                Assert.AreEqual(1, RigState.Level("s_rkt"), "s_rkt must be granted at level 1, not just reached");

                screen.ApplyCeremonyTiming(10f);   // well past the ceremony's own duration
                Assert.IsFalse(screen.IsCeremonyActive, "the ceremony must have ended well after its own duration");

                screen.Close();

                // ---------------------------------------------------------------- Act 4: a rocket fires within the next reload
                Advance(rack, 1.8f);
                Assert.AreEqual(1, PlayerRocket.Active.Count, "once revealed, the Rack must fire within its next reload window");
            }
            finally
            {
                PlayerRocket.DestroyAllActive();
                if (screenGo != null) Object.DestroyImmediate(screenGo);
                Object.DestroyImmediate(directorGo);
                Object.DestroyImmediate(rusher.gameObject);
                Object.DestroyImmediate(maxGo);
            }
        }

        /// <summary>Ticks in fixed <see cref="Dt"/> steps, one step PAST the exact target — same
        /// float-overshoot margin <c>MV694ShoulderRackTests.Advance</c> uses.</summary>
        private static void Advance(ShoulderRack rack, float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / Dt) + 1;
            for (int i = 0; i < steps; i++) rack.Tick(Dt);
        }

        private static RobotEnemy NewEnemy(EnemyArchetype archetype, Vector3 position)
        {
            var go = new GameObject($"Enemy {archetype.Kind}");
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.Apply(archetype);
            RobotOnEnableMethod.Invoke(e, null); // seeds RobotEnemy.Active -- see the field's own doc above
            return e;
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
