using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1105 — Lee, device, 2026-10-06: "I don't think chargers are doing [sentinels] damage"
    /// (MV-635 said the same of Heavy). Nothing had ever proven, per kind, that an engaged Sentinel
    /// actually loses health to that kind's own attack. One table-driven test (MV-465 Rule 1), one row
    /// per <see cref="EnemyKind"/>, driven entirely through real production entry points — no
    /// reflection, no hand-set private field, no authored-constant assertion:
    ///
    /// <list type="bullet">
    /// <item>the robot is built via <see cref="EnemySpawner.SpawnExact"/>, the same pool/factory path
    /// every in-game robot comes from;</item>
    /// <item>Max is found the same way <c>RobotEnemy.AcquireTarget</c> always finds him — the "Player"
    /// tag — never a private field set directly;</item>
    /// <item>the fight is driven by <see cref="RobotEnemy.Tick"/> and, for the three kinds whose hit
    /// lands from a separate flying projectile, that projectile's own public <c>Tick(dt)</c> (MV-1105
    /// split these out of each one's private <c>Update()</c> for exactly this reason, the same seam
    /// <see cref="RobotEnemy.Tick"/>/<see cref="Sentinel.TickSentinel"/> already carry) — nothing here
    /// waits on Unity's own player loop, which an EditMode test never gets.</item>
    /// </list>
    ///
    /// Geometry per row: an open area, a Sentinel 3 m from the robot, Max 9 m away on a perpendicular
    /// bearing (so neither distance can couple to the other as anything moves) — exactly AC1's own
    /// numbers, chosen so <see cref="SentinelTargeting.ShouldEngageSentinel"/> engages the Sentinel
    /// outright. Each row ticks up to 15 s (AC1's own cap) and then asserts the Sentinel lost at least
    /// one hit's worth of health and Max lost none. Failing kinds collect into ONE assertion so the
    /// failure message names every kind that failed, with the damage each actually dealt.
    /// </summary>
    public sealed class MV1105EveryKindDamagesASentinelTests
    {
        private const float Dt = 0.05f;
        private const float SimCapSeconds = 15f;
        private const float SentinelMaxHealth = 100f;

        // Well clear of every other EditMode fixture's own coordinates (same reasoning
        // MV1006SentinelFiresThroughBarriersTests/MV914SentinelWorldBeamTests give for their own
        // origins), but not so large that float precision at this magnitude erodes the metre-scale
        // thresholds (standoff bands, contact radii) this test depends on.
        private static readonly Vector3 RobotOrigin = new Vector3(97531f, 0f, 75319f);
        private static readonly Vector3 SentinelPos = RobotOrigin + new Vector3(3f, 0f, 0f);
        private static readonly Vector3 MaxPos = RobotOrigin + new Vector3(0f, 0f, 9f);

        private static readonly EnemyKind[] AllKinds =
        {
            EnemyKind.Rusher, EnemyKind.Bruiser, EnemyKind.Heavy, EnemyKind.Brute,
            EnemyKind.Gunner, EnemyKind.Launcher, EnemyKind.Blinker, EnemyKind.Bolter,
            EnemyKind.Lurker, EnemyKind.Turret, EnemyKind.Sludger, EnemyKind.Charger,
        };

        private static void ResetWorldState()
        {
            DifficultyDirector.Reset(); // ToughnessMultiplier == 1, so a kind's own authored
                                         // ContactDamage/TouchDamage reaches the Sentinel unscaled.
            EnemyNavigation.Reset();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            // A Rusher/Blinker row that hits the early-exit break mid-Lunge is torn down (its
            // GameObject destroyed) before its own EnterRecover ever runs to hand its token back —
            // LungeTokenPool.Reset() is this class's own documented "a test tears down" contract
            // (LungeTokenPool.cs), and skipping it here is what let one row's leaked token starve
            // every later Rusher/Blinker row of the full suite, in this class or any other.
            LungeTokenPool.Reset();
        }

        [SetUp]
        public void SetUp() => ResetWorldState();

        [TearDown]
        public void TearDown() => ResetWorldState();

        /// <summary>The minimum one-hit amount AC1 requires <paramref name="kind"/> to deal to a
        /// Sentinel with <paramref name="sentinelMaxHealth"/> — the same per-hit number the archetype
        /// authors for Max (<see cref="EnemyArchetype.ContactDamage"/> for a lunge/beam/splash hit,
        /// <see cref="EnemyArchetype.TouchDamage"/> for a repeating touch tick), except: the Bolter,
        /// whose hit is resolved live as 7% of the RECEIVER's own max health
        /// (<see cref="BolterBolt.DamageFor"/>) rather than a flat archetype number; and the Turret,
        /// whose archetype authors <c>ContactDamage</c> as 0 (unread) with <c>RobotEnemy</c>'s own
        /// <c>GlobDamage</c> (10) as the real fallback.</summary>
        private static float MinExpectedHit(EnemyKind kind, float sentinelMaxHealth)
        {
            switch (kind)
            {
                case EnemyKind.Bolter: return BolterBolt.DamageFor(sentinelMaxHealth);
                case EnemyKind.Turret: return 10f; // RobotEnemy.GlobDamage
                case EnemyKind.Bruiser:
                case EnemyKind.Heavy:
                case EnemyKind.Brute:
                case EnemyKind.Sludger:
                    return EnemyArchetype.Of(kind).TouchDamage;
                default:
                    return EnemyArchetype.Of(kind).ContactDamage;
            }
        }

        /// <summary>Advances every live flying projectile a fired shot may have spawned this frame
        /// (MV-1105) — an EditMode test never gets Unity's own player loop, so nothing but this call
        /// would ever move a <see cref="BolterBolt"/>/<see cref="HomingMissile"/>/<see cref="CorrosiveGlob"/>
        /// toward its own impact.</summary>
        private static void TickLiveProjectiles(float dt)
        {
            foreach (var bolt in Object.FindObjectsByType<BolterBolt>(FindObjectsSortMode.None))
                bolt.Tick(dt);
            foreach (var missile in Object.FindObjectsByType<HomingMissile>(FindObjectsSortMode.None))
                missile.Tick(dt);
            foreach (var glob in Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                glob.Tick(dt);
        }

        private static void DestroyStrayProjectiles()
        {
            foreach (var bolt in Object.FindObjectsByType<BolterBolt>(FindObjectsSortMode.None))
                Object.DestroyImmediate(bolt.gameObject);
            foreach (var missile in Object.FindObjectsByType<HomingMissile>(FindObjectsSortMode.None))
                Object.DestroyImmediate(missile.gameObject);
            foreach (var glob in Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                Object.DestroyImmediate(glob.gameObject);
        }

        [Test]
        public void EveryKind_DamagesAnEngagedSentinel_AndNeverDamagesMaxInstead()
        {
            var failures = new List<string>();
            foreach (EnemyKind kind in AllKinds)
                RunRow(kind, failures);

            Assert.IsTrue(failures.Count == 0,
                "kind(s) that failed to damage an engaged Sentinel (or leaked damage onto Max instead):\n" +
                string.Join("\n", failures));
        }

        private void RunRow(EnemyKind kind, List<string> failures)
        {
            ResetWorldState();

            GameObject maxGo = null, sentinelGo = null, spawnerGo = null, groundGo = null;
            try
            {
                // A real level always has a floor; this isolated fixture doesn't unless it builds one.
                // Without it the robot's own CharacterController is never grounded, so RobotEnemy.Tick's
                // ordinary gravity (ApplyGravity) free-falls it past FallSafetyNet.BelowFloorMargin
                // every half-second, and MV-946/MV-952's own recovery net teleports it straight back to
                // its spawn point — wiping out every tick's worth of walking before any kind can close
                // the distance to either target. A big, thin, static box with its top face at Y=0 (the
                // floor plane every area's ground sits on) is the real fix, not a workaround: it's
                // exactly what a level already provides.
                groundGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                groundGo.name = $"MV1105 Ground ({kind})";
                groundGo.transform.position = new Vector3(RobotOrigin.x, -1f, RobotOrigin.z);
                groundGo.transform.localScale = new Vector3(200f, 2f, 200f);

                // ---- Real deploy paths: Max via the "Player" tag AcquireTarget already reads, the
                // Sentinel via its own public Init, the robot via EnemySpawner's real pool/factory path.
                maxGo = new GameObject($"MV1105 Max ({kind})", typeof(CharacterController));
                maxGo.tag = "Player";
                maxGo.transform.position = MaxPos;
                var playerHealth = maxGo.AddComponent<PlayerHealth>();
                playerHealth.Initialize();

                sentinelGo = new GameObject($"MV1105 Sentinel ({kind})");
                var sentinel = sentinelGo.AddComponent<Sentinel>();
                sentinel.Init(SentinelPos, maxHp: SentinelMaxHealth, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);

                spawnerGo = new GameObject($"MV1105 Spawner ({kind})");
                spawnerGo.transform.position = RobotOrigin;
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                List<RobotEnemy> spawned = spawner.SpawnExact(kind, 1);
                Assert.AreEqual(1, spawned.Count, $"test setup: SpawnExact didn't spawn a {kind}");
                RobotEnemy robot = spawned[0];

                // SpawnExact places the robot at the factory's own door/mouth point — pin it to this
                // row's exact AC1 geometry and have it already facing the Sentinel, so every kind's own
                // commit (a Charger's locked ram direction, a Gunner's re-aimed telegraph) starts from
                // a clean, deterministic bearing rather than whatever the mouth fan happened to pick.
                robot.transform.position = RobotOrigin;
                robot.transform.rotation = Quaternion.LookRotation(
                    (SentinelPos - RobotOrigin).normalized, Vector3.up);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                float sentinelHealthBefore = sentinel.HealthCurrent;
                float maxHealthBefore = playerHealth.Current;
                float minExpected = MinExpectedHit(kind, sentinel.HealthMax);

                // A fired projectile's own Detonate()/DespawnHarmless() calls Destroy(gameObject)
                // unconditionally — edit-mode-illegal (same shape BolterBolt/HomingMissile/
                // CorrosiveGlob's own class docs already note for their Strip() helpers) — so this is
                // expected, harmless log noise for exactly the three projectile-firing kinds.
                LogAssert.ignoreFailingMessages = true;
                float sentinelDamage = 0f, maxDamage = 0f;
                try
                {
                    float elapsed = 0f;
                    while (elapsed < SimCapSeconds)
                    {
                        robot.Tick(Dt);
                        TickLiveProjectiles(Dt);
                        elapsed += Dt;

                        sentinelDamage = sentinelHealthBefore - sentinel.HealthCurrent;
                        maxDamage = maxHealthBefore - playerHealth.Current;

                        // Stop as soon as AC1's own "two full attack cycles" worth of proof exists, or
                        // the moment anything leaks onto Max — SentinelMaxHealth (100) comfortably
                        // exceeds any kind's own 2-hit amount (highest is the Charger's 60), so this
                        // never fires having killed the Sentinel outright and masks nothing. Running
                        // the full 15 s regardless would let a kind correctly re-engage Max once the
                        // Sentinel died of entirely legitimate, later hits — a test-harness false
                        // failure, not a production one.
                        if (maxDamage > 0f || sentinelDamage >= minExpected * 2f) break;
                    }
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = false;
                }

                if (maxDamage > 0f)
                {
                    failures.Add($"{kind}: leaked {maxDamage:0.00} damage onto Max instead of the " +
                        $"engaged Sentinel (Sentinel took {sentinelDamage:0.00})");
                }
                else if (sentinelDamage < minExpected)
                {
                    failures.Add($"{kind}: dealt only {sentinelDamage:0.00} damage to the Sentinel " +
                        $"in {SimCapSeconds:0}s (expected at least {minExpected:0.00}, one hit's worth)");
                }
            }
            finally
            {
                DestroyStrayProjectiles();
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
                if (sentinelGo != null) Object.DestroyImmediate(sentinelGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (groundGo != null) Object.DestroyImmediate(groundGo);
                ResetWorldState();
            }
        }
    }
}
