using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-546 — Magneto starts with a much smaller pull radius (2 m at level 1) and reaches the whole
    /// current area (its diagonal) at max level, interpolating linearly between the two.
    ///
    /// AC1 (the curve's three anchor points), AC2 (a far-corner cell reached only at max level, via
    /// <see cref="PickupDirector.MagnetoShouldPull"/> rather than reading the constant) and AC3 (the
    /// curve is monotonic) are pure-function tests against <see cref="AbilityTuning.MagnetoPullRadius"/>
    /// and need no live scene. AC4 (the no-area fallback is fixed, not 0 or unbounded) drives
    /// <see cref="PickupDirector.Update"/> directly by reflection — same idiom as
    /// <see cref="MV439CellCapacityRefusalTests"/> — with no <c>BackyardPath</c> in the test scene, so
    /// <see cref="EnemyNavigation.Map"/> is null and the fallback branch is what actually runs.
    ///
    /// AC5 (MV-439 still holds), AC6 (cc-verify), AC7 (all three kinds pull, range-gated) and AC8 (the
    /// MV-439 exception is PowerCell-only) are covered by the updated assertions in
    /// <see cref="MV439CellCapacityRefusalTests"/>, not duplicated here. AC9 is a human check.
    /// </summary>
    public sealed class MV546MagnetoAreaSweepTests
    {
        // AC1/AC2/AC3 use the ticket's own worked example: a 24x20 area, diagonal sqrt(24^2+20^2).
        private const float AreaWidth = 24f;
        private const float AreaDepth = 20f;
        private static readonly float AreaDiagonal = Mathf.Sqrt(AreaWidth * AreaWidth + AreaDepth * AreaDepth);
        private const int MaxLevel = 5; // e_mag's authored maxLevel in rig_board.json

        private GameObject _directorGo;
        private PickupDirector _director;
        private GameObject _maxGo;

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            DevTuning.Reset();
            EnemyNavigation.Reset();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);

            _directorGo = new GameObject("PickupDirector");
            _director = _directorGo.AddComponent<PickupDirector>();

            _maxGo = new GameObject("Max");
            _maxGo.tag = "Player";

            // e_mag's own parent chain is e_cel -> e_cd -> e_mag, and e_cel is a root node gated by
            // its ENERGY category rather than a parent level — all three must unlock in order for
            // AcquireCap("e_mag") to actually take (RigStateTests.cs carries the same pattern).
            RigState.UnlockCategory("ENERGY");
            RigState.AcquireCap("e_cel");
            RigState.AcquireCap("e_cd");
            RigState.AcquireCap("e_mag");
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
            EnemyNavigation.Reset();
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

        [Test]
        public void RadiusCurve_ZeroUnowned_BaseAtLevel1_AreaDiagonalAtMaxLevel()
        {
            float baseRadius = AbilityTuning.DefaultMagnetoPullRadiusBase;

            Assert.That(AbilityTuning.MagnetoPullRadius(0, MaxLevel, baseRadius, AreaDiagonal), Is.EqualTo(0f),
                "an un-drafted Magneto (level 0) must pull nothing");
            Assert.That(AbilityTuning.MagnetoPullRadius(1, MaxLevel, baseRadius, AreaDiagonal), Is.EqualTo(2f).Within(0.01f),
                "level 1 must be the much-smaller 2 m start");
            Assert.That(AbilityTuning.MagnetoPullRadius(MaxLevel, MaxLevel, baseRadius, AreaDiagonal), Is.EqualTo(AreaDiagonal).Within(0.01f),
                "max level must reach the current area's diagonal");
        }

        [Test]
        public void FarCornerCell_PulledAtMaxLevel_NotPulledAtLevel1()
        {
            float baseRadius = AbilityTuning.DefaultMagnetoPullRadiusBase;
            float maxLevelRadius = AbilityTuning.MagnetoPullRadius(MaxLevel, MaxLevel, baseRadius, AreaDiagonal);
            float level1Radius = AbilityTuning.MagnetoPullRadius(1, MaxLevel, baseRadius, AreaDiagonal);

            // Max in one corner of the 24x20 area, the cell essentially in the opposite corner —
            // worst-case distance is the diagonal itself, pulled a few cm short so the assertion
            // isn't riding the exact float boundary between "reaches" and "one ULP short".
            float cornerDistance = AreaDiagonal - 0.05f;
            float squaredDistance = cornerDistance * cornerDistance;

            PickupWallet.SetPowerCells(0);
            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.PowerCell, maxLevelRadius, squaredDistance), Is.True,
                "a fully-upgraded Magneto must reach corner-to-corner across the area");
            Assert.That(PickupDirector.MagnetoShouldPull(PickupKind.PowerCell, level1Radius, squaredDistance), Is.False,
                "level 1's small radius must not reach across the whole area");
        }

        [Test]
        public void RadiusIsMonotonicallyIncreasingAcrossLevels()
        {
            float baseRadius = AbilityTuning.DefaultMagnetoPullRadiusBase;
            float prev = AbilityTuning.MagnetoPullRadius(1, MaxLevel, baseRadius, AreaDiagonal);
            for (int level = 2; level <= MaxLevel; level++)
            {
                float r = AbilityTuning.MagnetoPullRadius(level, MaxLevel, baseRadius, AreaDiagonal);
                Assert.That(r, Is.GreaterThan(prev), $"level {level} must pull farther than level {level - 1}");
                prev = r;
            }
        }

        [Test]
        public void NoAreaContext_FallsBackToFixedRadius_NotZeroNotUnbounded()
        {
            // No BackyardPath exists in this EditMode test scene, so EnemyNavigation.Map is null and
            // PickupDirector must take the documented fallback branch rather than pulling nothing (0)
            // or pulling from anywhere (unbounded).
            Assert.That(EnemyNavigation.Map, Is.Null, "sanity: this test scene must have no live area");

            int maxLevel = RigBoard.MaxLevel("e_mag");
            for (int i = 1; i < maxLevel; i++) RigState.RaiseLevel("e_mag");
            Assert.That(RigState.Level("e_mag"), Is.EqualTo(maxLevel), "sanity: Magneto must be at max level");

            float fallback = AbilityTuning.DefaultMagnetoPullRadiusFallback;
            _maxGo.transform.position = Vector3.zero;

            // Just inside the fallback radius — must still be pulled.
            SpawnCellAt(_director, new Vector3(fallback - 0.5f, 0f, 0f));
            var insideCell = LiveList(_director)[0];
            Vector3 beforeInside = insideCell.transform.position;
            InvokeUpdate(_director);
            Assert.That(insideCell.transform.position, Is.Not.EqualTo(beforeInside),
                "with no live area, a max-level Magneto must still pull within the fallback radius");
            insideCell.gameObject.SetActive(false);
            LiveList(_director).Clear();

            // Clearly beyond the fallback radius — must NOT be pulled (rules out "unbounded").
            SpawnCellAt(_director, new Vector3(fallback + 10f, 0f, 0f));
            var outsideCell = LiveList(_director)[0];
            Vector3 beforeOutside = outsideCell.transform.position;
            InvokeUpdate(_director);
            Assert.That(outsideCell.transform.position, Is.EqualTo(beforeOutside),
                "the no-area fallback must be bounded, not unbounded");
        }
    }
}
