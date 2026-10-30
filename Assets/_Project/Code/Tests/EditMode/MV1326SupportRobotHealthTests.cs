using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1326 (Testing policy Rule 1 — one new test): play feedback on World 3 found the Phase 1
    /// support robots (<see cref="EnemyKind.Projector"/>/<see cref="EnemyKind.Jammer"/>) died before
    /// their effect was ever visible — 40 HP and 60 HP respectively, less than a City Courier's 48.
    /// AC1: the base archetype values move to 160/140. AC2: an elite of each (the ×2.0 multiplier,
    /// MV-1278, resolved by <see cref="RobotEnemy.Apply"/> — never baked into
    /// <see cref="EnemyArchetype.MaxHealth"/> itself) spawns at 320/280.
    ///
    /// Fails on main before this ticket: <c>EnemyArchetype.Projector.MaxHealth</c> is 40 (not 160) and
    /// <c>EnemyArchetype.Jammer.MaxHealth</c> is 60 (not 140), so AC1's two assertions fail outright,
    /// and AC2's elite spawns resolve to 80/120 instead of 320/280.
    /// </summary>
    public sealed class MV1326SupportRobotHealthTests
    {
        [Test]
        public void SupportRobots_BaseAndEliteMaxHealth_MatchTheRetunedValues_MV1326()
        {
            // --- AC1: base archetype values. ---
            Assert.AreEqual(160f, EnemyArchetype.Projector.MaxHealth, 0.001f,
                "MV-1326: Shield Projector base MaxHealth must be 160");
            Assert.AreEqual(140f, EnemyArchetype.Jammer.MaxHealth, 0.001f,
                "MV-1326: Jammer base MaxHealth must be 140");

            // --- AC2: an elite of each spawns at double base HP (MV-1278's ×2.0, resolved by Apply). ---
            GameObject projectorGo = null, jammerGo = null;
            try
            {
                projectorGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                projectorGo.AddComponent<CharacterController>();
                RobotEnemy eliteProjector = projectorGo.AddComponent<RobotEnemy>();
                eliteProjector.Apply(EnemyArchetype.Of(EnemyKind.Projector).WithElite(EliteAffix.Shielded));
                Assert.AreEqual(320f, eliteProjector.HealthCurrent, 0.001f,
                    "MV-1326: an elite Shield Projector must spawn with 320 HP");

                jammerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                jammerGo.AddComponent<CharacterController>();
                RobotEnemy eliteJammer = jammerGo.AddComponent<RobotEnemy>();
                eliteJammer.Apply(EnemyArchetype.Of(EnemyKind.Jammer).WithElite(EliteAffix.Shielded));
                Assert.AreEqual(280f, eliteJammer.HealthCurrent, 0.001f,
                    "MV-1326: an elite Jammer must spawn with 280 HP");
            }
            finally
            {
                if (projectorGo != null) Object.DestroyImmediate(projectorGo);
                if (jammerGo != null) Object.DestroyImmediate(jammerGo);
            }
        }
    }
}
