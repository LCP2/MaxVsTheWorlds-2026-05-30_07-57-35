namespace MaxWorlds.Weapons
{
    /// <summary>
    /// The Water Balloon primary add-on's three upgrade tracks (MV-370, replacing the single
    /// "level = throw distance" ability track it used to be under <see cref="AbilityKind"/>). Same
    /// shape as <see cref="WeaponTrackKind"/>: all three are owned from run start at Level 1 — MV-370
    /// moves Water Balloon out of the shed-acquired ability pool into a primary add-on, so there is no
    /// "not owned" state here either. Every throw still costs one power cell
    /// (<see cref="MaxWorlds.Pickups.PickupWallet.TrySpendPowerCell"/>), independent of these levels.
    /// </summary>
    public enum WaterBalloonTrackKind
    {
        /// <summary>How far the balloon can be lobbed.</summary>
        Range,

        /// <summary>Radius of the splash on impact — the MV-369 splash VFX and the splash's
        /// damage/halt query both size themselves from this.</summary>
        SplashArea,

        /// <summary>Balloons per minute — each level shortens the throw cooldown. A higher fire rate
        /// is only useful if the player can feed it, since every throw still spends one cell (MV-370
        /// design note).</summary>
        RepeatFire,
    }
}
