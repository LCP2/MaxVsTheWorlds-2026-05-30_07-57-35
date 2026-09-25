using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-948 (the one new test per CC_AUTONOMY's testing policy — AC1 and AC2 fold into one scenario).
    /// Before this ticket, <c>PickupDirector.OnFactoryDestroyed</c> dropped a Device on EVERY destroyed
    /// shed while any RIG category was still locked, so a World 1 run opened every remaining category on
    /// the first few sheds. Fails to COMPILE on the base commit: <c>PickupDirector.IsDeviceShedOrdinal</c>
    /// and <c>FactoryCensus.ReplicatorsDestroyed</c> do not exist there, and the base commit's
    /// `!anyLocked` gate alone would drop a Device on every one of the 8 sheds below, not just the odd
    /// ones, failing this test's own parity assertion.
    ///
    /// Each shed is destroyed for real (<c>MowerHutch.TakeDamage</c>, the same live path
    /// <c>MV922ShedCheckpointTests</c> uses) so <see cref="FactoryCensus.Destroyed"/> advances exactly the
    /// way a real run's does. <c>PickupDirector.OnFactoryDestroyed</c> itself is invoked directly via
    /// reflection rather than through <c>HudSignals.FactoryDestroyed</c> — <c>PickupDirector.OnEnable</c>
    /// (where it subscribes) never runs under <c>AddComponent</c> outside Play Mode, the same constraint
    /// <c>MowerHutch.Build</c>'s doc comment records for <c>Awake</c>, and the one <c>MV727RackModulePickupTests</c>
    /// already works around by calling the director's private members directly.
    ///
    /// Tier 2/Rule 3: every assertion reads the actual <see cref="PickupKind"/>(s) that landed in the
    /// director's live pickup list — never a raw counter value.
    /// </summary>
    public sealed class MV948ShedUnlockCadenceTests
    {
        private GameObject _directorGo;
        private PickupDirector _director;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            RigState.Reset();   // run-start baseline: PRIMARY unlocked, every other category locked
            DeathRunState.Reset();
            FactoryCensus.Reset();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);

            _directorGo = new GameObject("PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            RigState.Reset();
            DeathRunState.Reset();
            FactoryCensus.Reset();
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

        private static List<Pickup> LiveList(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        private static void InvokeOnFactoryDestroyed(PickupDirector director, Vector3 pos) =>
            typeof(PickupDirector).GetMethod("OnFactoryDestroyed", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { pos });

        [Test]
        public void EveryOtherShedDropsADevice_AfterTheFirst_AndSurvivesADeathAndContinue()
        {
            Assert.That(RigState.LockedCategoryIds().Any(), Is.True,
                "sanity: run-start baseline must leave at least one category locked");

            var droppedDevice = new List<bool>();

            for (int shed = 1; shed <= 8; shed++)
            {
                // AC2: a death mid-run -- which this game's CONTINUE resumes in place, never rebuilding
                // the level or touching FactoryCensus (MV-941) -- must not perturb the shed cadence.
                // Placed right before shed 3 so it's shed 3's parity actually put at risk.
                if (shed == 3) DeathRunState.RecordDeath();

                MowerHutch hutch = MakeHutch($"shed{shed}");
                int before = LiveList(_director).Count;
                hutch.TakeDamage(new DamageInfo(99999f, Vector3.zero, Vector3.up, Team.Player));
                Assert.IsFalse(hutch.IsAlive, $"shed {shed}: must actually be destroyed before its drop is judged");
                InvokeOnFactoryDestroyed(_director, hutch.transform.position);
                List<PickupKind> dropped = LiveList(_director).Skip(before).Select(p => p.Kind).ToList();

                bool gotDevice = dropped.Contains(PickupKind.Device);
                droppedDevice.Add(gotDevice);

                if (gotDevice)
                {
                    Assert.AreEqual(1, dropped.Count(k => k == PickupKind.Device),
                        $"shed {shed}: exactly one Device, never more");
                    Assert.That(dropped, Has.No.Member(PickupKind.Supercell),
                        $"shed {shed}: a Device drop must never ALSO give the cell-cache fallback");
                }
                else
                {
                    Assert.AreEqual(1, dropped.Count(k => k == PickupKind.Supercell),
                        $"shed {shed}: the non-unlock fallback is exactly one Supercell...");
                    Assert.AreEqual(PickupDirector.ShedCellCacheAmount, dropped.Count(k => k == PickupKind.PowerCell),
                        $"shed {shed}: ...plus the full cell-cache ring");
                }
            }

            Assert.AreEqual(
                new[] { true, false, true, false, true, false, true, false },
                droppedDevice.ToArray(),
                "sheds 1,3,5,7 (1-based, odd) must drop a Device; sheds 2,4,6,8 must give the cell cache and no Device");
        }
    }
}
