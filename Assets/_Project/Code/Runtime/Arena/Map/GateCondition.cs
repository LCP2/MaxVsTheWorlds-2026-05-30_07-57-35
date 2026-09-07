using System;
using System.Collections.Generic;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Arena
{
    /// <summary>The parsed shape of a <see cref="WorldGate.opensWith"/>/<see cref="WorldHatch.opensWith"/>
    /// string (MV-703).</summary>
    public enum GateConditionKind
    {
        Start,
        Primary,
        Sluice,
        ShedsDestroyedBefore,
        AllShedsDestroyed,
        ReplicatorsDestroyed,
    }

    /// <summary>
    /// The parsed, resolvable form of an authored <c>opensWith</c> string (MV-703).
    /// <see cref="GateConditionKind.Start"/>/<see cref="GateConditionKind.Primary"/>/
    /// <see cref="GateConditionKind.Sluice"/> open on combat alone — <see cref="IsConditionGated"/> is
    /// false for all three, so <see cref="WorldRunner"/> never locks a gate carrying one of them (a
    /// "sluice" is only a taller-HP combat gate, same as "primary"). The other three kinds are
    /// condition-gated: the gate is held <see cref="AreaGate.Locked"/> until <see cref="IsSatisfied"/>
    /// turns true, then force-opened.
    ///
    /// World 1 authors only "start" and "primary" today (no gate carries a condition string), so this
    /// parser and every lock/unlock decision it drives are inert there by construction.
    /// </summary>
    public readonly struct GateCondition
    {
        public const string ReplicatorsDestroyedPrefix = "replicators-destroyed:";
        private const string AllToken = "all";

        public readonly GateConditionKind Kind;

        /// <summary>Only meaningful for <see cref="GateConditionKind.ReplicatorsDestroyed"/>: true for
        /// the <c>"replicators-destroyed:all"</c> form (every replicator this run has), false for an
        /// explicit area-id list (<see cref="ReplicatorAreaIds"/>).</summary>
        public readonly bool ReplicatorsAll;

        /// <summary>The area ids named by an explicit <c>"replicators-destroyed:a2,a5,..."</c> list.
        /// Empty (never null) for every other kind, and for the "all" form.</summary>
        public readonly IReadOnlyList<string> ReplicatorAreaIds;

        private GateCondition(GateConditionKind kind, bool replicatorsAll, IReadOnlyList<string> replicatorAreaIds)
        {
            Kind = kind;
            ReplicatorsAll = replicatorsAll;
            ReplicatorAreaIds = replicatorAreaIds ?? Array.Empty<string>();
        }

        /// <summary>True for every kind that holds a gate <see cref="AreaGate.Locked"/> until
        /// <see cref="IsSatisfied"/> resolves true — false for the three combat-only kinds, which a
        /// lock/unlock engine never touches.</summary>
        public bool IsConditionGated =>
            Kind == GateConditionKind.ShedsDestroyedBefore ||
            Kind == GateConditionKind.AllShedsDestroyed ||
            Kind == GateConditionKind.ReplicatorsDestroyed;

        /// <summary>Parse an authored <c>opensWith</c> string. Strips a trailing
        /// <see cref="WorldMapLoader.DeckGateSuffix"/> itself — <see cref="WorldMapLoader.TryLoad"/> only
        /// reads that suffix to pick a gate's build height, it never removes it from the string (despite
        /// its own comment's phrasing), so a parser reading the same field has to strip it again. Returns
        /// false and names the bad token in <paramref name="reason"/> for anything unrecognised — the
        /// caller (<see cref="MapValidation"/>) decides how to report it.</summary>
        public static bool TryParse(string opensWith, out GateCondition condition, out string reason)
        {
            condition = default;

            if (string.IsNullOrWhiteSpace(opensWith))
            {
                reason = "opensWith is empty";
                return false;
            }

            string token = opensWith.EndsWith(WorldMapLoader.DeckGateSuffix, StringComparison.Ordinal)
                ? opensWith.Substring(0, opensWith.Length - WorldMapLoader.DeckGateSuffix.Length)
                : opensWith;

            switch (token)
            {
                case "start":
                    condition = new GateCondition(GateConditionKind.Start, false, null);
                    reason = null;
                    return true;
                case "primary":
                    condition = new GateCondition(GateConditionKind.Primary, false, null);
                    reason = null;
                    return true;
                case "sluice":
                    condition = new GateCondition(GateConditionKind.Sluice, false, null);
                    reason = null;
                    return true;
                case "sheds-destroyed-before":
                    condition = new GateCondition(GateConditionKind.ShedsDestroyedBefore, false, null);
                    reason = null;
                    return true;
                case "all-sheds-destroyed":
                    condition = new GateCondition(GateConditionKind.AllShedsDestroyed, false, null);
                    reason = null;
                    return true;
            }

            if (token.StartsWith(ReplicatorsDestroyedPrefix, StringComparison.Ordinal))
            {
                string list = token.Substring(ReplicatorsDestroyedPrefix.Length);
                if (list == AllToken)
                {
                    condition = new GateCondition(GateConditionKind.ReplicatorsDestroyed, true, null);
                    reason = null;
                    return true;
                }

                string[] rawIds = list.Split(',');
                var ids = new List<string>(rawIds.Length);
                foreach (string raw in rawIds)
                {
                    string id = raw.Trim();
                    if (id.Length > 0) ids.Add(id);
                }

                condition = new GateCondition(GateConditionKind.ReplicatorsDestroyed, false, ids);
                reason = null;
                return true;
            }

            reason = $"opensWith '{opensWith}' is not a recognised gate condition";
            return false;
        }

        /// <summary>True once this condition's gate should be open. The three combat-only kinds are
        /// always "satisfied" — they carry no lock for a caller to lift. <paramref name="supply"/>/
        /// <paramref name="areaIndex"/> are only consulted for the two legacy shed kinds; pass null/0 to
        /// resolve a replicators-destroyed condition in isolation (exactly what an EditMode test does).</summary>
        public bool IsSatisfied(SupplyLineNetwork supply, int areaIndex)
        {
            switch (Kind)
            {
                case GateConditionKind.AllShedsDestroyed:
                    return supply != null && supply.AllShedsDestroyed;
                case GateConditionKind.ShedsDestroyedBefore:
                    return supply != null && supply.ShedsDestroyedBefore(areaIndex);
                case GateConditionKind.ReplicatorsDestroyed:
                    return ReplicatorsAll
                        ? FactoryCensus.AllReplicatorsDestroyed
                        : FactoryCensus.ReplicatorsDestroyedInAreas(ReplicatorAreaIds);
                default:
                    return true; // Start/Primary/Sluice: combat-gated, never locked by this engine.
            }
        }
    }
}
