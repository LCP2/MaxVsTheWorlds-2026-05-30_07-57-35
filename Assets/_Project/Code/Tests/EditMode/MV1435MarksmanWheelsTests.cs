using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1435 - the City marksman Bolter rolls on a three-wheeled base instead of three static legs. ONE test
    /// (MV-465 Rule 1), Tier 2 resolved values (transform names, wheel rotation after a rig tick, renderer
    /// bounds). On the base commit the body has three MarksmanLeg/MarksmanFoot parts and no wheels.
    /// </summary>
    public sealed class MV1435MarksmanWheelsTests
    {
        private static void Invoke(RobotRig rig, string method)
        {
            LogAssert.ignoreFailingMessages = true;
            try { typeof(RobotRig).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rig, null); }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Test]
        public void MarksmanBolter_RollsOnThreeWheels_NoLegs_MV1435()
        {
            GameObject go = null;
            try
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.AddComponent<CharacterController>();
                RobotEnemy enemy = go.AddComponent<RobotEnemy>();
                enemy.Apply(EnemyArchetype.Of(EnemyKind.Bolter).WithOverride(new WorldEnemyOverride { marksman = true }));
                Assert.IsTrue(enemy.IsMarksman, "test setup: marksman role stamped");
                RobotRig rig = go.AddComponent<RobotRig>();
                go.transform.position = Vector3.zero;
                Invoke(rig, "EnsureBuilt");
                Invoke(rig, "LateUpdate");   // primes the rig's last position

                // 1. No leg or foot transforms anywhere.
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    Assert.IsFalse(t.name == "MarksmanLeg" || t.name == "MarksmanFoot", "no leg/foot: " + t.name);

                // 2. Exactly three wheels, found as the rig's own spinning pivots.
                var wheels = new List<Transform>();
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name == "MarksmanWheel") wheels.Add(t);
                Assert.AreEqual(3, wheels.Count, "three wheels");

                // 3. Move 2 m forward, tick: each wheel turns 2 / (2 pi r) * 360 degrees (within 10%).
                var before = new Quaternion[3];
                for (int i = 0; i < 3; i++) before[i] = wheels[i].localRotation;
                go.transform.position += go.transform.forward * 2f;
                Invoke(rig, "LateUpdate");
                for (int i = 0; i < 3; i++)
                {
                    float radius = wheels[i].GetComponent<MeshFilter>().sharedMesh.bounds.extents.x;
                    float expected = 2f / (2f * Mathf.PI * radius) * 360f;
                    float turned = Quaternion.Angle(before[i], wheels[i].localRotation);
                    float expectedMod = expected % 360f; if (expectedMod > 180f) expectedMod = 360f - expectedMod;
                    Assert.That(turned, Is.EqualTo(expectedMod).Within(Mathf.Max(expectedMod * 0.1f, 1f)), "wheel " + i + " turned by distance travelled");
                }

                // 4. Each wheel's lowest bound sits on the floor (the robot's feet are at y = 0 - spawn height).
                float feetY = go.transform.Find("RobotModel/Feet").position.y;
                foreach (Transform w in wheels)
                    Assert.That(w.GetComponent<Renderer>().bounds.min.y, Is.EqualTo(feetY).Within(0.05f), "wheel rests on the floor");
            }
            finally { if (go != null) Object.DestroyImmediate(go); }
        }
    }
}
