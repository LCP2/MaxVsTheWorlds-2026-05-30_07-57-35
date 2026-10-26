using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1279 — the Shield Projector's own hang-back movement. Fails to compile on base commit
    /// 4f8ca20: no <see cref="EnemyKind.Projector"/> value exists there at all (the same "brand new
    /// kind, fails to compile" proof <c>MV1244WelderRepairTests</c> uses for the Welder). Tier 2
    /// (resolved values): asserts the Projector's own resolved <c>transform.position</c> after ticking,
    /// never an authored constant.
    ///
    /// ONE new EditMode test (MV-465 Rule 1) — the ticket's own AC1 scenario: Max stationary, three
    /// (stationary) robots at a fixed point 6 m away, a Projector elsewhere, ticked to convergence.
    /// Built via <see cref="EnemySpawner.SpawnExact"/>, the real pool/factory path every in-game robot
    /// comes from — no hand-set private field. Driven entirely through the Projector's own public
    /// <see cref="RobotEnemy.Tick"/>; the three reference robots are never ticked — AC1 only needs them
    /// to sit still as awake pack members, not to act. <see cref="ComputeProjectorGoal"/> (the method
    /// under test) reads the pack off <c>RobotEnemy.Active</c>, which only <c>OnEnable</c> populates —
    /// Awake/OnEnable are not reliably invoked for a freshly-activated GameObject outside Play mode
    /// (the same note every other RobotOnEnableMethod-driven test in this tree carries, e.g.
    /// <c>MV1006SentinelFiresThroughBarriersTests</c>), so it is force-invoked via reflection below for
    /// every spawned robot, same idiom.
    /// </summary>
    public sealed class MV1279ShieldProjectorHangBackTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private const float Dt = 0.05f;
        private const float SimSeconds = 15f;   // ample for 0.9 m/s over the ~7.3 m starting gap

        // Well clear of every other EditMode fixture's own coordinates (same reasoning
        // MV1105EveryKindDamagesASentinelTests/MV1118RusherFourAtOnceTests give for their own origins).
        private static readonly Vector3 RigOrigin = new Vector3(-61234f, 0f, 83421f);
        private static readonly Vector3 MaxPos = RigOrigin;
        private static readonly Vector3 PackPos = RigOrigin + new Vector3(6f, 0f, 0f);
        private static readonly Vector3 ProjectorStart = RigOrigin + new Vector3(2f, 0f, 2f);

        [SetUp]
        public void SetUp()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            // A few ungrounded frames at spawn (before CharacterController.isGrounded first settles
            // true) is normal startup behaviour, not something this test is about — pre-trip the
            // one-time-per-session warning (CharacterControllerMotion's own "test-settable, same seam
            // as CallCount") so it can never turn an unrelated engine transient into a failure here.
            CharacterControllerMotion.HasWarnedOversizedMove = true;
        }

        [TearDown]
        public void TearDown()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
        }

        [Test]
        public void TickProjectorHangBack_ConvergesBehindItsPack_SevenToNineMetresFromMax()
        {
            GameObject groundGo = null, playerGo = null, spawnerGo = null;
            try
            {
                // A real level always has a floor; this isolated fixture doesn't unless it builds one
                // (same reasoning/shape as MV1118RusherFourAtOnceTests' own ground cube) — without it
                // the Projector never reads CharacterController.isGrounded true, free-falls, and
                // RobotEnemy's own fall-recovery teleports it back to its spawn point mid-test.
                groundGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                groundGo.name = "MV1279 Ground";
                groundGo.transform.position = new Vector3(RigOrigin.x, -1f, RigOrigin.z);
                groundGo.transform.localScale = new Vector3(200f, 2f, 200f);

                playerGo = new GameObject("MV1279 Max") { tag = "Player" };
                playerGo.transform.position = MaxPos;

                spawnerGo = new GameObject("MV1279 Spawner");
                spawnerGo.transform.position = MaxPos;
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                // Positioned at each archetype's own SpawnHeight above the floor (feet at Y=0, same as
                // every real spawn) rather than at the floor plane itself.
                float packFootHeight = EnemyArchetype.Of(EnemyKind.Rusher).SpawnHeight;
                float projectorFootHeight = EnemyArchetype.Of(EnemyKind.Projector).SpawnHeight;

                var pack = spawner.SpawnExact(EnemyKind.Rusher, 3);
                Assert.AreEqual(3, pack.Count, "test setup: SpawnExact didn't spawn every requested pack robot");
                foreach (RobotEnemy r in pack)
                {
                    r.transform.position = PackPos + Vector3.up * packFootHeight;
                    RobotOnEnableMethod.Invoke(r, null); // seeds RobotEnemy.Active — see class doc comment
                }

                var projectorList = spawner.SpawnExact(EnemyKind.Projector, 1);
                Assert.AreEqual(1, projectorList.Count, "test setup: SpawnExact didn't spawn the Projector");
                RobotEnemy projector = projectorList[0];
                projector.transform.position = ProjectorStart + Vector3.up * projectorFootHeight;
                RobotOnEnableMethod.Invoke(projector, null);

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.AreEqual(RobotEnemy.State.Chase, projector.Current,
                    "setup failure: a freshly activated Projector must already be awake");
                Assert.AreEqual(4, RobotEnemy.ActiveCount,
                    "test setup: the pack and the Projector must all be registered in RobotEnemy.Active");

                int steps = Mathf.CeilToInt(SimSeconds / Dt);
                for (int i = 0; i < steps; i++) projector.Tick(Dt);

                Vector3 local = projector.transform.position - MaxPos; local.y = 0f;
                float distFromMax = local.magnitude;

                Assert.That(distFromMax, Is.InRange(6.9f, 9.1f),
                    $"a Projector must converge to 7-9 m from Max (got {distFromMax:0.00} m) — " +
                    "the ticket's own clamp band");
                Assert.Greater(local.x, 6f,
                    "a Projector must converge on the FAR side of its pack (x > 6, the pack's own x) — " +
                    "using the pack as a shield between itself and Max, not merely matching its distance");
            }
            finally
            {
                // Destroys every spawned robot too — a robot's gameObject must never be destroyed
                // separately (same note MV1118RusherFourAtOnceTests' own RunScenario carries).
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (groundGo != null) Object.DestroyImmediate(groundGo);
            }
        }
    }
}
