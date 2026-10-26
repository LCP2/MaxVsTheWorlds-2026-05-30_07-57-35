using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1280 — the Jammer's own approach-then-circle movement. Fails to compile on base commit
    /// a2a730a: no <see cref="EnemyKind.Jammer"/> value exists there at all (the same "brand new kind,
    /// fails to compile" proof <c>MV1279ShieldProjectorHangBackTests</c> uses for the Projector). Tier 2
    /// (resolved values): asserts the Jammer's own resolved <c>transform.position</c> after ticking,
    /// never an authored constant.
    ///
    /// ONE new EditMode test (MV-465 Rule 1) — the ticket's own AC1 scenario: a Jammer starting 12 m
    /// from Max, ticked to convergence, ends 3.5-4.5 m from him. Built via
    /// <see cref="EnemySpawner.SpawnExact"/>, the real pool/factory path every in-game robot comes from
    /// — no hand-set private field. Driven entirely through the Jammer's own public
    /// <see cref="RobotEnemy.Tick"/>.
    /// </summary>
    public sealed class MV1280JammerApproachMovementTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private const float Dt = 0.05f;
        private const float SimSeconds = 20f;   // ample for 1.0 m/s over the 8 m starting gap, plus settle

        // Well clear of every other EditMode fixture's own coordinates (same reasoning
        // MV1105EveryKindDamagesASentinelTests/MV1118RusherFourAtOnceTests give for their own origins).
        private static readonly Vector3 RigOrigin = new Vector3(-74123f, 0f, 56789f);
        private static readonly Vector3 MaxPos = RigOrigin;
        private static readonly Vector3 JammerStart = RigOrigin + new Vector3(12f, 0f, 0f);

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
        public void TickJammerApproach_FromTwelveMetres_EndsWithinStandoffBand()
        {
            GameObject groundGo = null, playerGo = null, spawnerGo = null;
            try
            {
                // A real level always has a floor; this isolated fixture doesn't unless it builds one
                // (same reasoning/shape as MV1118RusherFourAtOnceTests' own ground cube) — without it
                // the Jammer never reads CharacterController.isGrounded true, free-falls, and
                // RobotEnemy's own fall-recovery teleports it back to its spawn point mid-test.
                groundGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                groundGo.name = "MV1280 Ground";
                groundGo.transform.position = new Vector3(RigOrigin.x, -1f, RigOrigin.z);
                groundGo.transform.localScale = new Vector3(200f, 2f, 200f);

                playerGo = new GameObject("MV1280 Max") { tag = "Player" };
                playerGo.transform.position = MaxPos;

                spawnerGo = new GameObject("MV1280 Spawner");
                spawnerGo.transform.position = MaxPos;
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                // Positioned at the archetype's own SpawnHeight above the floor (feet at Y=0, same as
                // every real spawn) rather than at the floor plane itself.
                float jammerFootHeight = EnemyArchetype.Of(EnemyKind.Jammer).SpawnHeight;

                var jammerList = spawner.SpawnExact(EnemyKind.Jammer, 1);
                Assert.AreEqual(1, jammerList.Count, "test setup: SpawnExact didn't spawn the Jammer");
                RobotEnemy jammer = jammerList[0];
                jammer.transform.position = JammerStart + Vector3.up * jammerFootHeight;
                RobotOnEnableMethod.Invoke(jammer, null); // seeds RobotEnemy.Active, same idiom every
                                                           // other OnEnable-driven fixture in this tree uses.

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.AreEqual(RobotEnemy.State.Chase, jammer.Current,
                    "setup failure: a freshly activated Jammer must already be awake");

                int steps = Mathf.CeilToInt(SimSeconds / Dt);
                for (int i = 0; i < steps; i++) jammer.Tick(Dt);

                Vector3 local = jammer.transform.position - MaxPos; local.y = 0f;
                float distFromMax = local.magnitude;

                Assert.That(distFromMax, Is.InRange(3.5f, 4.5f),
                    $"a Jammer must converge to 3.5-4.5 m from Max (got {distFromMax:0.00} m) — " +
                    "the ticket's own AC1 band");
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
