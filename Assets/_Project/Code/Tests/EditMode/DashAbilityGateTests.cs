using NUnit.Framework;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Dash became a shed-acquired ability (WV-231, spec §6) rather than base movement tech everyone
    /// starts with. <see cref="PlayerController.ShouldDash"/> is the pure trigger gate — mirroring
    /// <see cref="MaxWorlds.Combat.WaterBlaster.ShouldEmit"/> — so the "must be owned" rule is
    /// unit-testable without simulating a real Input System press.
    /// </summary>
    public sealed class DashAbilityGateTests
    {
        [Test]
        public void UnacquiredNeverDashesEvenWhenEverythingElseIsReady()
        {
            Assert.That(PlayerController.ShouldDash(pressed: true, idle: true, offCooldown: true, acquired: false),
                Is.False, "an unowned Dash must never trigger, however the press looks");
        }

        [Test]
        public void AcquiredAndReadyDashesOnPress()
        {
            Assert.That(PlayerController.ShouldDash(pressed: true, idle: true, offCooldown: true, acquired: true),
                Is.True);
        }

        [Test]
        public void MidDashIgnoresAnotherPress()
        {
            Assert.That(PlayerController.ShouldDash(pressed: true, idle: false, offCooldown: true, acquired: true),
                Is.False);
        }

        [Test]
        public void OnCooldownIgnoresAPress()
        {
            Assert.That(PlayerController.ShouldDash(pressed: true, idle: true, offCooldown: false, acquired: true),
                Is.False);
        }

        [Test]
        public void NoPressNeverDashesEvenWhenReady()
        {
            Assert.That(PlayerController.ShouldDash(pressed: false, idle: true, offCooldown: true, acquired: true),
                Is.False);
        }
    }
}
