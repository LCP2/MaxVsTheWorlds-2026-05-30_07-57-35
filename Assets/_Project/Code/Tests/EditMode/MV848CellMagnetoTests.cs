using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-848, Lee: "Magneto -- change to Part Magneto and add Cell Magneto." World 2's <c>e_mag</c> is
    /// renamed PART MAGNETO (unchanged behaviour) and a new <c>e_cmg</c> CELL MAGNETO node is added,
    /// which pulls <see cref="PickupKind.PowerCellSecondary"/> (the MV-672 "Power Cells" rocket currency)
    /// with the same radius curve/speed Part Magneto already uses for <see cref="PickupKind.PowerCell"/>.
    ///
    /// EditMode only. Reflection drives <c>PickupDirector.Update()</c> directly (same idiom as
    /// <see cref="MV439CellCapacityRefusalTests"/>) since Unity does not invoke a plain MonoBehaviour's
    /// Update outside Play mode.
    /// </summary>
    public sealed class MV848CellMagnetoTests
    {
        private GameObject _directorGo;
        private PickupDirector _director;
        private GameObject _maxGo;

        [SetUp]
        public void SetUp()
        {
            RigBoard.UseWorld(1);   // World 2 — e_cmg only exists on this board
            RigState.Reset();
            PickupWallet.Reset();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);

            _directorGo = new GameObject("PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();

            _maxGo = new GameObject("Max");
            _maxGo.tag = "Player";
            _maxGo.transform.position = Vector3.zero;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
            RigState.Reset();
            PickupWallet.Reset();
            RigBoard.ResetForTests();
        }

        private static void InvokeUpdate(PickupDirector director) =>
            typeof(PickupDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);

        private static void SpawnPowerCellSecondaryAt(PickupDirector director, Vector3 pos) =>
            typeof(PickupDirector).GetMethod("SpawnDrop", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { PickupKind.PowerCellSecondary, pos, default(MaxWorlds.Upgrades.PartKind), default(AbilityKind) });

        private static List<Pickup> LiveList(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        [Test]
        public void CellMagnetoPullsPowerCellSecondaryAtLevel3ButNotAtLevelZero()
        {
            // 6m out: inside e_cmg's L3 pull radius (3 + 2*(3-1) = 7m) but outside the 1.4m walk-over
            // CollectRadius, so only the Cell Magneto pull branch is under test.
            SpawnPowerCellSecondaryAt(_director, new Vector3(6f, 0f, 0f));
            var pickup = LiveList(_director)[0];
            Vector3 spawnedPos = pickup.transform.position;

            InvokeUpdate(_director);
            Assert.That(pickup.transform.position, Is.EqualTo(spawnedPos),
                "e_cmg at level 0 must not pull a Power Cell");

            RigState.UnlockCategory("ENERGY");
            Assert.That(RigState.AcquireCap("e_cel"), Is.True, "sanity: e_cel must be acquirable once ENERGY is unlocked");
            Assert.That(RigState.AcquireCap("e_mag"), Is.True, "sanity: e_mag must be acquirable once e_cel is owned");
            Assert.That(RigState.AcquireCap("e_cmg"), Is.True, "sanity: e_cmg must be acquirable once e_mag is owned");
            Assert.That(RigState.RaiseLevel("e_cmg"), Is.True);
            Assert.That(RigState.RaiseLevel("e_cmg"), Is.True);
            Assert.That(RigState.Level("e_cmg"), Is.EqualTo(3), "sanity: e_cmg must actually be at level 3");

            InvokeUpdate(_director);
            Assert.That(Vector3.Distance(pickup.transform.position, spawnedPos), Is.GreaterThan(0f),
                "e_cmg at level 3 must pull a Power Cell toward Max");
        }
    }
}
