using NUnit.Framework;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-659: <see cref="CellSpend.TryUpgradeNode"/> — THE RIG board's cell-bought level-raise
    /// path — must fire <see cref="WeaponSystemState.Changed"/> the same way
    /// <see cref="CellSpend.TryUnlockNode"/> already does, or anything gated on it (the water
    /// blaster's reticle and drawn stream, any HUD control keyed off a track level) stays stale
    /// until the next event that happens to re-fire Changed (previously only a respawn).
    /// </summary>
    public sealed class MV659CellUpgradeRefreshTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            WeaponSystemState.Reset();
        }

        [Test]
        public void UpgradingSpreadWithCellsFiresChanged()
        {
            RigState.AcquireCap("p_rng"); // p_spr's parent — reaches p_spr at level 1
            RigState.AcquireCap("p_spr"); // owns p_spr at level 1, ready to upgrade
            PickupWallet.SetPowerCells(CellSpend.UpgradeCostFor("p_spr", RigState.Level("p_spr")));

            int fired = 0;
            System.Action handler = () => fired++;
            WeaponSystemState.Changed += handler;
            try
            {
                Assert.That(CellSpend.TryUpgradeNode("p_spr"), Is.True);
                Assert.That(WeaponSystemState.TrackLevel(WeaponTrackKind.Spread), Is.EqualTo(2),
                    "the raise itself must still happen");
                Assert.That(fired, Is.EqualTo(1),
                    "TryUpgradeNode must fire Changed so anything gated on it (the reticle, the drawn stream) refreshes immediately");
            }
            finally
            {
                WeaponSystemState.Changed -= handler;
            }
        }
    }
}
