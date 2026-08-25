using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-439: walking over a power cell at a full reserve must do nothing — not destroy the cell
    /// while claiming a gain that never happened (the bug on 8cb70d3, where
    /// <c>PickupDirector.Collect</c> ignored <c>AddPowerCell</c>'s outcome and deactivated the pickup
    /// unconditionally). Magneto must also stop pulling once the reserve is full, at every level.
    ///
    /// EditMode only. Reflection drives <c>PickupDirector.Update()</c> directly (same idiom as
    /// <see cref="MV417OverflowPlacementTests"/>) since Unity does not invoke a plain MonoBehaviour's
    /// Update outside Play mode. <c>SpawnDrop</c> is invoked directly too, rather than routing through
    /// <c>DropSignals.EmitRobotDied</c>'s scatter pattern, so each test places exactly one pickup at an
    /// exact, deterministic distance from Max instead of a random ring of them.
    /// </summary>
    public sealed class MV439CellCapacityRefusalTests
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

        private static void InvokeUpdate(PickupDirector director) =>
            typeof(PickupDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);

        private static void SpawnCellAt(PickupDirector director, Vector3 pos) =>
            typeof(PickupDirector).GetMethod("SpawnDrop", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { PickupKind.PowerCell, pos, default(MaxWorlds.Upgrades.PartKind), default(AbilityKind) });

        private static List<Pickup> LiveList(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        private static void FillReserve()
        {
            while (PickupWallet.PowerCells < PickupWallet.Capacity) PickupWallet.AddPowerCell();
        }

        [Test]
        public void ACellWalkedOverAtCapacityStaysActiveAndInLive()
        {
            FillReserve();
            int before = PickupWallet.PowerCells;
            SpawnCellAt(_director, Vector3.zero);
            _maxGo.transform.position = Vector3.zero;   // inside CollectRadius

            InvokeUpdate(_director);

            var live = LiveList(_director);
            Assert.That(live.Count, Is.EqualTo(1), "a refused cell must not be removed from the live list");
            Assert.That(live[0].gameObject.activeInHierarchy, Is.True, "a refused pickup stays active on the ground");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(before), "nothing banked past the ceiling");
        }

        [Test]
        public void NoPlusOneCellHudEventFiresWhenTheReserveIsFull()
        {
            FillReserve();
            int fired = 0;
            void Handler(Vector3 pos, string label, Color c) { if (label == "+1 CELL") fired++; }
            HudSignals.Pickup += Handler;
            try
            {
                SpawnCellAt(_director, Vector3.zero);
                _maxGo.transform.position = Vector3.zero;
                InvokeUpdate(_director);
                Assert.That(fired, Is.EqualTo(0), "a refused pickup must never claim a gain that didn't happen");
            }
            finally { HudSignals.Pickup -= Handler; }
        }

        [Test]
        public void TheRefusedTellFiresAtMostOncePerEntryIntoRadius_NotPerFrame()
        {
            FillReserve();
            int fired = 0;
            void Handler(Vector3 pos, string label, Color c) { if (label == "RESERVE FULL") fired++; }
            HudSignals.Pickup += Handler;
            try
            {
                SpawnCellAt(_director, Vector3.zero);
                _maxGo.transform.position = Vector3.zero;

                InvokeUpdate(_director);
                InvokeUpdate(_director);
                InvokeUpdate(_director);
                Assert.That(fired, Is.EqualTo(1), "three frames standing on a refused pickup must tell once, not spam");

                _maxGo.transform.position = new Vector3(50f, 0f, 50f);   // step out of the radius
                InvokeUpdate(_director);
                _maxGo.transform.position = Vector3.zero;                // and back onto it
                InvokeUpdate(_director);
                Assert.That(fired, Is.EqualTo(2), "leaving and re-entering the radius is a fresh entry — one more tell");
            }
            finally { HudSignals.Pickup -= Handler; }
        }

        [Test]
        public void ARefusedCellIsCollectedNormallyOnceTheReserveDropsBelowTheCeiling()
        {
            FillReserve();
            SpawnCellAt(_director, Vector3.zero);
            _maxGo.transform.position = Vector3.zero;
            InvokeUpdate(_director);
            Assert.That(LiveList(_director).Count, Is.EqualTo(1), "still refused, still on the ground");

            PickupWallet.TrySpendPowerCell();   // reserve drops below the ceiling
            InvokeUpdate(_director);

            Assert.That(LiveList(_director).Count, Is.EqualTo(0),
                "the same pickup must not be left in a permanently dead state once there's room again");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(PickupWallet.Capacity), "the cell banked as soon as there was room");
        }

        [Test]
        public void MagnetoNeverPullsACellWhileTheReserveIsFull_AtEveryLevel()
        {
            RigState.AcquireCap("e_cd");
            RigState.AcquireCap("e_mag");
            int maxLevel = RigBoard.MaxLevel("e_mag");
            Assert.That(maxLevel, Is.GreaterThan(0), "sanity: e_mag must actually have levels to test");

            for (int level = 1; level <= maxLevel; level++)
            {
                PickupWallet.SetPowerCells(0);
                FillReserve();

                // 1.8m out: inside every level's Magneto pull radius (MV-546: 2m base, up to the area
                // diagonal or an 11m fallback) but outside the 1.4m walk-over CollectRadius, so only
                // the Magneto branch is under test here.
                SpawnCellAt(_director, new Vector3(1.8f, 0f, 0f));
                _maxGo.transform.position = Vector3.zero;

                var cell = LiveList(_director)[0];
                Vector3 before = cell.transform.position;
                InvokeUpdate(_director);

                Assert.That(cell.transform.position, Is.EqualTo(before),
                    $"Magneto level {level} must not pull a cell while the reserve is full");

                cell.gameObject.SetActive(false);
                LiveList(_director).Clear();
                if (level < maxLevel) RigState.RaiseLevel("e_mag");
            }
        }

        [Test]
        public void MagnetoShouldPullIsPureAndCapacityGated()
        {
            // MV-546: Magneto now hoovers every pickup kind, range-gated the same way for all three;
            // only PowerCell keeps MV-439's additional reserve-full exception.
            PickupWallet.SetPowerCells(0);
            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.PowerCell, 3f, 1f), Is.True,
                "below capacity, within radius, a power cell must pull");
            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.Supercell, 3f, 1f), Is.True,
                "MV-546: Supercells are now magneted too");
            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.Device, 3f, 1f), Is.True,
                "MV-546: Devices are now magneted too");

            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.PowerCell, 3f, 100f), Is.False,
                "out of range, a power cell must not pull");
            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.Supercell, 3f, 100f), Is.False,
                "out of range, a supercell must not pull");
            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.Device, 3f, 100f), Is.False,
                "out of range, a device must not pull");

            FillReserve();
            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.PowerCell, 3f, 1f), Is.False,
                "at capacity, Magneto must not pull a power cell — MV-439");
            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.Supercell, 3f, 1f), Is.True,
                "MV-546: at capacity, a supercell still pulls — no capacity limit on it");
            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.Device, 3f, 1f), Is.True,
                "MV-546: at capacity, a device still pulls — no capacity limit on it");
        }
    }
}
