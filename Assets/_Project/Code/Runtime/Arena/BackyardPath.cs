using System.Collections.Generic;
using UnityEngine;

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
        [Tooltip("Which map to build — a JSON file under Resources/Maps/.")]
        [SerializeField] private string mapKey = MapLibrary.BackyardSlice;

        private static readonly List<CoverPiece> NoCover = new List<CoverPiece>(0);

        private MapData _map;
        private MapBuild _build;
        private BackyardPathLayout _layout = BackyardPathLayout.Default;

        /// <summary>The map this level was built from. Null if it failed to load.</summary>
        public MapData Map => _map;

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
            if (string.IsNullOrWhiteSpace(mapKey)) mapKey = MapLibrary.BackyardSlice;

            _map = MapLibrary.Load(mapKey);
            if (_map == null) return;   // MapLibrary has already said why

            // A map that would not play does not get built. Half a level is worse than a loud error:
            // it looks like it worked.
            if (!MapValidation.Validate(_map, out string reason))
            {
                Debug.LogError($"[BackyardPath] map '{mapKey}' is not playable: {reason}");
                _map = null;
                return;
            }

            _layout = MapLayoutBridge.ToLayout(_map);
            _build = MapRuntime.Build(_map, transform);
        }
    }
}
