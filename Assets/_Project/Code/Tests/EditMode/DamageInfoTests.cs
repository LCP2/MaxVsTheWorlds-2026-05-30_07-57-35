using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Tests the damage contract that the Water Blaster (YT-35) and the enemy
    /// (YT-36) share: a mock receiver accumulates damage and dies at zero,
    /// proving the gadget→enemy decoupling works through <see cref="IDamageable"/>.
    /// </summary>
    public sealed class DamageInfoTests
    {
        private sealed class MockTarget : IDamageable
        {
            public float Health;
            public int Hits;
            public bool LastSoak;
            public bool IsAlive => Health > 0f;
            public Team Team { get; }
            public MockTarget(float hp, Team team = Team.Neutral) { Health = hp; Team = team; }
            public void TakeDamage(in DamageInfo info)
            {
                if (!DamageRules.Applies(info.Attacker, Team)) return; // friendly-fire reject
                Hits++;
                LastSoak = info.Soak;
                Health -= info.Amount;
            }
        }

        [Test]
        public void DamageInfo_CarriesFields()
        {
            var info = new DamageInfo(4f, new Vector3(1, 2, 3), Vector3.forward, Team.Player, soak: true);
            Assert.AreEqual(4f, info.Amount);
            Assert.AreEqual(new Vector3(1, 2, 3), info.Point);
            Assert.AreEqual(Vector3.forward, info.Direction);
            Assert.AreEqual(Team.Player, info.Attacker);
            Assert.IsTrue(info.Soak);
        }

        [Test]
        public void Receiver_AccumulatesAndDies()
        {
            var t = new MockTarget(10f);
            t.TakeDamage(new DamageInfo(4f, Vector3.zero, Vector3.forward, Team.Player, soak: true));
            Assert.IsTrue(t.IsAlive);
            Assert.IsTrue(t.LastSoak);
            t.TakeDamage(new DamageInfo(4f, Vector3.zero, Vector3.forward, Team.Player));
            t.TakeDamage(new DamageInfo(4f, Vector3.zero, Vector3.forward, Team.Player));
            Assert.IsFalse(t.IsAlive);
            Assert.AreEqual(3, t.Hits);
        }

        [Test]
        public void FriendlyFire_SameTeam_Rejected()
        {
            var enemy = new MockTarget(10f, Team.Enemy);
            enemy.TakeDamage(new DamageInfo(4f, Vector3.zero, Vector3.forward, Team.Enemy));
            Assert.AreEqual(10f, enemy.Health, 1e-4, "enemy took same-team damage");
            Assert.AreEqual(0, enemy.Hits);
        }

        [Test]
        public void CrossTeam_AndNeutral_Apply()
        {
            Assert.IsTrue(DamageRules.Applies(Team.Player, Team.Enemy));
            Assert.IsTrue(DamageRules.Applies(Team.Enemy, Team.Player));
            Assert.IsFalse(DamageRules.Applies(Team.Enemy, Team.Enemy));
            Assert.IsFalse(DamageRules.Applies(Team.Player, Team.Player));
            Assert.IsTrue(DamageRules.Applies(Team.Neutral, Team.Neutral)); // hazards always hit
        }
    }
}
