namespace MaxWorlds.Arena
{
    /// <summary>A map marker's resolved objective state (MV-1119): every shed, hutch, reactor,
    /// Replicator and boss that still needs destroying resolves the same way, in every world.</summary>
    public enum ObjectiveMarkerState { Blinking, Destroyed }

    /// <summary>Resolves whether an objective's map marker should blink (MV-1119). Pure: alive, full
    /// stop. Lee's ruling (2026-10-06): "Sheds / replicators / things that need to be destroyed on all
    /// levels need to be shown on the maps as blinking red lights as per World 2" applies the same
    /// treatment to every still-alive shed, reactor, Replicator and boss, in every world — superseding
    /// MV-855's narrower <c>ReplicatorMapModel</c>/<c>ReplicatorMarkerState</c>, which only blinked a
    /// Replicator whose area was named by a currently-unsatisfied gate and read everything else Steady.
    /// A Replicator not currently gating anything still needs destroying to finish the level, so that
    /// carve-out no longer applies.</summary>
    public static class ObjectiveMapModel
    {
        public static ObjectiveMarkerState Resolve(bool alive) =>
            alive ? ObjectiveMarkerState.Blinking : ObjectiveMarkerState.Destroyed;
    }
}
