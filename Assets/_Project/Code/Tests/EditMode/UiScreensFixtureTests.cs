using NUnit.Framework;
using MaxWorlds.Dev;
using MaxWorlds.UI;
using MaxWorlds.Weapons;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-421's RIG fixture (<see cref="UiScreensDirector.ApplyRigFixture"/>) has to reproduce the
    /// exact board state shown in MV-423.png, or the capture and the design image aren't comparable.
    /// The actual screenshot pixels are outside EditMode's reach (no GL context under
    /// <c>-nographics</c>, see <c>cc-verify.bat</c>) — this suite instead pins the one thing that
    /// EditMode can check without a play-mode capture: that the fixture leaves <see cref="RigState"/>
    /// and <see cref="PickupWallet"/> in exactly the levels/ownership/reached-ness the ticket spells
    /// out, using the same helper the real capture calls.
    /// </summary>
    public sealed class UiScreensFixtureTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            RigState.Reset();
            PickupWallet.Reset();
            AbilityCreditBank.Reset();
            PendingMorphingModule.Reset();
        }

        [Test]
        public void MatchesEveryLevelInTheTicketsSpec()
        {
            UiScreensDirector.ApplyRigFixture();

            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(4));
            Assert.That(RigState.Level("p_rng"), Is.EqualTo(3));
            Assert.That(RigState.Level("p_flw"), Is.EqualTo(2));
            Assert.That(RigState.Level("p_spr"), Is.EqualTo(0));
            Assert.That(RigState.Level("e_ff"), Is.EqualTo(2));
            Assert.That(RigState.Level("e_cel"), Is.EqualTo(1));
            Assert.That(RigState.Level("e_cd"), Is.EqualTo(3));
            Assert.That(RigState.Level("u_sen"), Is.EqualTo(1));
            Assert.That(RigState.Level("u_dmg"), Is.EqualTo(2));
            Assert.That(RigState.Level("u_rng"), Is.EqualTo(1));
            Assert.That(RigState.Level("u_hp"), Is.EqualTo(0));
            Assert.That(RigState.Level("u_mov"), Is.EqualTo(0));
            Assert.That(RigState.Level("u_cst"), Is.EqualTo(0));
        }

        [Test]
        public void LeavesTheListedCapsReachedButNotOwned()
        {
            UiScreensDirector.ApplyRigFixture();

            foreach (string cap in new[] { "s_bal", "e_mag", "m_spd", "m_tp" })
            {
                Assert.That(RigState.IsOwned(cap), Is.False, $"{cap} must not be owned");
                Assert.That(RigState.IsReached(cap), Is.True, $"{cap} must be reached");
            }
        }

        [Test]
        public void LeavesUSlotNotReached()
        {
            UiScreensDirector.ApplyRigFixture();

            // u_slt's parent is u_hp (rig_board.json), which the fixture deliberately leaves at 0.
            Assert.That(RigState.Level("u_hp"), Is.EqualTo(0));
            Assert.That(RigState.IsReached("u_slt"), Is.False);
        }

        [Test]
        public void SetsCellsTo28Of30()
        {
            UiScreensDirector.ApplyRigFixture();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(28));
            Assert.That(PickupWallet.Capacity, Is.EqualTo(30));
        }

        [Test]
        public void IsIdempotentAcrossRepeatedCaptures()
        {
            // The real director calls this once per shot (16x9, 16x10, ...) in the same play-mode
            // session — it must reset cleanly every time, not accumulate levels across calls.
            UiScreensDirector.ApplyRigFixture();
            UiScreensDirector.ApplyRigFixture();

            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(4));
            Assert.That(RigState.Level("u_dmg"), Is.EqualTo(2));
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(28));
        }

        // ---------------------------------------------------------------- MV-425/MV-519: WEAPONS button fixtures

        [Test]
        public void WeaponsButtonIdleFixtureLeavesEverythingAtZero()
        {
            UiScreensDirector.ApplyWeaponsButtonIdleFixture();

            Assert.That(HudController.ShouldShowSupercellAlert(AbilityCreditBank.Banked), Is.False);
            Assert.That(PendingMorphingModule.HasPending, Is.False);
        }

        [Test]
        public void WeaponsButtonPartsFixtureBanksExactlyFourAbilityCredits()
        {
            UiScreensDirector.ApplyWeaponsButtonPartsFixture();

            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(4));
            Assert.That(PendingMorphingModule.HasPending, Is.False);
        }

        [Test]
        public void WeaponsButtonModuleFixtureBanksADraftAndNothingElse()
        {
            UiScreensDirector.ApplyWeaponsButtonModuleFixture();

            Assert.That(PendingMorphingModule.HasPending, Is.True);
            Assert.That(HudController.ShouldShowSupercellAlert(AbilityCreditBank.Banked), Is.False);
        }

        [Test]
        public void WeaponsButtonBothFixtureBanksCreditsAndADraftTogether()
        {
            UiScreensDirector.ApplyWeaponsButtonBothFixture();

            Assert.That(AbilityCreditBank.Banked, Is.EqualTo(4));
            Assert.That(PendingMorphingModule.HasPending, Is.True);

            var alert = HudController.ComputeWeaponsButtonAlert(
                HudController.ShouldShowSupercellAlert(AbilityCreditBank.Banked),
                PendingMorphingModule.HasPending);
            Assert.That(alert, Is.EqualTo(HudController.WeaponsButtonAlert.Both));
        }
    }
}
