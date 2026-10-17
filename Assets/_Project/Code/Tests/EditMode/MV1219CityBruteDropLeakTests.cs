using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1219: <c>PickupDirector.OnRobotDied</c>'s Dredge Hulk guaranteed-drop check (MV-1106) keyed
    /// on <c>ResolvePlayedWorldIndex() &gt;= 2</c> — correct back when the Reef sat at world index 2,
    /// but MV-1176 inserted the City at index 2 and moved the Reef to index 3, so a City Brute now
    /// also clears that threshold and wrongly gets the Reef-only guaranteed 2 extra Power Cells.
    /// Fails on base commit 98e09a7 (PickupDirector.cs:228 still reads <c>&gt;= 2</c>): a Brute killed
    /// while the played world is the City drops 3 Power Cells (1 normal-roll + 2 guaranteed) instead
    /// of the 1 the normal per-kill roll alone accounts for.
    /// </summary>
    public sealed class MV1219CityBruteDropLeakTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _directorGo;
        private PickupDirector _director;
        private GameObject _areaDirectorGo;
        private AreaAccumulationDirector _areaDirector;

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            DevTuning.Reset();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            foreach (var a in Object.FindObjectsByType<AreaAccumulationDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(a.gameObject);

            _directorGo = new GameObject("MV1219 PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();
            // Same EditMode-only OnEnable gap MV1106World3BalanceTests works around — without this,
            // PickupDirector never subscribes to DropSignals.RobotDied and the kill below reaches no one.
            typeof(PickupDirector).GetMethod("OnEnable", NonPublicInstance).Invoke(_director, null);

            _areaDirectorGo = new GameObject("MV1219 Area Accumulation");
            _areaDirector = _areaDirectorGo.AddComponent<AreaAccumulationDirector>();
        }

        [TearDown]
        public void TearDown()
        {
            typeof(PickupDirector).GetMethod("OnDisable", NonPublicInstance).Invoke(_director, null);
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_areaDirectorGo != null) Object.DestroyImmediate(_areaDirectorGo);
            PickupWallet.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void ABruteKilledInTheCityDropsNoGuaranteedExtraCells()
        {
            _areaDirector.ConfigureWorld(null, worldIndex: WorldCatalog.IndexOf(WorldIds.City));

            DropSignals.EmitRobotDied(Vector3.zero, EnemyKind.Brute);

            int powerCells = 0;
            foreach (Pickup p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (p.Kind == PickupKind.PowerCell) powerCells++;

            Assert.AreEqual(1, powerCells,
                "a City Brute must drop only the normal per-kill roll (1, the flat " +
                "CellEconomyTuning.DefaultCellsPerLargeKill fallback with no live area context) — never " +
                "the Reef-only Dredge Hulk guaranteed 2 extra (DredgeHulkGuaranteedPartDrops)");
        }
    }
}
