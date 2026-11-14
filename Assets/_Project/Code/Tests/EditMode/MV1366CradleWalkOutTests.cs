using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1366 — the Assembly Cradle's finished robot spawns in the bay where the preview body stood
    /// and walks to the out pad over one second, instead of popping in on the pad. Tier 2: real
    /// transforms read back from the spawned robot. ONE test (MV-465 Rule 1).
    /// </summary>
    // Guards MV-1366
    public sealed class MV1366CradleWalkOutTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(-81234f, 0f, 45678f);
        private GameObject _cradleGo;

        [SetUp] public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            if (_cradleGo != null) Object.DestroyImmediate(_cradleGo);
            RobotEnemy.ResetRegistry();
        }

        [Test]
        public void FinishedRobot_SpawnsAtPreviewBody_ThenWalksToOutPadInOneSecond()
        {
            _cradleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cradleGo.transform.position = RigOrigin;
            var cradle = _cradleGo.AddComponent<AssemblyCradle>();
            cradle.Build();
            var spawner = _cradleGo.GetComponent<EnemySpawner>();
            spawner.ConfigureAreaComposition(new WorldComposition { rusher = 1 });
            cradle.SetMaxInArea(true);

            cradle.Tick(1.0f);
            cradle.Tick(AssemblyCradle.BuildSeconds - 1.0f - 0.01f);
            Vector3 previewPos = cradle.DoneStage.position; // the preview body's last position
            Assert.AreEqual(0, spawner.LiveCount, "test setup: robot must not exist before Done");
            cradle.Tick(0.02f);
            Assert.AreEqual(1, spawner.LiveCount, "test setup: exactly one robot at Done");

            RobotEnemy robot = null;
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                if (r.isActiveAndEnabled && Vector3.Distance(r.transform.position, RigOrigin) < 20f) robot = r;
            Assert.IsNotNull(robot, "test setup: the spawned robot must be findable");

            Assert.AreEqual(0f, Vector3.Distance(previewPos, robot.transform.position), 0.1f,
                "the robot must spawn within 0.1 m of the preview body's last position, not on the pad");

            Vector3 prev = robot.transform.position;
            float maxStep = 0f;
            for (int i = 0; i < 10; i++)
            {
                robot.Tick(0.1f);
                maxStep = Mathf.Max(maxStep, Vector3.Distance(prev, robot.transform.position));
                prev = robot.transform.position;
            }

            Vector3 exit = FactoryMouth.ExitPoint(RigOrigin, _cradleGo.transform.forward, 1.0f, RigOrigin.y);
            Vector3 pad = FactoryMouth.MusterPoint(exit, _cradleGo.transform.forward, 0);
            Assert.AreEqual(0f, Vector3.Distance(pad, robot.transform.position), 0.2f,
                "after 1.0 s the robot must be within 0.2 m of the out-pad centre");
            Assert.LessOrEqual(maxStep, 0.3f, "no single-frame step above 0.3 m across the walk-out");
        }
    }
}
