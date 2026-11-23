using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1433 (the one new test, Rule 1): the City Arc Striker hunts Max (advances from far or out of
    /// sight, holds and faces him inside 14 m in sight, never retreats), a4's Arc Striker has no post,
    /// and the City's ranged kinds never roll Shielded. Tier 2 resolved values only.
    /// </summary>
    public sealed class MV1433ArcStrikerHuntsTests
    {
        private sealed class StubMax : MonoBehaviour, IDamageable
        {
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) { }
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

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        [Test]
        public void ArcStriker_AdvancesFromFar_HoldsInRange_NoPost_AndCityRangedNeverShielded()
        {
            GameObject groundGo = null, maxGo = null, spawnerGo = null;
            RobotEnemy striker = null;
            try
            {
                groundGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                groundGo.transform.position = new Vector3(RigOrigin.x, -1f, RigOrigin.z);
                groundGo.transform.localScale = new Vector3(200f, 2f, 200f);
                maxGo = new GameObject("MV1433 Max") { tag = "Player" };
                maxGo.AddComponent<StubMax>();
                spawnerGo = new GameObject("MV1433 Spawner");
                spawnerGo.transform.position = RigOrigin;
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                striker = spawner.SpawnExact(EnemyKind.ArcStriker, 1)[0];
                striker.transform.position = RigOrigin + Vector3.up * EnemyArchetype.Of(EnemyKind.ArcStriker).SpawnHeight;
                RobotOnEnableMethod.Invoke(striker, null);

                // 1. Max 20 m away (past its 14 m hold): 5 s of ticks closes at least 5 m.
                maxGo.transform.position = RigOrigin + new Vector3(20f, 0f, 0f);
                Physics.SyncTransforms();
                float far0 = Flat(striker.transform.position, maxGo.transform.position);
                for (int i = 0; i < Mathf.RoundToInt(5f / Dt); i++) striker.Tick(Dt);
                float far1 = Flat(striker.transform.position, maxGo.transform.position);
                Assert.That(far0 - far1, Is.GreaterThanOrEqualTo(5f), "advancing on Max from 20 m");

                // 2. Max 5 m away in sight: 2 s later it has not backed off by more than 0.2 m.
                maxGo.transform.position = striker.transform.position + new Vector3(5f, 0f, 0f);
                Physics.SyncTransforms();
                float near0 = Flat(striker.transform.position, maxGo.transform.position);
                for (int i = 0; i < Mathf.RoundToInt(2f / Dt); i++) striker.Tick(Dt);
                float near1 = Flat(striker.transform.position, maxGo.transform.position);
                Assert.That(near1, Is.LessThanOrEqualTo(near0 + 0.2f), "never retreats from Max at 5 m");
            }
            finally
            {
                striker?.ArcStrike?.Dispose();
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (groundGo != null) Object.DestroyImmediate(groundGo);
            }

            // 3. a4's Arc Striker carries no post.
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            WorldArea a4 = city.areas.First(a => a.id == "a4");
            WorldGarrisonEntry entry = a4.garrison.First(g => g.kind == "arcstriker");
            Assert.IsFalse(entry.post, "a4's arcstriker is still pinned to a post");

            // 4. City elites: ranged kinds never Shielded; a Rusher still can.
            float chance = city.dials.eliteChancePct;
            int cap = city.dials.eliteMaxPerArea;
            city.dials.eliteChancePct = 100f;
            city.dials.eliteMaxPerArea = 100000;
            try
            {
                foreach (EnemyKind k in new[] { EnemyKind.Bolter, EnemyKind.ArcStriker, EnemyKind.Gunner, EnemyKind.Launcher, EnemyKind.Turret })
                    for (int i = 0; i < 200; i++)
                        Assert.AreNotEqual(EliteAffix.Shielded, EliteRoll.RollAffixForced(city.dials, k), k + " roll " + i);
                int shielded = 0;
                for (int i = 0; i < 200; i++)
                    if (EliteRoll.RollAffixForced(city.dials, EnemyKind.Rusher) == EliteAffix.Shielded) shielded++;
                Assert.GreaterOrEqual(shielded, 1, "a City Rusher can still roll Shielded");
            }
            finally
            {
                city.dials.eliteChancePct = chance;
                city.dials.eliteMaxPerArea = cap;
            }
        }
    }
}
