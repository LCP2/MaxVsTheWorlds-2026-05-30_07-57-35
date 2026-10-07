using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1147: <c>PickupDirector</c> dereferences a tracked <c>Pickup</c> without checking whether it has
    /// been destroyed out from under it -- <c>RecycleOldestCellIfAtCap</c>/<c>RetireCell</c> (cap eviction),
    /// <c>TickCellLifetimes</c> (ground-lifetime expiry) and <c>Tick</c>'s own live-pickup loop (walk-over/
    /// Magneto) all throw <c>MissingReferenceException</c> on a stale entry instead of dropping it, the
    /// same hole <see cref="MV626PickupCapAndLifetimeTests"/>'s own <c>CollectGroundedWeaponCore</c> already
    /// guards against with a plain <c>p != null</c> check. Fails on base commit 29770ce -- see the fix
    /// comment for the quoted failure output.
    ///
    /// Same reflection idiom as <see cref="MV626PickupCapAndLifetimeTests"/>: the methods under test are
    /// private, driven directly since Unity does not invoke a plain MonoBehaviour's lifecycle methods
    /// outside Play mode.
    /// </summary>
    public sealed class MV1147DestroyedPickupSafetyTests
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

        private static Pickup SpawnDrop(PickupDirector director, PickupKind kind, Vector3 pos) =>
            (Pickup)typeof(PickupDirector).GetMethod("SpawnDrop", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { kind, pos, default(MaxWorlds.Upgrades.PartKind), default(AbilityKind) });

        private static void MarkAgesOnGround(PickupDirector director, Pickup p) =>
            typeof(PickupDirector).GetMethod("MarkAgesOnGround", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { p });

        private static void TickLifetimes(PickupDirector director, float dt) =>
            typeof(PickupDirector).GetMethod("TickCellLifetimes", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { dt });

        private static void InvokeUpdate(PickupDirector director) =>
            typeof(PickupDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);

        private static List<Pickup> LiveList(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        private static bool CellOrderContains(PickupDirector director, Pickup p)
        {
            var order = (LinkedList<Pickup>)typeof(PickupDirector)
                .GetField("_cellOrder", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(director);
            return order.Contains(p);
        }

        private static int MaxLiveCells() =>
            (int)typeof(PickupDirector).GetField("MaxLiveCells", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private static float CellLifetimeSeconds() =>
            (float)typeof(PickupDirector).GetField("RobotDropLifetimeSeconds", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        [Test]
        public void PickupDirector_DropsADestroyedCellsBookkeeping_InsteadOfDereferencingIt()
        {
            DevTuning.PowerCellCapacity = 10_000f;   // keep the reserve-full gate out of this

            // ---- AC1a/AC1d: the cap's own oldest tracked cell is destroyed out from under the director,
            // then a cap eviction is driven to fire by spawning one more.
            int cap = MaxLiveCells();
            for (int i = 0; i < cap; i++) SpawnDrop(_director, PickupKind.PowerCell, new Vector3(i, 0f, 0f));
            Pickup oldest = LiveList(_director)[0];
            Object.DestroyImmediate(oldest.gameObject);

            Pickup fresh = null;
            Assert.DoesNotThrow(() => fresh = SpawnDrop(_director, PickupKind.PowerCell, new Vector3(cap, 0f, 0f)),
                "a cap eviction landing on a Pickup destroyed out from under the director must not throw (MV-1147)");

            Assert.That(LiveList(_director), Has.No.Member(oldest),
                "AC1a: the destroyed cell must be dropped from _live");
            Assert.That(CellOrderContains(_director, oldest), Is.False,
                "AC1a: the destroyed cell must be dropped from _cellOrder");
            Assert.That(fresh, Is.Not.Null, "AC1d: a fresh spawn right after the eviction must still succeed");
            Assert.That(LiveList(_director), Has.Member(fresh), "AC1d: the fresh spawn must be tracked live");
            Assert.That(CellOrderContains(_director, fresh), Is.True,
                "AC1d: the fresh spawn must be tracked in _cellOrder too -- proves the destroyed instance was " +
                "not recycled into _cellPool to BE this spawn");

            // ---- AC1b: the same shape of destroyed entry must survive a lifetime tick, not crash it.
            Pickup aging = SpawnDrop(_director, PickupKind.PowerCell, new Vector3(2000f, 0f, 0f));
            MarkAgesOnGround(_director, aging);
            Object.DestroyImmediate(aging.gameObject);

            Assert.DoesNotThrow(() => TickLifetimes(_director, CellLifetimeSeconds() * 2f),
                "a lifetime tick landing on a Pickup destroyed out from under the director must not throw (MV-1147)");
            Assert.That(LiveList(_director), Has.No.Member(aging),
                "AC1b: the destroyed cell must be dropped by the lifetime tick");

            // ---- AC1c: the same shape of destroyed entry must survive the per-frame live-pickup loop.
            Pickup walked = SpawnDrop(_director, PickupKind.PowerCell, new Vector3(3000f, 0f, 0f));
            Object.DestroyImmediate(walked.gameObject);

            Assert.DoesNotThrow(() => InvokeUpdate(_director),
                "the live-pickup loop landing on a Pickup destroyed out from under the director must not throw (MV-1147)");
            Assert.That(LiveList(_director), Has.No.Member(walked),
                "AC1c: the destroyed cell must be dropped by the live-pickup loop");
        }
    }
}
