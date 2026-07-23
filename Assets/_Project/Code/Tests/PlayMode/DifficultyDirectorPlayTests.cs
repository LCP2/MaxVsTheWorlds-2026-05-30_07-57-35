using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The Invasion Level wired into the real game (YT-181): the runner actually ticks the clock and
    /// reacts to a factory dying, and the spawner actually reads the result back into its cadence and
    /// its robots' toughness. <see cref="DifficultyDirectorTests"/> (EditMode) covers the curve maths
    /// in isolation; this proves the wiring.
    /// </summary>
    public sealed class DifficultyDirectorPlayTests
    {
        private GameObject _runnerGo;
        private GameObject _hutch;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            DifficultyDirector.Reset();

            // The runner self-installs once per test session (AfterSceneLoad), which does not repeat
            // between fixtures that share a scene. Take ownership of exactly one instance here, same
            // as SettingsPanelPlayTests does for the settings panel, so a fixture never double-counts
            // a signal through two subscribed runners.
            foreach (var r in Object.FindObjectsByType<DifficultyDirectorRunner>(FindObjectsSortMode.None))
                Object.DestroyImmediate(r.gameObject);
            _runnerGo = new GameObject("DifficultyDirectorRunner Test");
            _runnerGo.AddComponent<DifficultyDirectorRunner>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_runnerGo != null) Object.Destroy(_runnerGo);
            if (_hutch != null) Object.Destroy(_hutch);
            DevTuning.Reset();
            DifficultyDirector.Reset();
        }

        private static IEnumerator Run(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        [UnityTest]
        public IEnumerator TheRunnerTicksTheClockEveryFrame()
        {
            Assert.AreEqual(0f, DifficultyDirector.Elapsed, 1e-4);
            yield return Run(0.5f);
            Assert.Greater(DifficultyDirector.Elapsed, 0f,
                "the Invasion Level clock did not advance — nothing is ticking DifficultyDirector");
        }

        [UnityTest]
        public IEnumerator AFactoryDeathBumpsTheInvasionLevel_ThroughTheRealSignal()
        {
            DevTuning.EscalationRate = 0f; // isolate the shed bump from the time-based climb

            Assert.AreEqual(0, DifficultyDirector.ShedsDestroyed);
            HudSignals.EmitFactoryDestroyed(Vector3.zero); // exactly what MowerHutch.OnDestroyed emits
            yield return null;

            Assert.AreEqual(1, DifficultyDirector.ShedsDestroyed,
                "a real FactoryDestroyed signal did not reach the DifficultyDirector");
            Assert.Greater(DifficultyDirector.Level, 0f,
                "the shed kill did not raise the Invasion Level");
        }

        // --- the spawner actually reads the result back ---

        private GameObject NewHutch(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Mower Hutch";
            go.transform.position = at;
            go.transform.localScale = new Vector3(3f, 2f, 3f);
            go.AddComponent<MaxWorlds.Factories.MowerHutch>();
            return go;
        }

        private static void ForceSpawn(EnemySpawner spawner) =>
            typeof(EnemySpawner)
                .GetMethod("SpawnOne", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(spawner, null);

        [UnityTest]
        public IEnumerator AFullyEscalatedRun_SpawnsFasterThanARunAtItsStart()
        {
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationRate = 0f;
            DevTuning.EscalationPerShedBump = 1f;
            DevTuning.EscalationMax = 1f;

            _hutch = NewHutch(new Vector3(0f, 1f, 15f));
            var spawner = _hutch.GetComponent<EnemySpawner>();
            yield return null;

            float atStart = spawner.CurrentInterval;

            DifficultyDirector.ReportShedDestroyed(); // fully escalated: level hits the ceiling
            float fullyEscalated = spawner.CurrentInterval;

            Assert.Less(fullyEscalated, atStart,
                "a fully escalated Invasion Level did not speed the spawner's cadence up");
        }

        [UnityTest]
        public IEnumerator AFullyEscalatedRun_SpawnsToughierRobots()
        {
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationRate = 0f;
            DevTuning.EscalationPerShedBump = 1f;
            DevTuning.EscalationMax = 1f;
            DevTuning.SpawnInterval = 0f; // spawn on the very first check, no waiting on the ramp

            _hutch = NewHutch(new Vector3(0f, 1f, 15f));
            var spawner = _hutch.GetComponent<EnemySpawner>();

            ForceSpawn(spawner);
            yield return null;
            var early = _hutch.GetComponentInChildren<RobotEnemy>();
            Assert.IsNotNull(early, "nothing spawned before escalation");
            float earlyHealth = early.HealthCurrent;
            early.TakeDamage(new DamageInfo(9999f, early.transform.position, Vector3.forward, Team.Player));

            DifficultyDirector.ReportShedDestroyed(); // fully escalated: level hits the ceiling
            yield return null; // let the kill return the robot to its pool

            ForceSpawn(spawner);
            yield return null;
            var late = _hutch.GetComponentInChildren<RobotEnemy>();
            Assert.IsNotNull(late, "nothing spawned after escalation");

            Assert.Greater(late.HealthCurrent, earlyHealth,
                "a robot spawned at a fully escalated Invasion Level was not tougher than one spawned " +
                "at the run's start — even a POOLED robot must pick up the new toughness on reuse");
        }
    }
}
