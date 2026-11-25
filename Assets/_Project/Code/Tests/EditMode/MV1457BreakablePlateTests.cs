using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-1457: a Shielded elite's front plate has its own HP (50% of max); front hits drain the
    /// plate not the body; at 0 the plate breaks and the next front hit damages the body.</summary>
    public sealed class MV1457BreakablePlateTests
    {
        [Test]
        public void ShieldedElite_FrontHitsDrainPlateThenBreak_ThenBodyTakesDamage_MV1457()
        {
            var root = new GameObject("MV1457 Probe Root");
            try
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<CharacterController>();
                RobotEnemy robot = go.AddComponent<RobotEnemy>();
                robot.Apply(EnemyArchetype.Of(EnemyKind.Rusher).WithElite(EliteAffix.Shielded));

                PropertyInfo plate = typeof(RobotEnemy).GetProperty("PlateHealthCurrent");
                Assert.IsNotNull(plate, "RobotEnemy must expose the Shielded plate's remaining HP (PlateHealthCurrent)");
                float Plate() => (float)plate.GetValue(robot);

                float body0 = robot.HealthCurrent;
                float plate0 = Plate();
                Assert.AreEqual(body0 * 0.5f, plate0, 1e-3f, "plate HP = 50% of the elite's resolved max HP");

                var front = new DamageInfo(1f, go.transform.position, Vector3.back, Team.Player);
                robot.TakeDamage(front);
                Assert.Less(Plate(), plate0, "a front-arc hit must drain the plate");
                Assert.AreEqual(body0, robot.HealthCurrent, 1e-3f, "a front-arc hit must not touch the body while the plate holds");

                int guard = 0;
                while (Plate() > 0f && guard++ < 10000) robot.TakeDamage(front);
                Assert.AreEqual(0f, Plate(), 1e-3f, "front hits must break the plate");
                Assert.AreEqual(body0, robot.HealthCurrent, 1e-3f, "breaking the plate must not damage the body");

                robot.TakeDamage(front);
                Assert.Less(robot.HealthCurrent, body0, "after the plate breaks, a front hit must damage the body");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
