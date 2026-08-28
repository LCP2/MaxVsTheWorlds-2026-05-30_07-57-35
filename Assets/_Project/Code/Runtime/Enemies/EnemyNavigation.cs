using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Factories;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// How a robot asks the level the way (YT-93).
    ///
    /// The routing itself is pure maths over the map (<see cref="MapRoutes"/>). This is the thin piece
    /// that knows there is a level in the scene at all: it finds the map once, and turns "where am I,
    /// where am I going" into "walk at this point". It is also the thin piece that knows which of the
    /// map's named gates are actually built and live (MV-272) — <see cref="MapRoutes"/> stays pure data
    /// and never touches a GameObject, so something has to bridge a gate's real, live
    /// <see cref="AreaGate.IsOpen"/> into the string it named in the map.
    ///
    /// It answers in WORLD space and it never keeps a robot waiting: with no level loaded — a bare test
    /// fixture, a scene with no <see cref="BackyardPath"/> — it hands back the goal unchanged, which is
    /// precisely the beeline the robots had before. Nothing here can stop a robot from chasing; the
    /// worst it can do is fail to make the chase smarter.
    /// </summary>
    public static class EnemyNavigation
    {
        private static MapData _map;
        private static bool _looked;
        private static readonly Dictionary<string, AreaGate> _areaGates = new Dictionary<string, AreaGate>(8);

        // A shut SubZoneGate is just as solid as a shut AreaGate (MV-364), and needs the exact
        // same routing bridge — kept as its own dictionary rather than folded into _areaGates
        // because the two gate kinds are different Component types with no shared base, and this
        // stays a plain reference-typed lookup (Unity's null check works correctly) rather than
        // reaching for an interface, which would lose that null check across the interface cast.
        private static readonly Dictionary<string, SubZoneGate> _subZoneGates = new Dictionary<string, SubZoneGate>(4);

        /// <summary>Increments every time this level's solved routes are invalidated — a gate opening
        /// or re-shutting, or a new level replacing this one (<see cref="Reset"/>). MV-477's per-robot
        /// <see cref="MaxWorlds.Enemies.RouteDwell"/> reads this to know a route decision has genuinely
        /// changed, rather than waiting out its own dwell for a change the level itself just forced.
        /// </summary>
        public static int RouteEpoch { get; private set; }

        private static void BumpRouteEpoch() => RouteEpoch++;

        /// <summary>Forget the level — the map, the routes solved through it, and which gates it built.
        /// Both the map and the routes are cached because finding the map means a scene search and
        /// solving the routes means sixty-four searches, and a robot asks the way every frame; a new
        /// level has to be able to say so, and start the next one with nobody still listening for an
        /// old gate that no longer exists.</summary>
        public static void Reset()
        {
            _map = null;
            _looked = false;
            _areaGates.Clear();
            _subZoneGates.Clear();
            MapRoutes.Forget();
            ZoneRouteGrid.Forget();
            RouteEpoch++;
        }

        /// <summary>
        /// Tell the router about a gate a link names, so a shut one counts as impassable rather than as
        /// an open doorway (MV-272) — <see cref="MapRuntime"/> calls this the moment it builds one.
        ///
        /// Subscribes <see cref="MapRoutes.Forget"/> to the gate's own <see cref="AreaGate.Opened"/>
        /// and <see cref="AreaGate.Closed"/>: the instant a gate breaks OR is re-shut (MV-427's arena
        /// reset calls <see cref="AreaGate.Reclose"/>), every solved route is dropped, so the very next
        /// robot that asks the way re-solves the graph with this gate's current state — no polling, no
        /// per-frame "has anything changed" check, just a routing table that knows to distrust itself
        /// the moment the level it was solved from actually changes.
        /// </summary>
        /// <param name="map">The level this gate belongs to (MV-448) — asserts <paramref name="gate"/>
        /// isn't being registered after <see cref="MapRoutes"/> has already solved routes for it, which
        /// would mean this gate was read as open by <see cref="IsGateOpen"/>'s unregistered-default
        /// while the table solved and never gets a re-solve to correct it. Null (the default) skips the
        /// check — a bare test fixture with no real map has nothing to assert against.</param>
        public static void RegisterGate(string gateId, AreaGate gate, MapData map = null)
        {
            if (string.IsNullOrEmpty(gateId) || gate == null) return;
            AssertNotSolvedYet(gateId, map);
            _areaGates[gateId] = gate;
            gate.Opened += MapRoutes.Forget;
            gate.Opened += ZoneRouteGrid.Forget;
            gate.Opened += BumpRouteEpoch;
            gate.Closed += MapRoutes.Forget;
            gate.Closed += ZoneRouteGrid.Forget;
            gate.Closed += BumpRouteEpoch;
        }

        /// <summary>Same job as the <see cref="AreaGate"/> overload, for the scene-adopted
        /// <see cref="EntityKind.Gate"/> kind (MV-364) — <see cref="MapRuntime"/> calls this the
        /// moment it places one. Before this overload existed, a shut <see cref="SubZoneGate"/>'s
        /// link always read as passable to the router (see <see cref="IsGateOpen"/>'s old
        /// unregistered-default), even though the gate was already physically solid — a robot could
        /// be routed straight at a doorway it could never actually cross.
        ///
        /// No <c>Closed</c> subscription here (MV-448): unlike <see cref="AreaGate"/>,
        /// <see cref="SubZoneGate"/> has no re-lock path at all — <c>Open</c> is a one-way sink with no
        /// method that ever sets <see cref="SubZoneGate.IsOpen"/> back to false, so there is nothing to
        /// subscribe to.</summary>
        /// <param name="map">Same MV-448 assertion as the <see cref="AreaGate"/> overload.</param>
        public static void RegisterGate(string gateId, SubZoneGate gate, MapData map = null)
        {
            if (string.IsNullOrEmpty(gateId) || gate == null) return;
            AssertNotSolvedYet(gateId, map);
            _subZoneGates[gateId] = gate;
            gate.Opened += MapRoutes.Forget;
            gate.Opened += ZoneRouteGrid.Forget;
            gate.Opened += BumpRouteEpoch;
        }

        private static void AssertNotSolvedYet(string gateId, MapData map)
        {
            Debug.Assert(!MapRoutes.HasSolvedRoutesFor(map),
                $"[EnemyNavigation] gate '{gateId}' registered after this level's routes were already " +
                "solved (MV-448) — a gate registered this late is read as open by IsGateOpen's " +
                "unregistered-default while the table solves, and nothing re-solves it afterward. " +
                "Every gate must be registered before anything calls Waypoint.");
        }

        /// <summary>Whether the named gate — if this level built or adopted one by that id at all —
        /// currently lets a robot through. A link naming a gate nothing registered reads as open:
        /// this class only knows how to distrust a gate it was told about (exposed, like
        /// <see cref="MapRoutes.Searches"/>, so a test can check the routing bridge directly rather
        /// than reconstructing a whole level around it).</summary>
        public static bool IsGateOpen(string gateId)
        {
            if (_areaGates.TryGetValue(gateId, out AreaGate areaGate))
                return areaGate == null || areaGate.IsOpen;
            if (_subZoneGates.TryGetValue(gateId, out SubZoneGate subZoneGate))
                return subZoneGate == null || subZoneGate.Unlocked;
            return true;
        }

        /// <summary>The world-space span an open <see cref="AreaGate"/>'s own leaf still occupies
        /// across its doorway (MV-448) — <see cref="MapRoutes"/>'s bridge to
        /// <see cref="AreaGate.OpenLeafSpan"/>, kept here rather than in <see cref="MapRoutes"/> for
        /// the same reason <see cref="IsGateOpen"/> is: that class stays pure and never touches a live
        /// GameObject. Null for a <see cref="SubZoneGate"/> (it clears its doorway completely — no
        /// partial leaf to report) or an unregistered/still-shut gate, both of which mean "nothing
        /// occupies the doorway" to a caller.</summary>
        private static Span? GateLeafSpan(string gateId, bool alongX) =>
            _areaGates.TryGetValue(gateId, out AreaGate gate) && gate != null && gate.IsOpen
                ? gate.OpenLeafSpan(alongX)
                : (Span?)null;

        /// <summary>The map the robots are navigating, or null if there is no level in the scene.</summary>
        public static MapData Map
        {
            get
            {
                if (_looked) return _map;

                var path = Object.FindFirstObjectByType<BackyardPath>();
                _map = path != null ? path.Map : null;
                _looked = true;
                return _map;
            }
        }

        /// <summary>
        /// The point to walk at, to get from <paramref name="from"/> toward <paramref name="goal"/> —
        /// the goal itself once they are in the same room, and the next doorway before that.
        ///
        /// The goal is the robot's OWN idea of where Max is (YT-83's last-known position), not where he
        /// actually is, and that is load-bearing: routing to Max's live position would hand every robot
        /// a perfect path to a player it cannot see, which is the omniscience that ticket removed,
        /// wearing a pathfinder as a disguise. Cover has to keep working.
        /// </summary>
        /// <param name="fromZoneId">The room id the caller has already settled on routing from (MV-447
        /// cause 3) — see <see cref="MaxWorlds.Enemies.ZoneHysteresis"/>. Null (the default) falls back
        /// to asking the map directly for whatever room <paramref name="from"/> is in right now.</param>
        /// <param name="useZoneRoute">Route around this zone's own authored cover instead of beelining
        /// across it (MV-476) — <see cref="ZoneRouteGrid"/> composed behind <see cref="MapRoutes"/>,
        /// which still answers "which doorway next"; this only changes how a robot gets there WITHIN
        /// the current room. False (the default, and every ranged/Blinker caller today) is the exact
        /// beeline behaviour this class always had — the touch-damage archetypes are the only ones
        /// that opt in (<c>RobotEnemy.UsesGridRoute</c>), because Gunner/Launcher/Blinker are explicitly
        /// out of this ticket's scope.</param>
        /// <param name="budget">Throttles how often the in-room <see cref="ZoneRouteGrid"/> step is
        /// actually re-solved (MV-611) — null (the default, and every test caller today) always
        /// resolves fresh, exactly as before; a caller that hands in its own persistent
        /// <see cref="ZoneRouteBudget"/> (<c>RobotEnemy</c>, one per robot) gets the cached step back
        /// instead on a tick <see cref="ZoneRouteBudget.ShouldResolve"/> says to skip. No effect at all
        /// when <paramref name="useZoneRoute"/> is false.</param>
        /// <param name="dt">Seconds since this caller's last call — only read to advance
        /// <paramref name="budget"/>'s own clock; ignored when <paramref name="budget"/> is null.</param>
        public static Vector3 Waypoint(Vector3 from, Vector3 goal, string fromZoneId = null,
                                       bool useZoneRoute = false, ZoneRouteBudget budget = null,
                                       float dt = 0f)
        {
            MapData map = Map;
            if (map == null) return goal;

            MapZone hereZone = fromZoneId != null ? map.Zone(fromZoneId) : map.ZoneAt(from.x, from.z);

            Vector2 next = MapRoutes.Waypoint(map,
                                              new Vector2(from.x, from.z),
                                              new Vector2(goal.x, goal.z),
                                              IsGateOpen,
                                              hereZone,
                                              GateLeafSpan);

            if (useZoneRoute && hereZone != null)
            {
                if (budget == null || budget.ShouldResolve(RouteEpoch, dt))
                {
                    Vector2? step = ZoneRouteGrid.NextStep(map, hereZone, new Vector2(from.x, from.z), next);
                    budget?.Commit(step, RouteEpoch);
                    if (step.HasValue) next = step.Value;
                }
                else if (budget.CachedStep.HasValue)
                {
                    next = budget.CachedStep.Value;
                }
            }

            return new Vector3(next.x, goal.y, next.y);
        }
    }
}
