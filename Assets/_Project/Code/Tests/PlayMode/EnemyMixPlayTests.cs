using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Drives a real spawner (YT-66): both robot kinds must actually reach the field, and each must
    /// be wearing its OWN body. The bug this exists to catch is pooling — recycle a dead bruiser
    /// into the next rusher and you get a rusher's stats inside a bruiser's box, which no unit test
    /// on the archetype data would ever see.
    /// </summary>
    public sealed class EnemyMixPlayTests
    {
        private GameObject _go;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // The Invasion Level (YT-181) is a static, run-wide clock — reset it so a robot spawned
            // here always gets the AUTHORED, untoughened archetype. Without this, whichever fixture
            // ran before could have left the level escalated and this file's exact health arithmetic
            // (a rusher's own health vs. a bruiser's) would silently start comparing against the
            // wrong numbers.
            DifficultyDirector.Reset();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            DifficultyDirector.Reset();
            yield return null;
        }

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
             .SetValue(o, value);

        /// <summary>
        /// A spawner on a body scaled like the real Mower Hutch — (3, 2, 3). That scale is the whole
        /// point: the robots used to inherit it, so a 1.9 m bruiser spawned as a 5.7 m cube and
        /// walled the player in (YT-74). Every test here runs on the scaled body, because a test on
        /// an unscaled one would have passed happily while the game was unplayable.
        /// </summary>
        private EnemySpawner NewSpawner()
        {
            _go = new GameObject("Factory");
            _go.transform.localScale = new Vector3(3f, 2f, 3f);
            var s = _go.AddComponent<EnemySpawner>();
            Set(s, "spawnIntervalStart", 0f);   // spawn every frame — a test shouldn't wait 1.8 s
            Set(s, "spawnIntervalMin", 0f);
            Set(s, "maxLiveEnemies", 12);
            Set(s, "bruiserEvery", 4);
            Set(s, "firstBruiserAt", 3);
            return s;
        }

        private static IEnumerator RunUntilFull(EnemySpawner s)
        {
            for (int i = 0; i < 60 && s.LiveCount < 12; i++) yield return null;
        }

        /// <summary>Every live robot's BODY must match its KIND — the pooling invariant.</summary>
        private static void AssertBodiesMatchKinds(EnemySpawner s)
        {
            foreach (var e in s.GetComponentsInChildren<RobotEnemy>(false))
            {
                var mesh = e.GetComponent<MeshFilter>().sharedMesh;
                bool wearingABox = mesh.name.Contains("Cube");
                Assert.AreEqual(e.Kind == EnemyKind.Bruiser, wearingABox,
                    $"a {e.Kind} is wearing the wrong body ({mesh.name})");
            }
        }

        [UnityTest]
        public IEnumerator BothKindsReachTheField()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);

            Assert.Greater(s.LiveCountOf(EnemyKind.Rusher), 0, "no rushers");
            Assert.Greater(s.LiveCountOf(EnemyKind.Bruiser), 0, "no bruisers — the fight has no texture");
            Assert.Greater(s.LiveCountOf(EnemyKind.Rusher), s.LiveCountOf(EnemyKind.Bruiser),
                "bruisers should be punctuation, not the swarm");
        }

        [UnityTest]
        public IEnumerator EachKindWearsItsOwnBody()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);
            AssertBodiesMatchKinds(s);
        }

        [UnityTest]
        public IEnumerator RecycledRobotsKeepTheirOwnBodies()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);

            // Wipe the field, forcing every robot back into a pool. Check it emptied BEFORE yielding —
            // the spawner refills on the very next frame, so a yield here would race the assert.
            foreach (var e in s.GetComponentsInChildren<RobotEnemy>(false))
                e.TakeDamage(new DamageInfo(9999f, e.transform.position, Vector3.forward, Team.Player));
            Assert.AreEqual(0, s.LiveCount, "the field didn't clear");

            // …then refill it entirely from those pools.
            yield return RunUntilFull(s);

            Assert.Greater(s.LiveCountOf(EnemyKind.Bruiser), 0, "no bruisers after recycling");
            AssertBodiesMatchKinds(s);   // a bruiser must not come back as a rusher, or vice versa
        }

        // --- YT-74: the factory's scale must not leak into the robots ---------------------------

        [UnityTest]
        public IEnumerator RobotsAreTheSizeTheyWereAuthored_NotTheSizeOfTheFactory()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);

            foreach (var e in s.GetComponentsInChildren<RobotEnemy>(false))
            {
                var a = EnemyArchetype.Of(e.Kind);
                Vector3 world = e.transform.lossyScale;

                Assert.AreEqual(a.BodyScale.x, world.x, 0.01f,
                    $"the {e.Kind} inherited the factory's scale — it is {world.x / a.BodyScale.x:0.0}x too wide");
                Assert.AreEqual(a.BodyScale.y, world.y, 0.01f, $"the {e.Kind} is the wrong height");
            }
        }

        [UnityTest]
        public IEnumerator NoRobotIsBiggerThanMax()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);

            foreach (var e in s.GetComponentsInChildren<RobotEnemy>(false))
            {
                // The body itself, in metres. NOT Renderer.bounds: that's an axis-ALIGNED box, and
                // the robots are rotated to face Max — a 1.15 m cube turned 45° reports a 1.63 m
                // bounds and would fail this for no reason. Unity's cube and capsule meshes are both
                // 1 unit wide; the capsule is 2 units tall.
                Vector3 scale = e.transform.lossyScale;
                float width = Mathf.Max(scale.x, scale.z);
                float height = EnemyArchetype.Of(e.Kind).Shape == EnemyShape.Capsule
                    ? scale.y * 2f
                    : scale.y;

                Assert.LessOrEqual(width, EnemyArchetype.PlayerRadius * 2f * 1.3f,
                    $"a {e.Kind} is {width:0.00} m wide — Max is 1 m");
                Assert.LessOrEqual(height, EnemyArchetype.PlayerHeight + 0.01f,
                    $"a {e.Kind} is {height:0.00} m tall — Max is 2 m");
            }
        }

        [UnityTest]
        public IEnumerator TheCollidersMatchTheBodiesYouCanSee()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);

            foreach (var e in s.GetComponentsInChildren<RobotEnemy>(false))
            {
                var a = EnemyArchetype.Of(e.Kind);
                var cc = e.GetComponent<CharacterController>();
                Vector3 scale = e.transform.lossyScale;

                // A CharacterController multiplies its own numbers by the transform's scale.
                float worldRadius = cc.radius * Mathf.Max(scale.x, scale.z);
                float worldHeight = cc.height * scale.y;

                Assert.AreEqual(a.ColliderRadius, worldRadius, 0.02f,
                    $"the {e.Kind}'s collider is {worldRadius:0.00} m across, not {a.ColliderRadius:0.00}");
                Assert.AreEqual(a.ColliderHeight, worldHeight, 0.02f, $"the {e.Kind}'s collider is the wrong height");
            }
        }

        // --- YT-74: robots must never wall the player in -----------------------------------------

        /// <summary>A player CharacterController, as the scene builds Max.</summary>
        private static GameObject NewPlayerAt(Vector3 pos, out CharacterController cc)
        {
            var player = new GameObject("Max");
            player.tag = "Player";
            cc = player.AddComponent<CharacterController>();
            cc.height = EnemyArchetype.PlayerHeight;
            cc.radius = EnemyArchetype.PlayerRadius;
            player.transform.position = pos;
            return player;
        }

        [UnityTest]
        public IEnumerator MaxWalksThroughARobot_RatherThanBeingStoppedDeadByIt()
        {
            // The playability-breaker, tested the only way that holds: walk Max at a robot from a
            // clean start and see whether he gets past it.
            //
            // (An earlier version of this test ringed him with robots and checked he could escape —
            // and it PASSED even with the fix disabled, because a CharacterController that starts
            // already overlapping something de-penetrates itself out of it. It proved nothing. Start
            // clear of the body, and the block is real.)
            var player = NewPlayerAt(new Vector3(0f, 1f, 0f), out var cc);

            var s = NewSpawner();
            yield return RunUntilFull(s);

            var robot = s.GetComponentsInChildren<RobotEnemy>(false)[0];
            robot.enabled = false;                                   // hold it still: this is about bodies
            robot.transform.position = new Vector3(0f, 1f, 2f);      // squarely in his path
            Physics.SyncTransforms();
            yield return null;

            for (int i = 0; i < 40; i++) cc.Move(Vector3.forward * 0.1f);
            float z = player.transform.position.z;

            Object.Destroy(player);
            Assert.Greater(z, 3f,
                $"Max stopped at z={z:0.00} — a robot's body is a wall, and a swarm of them is a cage");
        }

        [UnityTest]
        public IEnumerator EveryRobotIsSetToLetThePlayerThrough_IncludingRecycledOnes()
        {
            // Unity drops an ignored collider pair when the collider is disabled, and pooling
            // disables it on every death — so this has to be re-applied on each spawn, not once.
            var player = NewPlayerAt(new Vector3(0f, 1f, 0f), out var cc);

            var s = NewSpawner();
            yield return RunUntilFull(s);

            // Kill the field and let it refill entirely from the pools.
            foreach (var e in s.GetComponentsInChildren<RobotEnemy>(false))
                e.TakeDamage(new DamageInfo(9999f, e.transform.position, Vector3.forward, Team.Player));
            yield return RunUntilFull(s);

            var robots = s.GetComponentsInChildren<RobotEnemy>(false);
            Assert.Greater(robots.Length, 0);

            foreach (var e in robots)
            {
                foreach (var ec in e.GetComponents<Collider>())
                {
                    Assert.IsTrue(Physics.GetIgnoreCollision(ec, cc),
                        $"a recycled {e.Kind} can body-block Max again");
                }
            }
            Object.Destroy(player);
        }

        [UnityTest]
        public IEnumerator RobotsStillCollideWithTheWorld_SoCoverStillWorks()
        {
            // Letting the player through must not turn the robots into ghosts: they still have to be
            // stopped by walls and cover, or YT-68's obstacles stop meaning anything.
            var s = NewSpawner();
            yield return RunUntilFull(s);

            var robot = s.GetComponentsInChildren<RobotEnemy>(false)[0];
            var rc = robot.GetComponent<CharacterController>();

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = robot.transform.position + Vector3.forward * 2f;
            wall.transform.localScale = new Vector3(8f, 4f, 1f);
            Physics.SyncTransforms();
            yield return null;

            Vector3 before = robot.transform.position;
            for (int i = 0; i < 20; i++) rc.Move(Vector3.forward * 0.3f);
            float advanced = robot.transform.position.z - before.z;

            Object.Destroy(wall);
            Assert.Less(advanced, 2f, "the robot walked through a solid wall");
        }

        [UnityTest]
        public IEnumerator ABruiserIsTougherThanARusher_InTheActualGame()
        {
            var s = NewSpawner();
            yield return RunUntilFull(s);

            // One rusher's worth of damage kills a rusher outright and barely dents a bruiser.
            float shot = EnemyArchetype.Rusher.MaxHealth;

            foreach (var e in s.GetComponentsInChildren<RobotEnemy>(false))
            {
                bool bruiser = e.Kind == EnemyKind.Bruiser;
                e.TakeDamage(new DamageInfo(shot, e.transform.position, Vector3.forward, Team.Player));
                Assert.AreEqual(bruiser, e.IsAlive,
                    bruiser ? "a bruiser died to one rusher's worth of damage"
                            : "a rusher survived a full-health shot");
            }
        }
    }
}
