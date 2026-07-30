using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The front-of-curve fix (YT-194): a playtest found Max overrun by a swarm in the opening
    /// seconds, then totally dominant once armed — too many weak robots too soon, and (since the
    /// field-wide cap of YT-186 means raw numbers can't carry late-game threat) not enough toughness
    /// to answer a fully-upgraded Max. These prove the three new knobs actually drive the game:
    /// a low starting population that opens up as the Invasion Level climbs, an intuitive
    /// production-per-minute unit, and a real health multiplier.
    /// </summary>
    public sealed class SwarmPacingPlayTests
    {
        private GameObject _hutch;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            DifficultyDirector.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hutch != null) Object.Destroy(_hutch);
            DevTuning.Reset();
            DifficultyDirector.Reset();
            RobotEnemy.ResetRegistry();
        }

        private GameObject NewHutch(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Mower Hutch";
            go.transform.position = at;
            go.transform.localScale = new Vector3(3f, 2f, 3f);
            go.AddComponent<MowerHutch>();
            return go;
        }

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
             .SetValue(o, value);

        private static void ForceSpawn(EnemySpawner spawner) =>
            typeof(EnemySpawner)
                .GetMethod("SpawnOne", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(spawner, null);

        private static IEnumerator Run(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        // ---------------------------------------------------------------- starting robots

        [UnityTest]
        public IEnumerator AFreshRun_OnlyEverShowsACoupleOfRobots_EvenWithRoomToSpare()
        {
            DevTuning.StartingRobots = 2f;
            DevTuning.SpawnInterval = 0.05f;   // plenty of chances to spawn if nothing capped it
            DevTuning.EscalationRate = 0f;     // freeze the Invasion Level so the cap can't creep up mid-test

            _hutch = NewHutch(new Vector3(0f, 1f, 15f));
            var spawner = _hutch.GetComponent<EnemySpawner>();
            Set(spawner, "maxLiveEnemies", 100);   // no per-factory ceiling in the way

            yield return Run(1.0f);

            Assert.LessOrEqual(spawner.LiveCount, 2,
                $"{spawner.LiveCount} robots were alive at run start against a 'Starting robots' " +
                "override of 2 — the opening must read as a couple of robots, not a swarm");
        }

        [UnityTest]
        public IEnumerator TheStartingCap_GrowsToTheFactorysMaxAsTheInvasionLevelClimbs()
        {
            // Same shape as DifficultyDirectorPlayTests' escalation proofs: pin the curve so
            // "fully escalated" is one ReportShedDestroyed() away. A shed kill (YT-210) only moves
            // the Level by skipping the clock forward, so drive it through RunLengthSeconds/the
            // derived rate rather than pinning EscalationRate to zero.
            DevTuning.EscalationStart = 0f;
            DevTuning.EscalationMax = 1f;
            DevTuning.RunLengthSeconds = 1000f;
            DevTuning.EscalationPerShedBump = 1000f; // one shed's skip == the whole run length
            DevTuning.StartingRobots = 1f;
            DevTuning.SpawnInterval = 0.02f;

            _hutch = NewHutch(new Vector3(0f, 1f, 15f));
            var spawner = _hutch.GetComponent<EnemySpawner>();
            Set(spawner, "maxLiveEnemies", 20);

            yield return Run(0.5f);
            int atStart = spawner.LiveCount;

            DifficultyDirector.ReportShedDestroyed();   // fully escalated: level hits the ceiling
            yield return Run(0.5f);
            int atMax = spawner.LiveCount;

            Assert.LessOrEqual(atStart, 1,
                $"the field should hold at 'Starting robots' (1) before the Invasion Level moves, got {atStart}");
            Assert.Greater(atMax, atStart,
                "a fully escalated Invasion Level did not open the field up past its starting cap " +
                $"(start={atStart}, max={atMax})");
        }

        // ---------------------------------------------------------------- production per minute

        [UnityTest]
        public IEnumerator ProductionPerMinute_ConvertsToTheEquivalentSecondsInterval()
        {
            _hutch = NewHutch(new Vector3(0f, 1f, 15f));
            var spawner = _hutch.GetComponent<EnemySpawner>();
            Set(spawner, "rampSeconds", 0f);   // collapse the ramp so the steady-state value applies now

            DevTuning.RobotProductionPerMinute = 30f;   // 30/min == one every 2 seconds
            yield return null;

            Assert.AreEqual(2.0f, spawner.CurrentInterval, 0.01f,
                "30 robots/minute must convert to a 2-second interval (60 / 30)");
        }

        // ---------------------------------------------------------------- robot health

        [UnityTest]
        public IEnumerator RobotHealthMultiplier_ScalesASpawnedRobotsHealth()
        {
            _hutch = NewHutch(new Vector3(0f, 1f, 15f));
            var spawner = _hutch.GetComponent<EnemySpawner>();

            DevTuning.RobotHealthMultiplier = 2f;
            ForceSpawn(spawner);
            yield return null;

            var robot = _hutch.GetComponentInChildren<RobotEnemy>();
            Assert.IsNotNull(robot, "nothing spawned");
            Assert.AreEqual(EnemyArchetype.Rusher.MaxHealth * 2f, robot.HealthCurrent, 0.1f,
                "a 2x 'Robot health' override must double a freshly spawned rusher's health");
        }
    }
}
