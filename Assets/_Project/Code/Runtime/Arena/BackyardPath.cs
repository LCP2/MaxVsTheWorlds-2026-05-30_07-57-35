using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Arena
{
    /// <summary>A cover prop as it ended up in the world: the authored data, and the primitive that
    /// carries its collider. The art pass (YT-75) needs both — the data to know what the piece is
    /// meant to be, the object to hide once a real model stands in its place.</summary>
    public readonly struct CoverPiece
    {
        public readonly ArenaCover Cover;
        public readonly GameObject Body;

        public CoverPiece(ArenaCover cover, GameObject body)
        {
            Cover = cover; Body = body;
        }
    }

    /// <summary>
    /// Loads the map and builds it (YT-89). This used to BE the level — a hand-written sequence of
    /// Box() calls, with its dimensions serialized into <c>Backyard_Slice.unity</c>, which is what
    /// made a layout change slow: the scene's copy of the numbers overrode the code's, the actors
    /// standing in the level were placed separately by hand, and an editor scaffold had to exist for
    /// the sole purpose of shoving the two back into agreement (Stage68).
    ///
    /// Now it is a host, not a level. The level is a JSON map under <c>Resources/Maps/</c>; this
    /// component names one, validates it, and hands it to <see cref="MapRuntime"/>. Reshaping the
    /// arena means editing a text file (or dragging a room in the map editor) — no scene edit, no
    /// recompile, no scaffold.
    ///
    /// It keeps its name and its place in the scene because the dressing, backdrop, minimap and map
    /// panel all find the level through it; <see cref="Layout"/> is now a view derived from the map
    /// (<see cref="MapLayoutBridge"/>) rather than a field a human typed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardPath : MonoBehaviour
    {
        [Tooltip("Which world to build — a JSON file under Resources/Worlds/ (MV-270). Authored in " +
                 "code, not the inspector default: a value baked into the scene would silently shadow " +
                 "this the way BlasterTuning's old serialized fields once did.")]
        [SerializeField] private string worldKey = WorldLibrary.World1;

        private static readonly List<CoverPiece> NoCover = new List<CoverPiece>(0);

        private MapData _map;
        private MapBuild _build;
        private BackyardPathLayout _layout = BackyardPathLayout.Default;
        private AreaAccumulationDirector _areaDirector;
        private WorldRunner _worldRunner;

        /// <summary>The map this level was built from. Null if it failed to load.</summary>
        public MapData Map => _map;

        /// <summary>Drives the gated arena's ambient population (v0.5 recut spec §2, MV-242). Null if
        /// the map failed to load — there is no run to populate.</summary>
        public AreaAccumulationDirector AreaDirector => _areaDirector;

        /// <summary>The map, described in the rooms-and-gate terms the minimap and the dressing pass
        /// read. Derived from <see cref="Map"/> — not a source of truth.</summary>
        public BackyardPathLayout Layout => _layout;

        public float ShedZ => MapLayoutBridge.ShedZ(_map);
        public float ShedSpawnRadius => MapValidation.SpawnRadius;

        /// <summary>The cover that actually got built — empty if the map failed to load or validate.
        /// The dressing layer reads this rather than the authored set, so it can never plant a tree
        /// where no cover was placed.</summary>
        public IReadOnlyList<CoverPiece> CoverPieces => _build?.Cover ?? (IReadOnlyList<CoverPiece>)NoCover;

        private void Awake()
        {
            if (string.IsNullOrWhiteSpace(worldKey)) worldKey = WorldLibrary.World1;

            WorldConfig cfg = WorldLibrary.Load(worldKey);
            if (cfg == null) return;   // WorldLibrary has already said why

            if (!WorldMapLoader.TryLoad(cfg, out _map, out string reason))
            {
                Debug.LogError($"[BackyardPath] world '{worldKey}' is not playable: {reason}");
                _map = null;
                return;
            }

            _layout = MapLayoutBridge.ToLayout(_map);
            _build = MapRuntime.Build(_map, transform);

            // ConfigureWorld MUST run before Configure (MV-311): Configure() fills area 1 synchronously,
            // so if the world config lands after that fill, area 1 permanently misses the budget-solver
            // path and is stuck on the legacy AreaPopulation fallback for the rest of the run.
            _areaDirector = new GameObject("Area Accumulation").AddComponent<AreaAccumulationDirector>();
            _areaDirector.transform.SetParent(transform, false);
            _areaDirector.ConfigureWorld(cfg);
            _areaDirector.Configure(_map, _build.Cover);

            // MV-362/MV-396: sentinels "do not travel between areas... passing a gate clears them and
            // refunds the slots" — "passing" means Max has actually walked through, not merely that the
            // gate broke, so this hangs off the position-driven PlayerCrossedIntoArea, not any AreaGate's
            // Opened (which fires early, for population, well before Max is through the doorway).
            _areaDirector.PlayerCrossedIntoArea += _ => Sentinel.DestroyAllActive();

            _worldRunner = new GameObject("World Runner").AddComponent<WorldRunner>();
            _worldRunner.transform.SetParent(transform, false);
            _worldRunner.Configure(cfg, _map, _build, _areaDirector);

            WireAreaGatesToPopulation();
        }

        /// <summary>Gives each area a head start on its ambient population (MV-245): the moment the
        /// gate guarding it breaks, not the moment the player is later found standing inside it — the
        /// gap between those two is exactly the time the player still needs to walk through the
        /// doorway, which is what lets the room be fully populated before they can see it.</summary>
        private void WireAreaGatesToPopulation()
        {
            if (_map.links == null) return;

            foreach (MapLink link in _map.links)
            {
                if (link == null || string.IsNullOrEmpty(link.gate)) continue;

                int nextArea = AreaAccumulationDirector.AreaIndexOf(link.to);
                if (nextArea <= 0) continue;   // e.g. area10 -> compost: the boss arena, not gated by this

                if (!_build.Actors.TryGetValue(link.gate, out GameObject gateGo) || gateGo == null) continue;

                AreaGate gate = gateGo.GetComponent<AreaGate>();
                if (gate == null) continue;   // e.g. boss_gate is the adopted SubZoneGate, not an AreaGate

                gate.Opened += () => _areaDirector.EnterArea(nextArea);
            }
        }
    }
}
