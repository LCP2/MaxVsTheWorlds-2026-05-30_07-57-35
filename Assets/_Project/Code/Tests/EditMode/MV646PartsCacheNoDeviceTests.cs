using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-646 — MV-644 wired <c>PlacePartsCache</c> as a straight delegate to
    /// <c>OnFactoryDestroyed</c>, so a shed-free area's cadence-cache pickup handed out a
    /// <see cref="PickupKind.Device"/> (a whole ability FAMILY unlock, walk-over) whenever any RIG
    /// category was still locked — which it always is early in a run. That breaks the game's central
    /// progression rule: an ability family is unlocked by DESTROYING A SHED, and by nothing else
    /// (WV-229, MV-357, MV-457). This proves <c>PlacePartsCache</c> now gives the CELL reward only,
    /// regardless of lock state (AC1/AC2), while <c>OnFactoryDestroyed</c> itself is untouched (AC3
    /// is covered by the pre-existing MV-626/MV-439 suites still passing against a locked category).
    /// </summary>
    public sealed class MV646PartsCacheNoDeviceTests
    {
        private GameObject _directorGo;
        private PickupDirector _director;

        [SetUp]
        public void SetUp()
        {
            RigState.Reset();   // run-start baseline: PRIMARY unlocked, every other category locked
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
            RigState.Reset();
        }

        // Reflection idiom matches MV626PickupCapAndLifetimeTests: reads this fixture's own director's
        // private _live list rather than the global Pickup.Active registry, which accumulates entries
        // from every other test fixture in the same batch run and is not test-isolated.
        private static List<Pickup> LiveList(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        [Test]
        public void PlacePartsCache_WithALockedCategory_NeverSpawnsADevice_AndSpawnsASupercell()
        {
            bool anyLocked = RigState.LockedCategoryIds().Any();
            Assert.That(anyLocked, Is.True, "sanity: run-start baseline must leave at least one category locked");

            _director.PlacePartsCache(Vector3.zero);

            var kinds = LiveList(_director).Select(p => p.Kind).ToList();

            Assert.That(kinds, Has.No.Member(PickupKind.Device),
                "MV-646: a cadence cache must never grant an ability-family unlock in a shed-free area");
            Assert.That(kinds.Count(k => k == PickupKind.Supercell), Is.EqualTo(1),
                "PlacePartsCache must give exactly the cell reward: one Supercell...");
            Assert.That(kinds.Count(k => k == PickupKind.PowerCell), Is.EqualTo(PickupDirector.ShedCellCacheAmount),
                "...plus the full ShedCellCacheAmount ring of power cells");
        }
    }
}
