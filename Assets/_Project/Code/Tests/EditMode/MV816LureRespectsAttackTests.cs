using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-816 — Lee's report (live WebGL build 3255d43, World 2): Chargers no longer charge, and robots
    /// queued at a Replicator never go in. Three independent root causes, one test each:
    ///
    /// (a) <see cref="Replicator.TickLure"/>'s selection screen only refused a robot within the 7 m
    /// <see cref="Replicator.MaxMeleeExclusionRadius"/>, while <see cref="RobotEnemy"/>'s own seeking
    /// tick cancelled on sight+lungeRange instead — a Charger's 12 m lungeRange left it lured, cancelled
    /// next tick, and re-lured 0.5 s later forever. Both call sites now share one
    /// <see cref="RobotEnemy.IsEngagingTarget"/> predicate, so a Charger already fighting Max is never
    /// lured in the first place, and reaches its own Telegraph/Lunge over 3 real seconds of lure ticks.
    ///
    /// (b) The lure never checked the robot's state at all — a robot in Telegraph or Lunge could be
    /// yanked into ReplicatorSeeking mid-attack. This robot is placed with no sight of Max and more than
    /// 12 m from him, so root cause (a)'s own fix would allow the lure on distance/sight alone — only the
    /// new state gate (Chase/Search only) stops it.
    ///
    /// (c) Intake was gated on <see cref="EnemySpawner.HasRoomForReplicatorIntake"/> only once a robot
    /// had already walked all the way to slot 0 and stood there until <see cref="Replicator.LureTimeoutSeconds"/>
    /// gave up. The lure itself now refuses to start at all while the field has no room.
    ///
    /// Fails on base commit 3255d43: none of <see cref="RobotEnemy.IsEngagingTarget"/>, the state gate, or
    /// the room gate exist there, so (a) a Charger 7-12 m from Max is lured into ReplicatorSeeking instead
    /// of ever reaching Telegraph; (b) a Telegraphing Rusher with no state check gets lured away from its
    /// wind-up; (c) an over-budget field still lures a Chase robot into a box with no room for it.
    ///
    /// Tier 2 (resolved values): every assertion reads a robot's resolved <see cref="RobotEnemy.Current"/>
    /// — never an authored constant, never a rendered pixel.
    /// </summary>
    public sealed class MV816LureRespectsAttackTests
    {
        private static readonly Vector3 RigOriginA = new Vector3(81660f, 0f, -41220f);
        private static readonly Vector3 RigOriginB = RigOriginA + new Vector3(2000f, 0f, 0f);
        private static readonly Vector3 RigOriginC = RigOriginA + new Vector3(4000f, 0f, 0f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickChaseMethod =
            typeof(RobotEnemy).GetMethod("TickChase", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickTelegraphMethod =
            typeof(RobotEnemy).GetMethod("TickTelegraph", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo TickLungeMethod =
            typeof(RobotEnemy).GetMethod("TickLunge", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo StateTimerField =
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance);

        private System.Collections.Generic.List<GameObject> _spawned = new System.Collections.Generic.List<GameObject>();
        private GameObject _playerGo;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            LungeTokenPool.Reset();
            EnemySpawner.ResetReplicatorReservations();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            RobotEnemy.ResetRegistry();
            LungeTokenPool.Reset();
            EnemySpawner.ResetReplicatorReservations();
        }

        private RobotEnemy NewRobot(in EnemyArchetype archetype, Vector3 position)
        {
            var go = new GameObject($"MV816-{archetype.Kind}");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // OnEnable never runs as a side effect of AddComponent outside Play mode — Apply's own
            // ResetState needs the tagged Player already in the scene, and TickLure reads RobotEnemy.Active,
            // which only OnEnable populates. Same idiom as every other Replicator/RobotEnemy EditMode test.
            CcField.SetValue(e, cc);
            e.Apply(archetype);
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        private Replicator NewBox(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(go);
            go.transform.position = position;
            var box = go.AddComponent<Replicator>();
            box.Build(); // AddComponent's own Awake never runs outside Play mode
            box.Configure(1);
            return box;
        }

        private static void Reposition(RobotEnemy e, Vector3 position)
        {
            var cc = (CharacterController)CcField.GetValue(e);
            cc.enabled = false;
            e.transform.position = position;
            cc.enabled = true;
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide, same idiom as MV707CartChargerTests
        }

        [Test]
        public void TickLure_RespectsEngagementStateAndRoom()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] every Replicator test carries

            _playerGo = new GameObject("Player") { tag = "Player" };

            // === (a) a Charger already engaging Max (in sight, within its own 12 m lungeRange) must ===
            // === never be lured, even at 10 m — squarely inside the OLD 7 m exclusion's blind spot. ===
            _playerGo.transform.position = RigOriginA;
            RobotEnemy charger = NewRobot(EnemyArchetype.Charger, RigOriginA + new Vector3(10f, 0f, 0f));
            charger.Sight.Tick(true, _playerGo.transform.position, 0.05f);
            Replicator boxA = NewBox(RigOriginA + new Vector3(10f, 0f, 5f)); // 5 m from the Charger, well inside the 16 m lure radius

            boxA.TickLure();
            Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, charger.Current,
                "a Charger in sight and within its own lungeRange of Max must never be lured, even from " +
                "outside the old 7 m exclusion radius (MV-816 change 1)");

            bool reachedAttack = false;
            float lureTimer = 0f;
            for (int i = 0; i < 60; i++) // 60 * 0.05 s = 3 s
            {
                const float dt = 0.05f;
                charger.Sight.Tick(true, _playerGo.transform.position, dt);
                StateTimerField.SetValue(charger, (float)StateTimerField.GetValue(charger) + dt);

                switch (charger.Current)
                {
                    case RobotEnemy.State.Chase: TickChaseMethod.Invoke(charger, new object[] { dt }); break;
                    case RobotEnemy.State.Telegraph: TickTelegraphMethod.Invoke(charger, new object[] { dt }); break;
                    case RobotEnemy.State.Lunge: TickLungeMethod.Invoke(charger, new object[] { dt }); break;
                }

                if (charger.Current == RobotEnemy.State.Telegraph || charger.Current == RobotEnemy.State.Lunge)
                    reachedAttack = true;

                lureTimer += dt;
                if (lureTimer >= 0.5f) // Replicator.TickLure's own real cadence
                {
                    lureTimer = 0f;
                    boxA.TickLure();
                }

                Assert.AreNotEqual(RobotEnemy.State.ReplicatorSeeking, charger.Current,
                    $"the Charger must never enter ReplicatorSeeking while it keeps fighting Max (MV-816), step {i}");
            }
            Assert.IsTrue(reachedAttack,
                $"the Charger must reach its own Telegraph or Lunge within 3 s instead of being perpetually " +
                $"re-lured, got {charger.Current}");

            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();

            // === (b) a Rusher already in Telegraph is never lured, even placed with no sight of Max and ===
            // === more than 12 m from him — where change 1's distance/sight rule ALONE would allow it. ===
            _playerGo.transform.position = RigOriginB;
            RobotEnemy rusher = NewRobot(EnemyArchetype.Rusher, RigOriginB + new Vector3(2f, 0f, 0f)); // within its own 2.2 m lungeRange
            rusher.Sight.Tick(true, _playerGo.transform.position, 0.02f);
            TickChaseMethod.Invoke(rusher, new object[] { 0.02f });
            Assert.AreEqual(RobotEnemy.State.Telegraph, rusher.Current,
                "setup failure: this robot must be telegraphing before the state gate can be tested");

            Reposition(rusher, RigOriginB + new Vector3(0f, 0f, 20f)); // now 20 m from Max (>12) — outside change 1's own reach
            rusher.Sight.Tick(false, _playerGo.transform.position, 0.02f); // and no sight of him at all
            Replicator boxB = NewBox(RigOriginB + new Vector3(0f, 0f, 30f)); // 10 m from the Rusher, well inside the 16 m lure radius

            boxB.TickLure();
            Assert.AreEqual(RobotEnemy.State.Telegraph, rusher.Current,
                "a robot already committed to Telegraph must never be lured — only the state gate stops " +
                "this one, since it has no sight of Max and sits well outside its own lungeRange (MV-816 change 2)");

            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();

            // === (c) with the field at/over GlobalMaxLiveEnemies, TickLure lures nobody at all. ===
            _playerGo.transform.position = RigOriginC;
            int cap = EnemySpawner.GlobalMaxLiveEnemies;
            for (int i = 0; i < cap + 1; i++)
                NewRobot(EnemyArchetype.Rusher, RigOriginC + new Vector3(0f, 0f, 500f + i)); // parked well away from everything below

            RobotEnemy chaser = NewRobot(EnemyArchetype.Rusher, RigOriginC + new Vector3(20f, 0f, 0f)); // 20 m from Max, ordinary Chase
            Replicator boxC = NewBox(RigOriginC + new Vector3(20f, 0f, 10f)); // 10 m from the chaser, well inside the 16 m lure radius

            boxC.TickLure();
            Assert.AreEqual(RobotEnemy.State.Chase, chaser.Current,
                "TickLure must lure nobody at all while the field is at or over GlobalMaxLiveEnemies (MV-816 change 3)");
        }
    }
}
