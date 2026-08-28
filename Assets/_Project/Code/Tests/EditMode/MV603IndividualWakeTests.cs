using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-603 — a shed/factory spawn must wait dormant, gated by its own AmbushWake tick, exactly
    /// like a placed garrison/concealed member, rather than handing off from its door-clearance walk
    /// straight into Chase unseen. Also proves the MV-363 group chain-wake is gone: waking one robot
    /// never wakes a sibling. EditMode only, same reflection idiom as MV363DormantRobotTests — Unity
    /// does not run Awake/OnEnable/Update for a plain MonoBehaviour outside Play mode.
    /// </summary>
    public sealed class MV603IndividualWakeTests
    {
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp() => _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();

        [TearDown]
        public void TearDown() => CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);

        private static RobotEnemy NewClearedTheDoorEnemy(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.ResetState(); // EditMode has no Awake/OnEnable lifecycle - init explicitly

            // Emergence target == spawn position, so the very first TickEmerge reads as "arrived".
            e.BeginEmergence(e.transform.position);
            InvokeTickEmerge(e, 0.1f);
            return e;
        }

        private static void InvokeTickEmerge(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickEmerge", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void InvokeTickDormant(RobotEnemy e) =>
            typeof(RobotEnemy).GetMethod("TickDormant", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, null);

        [TestCase(false, false, RobotEnemy.State.Dormant)]
        [TestCase(true, false, RobotEnemy.State.Dormant)]
        [TestCase(false, true, RobotEnemy.State.Dormant)]
        [TestCase(true, true, RobotEnemy.State.Alert)]
        public void ShedSpawnedRobot_ClearOfTheDoor_WakesOnlyOnItsOwnScreenAndSight_NeverBySibling(
            bool onScreen, bool sightClear, RobotEnemy.State expected)
        {
            RobotEnemy woken = NewClearedTheDoorEnemy("Woken");
            RobotEnemy sibling = NewClearedTheDoorEnemy("Sibling");
            GameObject cameraGo = null;
            try
            {
                Assert.AreEqual(RobotEnemy.State.Dormant, woken.Current,
                    "AC1: clearing the shed door must hand off into Dormant, not straight into Chase");
                Assert.AreEqual(RobotEnemy.State.Dormant, sibling.Current);

                // Same camera-at-origin-looking-down-+Z idiom as MV363DormantRobotTests: z=5 sits
                // inside the frustum, z=-20 sits behind the camera and outside it.
                cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
                Camera cam = cameraGo.AddComponent<Camera>();
                cam.transform.position = Vector3.zero;
                cam.transform.rotation = Quaternion.identity;
                woken.transform.position = onScreen ? new Vector3(0f, 0f, 5f) : new Vector3(0f, 0f, -20f);

                Vector3 before = woken.transform.position;
                woken.Sight.Tick(sightClear, woken.transform.position + Vector3.forward, 0.1f);
                InvokeTickDormant(woken);

                Assert.AreEqual(expected, woken.Current);

                if (expected == RobotEnemy.State.Dormant)
                {
                    Assert.AreEqual(before, woken.transform.position,
                        "AC1: a robot that fails its own wake test must not advance toward the target while off-screen");
                }

                Assert.AreEqual(RobotEnemy.State.Dormant, sibling.Current,
                    "AC2: waking one robot must never wake another - the DormantGroup chain-wake is retired");
            }
            finally
            {
                Object.DestroyImmediate(woken.gameObject);
                Object.DestroyImmediate(sibling.gameObject);
                if (cameraGo != null) Object.DestroyImmediate(cameraGo);
            }
        }
    }
}
