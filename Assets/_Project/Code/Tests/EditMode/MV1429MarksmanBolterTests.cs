using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1429 — the City MARKSMAN Bolter. ONE test (MV-465 Rule 1), Tier 2 resolved values, driven through
    /// the real spawner and the robot's own public <see cref="RobotEnemy.Tick"/>: it closes from 20 m into
    /// 9-13 m, backs off from 5 m to at least 9 m, aims for 0.6 s (+-0.05) before a bolt exists, the bolt's
    /// resolved core is >= 0.3 m in a colour that is not the Bolter body colour, it does exactly 50 to an
    /// unshielded Max, and a World 2 Bolter under the same setup is not a marksman and grows no rifle. On the base commit <c>RobotEnemy.IsMarksman</c> does not exist, so this fails to compile.
    /// </summary>
    public sealed class MV1429MarksmanBolterTests
    {
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float Dt = 0.02f;
        private static readonly Vector3 Origin = new Vector3(-74123f, 0f, 56789f);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            CharacterControllerMotion.HasWarnedOversizedMove = true;
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static RobotEnemy SpawnBolter(EnemySpawner spawner, WorldConfig world, Vector3 at)
        {
            var list = spawner.SpawnExact(EnemyKind.Bolter, 1);
            Assert.AreEqual(1, list.Count, "test setup: SpawnExact didn't spawn the Bolter");
            RobotEnemy r = list[0];
            r.Apply(EnemyArchetype.For(EnemyKind.Bolter, world));
            r.transform.position = at + Vector3.up * EnemyArchetype.Of(EnemyKind.Bolter).SpawnHeight;
            OnEnableMethod.Invoke(r, null);
            Physics.SyncTransforms();
            return r;
        }

        private static void TickFor(RobotEnemy r, float seconds)
        {
            int n = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < n; i++) r.Tick(Dt);
        }

        [Test]
        public void CityBolter_HoldsNineToThirteen_AimsPointSixSeconds_FiresABrightFiftyDamageBolt_MV1429()
        {
            GameObject groundGo = null, maxGo = null, spawnerGo = null;
            RobotEnemy marksman = null, world2Bolter = null;
            LogAssert.ignoreFailingMessages = true;   // a fired bolt's Destroy is edit-mode-illegal (MV-1105)
            try
            {
                WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
                WorldConfig world2 = WorldLibrary.Load(WorldLibrary.World2);
                Assert.IsNotNull(city, "city_config.json failed to load");
                Assert.IsNotNull(world2, "world2_config.json failed to load");

                groundGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                groundGo.transform.position = new Vector3(Origin.x, -1f, Origin.z);
                groundGo.transform.localScale = new Vector3(200f, 2f, 200f);

                maxGo = new GameObject("MV1429 Max", typeof(CharacterController)) { tag = "Player" };
                var health = maxGo.AddComponent<PlayerHealth>();
                health.Initialize();
                spawnerGo = new GameObject("MV1429 Spawner");
                spawnerGo.transform.position = Origin;
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                // 1. Max 20 m away: after 3 s the marksman has closed into 9-13 m.
                marksman = SpawnBolter(spawner, city, Origin);
                Assert.IsTrue(marksman.IsMarksman, "the City bolter override stamps the marksman role");
                maxGo.transform.position = Origin + new Vector3(20f, 0f, 0f);
                Physics.SyncTransforms();
                TickFor(marksman, 9f);   // MV-1475: MEDIUM tier 0.93 m/s (was 3.5)
                float d1 = Flat(marksman.transform.position, maxGo.transform.position);
                Assert.That(d1, Is.InRange(9f, 13f), "20 m away: it closes into the 9-13 m band within 9 s");

                // 2. In the band with sight: aim line active 0.6 s +-0.05 before a bolt spawns.
                Assert.IsNotNull(marksman.Rifle, "the rifle is ticking");
                int aimTicks = 0;
                bool fired = false;
                for (int i = 0; i < 400 && !fired; i++)
                {
                    int shotsBefore = marksman.Rifle.Shots;
                    marksman.Tick(Dt);
                    if (marksman.Rifle.Shots > shotsBefore) fired = true;
                    else if (marksman.Rifle.IsAiming) aimTicks++;
                }
                Assert.IsTrue(fired, "a bolt was fired");
                Assert.That((aimTicks + 1) * Dt, Is.EqualTo(0.6f).Within(0.05f), "aim line seconds before the bolt");
                Assert.GreaterOrEqual(marksman.Rifle.AimLine.widthMultiplier, 0.06f, "aim line width");

                // 3. The bolt's resolved look: core >= 0.3 m, not the Bolter body colour.
                BolterBolt bolt = marksman.Rifle.LastBolt;
                Assert.IsNotNull(bolt, "the bolt exists");
                HostileShotLook.Resolved look = bolt.ResolvedLook();
                Assert.GreaterOrEqual(look.CoreDiameter, 0.3f, "resolved core diameter");
                Color body = CharacterSkin.BaseColorFor(CharacterSkin.RoleFor(EnemyKind.Bolter));
                float far = Mathf.Abs(look.Color.r - body.r) + Mathf.Abs(look.Color.g - body.g) + Mathf.Abs(look.Color.b - body.b);
                Assert.Greater(far, 0.3f, $"bolt colour {look.Color} must not be the Bolter body colour {body}");

                // 4. It does exactly 50 to an unshielded Max.
                float before = health.Current;
                for (int i = 0; i < 200 && bolt != null; i++) bolt.Tick(Dt);
                Assert.AreEqual(50f, before - health.Current, 0.01f, "the bolt does 50 to Max");

                // 5. Max at 5 m: after 3 s it is at least 9 m away.
                maxGo.transform.position = marksman.transform.position + new Vector3(5f, 0f, 0f);
                Physics.SyncTransforms();
                TickFor(marksman, 6f);   // MV-1475: MEDIUM tier
                Assert.GreaterOrEqual(Flat(marksman.transform.position, maxGo.transform.position), 9f,
                    "5 m away: it backs off to at least 9 m within 6 s");

                // 6. A World 2 Bolter under the same setup is not a marksman and keeps today's behaviour
                //    (no rifle, no marksman role).
                maxGo.transform.position = Origin + new Vector3(0f, 0f, 60f) + new Vector3(20f, 0f, 0f);
                Physics.SyncTransforms();
                world2Bolter = SpawnBolter(spawner, world2, Origin + new Vector3(0f, 0f, 60f));
                Assert.IsFalse(world2Bolter.IsMarksman, "a World 2 Bolter is not a marksman");
                TickFor(world2Bolter, 3f);
                Assert.IsNull(world2Bolter.Rifle, "a World 2 Bolter has no rifle");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                marksman?.Rifle?.Dispose();
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (groundGo != null) Object.DestroyImmediate(groundGo);
            }
        }
    }
}
