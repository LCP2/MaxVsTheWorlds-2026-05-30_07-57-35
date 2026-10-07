namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Implemented by every PRIMARY weapon component (the RCDA's <see cref="MaxWorlds.Combat.WaterBlaster"/>,
    /// the LPPE's <see cref="MaxWorlds.Combat.PulseLaser"/>, UNDERTOW) so the floating gauge over Max
    /// (<see cref="MaxWorlds.Player.PlayerHealth.PrimaryEnergyNormalized"/>) can read whichever one is
    /// actually equipped (<see cref="WeaponSystemState.ActivePrimary"/>) without special-casing each
    /// concrete type.
    ///
    /// MV-1088: before this interface existed, the gauge special-cased the LPPE and fell through to the
    /// RCDA's own tank for every other primary — which silently included UNDERTOW once World 3 shipped
    /// it, so the gauge showed the idle WaterBlaster's full tank while UNDERTOW's own tank, the one
    /// actually draining, went undisplayed. A fourth primary added the same way would repeat the bug;
    /// implementing this interface is now the only thing a new primary needs to do to be read correctly.
    /// </summary>
    public interface IPrimaryEnergy
    {
        /// <summary>This weapon's own tank, 0..1 — the same contract every primary's own
        /// EnergyNormalized/WaterNormalized property already had, just reachable without the caller
        /// knowing the concrete weapon type.</summary>
        float EnergyNormalized { get; }
    }
}
