using NUnit.Framework;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The override layer behind the dev tuning panel (YT-105).
    ///
    /// The load-bearing property is the release gate: an override that survived into a shipped
    /// session would change how the game plays for a player, and there is no scripting define
    /// standing behind it — <see cref="DevMode.Enabled"/> is the whole guard, so it gets tested
    /// directly rather than assumed.
    /// </summary>
    public sealed class DevTuningTests
    {
        [SetUp]
        public void SetUp() => DevMode.Reset();

        [TearDown]
        public void TearDown() => DevMode.Reset();

        [Test]
        public void WithDevModeOff_EveryOverrideIsIgnored()
        {
            // Somebody dialled all seven knobs, then dev mode went off.
            DevTuning.CameraDistance = 40f;
            DevTuning.PlayerMoveSpeed = 99f;
            DevTuning.RobotMoveSpeed = 99f;
            DevTuning.BossMoveSpeed = 99f;
            DevTuning.PlayerMaxHealth = 99f;
            DevTuning.BlasterDrainPerSecond = 99f;
            DevTuning.BlasterRegenPerSecond = 99f;

            DevMode.Enabled = false;

            Assert.That(DevTuning.Or(DevTuning.PlayerMoveSpeed, 6f), Is.EqualTo(6f),
                "A dev override leaked into a non-dev session — this is the release gate.");
            Assert.That(DevTuning.Or(DevTuning.CameraDistance, 25.1f), Is.EqualTo(25.1f));
            Assert.That(DevTuning.Or(DevTuning.BlasterDrainPerSecond, 10f), Is.EqualTo(10f));
        }

        [Test]
        public void WithDevModeOn_AnOverrideWins()
        {
            DevMode.Enabled = true;
            DevTuning.PlayerMoveSpeed = 9f;

            Assert.That(DevTuning.Or(DevTuning.PlayerMoveSpeed, 6f), Is.EqualTo(9f));
        }

        [Test]
        public void WithDevModeOn_AnUnsetKnobStillReadsTheAuthoredNumber()
        {
            DevMode.Enabled = true;

            Assert.That(DevTuning.Or(DevTuning.BossMoveSpeed, 3.6f), Is.EqualTo(3.6f),
                "Turning dev mode on must not by itself change any value.");
            Assert.That(DevTuning.AnyOverride, Is.False);
        }

        [Test]
        public void TurningDevModeOff_DropsTheOverridesRatherThanParkingThem()
        {
            DevMode.Enabled = true;
            DevTuning.RobotMoveSpeed = 12f;

            DevMode.Reset();

            Assert.That(DevTuning.RobotMoveSpeed, Is.Null,
                "Overrides left set behind a switched-off dev mode would silently come back the " +
                "next time dev mode was switched on.");
            Assert.That(DevTuning.AnyOverride, Is.False);
        }

        [Test]
        public void AnOverrideOfZeroIsHonoured_NotTreatedAsUnset()
        {
            // Zero drain is a legitimate thing to want to try (infinite fire); a `!= 0` style guard
            // would silently ignore it. float? exists precisely so 0 and "unset" differ.
            DevMode.Enabled = true;
            DevTuning.BlasterDrainPerSecond = 0f;

            Assert.That(DevTuning.Or(DevTuning.BlasterDrainPerSecond, 10f), Is.EqualTo(0f));
        }
    }
}
