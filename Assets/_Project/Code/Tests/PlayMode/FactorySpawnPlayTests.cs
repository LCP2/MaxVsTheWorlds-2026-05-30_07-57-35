using System.Collections;
using System.Linq;
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
    /// What the factory produces, and when it stops (YT-100).
    ///
    /// PlayMode because both halves are lifecycle behaviour: the spawner only emits from Update, and
    /// the emergence is a walk that happens over frames.
    /// </summary>
    public sealed class FactorySpawnPlayTests
    {
        private GameObject _hutch;
        private GameObject _hutch2;
        private GameObject _ground;

        /// <summary>Comfortably past the 1.8 s opening spawn interval, so a running factory has
        /// certainly emitted and a stopped one has certainly had its chance to.</summary>
        private const float PastOneInterval = 2.5f;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();

            // Something to stand on. Robots are CharacterControllers under constant gravity, and one
            // falling out of the world moves in a way no assertion about walking should have to
            // reason about — a blocked robot would simply drop below whatever was blocking it.
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "Ground";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(400f, 1f, 400f);

            _hutch = NewHutch("Mower Hutch", new Vector3(0f, 1f, 15f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in new[] { _hutch, _hutch2, _ground })
                if (go != null) Object.Destroy(go);
            DevTuning.Reset();
        }

        /// <summary>The factory as the map builds it: a (3, 2, 3) body, which drags in the spawner.</summary>
        private static GameObject NewHutch(string name, Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = at;
            go.transform.localScale = new Vector3(3f, 2f, 3f);
            go.AddComponent<MowerHutch>();
            return go;
        }

        private static void Destroy(GameObject hutch) =>
            hutch.GetComponent<MowerHutch>().TakeDamage(
                new DamageInfo(100000f, hutch.transform.position, Vector3.forward, Team.Player));

        private static IEnumerator Run(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        /// <summary>Force one spawn now, the way the press-kit director does — so a test doesn't have
        /// to wait out a spawn interval to look at where a robot lands.</summary>
        private static void ForceSpawn(EnemySpawner spawner) =>
            typeof(EnemySpawner)
                .GetMethod("SpawnOne", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(spawner, null);

        private static RobotEnemy[] RobotsOf(GameObject hutch) =>
            hutch.GetComponentsInChildren<RobotEnemy>(includeInactive: false);

        // --- 1. it stops, and stays stopped -------------------------------------------------------

        [UnityTest]
        public IEnumerator ALiveFactoryEmits()
        {
            // The control. Without it, every "emitted nothing" assertion below would also pass on a
            // factory that was never capable of emitting in the first place.
            var spawner = _hutch.GetComponent<EnemySpawner>();
            yield return Run(PastOneInterval);

            Assert.Greater(spawner.Emitted, 0,
                "a live factory produced no robots at all — the rest of this fixture proves nothing.");
        }

        [UnityTest]
        public IEnumerator ADestroyedFactoryStopsProducing_Immediately()
        {
            var spawner = _hutch.GetComponent<EnemySpawner>();

            Destroy(_hutch);
            yield return null;
            int atDeath = spawner.Emitted;

            yield return Run(PastOneInterval);

            Assert.AreEqual(atDeath, spawner.Emitted,
                "the factory is destroyed and still making robots. Killing the source is the whole " +
                "teaching loop of the game; if the stream survives the kill, the lesson is that it " +
                "didn't work.");
            Assert.IsFalse(spawner.IsRunning);
        }

        /// <summary>
        /// The bug Lee actually hit, and the reason this needed a ticket at all.
        ///
        /// The factory DID stop its spawner on death — by switching the component off. But `enabled`
        /// is a shared channel: DevModeController re-asserts `spawner.enabled = !IsSpawnPaused` over
        /// every spawner every frame. So a destroyed factory switched itself off, dev mode switched
        /// it back on a frame later, and it produced robots for the rest of the run. It only ever
        /// reproduced under ?dev=1 — which is how the game is play-tested, so it reproduced always.
        ///
        /// This drives the same re-assertion by hand. Death has to be state the spawner owns, not a
        /// flag anyone can flip back.
        /// </summary>
        [UnityTest]
        public IEnumerator DevModeCannotResurrectADestroyedFactory()
        {
            var spawner = _hutch.GetComponent<EnemySpawner>();

            Destroy(_hutch);
            yield return null;
            int atDeath = spawner.Emitted;

            float t = 0f;
            while (t < PastOneInterval)
            {
                spawner.enabled = true;   // exactly what ApplySpawnPause does, every frame
                t += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(atDeath, spawner.Emitted,
                "switching the spawner component back on restarted a destroyed factory. Stopping " +
                "production must not be spelled `enabled = false` while something else writes " +
                "`enabled` every frame.");
        }

        [UnityTest]
        public IEnumerator ADestroyedFactoryEmitsNothing_EvenWhenSpawnIsForced()
        {
            // The press-kit director reflects SpawnOne directly. A dead source must not emit down
            // that path either — the guard belongs on the spawn, not only on the timer.
            var spawner = _hutch.GetComponent<EnemySpawner>();
            Destroy(_hutch);
            yield return null;

            int atDeath = spawner.Emitted;
            ForceSpawn(spawner);

            Assert.AreEqual(atDeath, spawner.Emitted, "a destroyed factory was forced to emit");
        }

        [UnityTest]
        public IEnumerator BreakingOneFactoryLeavesTheOtherProducing()
        {
            // Two sources (YT-92): the kill is per-factory, so the yard stays dangerous until you
            // have dealt with both. Stopping every stream on the first kill would end the fight early.
            _hutch2 = NewHutch("Greenhouse Hutch", new Vector3(30f, 1f, 15f));
            var first = _hutch.GetComponent<EnemySpawner>();
            var second = _hutch2.GetComponent<EnemySpawner>();

            Destroy(_hutch);
            yield return null;
            int firstAtDeath = first.Emitted;
            int secondAtDeath = second.Emitted;

            yield return Run(PastOneInterval);

            Assert.AreEqual(firstAtDeath, first.Emitted, "the destroyed factory kept producing");
            Assert.Greater(second.Emitted, secondAtDeath,
                "the surviving factory stopped producing when the OTHER one was destroyed.");
            Assert.IsTrue(second.IsRunning);
        }

        // --- 1b. the cadence honours the configured rate (YT-170) ---------------------------------

        [UnityTest]
        public IEnumerator TheSpawnIntervalSettingGovernsHowOftenRobotsEmerge()
        {
            // A flat override, not just a faster ramp: DevTuning.SpawnInterval replaces the whole
            // start->min ramp outright, so the count over a run is just duration / interval.
            const float interval = 0.3f;
            const float duration = 2.1f;
            DevTuning.SpawnInterval = interval;

            var spawner = _hutch.GetComponent<EnemySpawner>();
            yield return Run(duration);

            int expected = Mathf.FloorToInt(duration / interval);
            Assert.That(spawner.Emitted, Is.EqualTo(expected).Within(1),
                $"{duration}s at a configured {interval}s interval should produce ~{expected} spawns, " +
                $"got {spawner.Emitted} — the setting is not driving real spawn timing.");
        }

        [UnityTest]
        public IEnumerator ChangingTheSpawnIntervalSettingChangesHowOftenRobotsEmerge()
        {
            const float duration = 2.0f;
            var spawner = _hutch.GetComponent<EnemySpawner>();

            DevTuning.SpawnInterval = 1.0f;
            yield return Run(duration);
            int slowCount = spawner.Emitted;

            Object.Destroy(_hutch);   // the GameObject, not the in-fiction TakeDamage kill
            yield return null;
            _hutch = NewHutch("Mower Hutch", new Vector3(0f, 1f, 15f));
            spawner = _hutch.GetComponent<EnemySpawner>();

            DevTuning.SpawnInterval = 0.3f;
            yield return Run(duration);
            int fastCount = spawner.Emitted;

            Assert.Greater(fastCount, slowCount,
                "dialling the spawn-rate setting down did not visibly change how often robots " +
                $"emerge (slow={slowCount}, fast={fastCount}).");
        }

        // --- 2. robots emerge FROM the factory ----------------------------------------------------

        [UnityTest]
        public IEnumerator ARobotAppearsInTheDoorway_NotOutOnTheLawn()
        {
            // It used to appear at the exit point: 3.5 m out, a clear body-length past the shed, on
            // open grass. There was no instant at which it was near the building, so nothing about it
            // read as having come from the building.
            var spawner = _hutch.GetComponent<EnemySpawner>();
            ForceSpawn(spawner);
            yield return null;

            RobotEnemy robot = RobotsOf(_hutch).FirstOrDefault();
            Assert.IsNotNull(robot, "nothing was emitted");

            float d = Flat(robot.transform.position, _hutch.transform.position);
            Assert.Less(d, 3f,
                $"the robot appeared {d:0.0} m from the factory centre — that is out past the 3 m " +
                "body, on the lawn. It has to be born at the wall it is coming out of.");
            Assert.Greater(d, 1.5f, "the robot was born inside the factory body");
        }

        [UnityTest]
        public IEnumerator TheRobotWalksOutOfTheFactory_ThenChases()
        {
            var spawner = _hutch.GetComponent<EnemySpawner>();
            ForceSpawn(spawner);
            yield return null;

            RobotEnemy robot = RobotsOf(_hutch).FirstOrDefault();
            Assert.IsNotNull(robot, "nothing was emitted");
            Assert.AreEqual(RobotEnemy.State.Emerging, robot.Current,
                "a robot is chasing before it has left the building.");

            float born = Flat(robot.transform.position, _hutch.transform.position);
            // 1.3s, not 1.0s: YT-169 slowed the emerge walk (emergeSpeedScale 0.8 -> 0.65, on top of
            // the also-slowed chase speed) so the birth beat reads as a distinctly more deliberate
            // step than the chase that follows — still safely inside the 1.5s EmergeTimeout fallback.
            yield return Run(1.3f);

            float now = Flat(robot.transform.position, _hutch.transform.position);
            Assert.Greater(now, born + 0.5f,
                $"the robot has moved {now - born:0.00} m from the factory in {1.3f:0.0}s. It is not " +
                "emerging — it is standing in the doorway.");
            Assert.AreNotEqual(RobotEnemy.State.Emerging, robot.Current,
                "the robot never finished emerging and so never joined the fight. Emergence is a " +
                "hand-off, not a state to live in.");
        }

        [UnityTest]
        public IEnumerator ABlockedRobotStopsEmergingAnyway()
        {
            // A doorway can be blocked — by cover, by the corner of the shed, by another robot. A
            // robot that waits forever to finish emerging is a robot that never attacks, which is a
            // worse bug than the one this ticket fixes.
            var spawner = _hutch.GetComponent<EnemySpawner>();
            ForceSpawn(spawner);
            yield return null;

            RobotEnemy robot = RobotsOf(_hutch).FirstOrDefault();
            Assert.IsNotNull(robot, "nothing was emitted");

            // Wall it in where it stands: it can never reach the spot outside the door.
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.transform.position = robot.transform.position + robot.transform.forward * 1.2f;
            block.transform.localScale = new Vector3(12f, 4f, 1f);
            block.transform.rotation = robot.transform.rotation;

            yield return Run(2.0f);

            Assert.AreNotEqual(RobotEnemy.State.Emerging, robot.Current,
                "a robot with a blocked doorway is still trying to emerge, and will never fight.");
            Object.Destroy(block);
        }

        private static float Flat(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
