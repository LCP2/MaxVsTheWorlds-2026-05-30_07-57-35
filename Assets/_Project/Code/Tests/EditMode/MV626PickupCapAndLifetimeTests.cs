using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-626 — once the reserve filled, every subsequent power-cell drop stayed on the ground forever:
    /// <c>PickupDirector.Collect</c> declined a refused cell (MV-439) but never removed it from
    /// <c>_live</c>, and there was no cap or lifetime to bound the pile either way. Lee's report measured
    /// ~70 uncollected cells at 19fps against a 60 target. This covers the ticket's first three changes:
    ///
    /// 1) <c>SpawnDrop</c> refuses to create a cell once the reserve is already full (AC1).
    /// 2) <c>MaxLiveCells</c> bounds the live cell population regardless of kill rate, recycling
    ///    oldest-first (AC2).
    /// 3) <c>TickCellLifetimes</c> expires a cell back to the pool after <c>CellLifetimeSeconds</c> (AC3).
    ///
    /// AC5's other half (authored per-area cell budgets unchanged) is
    /// <see cref="CellEconomyTuningAreaCurveTests"/>'s job — this ticket never touches
    /// <see cref="CellEconomyTuning"/>, so that pre-existing suite passing unmodified is the proof. AC6
    /// (Magneto still refuses at capacity) is <see cref="MV439CellCapacityRefusalTests"/>'s job —
    /// <c>MagnetoShouldPull</c> is untouched by this ticket.
    ///
    /// Same reflection idiom as <see cref="MV439CellCapacityRefusalTests"/>: <c>SpawnDrop</c>,
    /// <c>Update</c> and <c>TickCellLifetimes</c> are private, driven directly since Unity does not
    /// invoke a plain MonoBehaviour's lifecycle methods outside Play mode.
    /// </summary>
    public sealed class MV626PickupCapAndLifetimeTests
    {
        private GameObject _directorGo;
        private PickupDirector _director;
        private GameObject _maxGo;

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            DevTuning.Reset();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);

            _directorGo = new GameObject("PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();

            _maxGo = new GameObject("Max");
            _maxGo.tag = "Player";
            _maxGo.transform.position = new Vector3(1000f, 0f, 1000f);   // well outside CollectRadius
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
            PickupWallet.Reset();
            DevTuning.Reset();
        }

        private static void SpawnDrop(PickupDirector director, PickupKind kind, Vector3 pos) =>
            typeof(PickupDirector).GetMethod("SpawnDrop", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { kind, pos, default(MaxWorlds.Upgrades.PartKind), default(AbilityKind) });

        private static void SpawnCellAt(PickupDirector director, Vector3 pos) =>
            SpawnDrop(director, PickupKind.PowerCell, pos);

        private static void TickLifetimes(PickupDirector director, float dt) =>
            typeof(PickupDirector).GetMethod("TickCellLifetimes", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { dt });

        private static void InvokeUpdate(PickupDirector director) =>
            typeof(PickupDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);

        private static List<Pickup> LiveList(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        private static int MaxLiveCells() =>
            (int)typeof(PickupDirector).GetField("MaxLiveCells", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private static float CellLifetimeSeconds() =>
            (float)typeof(PickupDirector).GetField("CellLifetimeSeconds", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private static void FillReserve()
        {
            while (PickupWallet.PowerCells < PickupWallet.Capacity) PickupWallet.AddPowerCell();
        }

        // ---------------------------------------------------------------------------- AC1

        [Test]
        public void SpawnDrop_RefusesAPowerCell_WhileTheReserveIsAlreadyFull()
        {
            FillReserve();

            SpawnCellAt(_director, Vector3.zero);

            Assert.That(LiveList(_director).Count, Is.EqualTo(0),
                "a cell dropped into an already-full reserve must never even be spawned — MV-626 change 1");
        }

        [Test]
        public void SpawnDrop_SpawnsACellAgain_AsSoonAsCapacityFreesUp()
        {
            FillReserve();
            SpawnCellAt(_director, Vector3.zero);
            Assert.That(LiveList(_director).Count, Is.EqualTo(0), "sanity: refused while full");

            PickupWallet.TrySpendPowerCell();   // reserve drops below the ceiling
            SpawnCellAt(_director, Vector3.zero);

            Assert.That(LiveList(_director).Count, Is.EqualTo(1),
                "a cell must spawn normally again the moment there's room in the reserve");
        }

        // ---------------------------------------------------------------------------- AC2

        [Test]
        public void SpawnDrop_NeverExceedsTheLiveCellCap_AndRecyclesTheOldestFirst()
        {
            DevTuning.PowerCellCapacity = 10_000f;   // keep the reserve-full gate (AC1) out of this test

            // Position-based, not object-identity-based: RecycleOldestCellIfAtCap pushes the evicted
            // cell straight back onto _cellPool, and the very next line in the same SpawnDrop call pops
            // it right back out to BE the newly-spawned cell — a live Pickup reference legitimately
            // getting reused for the new drop is correct pooling, not a leftover of the old one. What
            // must actually hold is that nothing sits at the FIRST cell's ground position any more.
            int cap = MaxLiveCells();
            for (int i = 0; i < cap; i++) SpawnCellAt(_director, new Vector3(i, 0f, 0f));
            Assert.That(LiveList(_director).Count, Is.EqualTo(cap), "sanity: filled exactly to the cap");

            SpawnCellAt(_director, new Vector3(cap, 0f, 0f));   // the cap+1'th drop

            var live = LiveList(_director);
            Assert.That(live.Count, Is.EqualTo(cap), "the cap+1'th drop must not grow the live count past the cap");
            Assert.That(live.Any(p => p.transform.position.x == 0f), Is.False,
                "the very first cell dropped (x=0) must have been recycled — oldest-first, MV-626 change 2");
            Assert.That(live.Any(p => p.transform.position.x == cap), Is.True,
                "the cap+1'th cell must actually be live, at its own position");

            // And the bound holds over a long streak, not just the first overflow.
            for (int i = 0; i < 20; i++)
            {
                SpawnCellAt(_director, new Vector3(cap + 1 + i, 0f, 0f));
                Assert.That(LiveList(_director).Count, Is.LessThanOrEqualTo(cap),
                    $"live cell count exceeded the cap after spawn #{cap + 1 + i}");
            }
        }

        // ---------------------------------------------------------------------------- AC3

        [Test]
        public void TickCellLifetimes_ExpiresACellPastItsLifetime_ReturningItToThePoolNotDestroyingIt()
        {
            SpawnCellAt(_director, Vector3.zero);
            Pickup cell = LiveList(_director)[0];

            TickLifetimes(_director, CellLifetimeSeconds() - 1f);
            Assert.That(LiveList(_director).Count, Is.EqualTo(1), "must not expire before its lifetime is up");

            TickLifetimes(_director, 2f);   // crosses the lifetime threshold
            Assert.That(LiveList(_director).Count, Is.EqualTo(0), "must expire once its lifetime elapses");
            Assert.That(cell.gameObject, Is.Not.Null,
                "an expired cell must be pooled (SetActive(false)), not Destroy()ed — it gets reused on the next drop");
            Assert.That(cell.gameObject.activeInHierarchy, Is.False, "an expired cell must be deactivated");
        }

        [Test]
        public void TickCellLifetimes_DoesNotTouchSupercellOrDevicePickups()
        {
            SpawnDrop(_director, PickupKind.Supercell, Vector3.zero);
            SpawnDrop(_director, PickupKind.Device, Vector3.zero);
            Assert.That(LiveList(_director).Count, Is.EqualTo(2), "sanity: both spawned");

            TickLifetimes(_director, CellLifetimeSeconds() * 10f);

            Assert.That(LiveList(_director).Count, Is.EqualTo(2),
                "a Supercell/Device grant must never expire — it always collects successfully, a different " +
                "population from the power-cell leak this ticket fixes (see MV-626's own Relationship note)");
        }

        // ---------------------------------------------------------------------------- AC5 (collection half)

        [Test]
        public void ACellBelowCapacity_StillCollectsNormally_WithTheNewCapAndLifetimeBookkeepingInPlace()
        {
            SpawnCellAt(_director, Vector3.zero);
            _maxGo.transform.position = Vector3.zero;   // inside CollectRadius

            InvokeUpdate(_director);

            Assert.That(PickupWallet.PowerCells, Is.EqualTo(1), "a normal below-capacity walk-over must still bank");
            Assert.That(LiveList(_director).Count, Is.EqualTo(0), "the collected cell must leave the live list");
        }
    }
}
