using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-984 — Lee's TestFlight v0.10.0 observation (World 1, 2026-09-27): an awake robot moving toward
    /// Max and firing takes no damage; the hose passes straight through it. MV-973 (f0db02f) set
    /// <c>Physics.simulationMode = SimulationMode.Script</c> in <c>Bootstrap.Awake</c> and drove a
    /// per-frame <c>PhysicsSimulationDriver.Tick</c> (only <c>Physics.SyncTransforms()</c>, never
    /// <c>Physics.Simulate</c>) instead of the engine's own automatic fixed-step simulation. In a real
    /// Play session that left every robot's ONLY collider (its <see cref="CharacterController"/>, per
    /// MV-966) unreachable by a physics query at its current, walked position. This fix reverts to
    /// automatic <c>SimulationMode.FixedUpdate</c> (already the on-disk default in
    /// <c>ProjectSettings/DynamicsManager.asset</c>) and removes the manual driver outright.
    ///
    /// Confirmed empirically that this is exactly the class of bug this project's own EditMode harness
    /// cannot observe end to end (the same gap <c>MV535RobotBodyOrderingTests</c>' own class doc
    /// documents for a different regression): reproducing MV-973's shipped configuration by hand inside
    /// an EditMode test — setting <c>Physics.simulationMode = SimulationMode.Script</c> and calling only
    /// <c>Physics.SyncTransforms()</c> per frame, bypassing <c>Bootstrap.Awake</c> (which no-ops outside
    /// Play mode) — still finds a moved <see cref="CharacterController"/> at its current position; the
    /// staleness only bites from the real engine loop's own per-frame scheduling, which nothing outside
    /// Play mode can drive. So, same two-part shape as MV535: Part 1 is a source-shape guard on the real
    /// file — THIS is what actually goes red on the commit before this fix (Bootstrap still installs and
    /// ticks <c>PhysicsSimulationDriver</c>) and green after (the driver is gone, automatic simulation is
    /// restored) — and what keeps failing if a future edit reintroduces script-mode physics. Part 2 is
    /// behavioural evidence, through the real spawn/move/fire path, that an awake robot moved by
    /// <c>CharacterController.Move</c> is still hittable at its new position; it cannot go red on its own
    /// (see above), so it is not the regression guard, only confirmation the fix's own mechanism works.
    /// </summary>
    public sealed class MV984AwakeRobotsTakeDamageTests
    {
        private static readonly MethodInfo CreateInstanceMethod = typeof(EnemySpawner).GetMethod(
            "CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo FireTickMethod =
            typeof(WaterBlaster).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo BlasterAwakeMethod =
            typeof(WaterBlaster).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown() => RobotEnemy.ResetRegistry();

        [Test]
        public void BootstrapNoLongerDrivesScriptModePhysics_AndAwakeRobotsStayHittableAfterMoving()
        {
            // ---- Part 1: source-shape guard on the real file — the actual regression guard ------------
            // Fails on the commit before this fix (f0db02f still wired in): Bootstrap.Awake calls
            // PhysicsSimulationDriver.Install() and Bootstrap.Update calls PhysicsSimulationDriver.Tick().
            string bootstrapPath = Path.Combine(
                Application.dataPath, "_Project", "Code", "Runtime", "Core", "Bootstrap.cs");
            Assert.IsTrue(File.Exists(bootstrapPath), $"source file not found: {bootstrapPath}");
            string bootstrapSource = File.ReadAllText(bootstrapPath);

            Assert.IsFalse(bootstrapSource.Contains("PhysicsSimulationDriver"),
                "MV-984: Bootstrap must not install or tick a script-mode physics driver — restore " +
                "automatic SimulationMode.FixedUpdate simulation (already the on-disk default in " +
                "ProjectSettings/DynamicsManager.asset) instead of MV-973's manual, sync-only driver, " +
                "which left every CharacterController's scene-query pose stuck wherever it was created.");

            string driverPath = Path.Combine(
                Application.dataPath, "_Project", "Code", "Runtime", "Core", "PhysicsSimulationDriver.cs");
            Assert.IsFalse(File.Exists(driverPath),
                "MV-984: PhysicsSimulationDriver.cs must be removed, not merely left unused — the " +
                "ticket's own 'do not re-raise' rules out keeping the script-mode driver at all.");

            // ---- Part 2: behavioural evidence through the real spawn/move/fire path -------------------
            // Cannot go red/green across the fix on its own (see class doc comment) — this only confirms
            // the mechanism Part 1 guards actually produces a hittable robot.
            RobotEnemy bolter = SpawnViaPool(EnemyArchetype.Bolter, new Vector3(0f, 1f, 0f));
            RobotEnemy rusher = SpawnViaPool(EnemyArchetype.Rusher, new Vector3(10f, 1f, 0f));
            try
            {
                MoveAtLeastFiveMetres(bolter);
                MoveAtLeastFiveMetres(rusher);

                AssertTakesWaterDamageAtCurrentPosition(bolter);
                AssertTakesWaterDamageAtCurrentPosition(rusher);
            }
            finally
            {
                Object.DestroyImmediate(bolter.gameObject);
                Object.DestroyImmediate(rusher.gameObject);
            }
        }

        private static void MoveAtLeastFiveMetres(RobotEnemy robot)
        {
            const int frames = 30;
            Vector3 step = new Vector3(0f, 0f, 5.2f / frames); // 5.2 m over 30 frames — clears the 5 m floor

            CharacterController cc = robot.GetComponent<CharacterController>();
            Assert.IsNotNull(cc, $"{robot.Kind}'s spawn-path instance has no CharacterController");

            for (int f = 0; f < frames; f++) MaxWorlds.Core.CharacterControllerMotion.SafeMove(cc, step);

            Assert.GreaterOrEqual(robot.transform.position.z, 5f,
                $"setup failure: {robot.Kind} must actually have moved 5+m for this test to mean anything");
        }

        private static void AssertTakesWaterDamageAtCurrentPosition(RobotEnemy robot)
        {
            float healthBefore = robot.HealthCurrent;
            Assert.Greater(healthBefore, 0f, $"setup failure: {robot.Kind} must start alive");

            Vector3 targetPos = robot.transform.position;
            Vector3 blasterPos = targetPos + new Vector3(0f, 0f, -2f);
            var blasterGo = new GameObject($"MV984 Water Blaster ({robot.Kind})");
            try
            {
                blasterGo.transform.position = blasterPos;
                blasterGo.transform.rotation =
                    Quaternion.LookRotation((targetPos - blasterPos).normalized, Vector3.up);
                var blaster = blasterGo.AddComponent<WaterBlaster>();
                BlasterAwakeMethod.Invoke(blaster, null); // AddComponent doesn't reliably run Awake outside Play mode
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                // 0.5s of stream at the authored 0.1s tick interval (WaterBlaster.FireInterval).
                for (int i = 0; i < 5; i++) FireTickMethod.Invoke(blaster, null);
            }
            finally
            {
                Object.DestroyImmediate(blasterGo);
            }

            Assert.Less(robot.HealthCurrent, healthBefore,
                $"MV-984: Max's water blaster passed straight through {robot.Kind} at its NEW, " +
                "CharacterController.Move-d position");
        }

        /// <summary>Drives the REAL, private <c>EnemySpawner.CreateInstance(in EnemyArchetype)</c> via
        /// reflection — the normal pool/factory path every in-game robot is built through, not a
        /// hand-rolled stand-in (same technique <c>MV535RobotBodyOrderingTests</c>/
        /// <c>MV746ReefBodyTests</c> use). <c>CreateInstance</c> itself leaves the instance inactive
        /// (pooling contract), so this activates it (running the real <c>OnEnable</c>/<c>ResetState</c>,
        /// putting it in a genuinely awake Chase state) at the given world position.</summary>
        private static RobotEnemy SpawnViaPool(EnemyArchetype archetype, Vector3 startPos)
        {
            var spawnerGo = new GameObject("MV984 test spawner");
            try
            {
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                Assert.IsNotNull(CreateInstanceMethod, "EnemySpawner.CreateInstance went missing");

                LogAssert.ignoreFailingMessages = true;
                RobotEnemy e;
                try { e = (RobotEnemy)CreateInstanceMethod.Invoke(spawner, new object[] { archetype }); }
                finally { LogAssert.ignoreFailingMessages = false; }

                // Detach before the spawner is torn down below, or DestroyImmediate(spawnerGo) cascades
                // and destroys the very GameObject this method hands back (MV746ReefBodyTests' own note).
                e.transform.SetParent(null, worldPositionStays: false);
                e.transform.position = startPos;
                e.gameObject.SetActive(true);
                Physics.SyncTransforms();
                return e;
            }
            finally
            {
                Object.DestroyImmediate(spawnerGo);
            }
        }
    }
}
