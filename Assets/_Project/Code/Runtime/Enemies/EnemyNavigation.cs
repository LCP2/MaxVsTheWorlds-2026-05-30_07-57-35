using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// How a robot asks the level the way (YT-93).
    ///
    /// The routing itself is pure maths over the map (<see cref="MapRoutes"/>). This is the thin piece
    /// that knows there is a level in the scene at all: it finds the map once, and turns "where am I,
    /// where am I going" into "walk at this point".
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

        /// <summary>Forget the level — the map, and the routes solved through it. Both are cached
        /// because finding the map means a scene search and solving the routes means sixty-four
        /// searches, and a robot asks the way every frame; a new level has to be able to say so.</summary>
        public static void Reset()
        {
            _map = null;
            _looked = false;
            MapRoutes.Forget();
        }

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
        public static Vector3 Waypoint(Vector3 from, Vector3 goal)
        {
            MapData map = Map;
            if (map == null) return goal;

            Vector2 next = MapRoutes.Waypoint(map,
                                              new Vector2(from.x, from.z),
                                              new Vector2(goal.x, goal.z));

            return new Vector3(next.x, goal.y, next.y);
        }
    }
}
