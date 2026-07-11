using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-60 — the dev/filming switches, and the energy refill they rely on.
    ///
    /// The player-damage half of this lives in DevModePlayTests: PlayerHealth reads its starting
    /// health in Awake, which never runs in edit mode, so an EditMode test would find a player who
    /// is already dead and prove nothing.
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
    }
}
