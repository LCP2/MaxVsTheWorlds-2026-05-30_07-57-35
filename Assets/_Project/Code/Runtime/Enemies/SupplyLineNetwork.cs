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

        /// <summary>Destroyed SHED entity ids (MV-475) — not area ids. Tracking moved from "is this
        /// area's shed down" to "is this specific shed down" because an area can now carry several.</summary>
        private readonly HashSet<string> _destroyedSheds = new HashSet<string>();

        /// <summary>Every shed entity id this world carries, keyed by its area — built once from
        /// <see cref="WorldArea.Sheds"/>/<see cref="WorldArea.ShedId"/> so every other method here just
        /// looks a shed's area up rather than re-deriving the id rule.</summary>
        private readonly Dictionary<string, List<string>> _areaSheds = new Dictionary<string, List<string>>();

        /// <summary>Every area's authored <see cref="WorldArea.index"/>, keyed by area id — what
        /// <see cref="ShedsDestroyedBefore"/> (MV-560) walks to decide which sheds sit "before" a
        /// mid-run boss, since <see cref="_areaSheds"/> only knows a shed's area id, not that area's
        /// position in the run.</summary>
        private readonly Dictionary<string, int> _areaIndex = new Dictionary<string, int>();

        /// <summary>The reverse of <see cref="_areaSheds"/> — which area a given shed entity id belongs
        /// to, so <see cref="DestroyShed"/> can take just the shed id (the identity a factory actually
        /// dies with) and still know which area's line it affects.</summary>
        private readonly Dictionary<string, string> _shedArea = new Dictionary<string, string>();

        public SupplyLineNetwork(WorldConfig cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));

            foreach (WorldArea a in _cfg.areas)
            {
                if (a == null) continue;
                _areaIndex[a.id] = a.index;

                WorldShed[] sheds = a.Sheds();
                if (sheds.Length == 0) continue;

                var ids = new List<string>(sheds.Length);
                for (int i = 0; i < sheds.Length; i++)
                {
                    string shedId = a.ShedId(i, sheds.Length);
                    ids.Add(shedId);
                    _shedArea[shedId] = a.id;
                }
                _areaSheds[a.id] = ids;
            }
        }

        /// <summary>Fired once, the instant a shed's supply line halts — the hook a future runner wires
        /// to the existing shed-destroyed reward path (<c>HudSignals.FactoryDestroyed</c> already drops
        /// a device/part pickup on every factory kill via <c>PickupDirector</c>; this event is what
        /// tells the ORIGINATION layer which area's line just went quiet, so it is not duplicating that
        /// drop, just naming the area it happened in).</summary>
        public event Action<string> SupplyLineHalted;

        public bool IsShedArea(string areaId) => _areaSheds.ContainsKey(areaId);

        /// <summary>Is the shed at entity id <paramref name="shedId"/> (not an area id — MV-475)
        /// destroyed. False for an id that isn't a shed at all.</summary>
        public bool IsShedDestroyed(string shedId) => _destroyedSheds.Contains(shedId);

        /// <summary>A shed area is supplying while ANY of its sheds still stands (MV-475 — an area with
        /// several sheds keeps feeding its line until the last one falls). Not a shed area at all is
        /// never "supplying" — there is no line to halt.</summary>
        public bool IsSupplying(string areaId)
        {
            if (!_areaSheds.TryGetValue(areaId, out List<string> shedIds)) return false;
            foreach (string shedId in shedIds)
                if (!_destroyedSheds.Contains(shedId)) return true;
            return false;
        }

        /// <summary>Report a shed destroyed, by its ENTITY id (MV-475 — a factory dies with a shed id,
        /// not its area's id, the moment an area can carry more than one). Halts only that one shed;
        /// raises <see cref="SupplyLineHalted"/>, naming the AREA, only once that area's LAST standing
        /// shed falls. A no-op on an id that isn't a known shed, or a shed already reported destroyed
        /// (idempotent, so a duplicate death report never double-fires the event).</summary>
        public void DestroyShed(string shedId)
        {
            if (!_shedArea.TryGetValue(shedId, out string areaId) || !_destroyedSheds.Add(shedId)) return;
            if (!IsSupplying(areaId)) SupplyLineHalted?.Invoke(areaId);
        }

        /// <summary>True once every authored shed in this world has been destroyed — the boss gate's
        /// <c>all-sheds-destroyed</c> condition (<see cref="MapValidation.ValidateWorldConfig"/>'s
        /// boss-gate rule guarantees exactly this string names the gate waiting on it). False for a
        /// world authored with no sheds at all — nothing has been cleared, there was just never
        /// anything to clear (same convention as <c>FactoryCensus.AllDown</c>). Counts every SHED, not
        /// every shed area (MV-475).</summary>
        public bool AllShedsDestroyed
        {
            get
            {
                bool anyShed = false;
                foreach (List<string> shedIds in _areaSheds.Values)
                {
                    foreach (string shedId in shedIds)
                    {
                        anyShed = true;
                        if (!_destroyedSheds.Contains(shedId)) return false;
                    }
                }
                return anyShed;
            }
        }

        /// <summary>True once every shed in every area with a LOWER <see cref="WorldArea.index"/> than
        /// <paramref name="areaIndex"/> has been destroyed — a mid-run boss gate's
        /// <c>sheds-destroyed-before</c> condition (MV-560; <see cref="MapValidation.ValidateWorldConfig"/>'s
        /// boss-gate rule guarantees only this string or <c>all-sheds-destroyed</c> names a gate waiting
        /// on a shed condition). Same "false when there is nothing to clear" convention as
        /// <see cref="AllShedsDestroyed"/> — a mid-run boss authored with no earlier sheds at all is a
        /// content bug, not a legitimately pre-opened gate. Counts every SHED, not every shed area
        /// (MV-475), same as <see cref="AllShedsDestroyed"/>.</summary>
        public bool ShedsDestroyedBefore(int areaIndex)
        {
            bool anyShed = false;
            foreach (KeyValuePair<string, List<string>> areaShedIds in _areaSheds)
            {
                if (!_areaIndex.TryGetValue(areaShedIds.Key, out int index) || index >= areaIndex) continue;

                foreach (string shedId in areaShedIds.Value)
                {
                    anyShed = true;
                    if (!_destroyedSheds.Contains(shedId)) return false;
                }
            }
            return anyShed;
        }

        /// <summary>The same set <see cref="ShedsDestroyedBefore"/> tests, counted rather than
        /// reduced to a bool (MV-571) — how many sheds sit before <paramref name="areaIndex"/> and how
        /// many of them are down, so a locked gate can show "SHEDS 3 / 8" instead of nothing.</summary>
        public void ShedProgressBefore(int areaIndex, out int destroyed, out int total)
        {
            destroyed = 0;
            total = 0;
            foreach (KeyValuePair<string, List<string>> areaShedIds in _areaSheds)
            {
                if (!_areaIndex.TryGetValue(areaShedIds.Key, out int index) || index >= areaIndex) continue;

                foreach (string shedId in areaShedIds.Value)
                {
                    total++;
                    if (_destroyedSheds.Contains(shedId)) destroyed++;
                }
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
