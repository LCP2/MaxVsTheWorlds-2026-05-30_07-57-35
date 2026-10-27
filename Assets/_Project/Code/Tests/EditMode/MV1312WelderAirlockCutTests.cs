using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1312 — "Mothership twist: Welders cut airlocks open on Max". Fails on base commit (before
    /// this ticket's change): <c>RobotEnemy.TickWelderRepair</c> there does nothing once no cradle is
    /// in range, so the panel never blows regardless of Max's distance (the same "runtime failure, not
    /// a compile failure" footing <c>MV1244WelderRepairTests</c> itself established for the Welder's
    /// own repair tick). Tier 2 (resolved values): every assertion reads the panel's own resolved
    /// <see cref="AirlockPanel.State"/>, never an authored constant.
    ///
    /// Drives the real, private <c>RobotEnemy.TickWelderRepair(float)</c> directly via reflection, the
    /// same idiom <c>MV1305AirlockPanelTests</c> already established for
    /// <c>RobotEnemy.ApplyAirlockPull</c> — never the whole <c>Tick</c>/state-machine pipeline, whose
    /// own gravity/fall-recovery (MV-952) has nothing to stand on in this synthetic scene (no floor
    /// collider) and would otherwise teleport the robot back toward the origin mid-run.
    ///
    /// ONE new EditMode test (MV-465 Rule 1) — both halves of AC1 (blows at 8 m, never triggers at
    /// 14 m) are one continuous scenario across two independent Welder/airlock/Max trios, not two
    /// tests.
    /// </summary>
    public sealed class MV1312WelderAirlockCutTests
    {
        private static readonly MethodInfo CreateInstanceMethod = typeof(EnemySpawner).GetMethod(
            "CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo TickWelderRepairMethod = typeof(RobotEnemy).GetMethod(
            "TickWelderRepair", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float Dt = 0.02f;
        private const int MaxTicks = 300; // up to 6 s — well past the ticket's own 2 s beam

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            AirlockPanel.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            AirlockPanel.ResetRegistry();
        }

        [Test]
        public void CradlelessWelder_BlowsAReadyAirlock_WhenMaxIsWithin12m_ButNeverAt14m()
        {
            Assert.IsNotNull(TickWelderRepairMethod, "RobotEnemy.TickWelderRepair went missing");

            // --- Max 8 m away: a Welder 2 m from a READY airlock must blow it at 2.0 +/- 0.2 s ---
            var nearOrigin = new Vector3(-71234f, 0f, 48213f); // distinctive far-off origin
            GameObject panelGoNear = new GameObject("MV1312 Airlock Near") { transform = { position = nearOrigin } };
            AirlockPanel panelNear = panelGoNear.AddComponent<AirlockPanel>();
            panelNear.Build(); // AddComponent's own Awake never runs outside Play mode

            GameObject playerGoNear = new GameObject("MV1312 Max Near") { tag = "Player" };
            playerGoNear.transform.position = nearOrigin + new Vector3(8f, 0f, 0f);

            RobotEnemy welderNear = SpawnWelder(nearOrigin + new Vector3(0f, 0f, 2f));
            Assert.AreEqual(RobotEnemy.State.Chase, welderNear.Current,
                "setup failure: a freshly activated Welder must already be awake");

            float elapsedNear = 0f;
            bool blew = false;
            for (int i = 0; i < MaxTicks && !blew; i++)
            {
                TickWelderRepairMethod.Invoke(welderNear, new object[] { Dt });
                panelNear.Tick(Dt);
                elapsedNear += Dt;
                if (panelNear.State == AirlockPanel.PanelState.Blowing) blew = true;
            }

            Assert.IsTrue(blew, "a Welder 2 m from a READY airlock must blow it once Max is within 12 m");
            Assert.AreEqual(2.0f, elapsedNear, 0.2f,
                $"must blow 2.0 +/- 0.2 s after starting the beam, actually blew at {elapsedNear:0.00}s");

            Object.DestroyImmediate(welderNear.gameObject);
            Object.DestroyImmediate(playerGoNear);
            Object.DestroyImmediate(panelGoNear);
            RobotEnemy.ResetRegistry();
            AirlockPanel.ResetRegistry();

            // --- Max 14 m away (outside the 12 m trigger): the same READY airlock must never blow ---
            var farOrigin = new Vector3(39120f, 0f, -82310f);
            GameObject panelGoFar = new GameObject("MV1312 Airlock Far") { transform = { position = farOrigin } };
            AirlockPanel panelFar = panelGoFar.AddComponent<AirlockPanel>();
            panelFar.Build();

            GameObject playerGoFar = new GameObject("MV1312 Max Far") { tag = "Player" };
            playerGoFar.transform.position = farOrigin + new Vector3(14f, 0f, 0f);

            RobotEnemy welderFar = SpawnWelder(farOrigin + new Vector3(0f, 0f, 2f));

            for (int i = 0; i < MaxTicks; i++)
            {
                TickWelderRepairMethod.Invoke(welderFar, new object[] { Dt });
                panelFar.Tick(Dt);
            }

            Assert.AreEqual(AirlockPanel.PanelState.Ready, panelFar.State,
                "a Welder must never cut a READY airlock when Max is 14 m away (outside the 12 m trigger)");

            Object.DestroyImmediate(welderFar.gameObject);
            Object.DestroyImmediate(playerGoFar);
            Object.DestroyImmediate(panelGoFar);
        }

        /// <summary>Drives the REAL, private <c>EnemySpawner.CreateInstance(in EnemyArchetype)</c> via
        /// reflection — the normal pool/factory path every in-game robot is built through (same
        /// technique <c>MV1244WelderRepairTests.SpawnWelder</c> already established). Activating it
        /// runs the real <c>OnEnable</c>/<c>ResetState</c>, landing it in a genuinely awake
        /// <c>State.Chase</c> and acquiring whatever "Player"-tagged object already exists in the scene
        /// as its target.</summary>
        private static RobotEnemy SpawnWelder(Vector3 position)
        {
            var spawnerGo = new GameObject("MV1312 test spawner");
            try
            {
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                Assert.IsNotNull(CreateInstanceMethod, "EnemySpawner.CreateInstance went missing");

                var e = (RobotEnemy)CreateInstanceMethod.Invoke(spawner, new object[] { EnemyArchetype.Welder });

                // Detach before the spawner is torn down below, or DestroyImmediate(spawnerGo) cascades
                // and destroys the very GameObject this method hands back (same note MV1244's own
                // SpawnWelder carries).
                e.transform.SetParent(null, worldPositionStays: false);
                e.transform.position = position;
                e.gameObject.SetActive(true);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)
                return e;
            }
            finally
            {
                Object.DestroyImmediate(spawnerGo);
            }
        }
    }
}
