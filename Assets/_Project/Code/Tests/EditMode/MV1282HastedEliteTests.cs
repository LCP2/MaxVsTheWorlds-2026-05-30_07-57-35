using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1282 AC1: a Hasted elite Rusher's resolved <see cref="RobotEnemy.EffectiveMoveSpeed"/> is
    /// 1.4x a non-elite Rusher's in the same conditions (no slow zone, no dev-tuning override, same
    /// transform position), and its <see cref="RobotEnemy.MaxHealth"/> equals the non-elite's — it
    /// does NOT receive MV-1278's x2.0 elite health bonus (the ticket's own "fast but fragile").
    ///
    /// Fails on a2a730a (the commit before this ticket): <c>RobotEnemy</c> has no Hasted speed
    /// multiplier at all yet, so the elite's <c>EffectiveMoveSpeed</c> equals the non-elite's instead
    /// of 1.4x it (first assertion fails, expected ~1.4x actual 1x); and <c>Apply</c> doubles
    /// <c>maxHealth</c> for every elite unconditionally, so the Hasted elite's <c>MaxHealth</c> is 2x
    /// the non-elite's instead of equal (second assertion fails, expected equal, actual double).
    /// </summary>
    public sealed class MV1282HastedEliteTests
    {
        [Test]
        public void HastedElite_MoveSpeedIs1Point4x_MaxHealthEqualsNonElite_MV1282()
        {
            var root = new GameObject("MV1282 Probe Root");
            try
            {
                GameObject nonEliteGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                nonEliteGo.transform.SetParent(root.transform, false);
                nonEliteGo.AddComponent<CharacterController>();
                RobotEnemy nonElite = nonEliteGo.AddComponent<RobotEnemy>();
                nonElite.Apply(EnemyArchetype.Of(EnemyKind.Rusher));

                GameObject hastedGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                hastedGo.transform.SetParent(root.transform, false);
                hastedGo.AddComponent<CharacterController>();
                RobotEnemy hasted = hastedGo.AddComponent<RobotEnemy>();
                hasted.Apply(EnemyArchetype.Of(EnemyKind.Rusher).WithElite(EliteAffix.Hasted));

                Assert.AreEqual(nonElite.EffectiveMoveSpeed * 1.4f, hasted.EffectiveMoveSpeed, 1e-3f,
                    "a Hasted elite's resolved EffectiveMoveSpeed must be 1.4x a non-elite's in the same conditions");

                Assert.AreEqual(nonElite.MaxHealth, hasted.MaxHealth, 1e-3f,
                    "a Hasted elite must NOT receive the x2.0 elite health bonus — its max health must equal the non-elite's");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
