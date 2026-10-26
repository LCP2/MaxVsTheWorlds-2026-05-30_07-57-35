using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1284 AC1: a Rusher 2 m from a Shield Projector dome's own centre takes 0 damage from a
    /// 20-damage <see cref="Team.Player"/> hit; the same hit, after the Projector is killed, deals
    /// the full 20; a second Projector standing inside the same dome (point 3's own "never
    /// protected: any Projector") takes the full 20 too.
    ///
    /// Fails on 81845c7 (the commit before this ticket): <c>RobotEnemy.TakeDamage</c> carries no
    /// dome check at all yet, so the in-dome hit deals its full, un-deflected 20 — the first
    /// assertion below fails (expected 0, actual 20).
    ///
    /// Tier 2 (resolved values): asserts the resolved <see cref="RobotEnemy.HealthCurrent"/> delta
    /// from a direct <see cref="RobotEnemy.TakeDamage"/> call, same idiom as
    /// <c>MV1281ShieldedEliteTests</c>, never an authored constant. Built via
    /// <see cref="EnemySpawner.SpawnExact"/>, the real pool/factory path every in-game robot comes
    /// from, with <c>OnEnable</c> force-invoked via reflection (same idiom as
    /// <c>MV1279ShieldProjectorHangBackTests</c> — Awake/OnEnable are not reliably invoked for a
    /// freshly-activated GameObject outside Play mode) so <see cref="RobotEnemy.Active"/> is seeded
    /// before the dome's own centroid query reads it.
    /// </summary>
    public sealed class MV1284ShieldProjectorDomeTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private const float Dt = 0.05f;
        private const float TestDamage = 20f;

        // Well clear of every other EditMode fixture's own coordinates (same reasoning
        // MV1279ShieldProjectorHangBackTests/MV1105EveryKindDamagesASentinelTests give for their own origins).
        private static readonly Vector3 RigOrigin = new Vector3(91234f, 0f, -55678f);
        private static readonly Vector3 MaxPos = RigOrigin + new Vector3(0f, 0f, -40f);
        private static readonly Vector3 ProjectorPos = RigOrigin;
        private static readonly Vector3 RushTestPos = RigOrigin + new Vector3(2f, 0f, 0f);
        private static readonly Vector3 RushOtherPos = RigOrigin + new Vector3(-2f, 0f, 0f);
        private static readonly Vector3 SecondProjectorPos = RigOrigin + new Vector3(1f, 0f, 0f);

        [SetUp]
        public void SetUp()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            CharacterControllerMotion.HasWarnedOversizedMove = true;
        }

        [TearDown]
        public void TearDown()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            // The dome's own visual (ProjectorDome) is an independent GameObject, never parented to
            // the spawner — a Collapse() it starts only finishes self-destroying via Update(), which
            // never runs outside Play mode (see ProjectorDome's own class doc comment), so this test
            // must sweep any it created itself rather than rely on the spawner's own teardown.
            foreach (var dome in Object.FindObjectsByType<ProjectorDome>(FindObjectsInactive.Include))
                Object.DestroyImmediate(dome.gameObject);
        }

        [Test]
        public void TakeDamage_ZeroInsideLiveDome_FullAfterProjectorDies_FullForASecondProjectorInside_MV1284()
        {
            GameObject groundGo = null, playerGo = null, spawnerGo = null;
            try
            {
                // A real level always has a floor; this isolated fixture doesn't unless it builds
                // one (same reasoning/shape as MV1279ShieldProjectorHangBackTests' own ground cube).
                groundGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                groundGo.name = "MV1284 Ground";
                groundGo.transform.position = new Vector3(RigOrigin.x, -1f, RigOrigin.z);
                groundGo.transform.localScale = new Vector3(200f, 2f, 200f);

                playerGo = new GameObject("MV1284 Max") { tag = "Player" };
                playerGo.transform.position = MaxPos;

                spawnerGo = new GameObject("MV1284 Spawner");
                spawnerGo.transform.position = MaxPos;
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                float rusherFoot = EnemyArchetype.Of(EnemyKind.Rusher).SpawnHeight;
                float projectorFoot = EnemyArchetype.Of(EnemyKind.Projector).SpawnHeight;

                var projectorList = spawner.SpawnExact(EnemyKind.Projector, 1);
                Assert.AreEqual(1, projectorList.Count, "test setup: SpawnExact didn't spawn the Projector");
                RobotEnemy projector = projectorList[0];
                projector.transform.position = ProjectorPos + Vector3.up * projectorFoot;
                RobotOnEnableMethod.Invoke(projector, null);

                var testRushList = spawner.SpawnExact(EnemyKind.Rusher, 1);
                Assert.AreEqual(1, testRushList.Count, "test setup: SpawnExact didn't spawn the test Rusher");
                RobotEnemy testRusher = testRushList[0];
                testRusher.transform.position = RushTestPos + Vector3.up * rusherFoot;
                RobotOnEnableMethod.Invoke(testRusher, null);

                var otherRushList = spawner.SpawnExact(EnemyKind.Rusher, 1);
                Assert.AreEqual(1, otherRushList.Count, "test setup: SpawnExact didn't spawn the pulling Rusher");
                RobotEnemy otherRusher = otherRushList[0];
                otherRusher.transform.position = RushOtherPos + Vector3.up * rusherFoot;
                RobotOnEnableMethod.Invoke(otherRusher, null);

                var secondProjectorList = spawner.SpawnExact(EnemyKind.Projector, 1);
                Assert.AreEqual(1, secondProjectorList.Count, "test setup: SpawnExact didn't spawn the second Projector");
                RobotEnemy secondProjector = secondProjectorList[0];
                secondProjector.transform.position = SecondProjectorPos + Vector3.up * projectorFoot;
                RobotOnEnableMethod.Invoke(secondProjector, null);

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.AreEqual(4, RobotEnemy.ActiveCount,
                    "test setup: both Projectors and both Rushers must be registered in RobotEnemy.Active");

                // One tick is enough: TickProjectorDome's own retarget timer starts at 0, so it
                // recomputes the dome centroid on the very first Tick() call. testRusher sits at
                // RigOrigin+(2,0,0) and otherRusher at RigOrigin+(-2,0,0) — both within the dome's
                // own 8 m search radius of the Projector at RigOrigin — so the raw centroid lands
                // exactly on RigOrigin, 2 m from testRusher, matching the ticket's own AC1 wording.
                projector.Tick(Dt);

                var hit = new DamageInfo(TestDamage, testRusher.transform.position, Vector3.down, Team.Player);

                float beforeInDome = testRusher.HealthCurrent;
                testRusher.TakeDamage(hit);
                Assert.AreEqual(0f, beforeInDome - testRusher.HealthCurrent, 1e-3f,
                    "a robot within the dome's own radius must take 0 damage from a player-side hit");

                float beforeSecondProjector = secondProjector.HealthCurrent;
                secondProjector.TakeDamage(hit);
                Assert.AreEqual(TestDamage, beforeSecondProjector - secondProjector.HealthCurrent, 1e-3f,
                    "a Projector standing inside another dome is never itself protected");

                // Kill the dome's own Projector — its dome must drop at once.
                projector.TakeDamage(new DamageInfo(projector.HealthCurrent + 10f, projector.transform.position,
                    Vector3.down, Team.Player));
                Assert.IsFalse(projector.IsAlive, "test setup: the lethal hit must actually kill the Projector");

                float beforeAfterDeath = testRusher.HealthCurrent;
                testRusher.TakeDamage(hit);
                Assert.AreEqual(TestDamage, beforeAfterDeath - testRusher.HealthCurrent, 1e-3f,
                    "once the Projector dies its dome must drop at once — the same hit now deals full damage");
            }
            finally
            {
                // Destroys every spawned robot too — a robot's gameObject must never be destroyed
                // separately (same note MV1279ShieldProjectorHangBackTests' own cleanup carries).
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (groundGo != null) Object.DestroyImmediate(groundGo);
            }
        }
    }
}
