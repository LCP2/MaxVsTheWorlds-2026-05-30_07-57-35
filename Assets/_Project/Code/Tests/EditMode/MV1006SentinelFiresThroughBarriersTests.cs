using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1006 (Lee, device, 2026-09-28: "Revise the Sentinel ability: they should be able to fire
    /// through barriers"). <see cref="Sentinel.IsEligibleTarget"/> used to end on
    /// <c>LineOfSight.Clear(muzzle, rp, robot.transform)</c> — any solid cover between a Sentinel and a
    /// robot made that robot ineligible, so a Sentinel standing right next to a barrier could never hit
    /// anything hiding behind it. Lee's revision (comment, 2026-09-28 18:09) additionally requires a
    /// Dormant robot to become eligible too, but ONLY when it shares the Sentinel's own area (MV-832
    /// <see cref="MapZone.ShareFootprint"/> semantics) — a Dormant robot elsewhere in range must stay
    /// ineligible even with no line-of-sight gate at all.
    ///
    /// One test (testing policy MV-465 Rule 1) covering both halves of the same eligibility rewrite as
    /// facets of one change: (1) an awake robot behind a barrier now takes damage, (2) a Dormant robot
    /// behind a barrier in the Sentinel's own area takes damage and wakes, (3) a Dormant robot in a
    /// different area, in range, takes none. Fails on base commit fdd7942 (today) at part (1) with:
    /// <c>Expected: greater than 0f. But was: 0f</c> — <c>LineOfSight.Clear</c> blocks the shot on the
    /// barrier, so no damage lands and the old code never even reaches the Dormant-area question.
    /// </summary>
    public sealed class MV1006SentinelFiresThroughBarriersTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", NonPublicInstance);
        private static readonly MethodInfo SentinelUpdateMethod =
            typeof(Sentinel).GetMethod("Update", NonPublicInstance);
        private static readonly FieldInfo FireCooldownField =
            typeof(Sentinel).GetField("_fireCooldown", NonPublicInstance);

        // Well clear of every other EditMode fixture's coordinates — NearestRobotInRange reads
        // RobotEnemy.Active globally, so a stray leftover elsewhere could otherwise outrank this
        // test's own targets (same reasoning MV914SentinelWorldBeamTests gives for its own origin).
        private static readonly Vector3 Origin = new Vector3(91234f, 0f, 61234f);

        private System.Collections.Generic.List<GameObject> _spawned;

        [SetUp]
        public void SetUp()
        {
            _spawned = new System.Collections.Generic.List<GameObject>();
            Sentinel.FocusEnabled = false;
            EnemyNavigation.Reset();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();

            Sentinel.FocusEnabled = false;
            EnemyNavigation.Reset();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
        }

        private static void InvokeUpdate(Sentinel sentinel) => SentinelUpdateMethod.Invoke(sentinel, null);

        private RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV1006-Rusher");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance).SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            RobotOnEnableMethod.Invoke(robot, null); // seeds Active/ResetState — Awake/OnEnable don't run outside Play mode
            go.transform.position = position;
            return robot;
        }

        private GameObject NewBarrier(Vector3 center)
        {
            var go = new GameObject("MV1006-Barrier");
            _spawned.Add(go);
            go.transform.position = center;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(1f, 1.6f, 4f);
            CoverLayer.Assign(go);
            return go;
        }

        /// <summary>Two 20x20 m rooms sharing a wall (MV-832's own idiom) — <c>own</c> is the Sentinel's
        /// own area, <c>other</c> is a different one, both installed through a bare
        /// <see cref="BackyardPath"/> so <see cref="EnemyNavigation.Map"/> resolves it.</summary>
        private void InstallTwoZoneMap()
        {
            var own = new MapZone { id = "mv1006-own", x = Origin.x - 10f, z = Origin.z, width = 20f, depth = 20f };
            var other = new MapZone { id = "mv1006-other", x = Origin.x + 10f, z = Origin.z, width = 20f, depth = 20f };
            var map = new MapData { zones = new[] { own, other } };

            var mapRoot = new GameObject("MV1006-map-root");
            _spawned.Add(mapRoot);
            MapRuntime.Build(map, mapRoot.transform);

            var pathGo = new GameObject("MV1006-backyard-path");
            _spawned.Add(pathGo);
            var path = pathGo.AddComponent<BackyardPath>();
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            BackyardPathMapField.SetValue(path, map);

            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)
        }

        [Test]
        public void EligibilityIgnoresBarriers_ButDormantStaysGatedToTheSentinelsOwnArea()
        {
            Assert.IsTrue(CoverLayer.Exists, "no Cover layer in TagManager — a barrier can't block anything to prove this test fail-first");

            InstallTwoZoneMap();

            var sentinelGo = new GameObject("MV1006-Sentinel");
            _spawned.Add(sentinelGo);
            var sentinel = sentinelGo.AddComponent<Sentinel>();
            sentinel.Init(Origin + new Vector3(-15f, 0f, 0f), maxHp: 100f, range: 30f, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);

            // ---------------------------------------------------------------- Part 1: awake robot, barrier
            // Sentinel and robot both sit in the "own" zone, 6 m apart on the X axis, with a solid cover
            // box directly on the muzzle-to-target line between them.
            RobotEnemy awake = NewRobot(Origin + new Vector3(-9f, 0f, 0f));
            NewBarrier(Origin + new Vector3(-12f, 0.8f, 0f));
            Physics.SyncTransforms();
            float awakeHealthBefore = awake.HealthCurrent;

            InvokeUpdate(sentinel);

            float awakeDamage = awakeHealthBefore - awake.HealthCurrent;
            Assert.That(awakeDamage, Is.GreaterThan(0f),
                "an awake robot behind a barrier must still take damage — this fails against the old " +
                "LineOfSight-gated IsEligibleTarget, which blocks the shot on the barrier");

            // ---------------------------------------------------------------- Part 2: Dormant, own area
            RobotEnemy.ResetRegistry(); // retires the awake robot above; NearestRobotInRange reads Active
            FireCooldownField.SetValue(sentinel, 0f); // force the next Update to re-evaluate a target

            RobotEnemy dormantOwnArea = NewRobot(Origin + new Vector3(-9f, 0f, 0f));
            dormantOwnArea.BeginDormant();
            Assert.IsTrue(dormantOwnArea.IsDormant, "test setup: BeginDormant must actually land in State.Dormant");
            NewBarrier(Origin + new Vector3(-12f, 0.8f, 0f));
            Physics.SyncTransforms();
            float dormantOwnHealthBefore = dormantOwnArea.HealthCurrent;

            InvokeUpdate(sentinel);

            float dormantOwnDamage = dormantOwnHealthBefore - dormantOwnArea.HealthCurrent;
            Assert.That(dormantOwnDamage, Is.GreaterThan(0f),
                "a Dormant robot behind a barrier, in the Sentinel's own area, must take damage");
            Assert.IsFalse(dormantOwnArea.IsDormant,
                "taking a hit must wake a Dormant robot in the Sentinel's own area, exactly as any damage already does (MV-980)");

            // ---------------------------------------------------------------- Part 3: Dormant, different area
            RobotEnemy.ResetRegistry(); // retires part 2's now-awake robot
            FireCooldownField.SetValue(sentinel, 0f);

            RobotEnemy dormantOtherArea = NewRobot(Origin + new Vector3(9f, 0f, 0f)); // in "other", 24 m away, in range
            dormantOtherArea.BeginDormant();
            Physics.SyncTransforms();
            float dormantOtherHealthBefore = dormantOtherArea.HealthCurrent;

            InvokeUpdate(sentinel);

            Assert.That(dormantOtherArea.HealthCurrent, Is.EqualTo(dormantOtherHealthBefore),
                "a Dormant robot in a DIFFERENT area, even within range, must take no damage");
            Assert.IsTrue(dormantOtherArea.IsDormant, "a Dormant robot in a different area must stay asleep");
        }
    }
}
