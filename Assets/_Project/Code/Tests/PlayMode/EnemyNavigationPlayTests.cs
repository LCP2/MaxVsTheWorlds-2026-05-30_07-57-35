using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The robots get out of the shed (YT-93).
    ///
    /// The EditMode tests prove the ROUTE is right — a robot can be walked across the yard on paper.
    /// This proves the actual robots do it: real bodies, real colliders, real walls, spawned by the
    /// real factories out of the real map, chasing a real player. Everything the paper version leaves
    /// out is exactly where a pile-up would hide.
    ///
    /// The playtest verdict was "they accumulate behind walls and don't reach Max". That is the claim
    /// under test, and it is asserted the way it was reported: after long enough to have walked there,
    /// where are they?
    /// </summary>
    public sealed class EnemyNavigationPlayTests
    {
        private GameObject _path, _player, _gate, _boss;
        private float _timeScale;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _timeScale;

            foreach (GameObject go in new[] { _path, _player, _gate, _boss })
                if (go != null) Object.Destroy(go);

            yield return null;
        }

        private static MapData Shipped() => MapLibrary.Load(MapLibrary.BackyardSlice);

        /// <summary>The level, from the map, with Max standing where it says he starts.</summary>
        private IEnumerator BuildTheYard()
        {
            _timeScale = Time.timeScale;

            _player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _player.name = "Max (Greybox)";
            _player.tag = "Player";
            _player.AddComponent<CharacterController>();

            _gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _gate.AddComponent<SubZoneGate>();

            _boss = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _boss.AddComponent<MaxWorlds.Bosses.BigBermudaBoss>();

            yield return null;

            _path = new GameObject("Backyard Path", typeof(BackyardPath));
            yield return null;
            Physics.SyncTransforms();
            yield return null;
        }

        /// <summary>Run the fight for <paramref name="seconds"/> of GAME time. Sped up, because the
        /// walk across a yard this size is half a minute and a test suite is not a playtest.</summary>
        private static IEnumerator Fight(float seconds)
        {
            Time.timeScale = 8f;

            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                yield return null;
            }

            Time.timeScale = 1f;
        }

        private static List<RobotEnemy> RobotsOf(MowerHutch hutch)
        {
            var live = new List<RobotEnemy>();

            foreach (RobotEnemy r in hutch.GetComponentsInChildren<RobotEnemy>())
                if (r.gameObject.activeInHierarchy && r.IsAlive) live.Add(r);

            return live;
        }

        private static string RoomOf(MapData map, Vector3 at) => map.ZoneAt(at.x, at.z)?.id ?? "outside";

        /// <summary>
        /// THE ticket: robots from both factories leave the room they were born in and come for Max.
        ///
        /// Before this they could not. Every robot spawns out of sight of Max — the factories are in
        /// rooms off the side of the yard — so each one beelined into the shed wall, and then, 2.5 s
        /// after being born, decided it had "lost" a player it had never seen and stopped to look
        /// around. Forever. That is the pile-up, and both halves of it are fixed here: it routes out
        /// through the doorway, and it does not give up while it is still getting closer.
        /// </summary>
        [UnityTest]
        public IEnumerator RobotsFromBothFactories_LeaveTheirShedAndComeForMax()
        {
            yield return BuildTheYard();

            MapData map = Shipped();
            Assert.AreEqual(3, FactoryCensus.Total, "the yard has lost a factory");

            yield return Fight(26f);

            Vector3 max = _player.transform.position;

            foreach (MowerHutch hutch in FactoryCensus.All)
            {
                string shed = RoomOf(map, hutch.transform.position);
                List<RobotEnemy> robots = RobotsOf(hutch);

                Assert.IsNotEmpty(robots, $"the factory in '{shed}' produced nothing to test");

                int out_ = 0;
                float nearest = float.MaxValue;

                foreach (RobotEnemy robot in robots)
                {
                    if (RoomOf(map, robot.transform.position) != shed) out_++;

                    Vector3 to = robot.transform.position - max;
                    to.y = 0f;
                    nearest = Mathf.Min(nearest, to.magnitude);
                }

                Assert.Greater(out_, 0,
                    $"every robot from the factory in '{shed}' is still standing in '{shed}' after 26 " +
                    "seconds. They are piled up against the inside of a wall while the player walks away.");

                float shedToMax = Vector3.Distance(hutch.transform.position, max);
                Assert.Less(nearest, shedToMax * 0.6f,
                    $"the closest robot from '{shed}' is {nearest:0} m from Max and its factory is " +
                    $"{shedToMax:0} m away — nothing is closing on him.");
            }
        }

        /// <summary>
        /// No robot gives up while it is still on its way.
        ///
        /// "It has lost him" used to mean "it has not seen him for 2.5 seconds", which is true of every
        /// robot ever born in a shed on the far side of the yard. They downed tools mid-walk. A robot
        /// that is standing in its own factory's room, in Search, having never got anywhere, is the
        /// exact shape of that bug.
        /// </summary>
        [UnityTest]
        public IEnumerator NoRobotGivesUpWhileItIsStillWalkingToMax()
        {
            yield return BuildTheYard();

            MapData map = Shipped();
            yield return Fight(12f);

            foreach (MowerHutch hutch in FactoryCensus.All)
            {
                string shed = RoomOf(map, hutch.transform.position);

                foreach (RobotEnemy robot in RobotsOf(hutch))
                {
                    bool stillHome = RoomOf(map, robot.transform.position) == shed;

                    Assert.IsFalse(stillHome && robot.Current == RobotEnemy.State.Search,
                        $"a robot is standing in '{shed}' having given up — it never saw Max, because " +
                        "Max was never visible from inside the shed it was born in, and it stopped " +
                        "walking on the strength of that.");
                }
            }
        }

        /// <summary>
        /// They arrive as a fan, not a queue.
        ///
        /// One route to one goal makes a conga line: the pack files down a single lane, the fight only
        /// ever happens on one side, and killing the front robot buys the whole pack's time. The
        /// playtest asked for pressure from several directions, so each robot approaches on its own
        /// bearing (EnemyFormation) — asserted here as the spread of the angles they close from.
        /// </summary>
        [UnityTest]
        public IEnumerator ThePackClosesFromSeveralAngles_NotInSingleFile()
        {
            yield return BuildTheYard();
            yield return Fight(26f);

            Vector3 max = _player.transform.position;
            var bearings = new List<float>();

            foreach (MowerHutch hutch in FactoryCensus.All)
            foreach (RobotEnemy robot in RobotsOf(hutch))
            {
                Vector3 to = robot.transform.position - max;
                to.y = 0f;
                if (to.magnitude > 30f) continue;   // still on the road; it hasn't chosen a side yet

                bearings.Add(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
            }

            Assert.GreaterOrEqual(bearings.Count, 3, "not enough robots reached Max to say anything");

            bearings.Sort();
            float spread = bearings[bearings.Count - 1] - bearings[0];

            Assert.Greater(spread, 20f,
                $"the pack is closing on Max down a {spread:0}° lane — that is a queue, and a queue is " +
                "one robot's worth of pressure however many are in it");
        }
    }
}
