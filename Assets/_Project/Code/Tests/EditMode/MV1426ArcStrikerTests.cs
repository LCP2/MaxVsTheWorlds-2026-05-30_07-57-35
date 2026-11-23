using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1426 — the ARC STRIKER: marks a 2.5 m circle at Max's position, 1.0 s later a lightning column
    /// hits it for 60; (MV-1433 removed its retreat; see MV1433ArcStrikerHuntsTests). ONE test (MV-465 Rule 1), Tier 2 resolved
    /// values only, driven through the real spawner/pool and the robot's own public
    /// <see cref="RobotEnemy.Tick"/> (same shape as MV1280's Jammer test). On the base commit
    /// <c>EnemyKind.ArcStriker</c> does not exist, so this fails to compile there.
    /// </summary>
    public sealed class MV1426ArcStrikerTests
    {
        private sealed class StubMax : MonoBehaviour, IDamageable
        {
            public float Damage;
            public int Hits;
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) { Damage += info.Amount; Hits++; }
        }

        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float Dt = 0.02f;
        private static readonly Vector3 RigOrigin = new Vector3(-74123f, 0f, 56789f);

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
        }

        private static bool TickUntil(RobotEnemy r, System.Func<bool> done, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                r.Tick(Dt);
                if (done()) return true;
            }
            return false;
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        [Test]
        public void ArcStriker_MarksUnderMax_StrikesTheCircleForSixtyOnly()
        {
            GameObject groundGo = null, maxGo = null, spawnerGo = null;
            RobotEnemy striker = null;
            try
            {
                groundGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                groundGo.transform.position = new Vector3(RigOrigin.x, -1f, RigOrigin.z);
                groundGo.transform.localScale = new Vector3(200f, 2f, 200f);

                maxGo = new GameObject("MV1426 Max") { tag = "Player" };
                var max = maxGo.AddComponent<StubMax>();
                spawnerGo = new GameObject("MV1426 Spawner");
                spawnerGo.transform.position = RigOrigin;
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                var list = spawner.SpawnExact(EnemyKind.ArcStriker, 1);
                Assert.AreEqual(1, list.Count, "test setup: SpawnExact didn't spawn the ArcStriker");
                striker = list[0];
                Vector3 strikerStart = RigOrigin + Vector3.up * EnemyArchetype.Of(EnemyKind.ArcStriker).SpawnHeight;
                striker.transform.position = strikerStart;
                RobotOnEnableMethod.Invoke(striker, null);
                maxGo.transform.position = RigOrigin + new Vector3(12f, 0f, 0f);
                Physics.SyncTransforms();
                Assert.AreEqual(RobotEnemy.State.Chase, striker.Current, "setup: a fresh ArcStriker is awake");

                // 1. Max 12 m away in sight: a mark appears at Max's position within one tick of ready.
                Vector3 markedAt = maxGo.transform.position;
                striker.Tick(Dt);
                if (!striker.ArcStrike.IsMarking) striker.Tick(Dt);
                Assert.IsTrue(striker.ArcStrike != null && striker.ArcStrike.IsMarking, "a mark appears within a tick of ready");
                Assert.That(Flat(striker.ArcStrike.MarkCenter, markedAt), Is.LessThan(0.01f), "mark is centred on Max");

                // 5. The circle's resolved diameter.
                Assert.That(striker.ArcStrike.MarkRenderer.bounds.size.x, Is.EqualTo(2.5f).Within(0.05f), "resolved circle diameter");

                // 2. Max steps 2 m during the mark: the circle stays put and the strike does 0 to him.
                maxGo.transform.position += new Vector3(2f, 0f, 0f);
                Physics.SyncTransforms();
                Assert.IsTrue(TickUntil(striker, () => striker.ArcStrike.Strikes == 1, 80), "the first strike lands");
                Assert.That(Flat(striker.ArcStrike.MarkCenter, markedAt), Is.LessThan(0.01f), "the circle did not follow Max");
                Assert.AreEqual(0f, max.Damage, "Max had left the circle: 0 damage");

                // 3. Standing in the circle: exactly 60.
                Assert.IsTrue(TickUntil(striker, () => striker.ArcStrike.IsMarking, 400), "the 4 s cooldown readies a second mark");
                Assert.IsTrue(TickUntil(striker, () => striker.ArcStrike.Strikes == 2, 80), "the second strike lands");
                Assert.AreEqual(60f, max.Damage, "Max inside the circle takes exactly 60");
            }
            finally
            {
                striker?.ArcStrike?.Dispose();
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (groundGo != null) Object.DestroyImmediate(groundGo);
            }
        }
    }
}
