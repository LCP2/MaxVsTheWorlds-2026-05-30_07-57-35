using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Builds the greybox Backyard critical path in code (YT-38): a walled corridor
    /// patio → lawn → shed → boss gate, opening into the Big Bermuda arena. Geometry is
    /// generated from <see cref="BackyardPathLayout"/> as primitives, so a fresh clone / the
    /// WebGL build assemble an identical, traversable blockout with no hand-placed level file.
    ///
    /// Only shapes are set here — the stylised surface look is applied automatically by the
    /// rendering layer's WorldMaterials (flat → ground, tall → wall), so this stays pure level
    /// geometry and doesn't fight the material system. Primitives keep their box colliders, which
    /// is what channels Max down the lane and walls the arena in.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardPath : MonoBehaviour
    {
        [SerializeField] private BackyardPathLayout layout = BackyardPathLayout.Default;

        [Tooltip("Where the factory sits (the 'shed'). A couple of posts flank it to read as a shed.")]
        [SerializeField] private float shedZ = 10f;

        public BackyardPathLayout Layout => layout;

        private Transform _root;

        private void Awake()
        {
            if (!layout.IsValid())
            {
                Debug.LogWarning("[BackyardPath] layout invalid; using default.");
                layout = BackyardPathLayout.Default;
            }
            Build();
        }

        private void Build()
        {
            _root = new GameObject("BackyardPath Geometry").transform;
            _root.SetParent(transform, false);

            float h = layout.WallHeight;
            float t = layout.WallThickness;
            float lane = layout.LaneHalfWidth;
            float arena = layout.ArenaHalfWidth;
            float y = h * 0.5f;

            // --- Arena floor: extends the ground past the existing 30 m plane (which reaches
            // z≈15) out to the arena back wall. Butts against the plane's edge — no overlap, so no
            // z-fighting, and the existing Ground object is left untouched. Top at y=0. ---
            float floorMinZ = 15f, floorMaxZ = layout.ArenaEndZ + 2f;
            Box("Arena Floor",
                new Vector3(0f, -0.05f, (floorMinZ + floorMaxZ) * 0.5f),
                new Vector3((arena + 2f) * 2f, 0.1f, floorMaxZ - floorMinZ));

            // --- Corridor side walls (patio → gate) ---
            float corrZ = (layout.StartZ + layout.GateZ) * 0.5f;
            Box("Corridor Wall L", new Vector3(-(lane + t * 0.5f), y, corrZ), new Vector3(t, h, layout.CorridorLength));
            Box("Corridor Wall R", new Vector3(lane + t * 0.5f, y, corrZ), new Vector3(t, h, layout.CorridorLength));

            // --- Patio back wall (no retreating off the path) ---
            Box("Patio Back Wall", new Vector3(0f, y, layout.StartZ - t * 0.5f), new Vector3(layout.LaneWidth + t * 2f, h, t));

            // --- Boss-gate shoulders: fill lane-edge → arena-edge, leaving the gate opening ---
            float shoulderW = arena - lane;
            float shoulderX = (lane + arena) * 0.5f;
            Box("Gate Shoulder L", new Vector3(-shoulderX, y, layout.GateZ), new Vector3(shoulderW, h, t));
            Box("Gate Shoulder R", new Vector3(shoulderX, y, layout.GateZ), new Vector3(shoulderW, h, t));

            // --- Arena walls (gate → back) ---
            float arenaZ = (layout.GateZ + layout.ArenaEndZ) * 0.5f;
            Box("Arena Wall L", new Vector3(-(arena + t * 0.5f), y, arenaZ), new Vector3(t, h, layout.ArenaLength));
            Box("Arena Wall R", new Vector3(arena + t * 0.5f, y, arenaZ), new Vector3(t, h, layout.ArenaLength));
            Box("Arena Back Wall", new Vector3(0f, y, layout.ArenaEndZ + t * 0.5f), new Vector3(arena * 2f + t * 2f, h, t));

            // --- Shed posts flanking the factory (read, don't enclose — spawns stay clear) ---
            Box("Shed Post L", new Vector3(-2.6f, 1.25f, shedZ), new Vector3(0.4f, 2.5f, 0.4f));
            Box("Shed Post R", new Vector3(2.6f, 1.25f, shedZ), new Vector3(0.4f, 2.5f, 0.4f));
        }

        private void Box(string name, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_root, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.isStatic = true;
        }
    }
}
