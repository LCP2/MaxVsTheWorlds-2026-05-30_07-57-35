using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-870 — <c>RobotEnemy.Update</c> ran its full per-frame body (a CharacterController gravity
    /// sweep via <see cref="CharacterControllerMotion.SafeMove"/>, <c>ClampToDeckFootprint</c>, a
    /// separation-grid write, every timer) for EVERY live robot every frame, whatever its state —
    /// including a <c>Dormant</c> robot that is well behind the player
    /// (<see cref="RobotEnemy.IsWellBehindPlayer"/>, MV-611) and therefore can never be seen, can never
    /// see, and can never change anything the player will observe. That population runs into the
    /// hundreds by World 2's midgame (this ticket's own analysis: 194 by a11, up to 412 by the boss).
    ///
    /// Fix: a Dormant-and-well-behind robot's WHOLE tick throttles to 4 Hz (<c>RobotEnemy.Tick</c>'s
    /// own doc comment) instead of 60. This pins two things a description alone can't prove: (1) the
    /// physics-sweep count is MEASURED, via an internal static call counter on <c>SafeMove</c> itself,
    /// not inferred from anything else; (2) the throttle provably loses no time — a well-behind robot's
    /// corroded timer decays by exactly the same total elapsed time as an equivalent robot ticking
    /// every frame, even though its body actually ran only a handful of times, not 60.
    ///
    /// Must FAIL on base commit 68d962b: <c>RobotEnemy.Update</c> ran unthrottled for every robot
    /// regardless of state, so the well-behind robot's <c>SafeMove</c> count read 60, not "at most 5".
    ///
    /// Same reflection idiom as <c>MV657TargetReacquireTests</c>: <c>RobotEnemy.Tick</c> (split out of
    /// <c>Update</c> by this ticket for exactly this purpose) never runs outside Play mode, so it is
    /// invoked directly with an explicit, controlled dt — the same seam every per-state Tick* method
    /// already has. No GameObject is tagged "Player" anywhere in this test: instead, <c>target</c>/
    /// <c>_playerTarget</c> are stamped by hand on the two Dormant robots, which both (a) keeps the
    /// well-behind robot's own sight tick out of the picture (already skipped by MV-611 for it) and
    /// (b) leaves the Chasing robot's own `target` permanently null, so <c>TickChase</c> short-circuits
    /// on its own first line every tick instead of running real navigation/steering — the only way to
    /// pin its own <c>SafeMove</c> count to a clean, deterministic 60 (gravity only) rather than one
    /// that also depends on formation/wall-latch/route-dwell steering internals this ticket does not
    /// touch.
    /// </summary>
    public sealed class MV870DormantFarTickThrottleTests
    {
        private GameObject _pathGo;
        private GameObject _playerStandIn;

        private static readonly MethodInfo TickMethod =
            typeof(RobotEnemy).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TargetField =
            typeof(RobotEnemy).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PlayerTargetField =
            typeof(RobotEnemy).GetField("_playerTarget", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CorrodedTimerField =
            typeof(RobotEnemy).GetField("_corrodedTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo DormantFarStaggeredField =
            typeof(RobotEnemy).GetField("_dormantFarStaggered", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo DormantFarAccumulatorField =
            typeof(RobotEnemy).GetField("_dormantFarAccumulator", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            if (_pathGo != null) Object.DestroyImmediate(_pathGo);
            if (_playerStandIn != null) Object.DestroyImmediate(_playerStandIn);
        }

        /// <summary>Four 10x10 m areas in a row along Z (same layout as MV611DormantAreaGateTests) —
        /// enough for a robot in area1 to be 3 areas behind a "player" standing in area4, well past
        /// <c>RobotEnemy</c>'s own <c>WellBehindAreaSlack</c> of 2.</summary>
        private static MapData FourAreasInARow(out MapZone area1, out MapZone area4)
        {
            area1 = new MapZone { id = "area1", x = 0f, z = 5f, width = 10f, depth = 10f };
            var area2 = new MapZone { id = "area2", x = 0f, z = 15f, width = 10f, depth = 10f };
            var area3 = new MapZone { id = "area3", x = 0f, z = 25f, width = 10f, depth = 10f };
            area4 = new MapZone { id = "area4", x = 0f, z = 35f, width = 10f, depth = 10f };
            return new MapData { zones = new[] { area1, area2, area3, area4 } };
        }

        private void InstallMap(MapData map)
        {
            _pathGo = new GameObject("MV870-dormant-far-throttle-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            FieldInfo mapField = typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            mapField.SetValue(path, map);
        }

        private static RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV870-robot");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.ResetState(); // EditMode has no Awake/OnEnable lifecycle — init explicitly. No "Player"
                             // tag exists anywhere in this test, so this leaves target/_playerTarget null.
            return e;
        }

        private static void TickOnce(RobotEnemy e, float dt) => TickMethod.Invoke(e, new object[] { dt });

        [Test]
        public void DormantRobotWellBehindThePlayer_ThrottlesItsWholeTickAndLosesNoTime()
        {
            MapData map = FourAreasInARow(out MapZone area1, out MapZone area4);
            InstallMap(map);

            // Stands in for the player's position only (deliberately NOT tagged "Player" — see class
            // doc comment) — set directly onto target/_playerTarget below.
            _playerStandIn = new GameObject("MV870-player-stand-in");
            _playerStandIn.transform.position = new Vector3(0f, 0f, area4.z);

            RobotEnemy wellBehind = NewRobot(new Vector3(0f, 0f, area1.z));
            RobotEnemy near = NewRobot(new Vector3(0f, 0f, area4.z));
            RobotEnemy chasing = NewRobot(new Vector3(2f, 0f, area4.z));

            try
            {
                wellBehind.BeginDormant();
                near.BeginDormant();
                // chasing stays whatever ResetState() left it in: State.Chase.
                Assert.AreEqual(RobotEnemy.State.Chase, chasing.Current, "sanity: the third robot must actually be Chasing");

                TargetField.SetValue(wellBehind, _playerStandIn.transform);
                PlayerTargetField.SetValue(wellBehind, _playerStandIn.transform);
                TargetField.SetValue(near, _playerStandIn.transform);
                PlayerTargetField.SetValue(near, _playerStandIn.transform);
                // chasing's target/_playerTarget stay null — no "Player"-tagged object exists anywhere
                // in this test, so TickChase's own "if (target == null) { AcquireTarget(); return; }"
                // fires every tick, deterministically producing zero movement-driven SafeMove calls.

                Assert.IsTrue(wellBehind.IsWellBehindPlayer, "sanity: the rig must actually place this robot well behind for the throttle to engage");
                Assert.IsFalse(near.IsWellBehindPlayer, "sanity: the rig must actually place this robot near the player, or it throttles too");

                // Pin the well-behind robot's per-robot stagger to exactly 0. The stagger only exists
                // to desynchronise SEPARATE robots from each other (see Tick's own comment) — it is
                // irrelevant to whether time is conserved, and leaving it to GetInstanceID() would make
                // the corroded-timer comparison below depend on an unpredictable, untestable value.
                DormantFarStaggeredField.SetValue(wellBehind, true);
                DormantFarAccumulatorField.SetValue(wellBehind, 0f);

                // Big enough that a second of decay can't clip either robot at its 0 floor, so the
                // comparison below is a clean subtraction on both sides.
                const float startingCorroded = 10f;
                CorrodedTimerField.SetValue(wellBehind, startingCorroded);
                CorrodedTimerField.SetValue(near, startingCorroded);

                const float dt = 1f / 60f;

                CharacterControllerMotion.CallCount = 0;
                for (int i = 0; i < 60; i++) TickOnce(wellBehind, dt);
                int wellBehindCalls = CharacterControllerMotion.CallCount;

                CharacterControllerMotion.CallCount = 0;
                for (int i = 0; i < 60; i++) TickOnce(near, dt);
                int nearCalls = CharacterControllerMotion.CallCount;

                CharacterControllerMotion.CallCount = 0;
                for (int i = 0; i < 60; i++) TickOnce(chasing, dt);
                int chasingCalls = CharacterControllerMotion.CallCount;

                Assert.LessOrEqual(wellBehindCalls, 5,
                    "MV-870: a Dormant robot well behind the player must throttle its gravity sweep to " +
                    $"~4 Hz, not run it every frame — got {wellBehindCalls} SafeMove calls over 60 ticks");
                Assert.AreEqual(60, nearCalls,
                    "a Dormant robot near the player must be completely unaffected by the throttle");
                Assert.AreEqual(60, chasingCalls,
                    "a Chasing robot must be completely unaffected by the throttle");

                float wellBehindCorroded = (float)CorrodedTimerField.GetValue(wellBehind);
                float nearCorroded = (float)CorrodedTimerField.GetValue(near);
                Assert.AreEqual(nearCorroded, wellBehindCorroded, 1e-3f,
                    "MV-870 AC1: the well-behind robot's corroded timer must have advanced by the same " +
                    "total elapsed time as the near robot's -- the 4 Hz tick must lose no time even " +
                    "though its body ran only a handful of times, not 60");
            }
            finally
            {
                Object.DestroyImmediate(wellBehind.gameObject);
                Object.DestroyImmediate(near.gameObject);
                Object.DestroyImmediate(chasing.gameObject);
            }
        }
    }
}
