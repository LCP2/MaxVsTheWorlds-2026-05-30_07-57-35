using System.Collections.Generic;
using MaxWorlds.Factories;

namespace MaxWorlds.Arena
{
    /// <summary>A map marker's resolved Replicator state (MV-855).</summary>
    public enum ReplicatorMarkerState { Steady, Blinking, DestroyedOutline }

    /// <summary>Resolves whether a Replicator's map marker should blink (MV-855): required to open some
    /// gate that is still locked. Pure and config-driven — nothing here names a specific area or gate, so
    /// a new world's own <c>replicators-destroyed</c> gate blinks the right set with no code change here.</summary>
    public static class ReplicatorMapModel
    {
        /// <summary>Every area id named by a currently-unsatisfied <c>replicators-destroyed</c> gate in
        /// <paramref name="cfg"/>, resolved against the live <see cref="FactoryCensus"/> exactly the way
        /// <see cref="WorldRunner.RefreshGateLocks"/> itself locks/unlocks a gate — this can never
        /// disagree with which gates are actually still closed. The <c>"all"</c> form (Lee: "counts every
        /// Replicator; apply the same rule") expands to every area <see cref="FactoryCensus"/> has a
        /// Replicator registered for THIS run — not every area a design file happens to author one into
        /// — for as long as that gate is itself still unsatisfied.</summary>
        public static HashSet<string> RequiredAreaIds(WorldConfig cfg)
        {
            var required = new HashSet<string>();
            if (cfg?.gates == null) return required;

            bool needsAll = false;
            foreach (WorldGate gate in cfg.gates)
            {
                if (gate == null || string.IsNullOrEmpty(gate.opensWith)) continue;
                if (!GateCondition.TryParse(gate.opensWith, out GateCondition condition, out _)) continue;
                if (condition.Kind != GateConditionKind.ReplicatorsDestroyed) continue;
                if (condition.IsSatisfied(null, 0)) continue; // this gate's set has already stopped blinking

                if (condition.ReplicatorsAll) needsAll = true;
                else foreach (string areaId in condition.ReplicatorAreaIds) required.Add(areaId);
            }

            if (needsAll)
            {
                foreach (Replicator replicator in FactoryCensus.RegisteredReplicators)
                {
                    string areaId = FactoryCensus.AreaIdOf(replicator);
                    if (areaId != null) required.Add(areaId);
                }
            }

            return required;
        }

        /// <summary>One Replicator's resolved marker state: destroyed always wins over required, then
        /// required reads Blinking, everything else reads Steady.</summary>
        public static ReplicatorMarkerState Resolve(string areaId, bool alive, HashSet<string> requiredAreaIds)
        {
            if (!alive) return ReplicatorMarkerState.DestroyedOutline;
            return requiredAreaIds != null && areaId != null && requiredAreaIds.Contains(areaId)
                ? ReplicatorMarkerState.Blinking
                : ReplicatorMarkerState.Steady;
        }
    }
}
