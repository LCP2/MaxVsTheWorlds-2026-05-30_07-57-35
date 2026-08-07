using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
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
        private GameObject _boss;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            DifficultyDirector.Reset();
            FactoryCensus.Reset();

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
            if (_boss != null) Object.Destroy(_boss);
            DevTuning.Reset();
            DifficultyDirector.Reset();
            FactoryCensus.Reset();
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
            // A shed kill (YT-210) only moves the Level by skipping the clock forward, so a huge
            // RunLengthSeconds keeps the real-time climb negligible over this test's single frame
            // while still letting the shed's skip register as a clear rise.
            DevTuning.RunLengthSeconds = 1_000_000f;

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
            // A shed kill (YT-210) only moves the Level by skipping the clock forward, so drive it
            // through RunLengthSeconds/the derived rate rather than pinning EscalationRate to zero.
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 1f;
            DevTuning.RunLengthSeconds = 1000f;
            DevTuning.EscalationPerShedBump = 1000f; // one shed's skip == the whole run length

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
            // A shed kill (YT-210) only moves the Level by skipping the clock forward, so drive it
            // through RunLengthSeconds/the derived rate rather than pinning EscalationRate to zero.
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 1f;
            DevTuning.RunLengthSeconds = 1000f;
            DevTuning.EscalationPerShedBump = 1000f; // one shed's skip == the whole run length
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

        // --- MV-279: the boss must never erupt off the Invasion Level clock alone — only off
        // FactoryCensus.Cleared (every shed down), the same condition that opens its own gate.
        // (This used to be YT-210's SECOND wake path; a real 3-shed map could top the dial out
        // before all 3 sheds were actually destroyed, so the boss appeared before its gate opened.)

        [UnityTest]
        public IEnumerator TheBossStaysDormant_WhenTheInvasionLevelTopsOut_WithNoFactoryEverDestroyed()
        {
            // Level pinned exactly at its own ceiling from frame one — no elapsed time, no shed kill,
            // and NO factory registered at all, so FactoryCensus.Cleared can never fire either. If the
            // boss wakes here, it can only be because the dial alone topped out — the exact bug MV-279
            // fixed.
            DevTuning.EscalationStart = 5f;
            DevTuning.EscalationMax = 5f;

            Assert.AreEqual(0, FactoryCensus.Total, "this test must run with no factories in the census");

            _boss = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _boss.name = "Big Bermuda";
            _boss.AddComponent<BigBermudaBoss>();
            var boss = _boss.GetComponent<BigBermudaBoss>();

            Assert.IsFalse(boss.Engaged, "the boss should still be dormant the instant it wakes up");
            yield return null;
            yield return null;

            Assert.IsFalse(boss.Engaged,
                "the boss erupted off the Invasion Level clock alone, with no shed ever destroyed");
        }

        [UnityTest]
        public IEnumerator TheBossStaysDormant_WhileTheInvasionLevelIsBelowItsCeiling()
        {
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationRate = 0f; // no time-driven climb
            DevTuning.EscalationMax = 10f;

            _boss = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _boss.name = "Big Bermuda";
            _boss.AddComponent<BigBermudaBoss>();
            var boss = _boss.GetComponent<BigBermudaBoss>();

            yield return Run(0.2f);

            Assert.IsFalse(boss.Engaged,
                "the boss erupted before the Invasion Level ever reached its ceiling");
        }
    }
}
