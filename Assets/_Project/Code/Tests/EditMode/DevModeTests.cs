using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-60 — the dev/filming switches, and the energy refill they rely on.
    ///
    /// MV-464: the player-damage half of this used to live in a separate PlayMode fixture, because
    /// PlayerHealth read its starting health in Awake, which never runs in edit mode. Now that
    /// <see cref="PlayerHealth.Initialize"/> is a callable entry point (Awake just forwards to it),
    /// that half moves in here too instead of paying the PlayMode premium for it.
    /// </summary>
    public sealed class DevModeTests
    {
        [SetUp]
        public void SetUp() => DevMode.Reset();

        [TearDown]
        public void TearDown() => DevMode.Reset();

        [Test]
        public void IsOffByDefault_AndEveryGateIsClosed()
        {
            Assert.IsFalse(DevMode.Enabled);
            Assert.IsFalse(DevMode.IsInvincible);
            Assert.IsFalse(DevMode.IsInfiniteEnergy);
            Assert.IsFalse(DevMode.IsAutoFiring);
            Assert.IsFalse(DevMode.IsSpawnPaused);
        }

        [Test]
        public void SubSwitchesDoNothingUnlessTheMasterSwitchIsOn()
        {
            DevMode.Enabled = false;
            DevMode.AutoFire = true;
            DevMode.Invincible = true;

            Assert.IsFalse(DevMode.IsAutoFiring, "a sub-switch must not fire with the master off");
            Assert.IsFalse(DevMode.IsInvincible);
        }

        [Test]
        public void EnergyRefill_TopsTheTankAndNotifies()
        {
            var pool = new EnergyPool(100f, 10f, 0.5f);
            bool notified = false;
            pool.Changed += _ => notified = true;

            Assert.IsTrue(pool.TrySpend(60f));
            Assert.AreEqual(40f, pool.Current, 1e-3f);

            pool.Refill();

            Assert.AreEqual(100f, pool.Current, 1e-3f, "the tank should be full again");
            Assert.IsTrue(notified, "the HUD listens to Changed — a silent refill would desync the bar");
        }

        [Test]
        public void Player_IsAsMortalAsEver_WhenDevModeIsOff()
        {
            DevMode.Reset();

            var go = new GameObject("player", typeof(PlayerHealth));
            var hp = go.GetComponent<PlayerHealth>();
            hp.Initialize();

            float before = hp.Current;
            Assert.That(before, Is.GreaterThan(0f), "the player should start alive");

            hp.TakeDamage(new DamageInfo(10f, Vector3.zero, Vector3.forward, Team.Enemy));

            Assert.That(hp.Current, Is.LessThan(before),
                "with dev mode off, nothing about damage may change");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Player_IsInvincible_OnlyWhileDevModeIsOn()
        {
            var go = new GameObject("player", typeof(PlayerHealth));
            var hp = go.GetComponent<PlayerHealth>();
            hp.Initialize();

            float full = hp.Current;

            DevMode.Enabled = true;
            hp.TakeDamage(new DamageInfo(9999f, Vector3.zero, Vector3.forward, Team.Enemy));
            Assert.AreEqual(full, hp.Current, 1e-3f, "dev mode should make Max invincible");
            Assert.IsTrue(hp.IsAlive);

            DevMode.Reset();
            hp.TakeDamage(new DamageInfo(10f, Vector3.zero, Vector3.forward, Team.Enemy));
            Assert.That(hp.Current, Is.LessThan(full), "and mortal again the moment it's switched off");

            Object.DestroyImmediate(go);
        }
    }
}
