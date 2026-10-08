using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1095 (the one new test per CC_AUTONOMY's testing policy, every AC sub-check folded into this
    /// one scenario): on every world after the first, SUPPORT (the sentinel family) is locked again —
    /// same as SECONDARY — and re-earned through that world's own unlock cadence, restoring every
    /// upgrade it held except SLOTS (Lee, 2026-10-06: the player re-buys every slot from one, no
    /// refund for parts already spent on it).
    ///
    /// Drives the real production path throughout: <see cref="WeaponSystemState.ApplyWeaponCoreMorph"/>
    /// (the single call every corridor/NEXT WORLD/dev-jump world transition makes), a real
    /// <see cref="PickupDirector"/>'s <c>OnFactoryDestroyed</c> (shed-cadence Device/Rack Module drops,
    /// invoked via reflection the same way <c>MV948ShedUnlockCadenceTests</c> already does, since
    /// <c>OnEnable</c> never runs under <c>AddComponent</c> outside Play Mode), real
    /// walk-over <c>Collect</c>, a real <see cref="WeaponsScreen"/> reveal ceremony, a real
    /// <see cref="PlayerAbilities"/> deploy attempt, and a real <see cref="SaveSystem"/>
    /// checkpoint/restore round trip simulating a cold-boot RESUME.
    ///
    /// Fails on base commit d30d293: <c>WeaponSystemState.ApplyWeaponCoreMorph</c> carries SUPPORT's
    /// levels/unlock across a world morph untouched (the same branch ENERGY/MOVE use), so the very
    /// first assertion below -- SUPPORT reading LOCKED right after entering World 2 -- fails there with
    /// "Expected: False But was: True".
    /// </summary>
    public sealed class MV1095SentinelLockedPerWorldTests
    {
        private string _dir;
        private GameObject _directorGo;
        private PickupDirector _director;
        private GameObject _areaGo;
        private AreaAccumulationDirector _areaDirector;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1095-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;

            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
            PendingMorphingModule.Reset();
            PickupWallet.Reset();
            FactoryCensus.Reset();
            Sentinel.DestroyAllActive();

            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            foreach (var a in Object.FindObjectsByType<AreaAccumulationDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(a.gameObject);

            _areaGo = new GameObject("AreaAccumulationDirector");
            _areaDirector = _areaGo.AddComponent<AreaAccumulationDirector>();
            _areaDirector.ConfigureWorld(new WorldConfig { dials = new WorldDials { areaCount = 1 } }, 1);

            _directorGo = new GameObject("PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();
        }

        [TearDown]
        public void TearDown()
        {
            Sentinel.DestroyAllActive();
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();

            SaveSystem.ResetForTests();
            WeaponSystemState.Reset();
            PendingMorphingModule.Reset();
            PickupWallet.Reset();
            FactoryCensus.Reset();
            RigBoard.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private MowerHutch MakeShed(string id)
        {
            var go = new GameObject($"shed_{id}");
            _spawned.Add(go);
            var hutch = go.AddComponent<MowerHutch>();   // RequireComponent brings EnemySpawner
            hutch.Build();                               // Build() registers it with FactoryCensus
            hutch.SetId(id);
            return hutch;
        }

        /// <summary>Destroys <paramref name="hutch"/> for real, then invokes the exact signal handler
        /// <c>HudSignals.FactoryDestroyed</c> would have called in Play Mode (<c>OnEnable</c> never
        /// runs under <c>AddComponent</c> outside it) — same two-step idiom <c>MV948ShedUnlockCadenceTests</c>
        /// already established.</summary>
        private void DestroyShed(MowerHutch hutch)
        {
            hutch.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
            Assert.IsFalse(hutch.IsAlive, "the shed must actually be destroyed before its drop is judged");
            typeof(PickupDirector).GetMethod("OnFactoryDestroyed", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_director, new object[] { hutch.transform.position });
        }

        private List<Pickup> LiveList() =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_director);

        private Pickup FindLive(PickupKind kind)
        {
            foreach (Pickup p in LiveList())
                if (p.Kind == kind) return p;
            return null;
        }

        /// <summary>Rips a pickup out of the director's own tracking without collecting/banking it —
        /// used only to discard a redundant 1-candidate SECONDARY Device this test's own shed-ordinal
        /// parity incidentally drops alongside the Rack Module on the very first World 2 shed (both
        /// locked categories are still open at that instant) — real production behaviour, harmless
        /// (re-unlocking an already-unlocked category is a no-op), but left uncollected here would
        /// otherwise be mistaken by a later search for the SUPPORT Device this test actually waits for.</summary>
        private void DiscardLive(Pickup pickup)
        {
            LiveList().Remove(pickup);
            Object.DestroyImmediate(pickup.gameObject);
        }

        private void Collect(Pickup pickup)
        {
            var live = LiveList();
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_director, new object[] { index, pickup });
        }

        /// <summary>Destroys sheds (up to <paramref name="maxSheds"/>) until one drops a Device,
        /// discarding any uncollected pickup from a prior shed of a different kind this test doesn't
        /// care about. The exact shed ordinal a Device lands on depends on the cumulative shed-ordinal
        /// parity <c>FactoryCensus</c> carries across the whole run (MV-948) — bounding this rather than
        /// hard-coding a count keeps the test honest about what it actually knows (SUPPORT is next in
        /// THE RIG's own authored left-to-right order once SECONDARY/ENERGY/MOVE are all unlocked, not
        /// which exact shed ordinal lands the Device).</summary>
        private Pickup DestroyShedsUntilDeviceDrops(string label, int maxSheds)
        {
            for (int i = 1; i <= maxSheds; i++)
            {
                DestroyShed(MakeShed($"{label}{i}"));
                Pickup device = FindLive(PickupKind.Device);
                if (device != null) return device;
            }
            return null;
        }

        [Test]
        public void SupportLocksOnEveryNewWorldAndRestoresEverythingButSlotsOnReEarn_MV1095()
        {
            // ---------------------------------------------------------------- Arrange: a World 1 save
            // ENERGY/MOVE already unlocked (no levels needed for this test), SUPPORT unlocked with
            // damage 3 / range 2 / health 2 / slots 2 -- exactly the AC's own starting state.
            RigState.UnlockCategory("ENERGY");
            RigState.UnlockCategory("MOVE");
            RigState.UnlockCategory("SUPPORT");
            RigState.AcquireCap("u_sen");
            RigState.AcquireCap("u_dmg"); RigState.RaiseLevel("u_dmg"); RigState.RaiseLevel("u_dmg");   // -> 3
            RigState.AcquireCap("u_rng"); RigState.RaiseLevel("u_rng");                                  // -> 2
            RigState.AcquireCap("u_hp");  RigState.RaiseLevel("u_hp");                                   // -> 2
            RigState.AcquireCap("u_slt"); RigState.RaiseLevel("u_slt");                                  // -> 2 (cap)
            Assert.AreEqual(3, RigState.Level("u_dmg"), "fixture: damage 3");
            Assert.AreEqual(2, RigState.Level("u_rng"), "fixture: range 2");
            Assert.AreEqual(2, RigState.Level("u_hp"), "fixture: health 2");
            Assert.AreEqual(2, RigState.Level("u_slt"), "fixture: slots 2");

            PickupWallet.SetPowerCells(50);
            PickupWallet.SetPowerCellSecondary(50);
            int partsBeforeAnyLock = PickupWallet.PowerCells;

            var maxGo = new GameObject("Max");
            var abilities = maxGo.AddComponent<PlayerAbilities>();
            GameObject screenGo = null;

            try
            {
                // ---------------------------------------------------------------- Act 1: enter World 2 for real
                WeaponSystemState.ApplyWeaponCoreMorph(1);

                Assert.IsFalse(RigState.IsCategoryUnlocked("SUPPORT"), "AC1: SUPPORT must read LOCKED on a new-world arrival");
                foreach (string id in new[] { "u_sen", "u_dmg", "u_rng", "u_hp", "u_mov", "u_cst", "u_slt" })
                    Assert.AreEqual(0, RigState.Level(id), $"AC1: {id} must read 0 while SUPPORT is locked");
                Assert.IsFalse(WeaponSystemState.IsAcquired(AbilityKind.Sentinels), "AC1: the family must read unowned while locked");
                Assert.AreEqual(0, Sentinel.Active.Count, "AC1: no sentinel exists on a fresh world entry");
                Assert.IsFalse(abilities.SentinelReady, "AC1: SentinelDeploymentSlots must be unusable while locked");
                Assert.IsFalse(abilities.TryDeploySentinel(), "AC1: a deploy attempt must fail outright while locked");
                Assert.AreEqual(0, Sentinel.Active.Count, "AC1: still no sentinel after a refused deploy attempt");

                // ---------------------------------------------------------------- Sub-check (rule 5): a cold-boot resume between the LOCK and the re-earn must not lose what's owed back
                SaveSystem.ActiveSlot = 0;
                SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 1 });
                SaveSystem.CaptureCheckpoint(0, areaIndex: 1, worldIndex: 1);

                WeaponSystemState.Reset();
                PendingMorphingModule.Reset();
                WeaponSystemState.ApplyWorldLoadout(1);
                Assert.IsTrue(SaveSystem.RestoreCheckpoint(0), "fixture: the checkpoint captured above must restore");

                Assert.IsFalse(RigState.IsCategoryUnlocked("SUPPORT"), "sub-check: still LOCKED after a cold-boot resume between the lock and the re-earn");
                Assert.AreEqual(0, RigState.Level("u_dmg"), "sub-check: still 0 after the resume -- a remembered level is not a live level");

                // ---------------------------------------------------------------- Act 2: destroy replicators -> SECONDARY banked via the Rack Module
                DestroyShed(MakeShed("w2rep1"));

                Pickup rackModule = FindLive(PickupKind.RackModule);
                Assert.IsNotNull(rackModule, "World 2's first factory destroyed must drop the Rack Module");
                Collect(rackModule);
                Assert.IsTrue(PendingMorphingModule.RackModulePending, "collecting the Rack Module must bank it");

                // The same shed, being an odd ordinal with both locked categories still open, also drops
                // a redundant Device offering SECONDARY (the authored first-locked category) -- harmless
                // (idempotent), but left uncollected here so it can't be mistaken for the SUPPORT Device
                // this test waits for later.
                Pickup redundantDevice = FindLive(PickupKind.Device);
                if (redundantDevice != null) DiscardLive(redundantDevice);

                DestroyShed(MakeShed("w2rep2"));
                Assert.IsFalse(RigState.IsCategoryUnlocked("SUPPORT"), "AC2: sentinels must still be LOCKED after 2 factories");

                // Resolve the banked Rack Module through THE RIG's own reveal -- the real unlock moment.
                screenGo = new GameObject("WeaponsScreen W2 SECONDARY");
                var screenSecondary = screenGo.AddComponent<WeaponsScreen>();
                screenSecondary.Open();
                Assert.IsTrue(RigState.IsCategoryUnlocked("SECONDARY"), "AC2: SECONDARY must be unlocked by now");
                Assert.IsFalse(RigState.IsCategoryUnlocked("SUPPORT"), "AC2: sentinels must STILL be locked -- SECONDARY unlocks first");
                screenSecondary.Close();
                Object.DestroyImmediate(screenGo);
                screenGo = null;

                // ---------------------------------------------------------------- Act 3: destroy more factories -> the next Device offers SUPPORT
                // (ENERGY/MOVE/SECONDARY are all unlocked by now, leaving SUPPORT the sole locked
                // category -- RigDraft.DrawCandidateCategories' own authored left-to-right order, MV-595).
                Pickup supportDevice = DestroyShedsUntilDeviceDrops("w2cad", maxSheds: 6);
                Assert.IsNotNull(supportDevice, "AC2/3: a Device offering SUPPORT must eventually drop once SECONDARY/ENERGY/MOVE are all unlocked");
                Collect(supportDevice);

                screenGo = new GameObject("WeaponsScreen W2 SUPPORT");
                var screenSupport = screenGo.AddComponent<WeaponsScreen>();
                screenSupport.Open();   // resolves the 1-candidate SUPPORT draft and plays the reveal (MV-521/MV-595)
                Assert.IsTrue(screenSupport.IsCeremonyActive, "AC3: the family reveal must play on the SUPPORT re-unlock, same as any other draft");
                screenSupport.ApplyCeremonyTiming(10f);   // well past the ceremony's own duration
                screenSupport.Close();
                Object.DestroyImmediate(screenGo);
                screenGo = null;

                // ---------------------------------------------------------------- Assert: unlocked, every upgrade restored except SLOTS, free, wallet untouched
                Assert.IsTrue(RigState.IsCategoryUnlocked("SUPPORT"), "AC3: SUPPORT must be unlocked after the re-earn");
                Assert.AreEqual(3, RigState.Level("u_dmg"), "AC3: DAMAGE must restore exactly");
                Assert.AreEqual(2, RigState.Level("u_rng"), "AC3: RANGE must restore exactly");
                Assert.AreEqual(2, RigState.Level("u_hp"), "AC3: HEALTH must restore exactly");
                Assert.AreEqual(0, RigState.Level("u_slt"), "AC3: SLOTS must NOT restore -- re-earned from one, Lee's ruling");
                Assert.AreEqual(1, PlayerAbilities.SentinelDeploymentCap, "AC3: slots level 0 means exactly one deployable");
                Assert.AreEqual(partsBeforeAnyLock, PickupWallet.PowerCells, "AC3: the restore must cost nothing -- parts wallet untouched");
                Assert.IsTrue(abilities.SentinelReady, "AC3: the family must be usable again once restored");
                Assert.IsTrue(abilities.TryDeploySentinel(), "AC3: a deploy must now succeed");
                Assert.AreEqual(1, Sentinel.Active.Count);
                // MV-1117: that deploy itself now spends Parts (SentinelCost, not a secondary-bank cell)
                // — top the wallet back up to its pre-deploy baseline so Act 4/5's "the restore costs
                // nothing" checks below stay about the RESTORE, not this unrelated deploy sanity check.
                PickupWallet.SetPowerCells(partsBeforeAnyLock);

                // ---------------------------------------------------------------- Act 4: save, then a cold-boot resume
                SaveSystem.ActiveSlot = 0;
                SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = 1 });
                SaveSystem.CaptureCheckpoint(0, areaIndex: 1, worldIndex: 1);

                int dmgBefore = RigState.Level("u_dmg");
                int rngBefore = RigState.Level("u_rng");
                int hpBefore = RigState.Level("u_hp");

                // Simulate the process restart losing every in-memory static a real cold boot would.
                WeaponSystemState.Reset();
                PendingMorphingModule.Reset();

                WeaponSystemState.ApplyWorldLoadout(1);   // the real RESUME's own board-repositioning step, before RestoreCheckpoint
                bool restored = SaveSystem.RestoreCheckpoint(0);
                Assert.IsTrue(restored, "fixture: the checkpoint captured above must restore");

                Assert.IsTrue(RigState.IsCategoryUnlocked("SUPPORT"), "AC4: a cold-boot RESUME must keep SUPPORT unlocked");
                Assert.AreEqual(dmgBefore, RigState.Level("u_dmg"), "AC4: DAMAGE must survive the resume");
                Assert.AreEqual(rngBefore, RigState.Level("u_rng"), "AC4: RANGE must survive the resume");
                Assert.AreEqual(hpBefore, RigState.Level("u_hp"), "AC4: HEALTH must survive the resume");
                Assert.AreEqual(0, RigState.Level("u_slt"), "AC4: SLOTS must stay at 0 across the resume too");

                // ---------------------------------------------------------------- Act 5: repeat entering World 3
                Sentinel.DestroyAllActive();   // a real world transition reloads the scene -- any standing sentinel goes with it
                _areaDirector.ConfigureWorld(new WorldConfig { dials = new WorldDials { areaCount = 1 } }, 2);

                WeaponSystemState.ApplyWeaponCoreMorph(2);

                Assert.IsFalse(RigState.IsCategoryUnlocked("SUPPORT"), "AC5: SUPPORT must read LOCKED again on the World 3 arrival");
                Assert.IsTrue(RigState.IsCategoryUnlocked("SECONDARY"), "AC5: SECONDARY carries across World 2 -> World 3 (MV-1017), unlike SUPPORT");
                foreach (string id in new[] { "u_sen", "u_dmg", "u_rng", "u_hp", "u_slt" })
                    Assert.AreEqual(0, RigState.Level(id), $"AC5: {id} must read 0 while SUPPORT is locked again");

                Pickup world3Device = DestroyShedsUntilDeviceDrops("w3fac", maxSheds: 6);
                Assert.IsNotNull(world3Device, "AC5: World 3's own cadence must eventually offer SUPPORT too (the sole remaining locked category)");
                Collect(world3Device);

                screenGo = new GameObject("WeaponsScreen W3 SUPPORT");
                var screenW3 = screenGo.AddComponent<WeaponsScreen>();
                screenW3.Open();
                Assert.IsTrue(screenW3.IsCeremonyActive, "AC5: the family reveal must play on World 3's SUPPORT re-unlock too");
                screenW3.ApplyCeremonyTiming(10f);
                screenW3.Close();
                Object.DestroyImmediate(screenGo);
                screenGo = null;

                Assert.IsTrue(RigState.IsCategoryUnlocked("SUPPORT"), "AC5: SUPPORT must be unlocked again after World 3's own re-earn");
                Assert.AreEqual(3, RigState.Level("u_dmg"), "AC5: DAMAGE must restore exactly again");
                Assert.AreEqual(2, RigState.Level("u_rng"), "AC5: RANGE must restore exactly again");
                Assert.AreEqual(2, RigState.Level("u_hp"), "AC5: HEALTH must restore exactly again");
                Assert.AreEqual(0, RigState.Level("u_slt"), "AC5: SLOTS must NOT restore again either");
                Assert.AreEqual(partsBeforeAnyLock, PickupWallet.PowerCells, "AC5: World 3's restore must also cost nothing");
            }
            finally
            {
                if (screenGo != null) Object.DestroyImmediate(screenGo);
                Sentinel.DestroyAllActive();
                Object.DestroyImmediate(maxGo);
            }
        }
    }
}
