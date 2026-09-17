using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-832: Sentinels picked the nearest robot from an all-layers <c>Physics.OverlapSphereNonAlloc</c>
    /// with a 16-slot buffer — no line-of-sight check (shot straight through walls into a neighbouring
    /// room), no area check (ignored Max's own room in favour of a nearer one next door), and no
    /// Dormant/<see cref="RobotEnemy.IsDamageable"/> gate (wasted shots waking sleeping garrisons or
    /// hitting an invulnerable Grate Lurker) — and in a room dense enough with non-robot colliders, the
    /// buffer could fill before a single robot was ever returned, so the turret never fired at all.
    /// Fixed by sourcing candidates from <see cref="RobotEnemy.Active"/> directly, filtered by
    /// alive/awake/damageable/range/<see cref="LineOfSight"/>, then narrowed to Max's own zone (or a
    /// zone sharing its footprint, MV-697) whenever anything qualifies there.
    ///
    /// One test, per CC_AUTONOMY's "at most one new test per ticket" rule — this is that one, covering
    /// the ticket's AC1-AC4 (AC5 is the pre-existing <c>SentinelTargetingTests</c> suite, untouched by
    /// this change; AC6 is <c>cc-verify.bat</c>). AC1/AC2 are built so the OLD nearest-by-raw-distance
    /// <c>NearestRobotInRange</c> gets them wrong: R2 sits CLOSER in straight-line distance (4 m) than
    /// R1 (6 m) despite being through the wall, so a fix that merely re-ordered without adding the real
    /// LOS/area/damage gates would still fail this test.
    ///
    /// Actually run (not just reasoned about) against the pre-fix <c>NearestRobotInRange</c> — reflection
    /// targets the method by name, which existed under both implementations, so this same file runs
    /// unmodified against either one. At base commit 287c461 (<c>Sentinel.cs</c> stashed back to that
    /// commit, test re-run, then restored) it failed at AC1 with:
    /// <c>Expected: same as R1; But was: R2</c> — the old physics-overlap rule picked the nearer-but-
    /// blocked R2 exactly as predicted. Passes clean against the fix below.
    /// </summary>
    public sealed class MV832SentinelTargetingTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly MethodInfo NearestRobotInRangeMethod =
            typeof(Sentinel).GetMethod("NearestRobotInRange", NonPublicInstance);
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", NonPublicInstance);

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
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

            EnemyNavigation.Reset();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
        }

        private static RobotEnemy InvokeNearestRobotInRange(Sentinel sentinel) =>
            (RobotEnemy)NearestRobotInRangeMethod.Invoke(sentinel, null);

        private Sentinel NewSentinel(Vector3 position, float range, Transform followTarget)
        {
            var go = new GameObject("MV832-Sentinel");
            _spawned.Add(go);
            var sentinel = go.AddComponent<Sentinel>();
            sentinel.Init(position, maxHp: 100f, range: range, fireInterval: 0.6f,
                moveSpeed: 0f, standoffDistance: 2.5f, followTarget: followTarget);
            return sentinel;
        }

        private RobotEnemy NewRobot(Vector3 position, EnemyKind kind = EnemyKind.Rusher)
        {
            var go = new GameObject($"MV832-{kind}");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            CcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Of(kind));
            RobotOnEnableMethod.Invoke(robot, null); // seeds Active/ResetState — Awake/OnEnable don't run outside Play mode
            go.transform.position = position;
            return robot;
        }

        /// <summary>Two 20x20 m rooms sharing a wall at x=0 (no link between them, so the shared edge
        /// stays a solid, unbroken wall — MV-832's "two zones separated by a solid wall"), installed
        /// through a bare <see cref="BackyardPath"/> so <see cref="EnemyNavigation.Map"/> resolves it —
        /// same idiom <c>MV611DormantAreaGateTests</c>/<c>MV795FloodRobotImmunityTests</c> already use.</summary>
        private void InstallTwoZoneMap()
        {
            var zoneA = new MapZone { id = "area1", x = -10f, z = 0f, width = 20f, depth = 20f };
            var zoneB = new MapZone { id = "area2", x = 10f, z = 0f, width = 20f, depth = 20f };
            var map = new MapData { zones = new[] { zoneA, zoneB } };

            var mapRoot = new GameObject("MV832-map-root");
            _spawned.Add(mapRoot);
            MapRuntime.Build(map, mapRoot.transform);

            var pathGo = new GameObject("MV832-backyard-path");
            _spawned.Add(pathGo);
            var path = pathGo.AddComponent<BackyardPath>();
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            BackyardPathMapField.SetValue(path, map);

            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)
        }

        [Test]
        public void NearestRobotInRange_RespectsWallsAreasAndDamageability()
        {
            // ---------------------------------------------------------------- AC1 + AC2: line of sight
            // Sentinel and Max sit right against the shared wall (x=0). R2 is only 4 m away in RAW
            // distance but on the far side of the wall (blocked); R1 is 6 m away but in the clear —
            // the old nearest-by-distance rule picks R2 (4 m < 6 m); the fix must pick R1 instead.
            InstallTwoZoneMap();
            var maxGo = new GameObject("MV832-Max", typeof(CharacterController));
            _spawned.Add(maxGo);
            maxGo.transform.position = new Vector3(-1f, 0f, 0f);

            Sentinel sentinel = NewSentinel(new Vector3(-1f, 0f, 0f), range: 20f, followTarget: maxGo.transform);
            RobotEnemy r1 = NewRobot(new Vector3(-1f, 0f, 6f));   // zone A, 6 m, clear sight
            RobotEnemy r2 = NewRobot(new Vector3(3f, 0f, 0f));    // zone B, 3 m past the wall, 4 m raw distance
            Physics.SyncTransforms();

            RobotEnemy chosen = InvokeNearestRobotInRange(sentinel);
            Assert.AreSame(r1, chosen,
                "AC1: R1 (farther, but clear sight and Max's own zone) must be chosen over R2 " +
                "(nearer in raw distance, but through the wall) — this fails against the old " +
                "nearest-by-distance NearestRobotInRange, which picks R2");

            // AC2: with R1 gone, only R2 remains — and it's still behind the wall, so nothing qualifies.
            GameObject r1Go = r1.gameObject;
            _spawned.Remove(r1Go);
            Object.DestroyImmediate(r1Go);
            Physics.SyncTransforms();

            RobotEnemy chosenAfterR1Removed = InvokeNearestRobotInRange(sentinel);
            Assert.IsNull(chosenAfterR1Removed,
                "AC2: with R1 gone, R2 sits behind a wall and must never be chosen, and no bolt fires");

            // ---------------------------------------------------------------- AC3: Dormant / non-damageable
            // A Dormant robot at 2 m and a Lurker (IsDamageable false while Submerged, its spawn default)
            // at 1.5 m must both be skipped in favour of the only real candidate, 5 m away. Clears the
            // registry first — R2 (AC1/AC2, still alive behind the wall) must not leak into this section's
            // candidate pool; NearestRobotInRange reads RobotEnemy.Active, not a physics query, so
            // clearing the registry (not the GameObject) is enough to retire it.
            RobotEnemy.ResetRegistry();
            Sentinel sentinelAc3 = NewSentinel(Vector3.zero, range: 20f, followTarget: null);

            RobotEnemy dormant = NewRobot(new Vector3(2f, 0f, 0f));
            dormant.BeginDormant();
            Assert.IsTrue(dormant.IsDormant, "test setup: BeginDormant must actually land in State.Dormant");

            RobotEnemy lurker = NewRobot(new Vector3(1.5f, 0f, 0f), EnemyKind.Lurker);
            Assert.IsFalse(lurker.IsDamageable, "test setup: a freshly-spawned Lurker defaults to Submerged/undamageable");

            RobotEnemy eligible = NewRobot(new Vector3(5f, 0f, 0f));
            Physics.SyncTransforms();

            RobotEnemy chosenAc3 = InvokeNearestRobotInRange(sentinelAc3);
            Assert.AreSame(eligible, chosenAc3,
                "AC3: a Dormant robot and an undamageable Lurker must never be chosen, even far closer " +
                "than the only real candidate");

            // ---------------------------------------------------------------- AC4: the "never fires" probe
            // 40 non-robot colliders inside range, plus one awake/visible robot at 5 m — the old
            // Physics.OverlapSphereNonAlloc(s_hits: Collider[16]) could fill its whole buffer with these
            // before ever reaching the robot, returning null and never firing.
            RobotEnemy.ResetRegistry(); // retire AC3's dormant/lurker/eligible robots first
            Sentinel sentinelAc4 = NewSentinel(Vector3.zero, range: 10f, followTarget: null);
            for (int i = 0; i < 40; i++)
            {
                var clutter = new GameObject($"MV832-clutter-{i}");
                _spawned.Add(clutter);
                clutter.transform.position = new Vector3(1f + i * 0.05f, 0f, 0f);
                clutter.AddComponent<BoxCollider>();
            }
            RobotEnemy onlyRobot = NewRobot(new Vector3(5f, 0f, 0f));
            Physics.SyncTransforms();

            RobotEnemy chosenAc4 = InvokeNearestRobotInRange(sentinelAc4);
            Assert.AreSame(onlyRobot, chosenAc4,
                "AC4 'never fires' probe: 40 non-robot colliders inside range must never prevent the " +
                "one awake, visible robot from being chosen");
        }
    }
}
