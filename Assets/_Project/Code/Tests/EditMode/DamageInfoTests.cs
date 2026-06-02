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
            public MockTarget(float hp) { Health = hp; }
            public void TakeDamage(in DamageInfo info)
            {
                Hits++;
                LastSoak = info.Soak;
                Health -= info.Amount;
            }
        }

        [Test]
        public void DamageInfo_CarriesFields()
        {
            var info = new DamageInfo(4f, new Vector3(1, 2, 3), Vector3.forward, soak: true);
            Assert.AreEqual(4f, info.Amount);
            Assert.AreEqual(new Vector3(1, 2, 3), info.Point);
            Assert.AreEqual(Vector3.forward, info.Direction);
            Assert.IsTrue(info.Soak);
        }

        [Test]
        public void Receiver_AccumulatesAndDies()
        {
            var t = new MockTarget(10f);
            t.TakeDamage(new DamageInfo(4f, Vector3.zero, Vector3.forward, soak: true));
            Assert.IsTrue(t.IsAlive);
            Assert.IsTrue(t.LastSoak);
            t.TakeDamage(new DamageInfo(4f, Vector3.zero, Vector3.forward));
            t.TakeDamage(new DamageInfo(4f, Vector3.zero, Vector3.forward));
            Assert.IsFalse(t.IsAlive);
            Assert.AreEqual(3, t.Hits);
        }
    }
}
