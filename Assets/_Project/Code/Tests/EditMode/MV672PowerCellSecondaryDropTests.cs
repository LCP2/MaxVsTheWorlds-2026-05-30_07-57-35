using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-672 AC1: Power Cells (the new secondary currency) must drop at a tunable fraction of the
    /// Parts drop rate, via a running fractional accumulator — not a per-kill round-off — so a
    /// non-integer ratio (the authored default, 0.1) still lands on the right long-run average rather
    /// than rounding away every kill. Fails on the pre-fix code: no accumulator wires
    /// <c>PickupKind.PowerCellSecondary</c> drops into <c>PickupDirector.OnRobotDied</c> at all, so
    /// zero ever spawn regardless of kill count or ratio.
    ///
    /// Same reflection idiom as <see cref="MV626PickupCapAndLifetimeTests"/>: <c>OnRobotDied</c> is
    /// private, driven directly since Unity does not invoke a plain MonoBehaviour's lifecycle/event
    /// handlers outside Play mode. <c>DevTuning.CellsPerLargeKill</c> is pinned to a whole number so
    /// each kill's Parts drop count is deterministic, isolating this test from
    /// <c>CellEconomyTuning</c>'s own per-area budget curve (that curve is
    /// <see cref="CellEconomyTuningAreaCurveTests"/>'s job, untouched by this ticket).
    /// </summary>
    public sealed class MV672PowerCellSecondaryDropTests
    {
        private GameObject _directorGo;
        private PickupDirector _director;

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            DevTuning.Reset();
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
            PickupWallet.Reset();
            DevTuning.Reset();
        }

        private static void KillOneLargeRobot(PickupDirector director, Vector3 pos) =>
            typeof(PickupDirector).GetMethod("OnRobotDied", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { pos, EnemyKind.Heavy });   // large, not Bruiser (avoids the Supercell branch)

        private static List<Pickup> LiveList(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        [Test]
        public void OnRobotDied_DropsPowerCellSecondary_WithinOneOfTheAccumulatedRatio()
        {
            DevTuning.CellsPerLargeKill = 1f;      // deterministic: exactly 1 Parts pickup per large kill
            DevTuning.PowerCellDropRatio = 0.1f;

            const int kills = 47;   // not a multiple of 10 — exercises the fractional remainder
            for (int i = 0; i < kills; i++)
                KillOneLargeRobot(_director, new Vector3(i, 0f, 0f));

            int secondaryDrops = LiveList(_director).Count(p => p.Kind == PickupKind.PowerCellSecondary);
            int expected = Mathf.FloorToInt(kills * 0.1f);   // total Parts drops (== kills) * ratio

            Assert.That(secondaryDrops, Is.InRange(expected - 1, expected + 1),
                $"expected ~{expected} Power Cells across {kills} kills at ratio 0.1 (fractional accumulator), got {secondaryDrops}");
        }
    }
}
