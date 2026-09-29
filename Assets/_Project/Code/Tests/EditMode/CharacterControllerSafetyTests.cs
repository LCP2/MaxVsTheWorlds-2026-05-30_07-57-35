using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1021: three TestFlight crashes all shared the same native top frame,
    /// <c>PhysicsCommands::PhysX::CreateCharacterController</c> — Unity 6000.4.9f1 dereferences NULL
    /// when the controller desc PhysX refuses is fed back into the engine, and a non-finite pose was
    /// one of the two hypothesised causes. <see cref="EnemyBodySeparation.Clamp"/> is one of the paths
    /// that reaches a <c>CharacterController</c> reposition (via <c>RobotEnemy.ClampBodySeparation</c>),
    /// and a non-finite <c>playerPos</c> used to propagate a NaN straight through it. Must fail on
    /// <c>main</c> @ <c>b4c4d38</c> (the commit before this ticket's fix), where <c>Clamp</c> has no
    /// finite check at all.
    /// </summary>
    public sealed class CharacterControllerSafetyTests
    {
        // Guards MV-1021
        [Test]
        public void Clamp_NonFinitePlayerPos_ReturnsRobotPosUnchanged()
        {
            Vector3 robotPos = new Vector3(1f, 0f, 1f);
            Vector3 playerPos = new Vector3(float.NaN, 0f, 0f);

            Vector3 result = EnemyBodySeparation.Clamp(robotPos, playerPos, 1.2f);

            Assert.IsFalse(float.IsNaN(result.x) || float.IsNaN(result.y) || float.IsNaN(result.z),
                "a non-finite playerPos must never produce a non-finite result — this is the value " +
                "that used to reach PhysX's CreateCharacterController and crash it");
            Assert.AreEqual(robotPos, result,
                "with no finite player position to separate from, robotPos must pass through unchanged");
        }
    }
}
