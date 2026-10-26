using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1281 AC1: a Shielded elite facing +Z (the default <c>transform.forward</c>) takes 0 damage
    /// from a hit whose <see cref="DamageInfo.Direction"/> is -Z (front), full damage from +Z (rear),
    /// and full damage from <see cref="Vector3.up"/> (no flattened XZ component — ShoulderRack
    /// rockets/WaterBalloon/Orbitals bypass the shield, per the ticket's own "do not re-raise").
    ///
    /// Fails on 8041233 (the commit before this ticket): <c>EliteAffixes.IsImplemented(Shielded)</c>
    /// ships false and <c>RobotEnemy.TakeDamage</c> has no Shielded front-arc check at all yet, so the
    /// front-arc hit takes full damage exactly like the rear hit — the first assertion below fails
    /// (expected 0, actual 100).
    /// </summary>
    public sealed class MV1281ShieldedEliteTests
    {
        [Test]
        public void ShieldedElite_DeflectsFrontHit_TakesFullDamageFromRearAndAbove_MV1281()
        {
            var root = new GameObject("MV1281 Probe Root");
            try
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<CharacterController>();
                RobotEnemy robot = go.AddComponent<RobotEnemy>();
                EnemyArchetype shielded = EnemyArchetype.Of(EnemyKind.Rusher).WithElite(EliteAffix.Shielded);

                const float testDamage = 100f;

                // --- front (Direction -Z, same DredgeHulk convention MV715ReefRosterTests already
                // uses): source-to-target travel -Z means the hit arrived FROM +Z, this robot's front. ---
                robot.Apply(shielded);
                float beforeFront = robot.HealthCurrent;
                robot.TakeDamage(new DamageInfo(testDamage, go.transform.position, Vector3.back, Team.Player));
                float frontDamageTaken = beforeFront - robot.HealthCurrent;
                Assert.AreEqual(0f, frontDamageTaken, 1e-3f,
                    "a Shielded elite must take 0 damage from a front-arc hit (Direction -Z)");

                // --- rear (Direction +Z: arrived from -Z, behind) ---
                robot.Apply(shielded); // reset to full health
                float beforeRear = robot.HealthCurrent;
                robot.TakeDamage(new DamageInfo(testDamage, go.transform.position, Vector3.forward, Team.Player));
                float rearDamageTaken = beforeRear - robot.HealthCurrent;
                Assert.AreEqual(testDamage, rearDamageTaken, 1e-3f,
                    "a Shielded elite must take full damage from behind (Direction +Z)");

                // --- no flattened XZ direction (a rocket/balloon send Vector3.up) ---
                robot.Apply(shielded); // reset to full health
                float beforeAbove = robot.HealthCurrent;
                robot.TakeDamage(new DamageInfo(testDamage, go.transform.position, Vector3.up, Team.Player));
                float aboveDamageTaken = beforeAbove - robot.HealthCurrent;
                Assert.AreEqual(testDamage, aboveDamageTaken, 1e-3f,
                    "a Shielded elite must take full damage from a hit with no flattened XZ direction");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
