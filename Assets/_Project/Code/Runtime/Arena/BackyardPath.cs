using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Builds the greybox Backyard critical path in code (YT-38, reshaped by YT-68): a narrow patio
    /// that opens out into the lawn — a wide fight room with cover to circle and kite around — which
    /// necks down to the boss gate and opens out again into the Big Bermuda arena. Geometry is
    /// generated from <see cref="BackyardPathLayout"/> and <see cref="BackyardCover"/> as primitives,
    /// so a fresh clone / the WebGL build assemble an identical, traversable blockout with no
    /// hand-placed level file.
    ///
    /// Only shapes are set here — the stylised surface look is applied automatically by the
    /// rendering layer's WorldMaterials (flat → ground, tall → wall, short → prop), so this stays
    /// pure level geometry and doesn't fight the material system. Primitives keep their colliders,
    /// which is what walls the rooms in and makes the cover something to hide behind.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardPath : MonoBehaviour
    {
        [SerializeField] private BackyardPathLayout layout = BackyardPathLayout.Default;

        [Tooltip("Where the factory sits (the 'shed') — the far end of the lawn, so there's a run-up.")]
        [SerializeField] private float shedZ = 15f;

        [Tooltip("Radius the factory spawns robots on. Cover is kept out of this ring.")]
        [SerializeField] private float shedSpawnRadius = 3.5f;

        public BackyardPathLayout Layout => layout;
        public float ShedZ => shedZ;

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
            float patio = layout.PatioHalfWidth;
            float lawn = layout.LawnHalfWidth;
            float gate = layout.GateHalfWidth;
            float arena = layout.ArenaHalfWidth;
            float y = h * 0.5f;

            // --- Floor beyond the scene's 30 m Ground plane (which covers x/z ±15). Butts against
            // the plane's edge at z=15 — no overlap, so no z-fighting, and the existing Ground
            // object is left untouched. Wide enough for the boss arena. Top at y=0. ---
            float floorMinZ = 15f, floorMaxZ = layout.ArenaEndZ + 2f;
            Box("Arena Floor",
                new Vector3(0f, -0.05f, (floorMinZ + floorMaxZ) * 0.5f),
                new Vector3((arena + 2f) * 2f, 0.1f, floorMaxZ - floorMinZ));

            // --- Patio: the narrow entry. Side walls + a back wall so there's no retreating off
            // the path. It stays tight on purpose — the lawn beyond it reads as a release. ---
            float patioZ = (layout.StartZ + layout.LawnStartZ) * 0.5f;
            Box("Patio Wall L", new Vector3(-(patio + t * 0.5f), y, patioZ), new Vector3(t, h, layout.PatioLength));
            Box("Patio Wall R", new Vector3(patio + t * 0.5f, y, patioZ), new Vector3(t, h, layout.PatioLength));
            Box("Patio Back Wall", new Vector3(0f, y, layout.StartZ - t * 0.5f),
                new Vector3(layout.PatioWidth + t * 2f, h, t));

            // --- Patio mouth: shoulders filling patio-edge → lawn-edge, so the patio reads as a
            // doorway you step through into the open lawn. ---
            float mouthW = lawn - patio;
            float mouthX = (patio + lawn) * 0.5f;
            Box("Lawn Shoulder L", new Vector3(-mouthX, y, layout.LawnStartZ), new Vector3(mouthW, h, t));
            Box("Lawn Shoulder R", new Vector3(mouthX, y, layout.LawnStartZ), new Vector3(mouthW, h, t));

            // --- Lawn: the fight room. Wide, long, walled either side — room to circle. ---
            float lawnZ = (layout.LawnStartZ + layout.GateZ) * 0.5f;
            Box("Lawn Wall L", new Vector3(-(lawn + t * 0.5f), y, lawnZ), new Vector3(t, h, layout.LawnLength));
            Box("Lawn Wall R", new Vector3(lawn + t * 0.5f, y, lawnZ), new Vector3(t, h, layout.LawnLength));

            // --- Boss-gate shoulders: one piece each side spanning gate-edge → arena-edge. That
            // seals the lawn's end AND the step out to the wider arena, leaving just the doorway. ---
            float shoulderW = arena - gate;
            float shoulderX = (gate + arena) * 0.5f;
            Box("Gate Shoulder L", new Vector3(-shoulderX, y, layout.GateZ), new Vector3(shoulderW, h, t));
            Box("Gate Shoulder R", new Vector3(shoulderX, y, layout.GateZ), new Vector3(shoulderW, h, t));

            // --- Boss arena (gate → back). Kept clear of cover: Big Bermuda's AoEs need the space. ---
            float arenaZ = (layout.GateZ + layout.ArenaEndZ) * 0.5f;
            Box("Arena Wall L", new Vector3(-(arena + t * 0.5f), y, arenaZ), new Vector3(t, h, layout.ArenaLength));
            Box("Arena Wall R", new Vector3(arena + t * 0.5f, y, arenaZ), new Vector3(t, h, layout.ArenaLength));
            Box("Arena Back Wall", new Vector3(0f, y, layout.ArenaEndZ + t * 0.5f),
                new Vector3(arena * 2f + t * 2f, h, t));

            // --- Shed posts flanking the factory (read, don't enclose — spawns stay clear) ---
            Box("Shed Post L", new Vector3(-2.6f, 1.25f, shedZ), new Vector3(0.4f, 2.5f, 0.4f));
            Box("Shed Post R", new Vector3(2.6f, 1.25f, shedZ), new Vector3(0.4f, 2.5f, 0.4f));

            BuildCover();
        }

        /// <summary>The lawn's cover props (YT-68). Skipped rather than built badly if the set
        /// breaches a placement invariant — a wide empty lawn still plays; one with a prop sitting
        /// on the spawn ring does not.</summary>
        private void BuildCover()
        {
            var cover = BackyardCover.Default;
            if (!BackyardCover.Validate(layout, cover, shedZ, shedSpawnRadius, out string reason))
            {
                Debug.LogWarning($"[BackyardPath] cover not placed: {reason}");
                return;
            }

            // Left non-static, unlike the walls: the ambience layer gently sways anything prop-sized.
            foreach (var c in cover)
            {
                // The cylinder mesh is 2 units tall, so half its height goes into the Y scale.
                Vector3 scale = c.Shape == CoverShape.Cylinder
                    ? new Vector3(c.Size.x, c.Size.y * 0.5f, c.Size.z)
                    : c.Size;
                Spawn(c.Name, c.Shape == CoverShape.Cylinder ? PrimitiveType.Cylinder : PrimitiveType.Cube,
                    c.Center, scale);
            }
        }

        private void Box(string name, Vector3 center, Vector3 size)
        {
            var go = Spawn(name, PrimitiveType.Cube, center, size);
            go.isStatic = true;
        }

        private GameObject Spawn(string name, PrimitiveType type, Vector3 center, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(_root, false);
            go.transform.localPosition = center;
            go.transform.localScale = scale;
            return go;
        }
    }
}
