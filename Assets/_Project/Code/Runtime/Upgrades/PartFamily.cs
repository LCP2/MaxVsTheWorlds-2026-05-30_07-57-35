namespace MaxWorlds.Upgrades
{
    /// <summary>
    /// The three families the five hose parts group into (YT-166): HOSE (the nozzles and the
    /// capacity harness), MOVEMENT (the Acceleration engine), DETACH (the Hydro sub-assembly kit).
    /// Lets the upgrade screen label which family a reveal belongs to, so the system reads at a
    /// glance instead of every part looking like an undifferentiated drop.
    /// </summary>
    public enum PartFamily
    {
        Hose,
        Movement,
        Detach,
    }
}
