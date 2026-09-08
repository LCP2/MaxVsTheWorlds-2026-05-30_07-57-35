using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-722 — Max taking damage had no on-screen feedback beyond the health bar moving.
    /// <see cref="PlayerHealth.TakeDamage"/> now raises <see cref="HudSignals.PlayerHit"/> so
    /// CombatVfx can spawn a subtle spark; this pins the RESOLVED split the ticket asked for — a hit
    /// landing with nothing recent before it reads as an isolated PROJECTILE hit, one landing hot on
    /// the heels of the last reads as ongoing CONTACT — and that the event carries the DamageInfo's
    /// own hit point, never a stand-in.
    /// </summary>
    public sealed class MV722PlayerHitFeedbackTests
    {
        private GameObject _playerGo;
        private PlayerHealth _playerHealth;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player", typeof(CharacterController)) { tag = "Player" };
            _playerGo.AddComponent<PlayerController>();
            _playerHealth = _playerGo.AddComponent<PlayerHealth>();
            _playerHealth.Initialize(); // MV-464: exposed publicly so an EditMode test can invoke it directly
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        [Test]
        public void TakeDamage_RaisesProjectileForAnIsolatedHit_AndContactForOneHotOnItsHeels_CarryingThePoint()
        {
            Vector3? lastPoint = null;
            bool? lastIsContact = null;
            int raiseCount = 0;
            void OnHit(Vector3 point, Vector3 direction, bool isContact)
            {
                raiseCount++;
                lastPoint = point;
                lastIsContact = isContact;
            }

            HudSignals.PlayerHit += OnHit;
            try
            {
                var firstPoint = new Vector3(3f, 0.5f, -2f);
                _playerHealth.TakeDamage(new DamageInfo(10f, firstPoint, Vector3.forward, Team.Enemy));
                Assert.AreEqual(1, raiseCount, "an applied hit must raise exactly one PlayerHit");
                Assert.AreEqual(firstPoint, lastPoint, "the raised effect must carry the DamageInfo's own hit point");
                Assert.IsFalse(lastIsContact,
                    "a hit landing with nothing recent before it must resolve as an isolated PROJECTILE hit");

                var secondPoint = new Vector3(-1f, 0.5f, 4f);
                _playerHealth.TakeDamage(new DamageInfo(10f, secondPoint, Vector3.forward, Team.Enemy));
                Assert.AreEqual(2, raiseCount);
                Assert.AreEqual(secondPoint, lastPoint, "the second hit must carry ITS OWN point, not the first hit's");
                Assert.IsTrue(lastIsContact,
                    "a hit landing immediately after the last one must resolve as ongoing CONTACT, not a repeated one-shot spark");
            }
            finally
            {
                HudSignals.PlayerHit -= OnHit;
            }
        }
    }
}
