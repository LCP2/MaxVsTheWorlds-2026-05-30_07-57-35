using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1240 (ticket AC1): "over 8.0 s of explicit-dt ticks, a robot and Max each standing in a vent
    /// take exactly 40 damage, and standing 0.2 m outside it they take none." Fails to COMPILE on the
    /// pre-fix base commit (1763abb): <c>MapVentDamageRunner</c>/<c>MapVentDamage</c> do not exist there
    /// (CS0246) before a single assertion runs.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), same idiom as <c>MV838SludgeDamageTests</c>:
    /// a hand-built <see cref="MapData"/> carrying one authored vent rect (2.5x2.5, centred at the
    /// origin), injected into <see cref="EnemyNavigation.Map"/> through a live <see cref="BackyardPath"/>
    /// stub, driving the real <see cref="MapVentDamageRunner.TickVentDamage"/> path.
    /// </summary>
    public sealed class MV1240PlasmaVentDamageTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [Test]
        public void TwoCyclesOver8Seconds_HitsMaxAndARobotStandingInTheVentFor40_MissesBothJust0Point2mOutside()
        {
            // PlayerHealth.Initialize() -> DamageFeedbackVfx.Initialize() logs "[VfxMaterials] particle
            // shader: ..." once (VfxMaterials.get_ParticleShader) — a plain Debug.Log, not a failure, but
            // Unity's strict per-test log check fails the test on ANY unexpected message. Same idiom
            // MV838SludgeDamageTests uses for the same reason.
            LogAssert.ignoreFailingMessages = true;

            GameObject pathGo = null, playerGo = null, rusherGo = null, runnerGo = null;
            try
            {
                var map = new MapData
                {
                    entities = new[]
                    {
                        new MapEntity { id = "a22_vent1", kind = "vent", x = 0f, z = 0f, width = 2.5f, depth = 2.5f },
                    },
                };

                pathGo = new GameObject("MV1240-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                // Vent half-extent is 1.25 m (2.5 m square) either side of its centre; 0.2 m outside the
                // edge along X is x = 1.25 + 0.2.
                var insidePoint = new Vector3(0f, 0f, 0f);
                var outsidePoint = new Vector3(1.45f, 0f, 0f);

                playerGo = new GameObject("Player") { tag = "Player" };
                playerGo.AddComponent<CharacterController>();
                playerGo.AddComponent<PlayerController>();
                var health = playerGo.AddComponent<PlayerHealth>();
                health.Initialize();
                playerGo.transform.position = outsidePoint;

                rusherGo = new GameObject("Rusher");
                var rusher = rusherGo.AddComponent<RobotEnemy>();
                rusher.Apply(EnemyArchetype.Rusher);
                // Awake/OnEnable don't run outside Play mode (MV1006SentinelFiresThroughBarriersTests'
                // own idiom) — RobotEnemy.Active is seeded by OnEnable alone, so it must be invoked
                // directly or the runner's robot loop below would silently see no robots at all.
                RobotOnEnableMethod.Invoke(rusher, null);
                rusherGo.transform.position = outsidePoint;

                const float step = 0.1f;
                const float simulatedSeconds = 8.0f; // two full 4.0s cycles (ticket table)

                // === Standing 0.2 m outside the vent: two full cycles fire, neither hits. ===
                runnerGo = new GameObject("MV1240 vent runner (outside)");
                var outsideRunner = runnerGo.AddComponent<MapVentDamageRunner>();
                float playerHealthOutside = health.Current;
                float rusherHealthOutside = rusher.HealthCurrent;
                for (float t = 0f; t < simulatedSeconds; t += step) outsideRunner.TickVentDamage(step);

                Assert.AreEqual(playerHealthOutside, health.Current, 0.001f,
                    "MV-1240: Max standing 0.2m outside a vent must take no damage over two full cycles");
                Assert.AreEqual(rusherHealthOutside, rusher.HealthCurrent, 0.001f,
                    "MV-1240: a robot standing 0.2m outside a vent must take no damage over two full cycles");

                // === Standing inside the vent: two full cycles fire, both hit, for exactly 20 each. ===
                UnityEngine.Object.DestroyImmediate(runnerGo);
                runnerGo = new GameObject("MV1240 vent runner (inside)");
                var insideRunner = runnerGo.AddComponent<MapVentDamageRunner>();

                health.Revive();
                playerGo.transform.position = insidePoint;
                rusherGo.transform.position = insidePoint;
                float playerHealthBeforeInside = health.Current;
                float rusherHealthBeforeInside = rusher.HealthCurrent;
                for (float t = 0f; t < simulatedSeconds; t += step) insideRunner.TickVentDamage(step);

                Assert.AreEqual(40f, playerHealthBeforeInside - health.Current, 0.001f,
                    "MV-1240: Max standing in a vent for 8.0s (two 4.0s cycles) must take exactly 40 damage");
                Assert.AreEqual(40f, rusherHealthBeforeInside - rusher.HealthCurrent, 0.001f,
                    "MV-1240: a robot standing in a vent for 8.0s (two 4.0s cycles) must take exactly 40 damage");
            }
            finally
            {
                if (rusherGo != null) UnityEngine.Object.DestroyImmediate(rusherGo);
                if (runnerGo != null) UnityEngine.Object.DestroyImmediate(runnerGo);
                if (playerGo != null) UnityEngine.Object.DestroyImmediate(playerGo);
                if (pathGo != null) UnityEngine.Object.DestroyImmediate(pathGo);
            }
        }
    }
}
