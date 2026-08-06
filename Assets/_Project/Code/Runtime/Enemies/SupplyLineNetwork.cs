using System;
using System.Collections.Generic;
using MaxWorlds.Arena;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Origination — sheds as reinforcement engines (World &amp; Difficulty Framework, Confluence MVW
    /// 34439170 §6, MV-269): a standing shed streams reinforcements to its own area and every
    /// gate-adjacent shed-free area (that is how a shed-free area gets fed at all), routed along the
    /// gate graph toward the player's current front. Destroying a shed halts only ITS OWN line — a
    /// different, still-standing shed keeps supplying whatever it already reaches.
    ///
    /// Stateful (which sheds are destroyed) but otherwise the same idiom as <see cref="DifficultyEngine"/>:
    /// pure, unit-testable, and reads no live run/scene state — a caller (a future runner) is
    /// responsible for calling <see cref="DestroyShed"/> when a shed's factory actually dies.
    /// </summary>
    public sealed class SupplyLineNetwork
    {
        private readonly WorldConfig _cfg;
        private readonly HashSet<string> _destroyedSheds = new HashSet<string>();

        public SupplyLineNetwork(WorldConfig cfg) => _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));

        /// <summary>Fired once, the instant a shed's supply line halts — the hook a future runner wires
        /// to the existing shed-destroyed reward path (<c>HudSignals.FactoryDestroyed</c> already drops
        /// a device/part pickup on every factory kill via <c>PickupDirector</c>; this event is what
        /// tells the ORIGINATION layer which area's line just went quiet, so it is not duplicating that
        /// drop, just naming the area it happened in).</summary>
        public event Action<string> SupplyLineHalted;

        public bool IsShedArea(string areaId)
        {
            WorldArea a = _cfg.Area(areaId);
            return a != null && a.hasShed;
        }

        public bool IsShedDestroyed(string areaId) => _destroyedSheds.Contains(areaId);

        /// <summary>A shed area that is standing (authored with a shed, not yet destroyed) is
        /// supplying. Not a shed at all is never "supplying" — there is no line to halt.</summary>
        public bool IsSupplying(string areaId) => IsShedArea(areaId) && !IsShedDestroyed(areaId);

        /// <summary>Report a shed destroyed — halts its line and raises <see cref="SupplyLineHalted"/>.
        /// A no-op on an area with no shed, or a shed already reported destroyed (idempotent, so a
        /// duplicate death report never double-fires the event).</summary>
        public void DestroyShed(string areaId)
        {
            if (!IsShedArea(areaId) || !_destroyedSheds.Add(areaId)) return;
            SupplyLineHalted?.Invoke(areaId);
        }

        /// <summary>True once every authored shed in this world has been destroyed — the boss gate's
        /// <c>all-sheds-destroyed</c> condition (<see cref="MapValidation.ValidateWorldConfig"/>'s
        /// boss-gate rule guarantees exactly this string names the gate waiting on it). False for a
        /// world authored with no sheds at all — nothing has been cleared, there was just never
        /// anything to clear (same convention as <c>FactoryCensus.AllDown</c>).</summary>
        public bool AllShedsDestroyed
        {
            get
            {
                bool anyShed = false;
                foreach (WorldArea a in _cfg.areas)
                {
                    if (a == null || !a.hasShed) continue;
                    anyShed = true;
                    if (!IsShedDestroyed(a.id)) return false;
                }
                return anyShed;
            }
        }

        /// <summary>Every area a STANDING shed at <paramref name="shedAreaId"/> currently tops up: its
        /// own area, plus every gate-adjacent area that has no shed of its own. Empty for a
        /// non-shed area or a destroyed shed — its line has nothing to top up.</summary>
        public IEnumerable<string> Recipients(string shedAreaId)
        {
            if (!IsSupplying(shedAreaId)) yield break;

            yield return shedAreaId;
            foreach (string neighbor in GateNeighbors(shedAreaId))
                if (!IsShedArea(neighbor)) yield return neighbor;
        }

        /// <summary>The gate-graph path a standing shed's stream walks to reach the player's current
        /// front — "reinforcements bleeding forward through the gate" (spec §6), literally the same hop
        /// <see cref="Recipients"/> counts one step out. Null if the shed is destroyed, or the front is
        /// not reachable from it through the gate graph at all.</summary>
        public List<string> RouteToFront(string shedAreaId, string frontAreaId)
        {
            if (!IsSupplying(shedAreaId)) return null;

            var cameFrom = new Dictionary<string, string> { [shedAreaId] = null };
            var queue = new Queue<string>();
            queue.Enqueue(shedAreaId);

            while (queue.Count > 0)
            {
                string here = queue.Dequeue();
                if (here == frontAreaId) break;

                foreach (string next in GateNeighbors(here))
                {
                    if (cameFrom.ContainsKey(next)) continue;
                    cameFrom[next] = here;
                    queue.Enqueue(next);
                }
            }

            if (!cameFrom.ContainsKey(frontAreaId)) return null;

            var path = new List<string>();
            for (string at = frontAreaId; at != null; at = cameFrom[at]) path.Add(at);
            path.Reverse();
            return path;
        }

        private IEnumerable<string> GateNeighbors(string areaId)
        {
            if (_cfg.gates == null) yield break;

            foreach (WorldGate g in _cfg.gates)
            {
                if (g?.from == null || g.to == null) continue;
                if (g.from.area == areaId) yield return g.to.area;
                else if (g.to.area == areaId) yield return g.from.area;
            }
        }
    }
}
