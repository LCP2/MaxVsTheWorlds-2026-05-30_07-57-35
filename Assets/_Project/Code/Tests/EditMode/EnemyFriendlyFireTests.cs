using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Regression guard (YT-36): enemies must not damage each other. A cluster of
    /// enemies in overlapping colliders was producing death puffs with the player
    /// not firing — a self-grinder. Whatever path delivers an enemy-faction hit to
    /// an enemy, the receiver-side friendly-fire check must reject it: target stays
    /// alive, HP unchanged, no Died event.
    /// </summary>
    public sealed class EnemyFriendlyFireTests
    {
        private static RobotEnemy NewEnemy(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.ResetState(); // EditMode has no Awake/OnEnable lifecycle — init explicitly
            return e;
        }

        [Test]
        public void EnemyHitByEnemyFaction_SurvivesAndNoDeath()
        {
            var a = NewEnemy("EnemyA");
            var b = NewEnemy("EnemyB");
            try
            {
                bool aDied = false;
                a.Died += _ => aDied = true;

                Assert.IsTrue(a.IsAlive);
                Assert.AreEqual(Team.Enemy, a.Team);

                // Simulate what neighbour contact / a stray overlap would deliver:
                // many enemy-faction hits, far exceeding the enemy's health.
                for (int i = 0; i < 50; i++)
                {
                    a.TakeDamage(new DamageInfo(999f, a.transform.position, Vector3.forward, Team.Enemy));
                }

                Assert.IsTrue(a.IsAlive, "enemy died to same-team (enemy) damage — self-grinder regression");
                Assert.IsFalse(aDied, "Died event fired from same-team damage");
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
            }
        }

        [Test]
        public void EnemyHitByPlayerFaction_TakesDamageAndDies()
        {
            var e = NewEnemy("Enemy");
            try
            {
                bool died = false;
                e.Died += _ => died = true;
                // Player-faction damage must still kill — the guard isn't over-broad.
                e.TakeDamage(new DamageInfo(999f, e.transform.position, Vector3.forward, Team.Player));
                Assert.IsFalse(e.IsAlive);
                Assert.IsTrue(died, "player-faction damage failed to kill enemy");
            }
            finally
            {
                Object.DestroyImmediate(e.gameObject);
            }
        }
    }
}
