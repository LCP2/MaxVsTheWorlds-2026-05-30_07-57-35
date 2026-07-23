using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Hose;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Max's home shed (YT-163, relates to the intro cinematic epic YT-154): the backdrop landmark
    /// that closes the loop between the opening — Max leaves his shed, hose in hand — and the start
    /// of the playable Backyard.
    ///
    /// Decided by Lee: this is a BACKDROP behind the arena, not a porch the player stands in. It
    /// stands just past the patio's own back wall, matching the intro's exterior shed look (plank
    /// walls, pitched roof, a door), centred on the starting tap so the first hose plainly reads as
    /// running back to it. Built the same self-installing, code-only way as
    /// <see cref="BackyardBackdrop"/> and <see cref="BackyardDressing"/> — no scene wiring, and it
    /// re-installs itself on Replay the same way.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardHomeShed : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BackyardHomeShed>() != null) return;
            if (FindFirstObjectByType<BackyardPath>() == null) return;   // not the Backyard
            new GameObject("BackyardHomeShed").AddComponent<BackyardHomeShed>();
        }

        public const float Width = 6f;
        public const float Depth = 5f;
        public const float WallHeight = 2.6f;
        public const float RoofRise = 1.6f;

        /// <summary>How far clear of the patio's own back wall the shed's front face stands, past the
        /// wall's thickness and <see cref="BackyardBackdrop.MinClearance"/> — the same "never mistaken
        /// for cover, never reachable" guarantee the neighbourhood keeps, plus a visible gap so the
        /// shed reads as a landmark and not as part of the wall.</summary>
        public const float ExtraSetback = 0.4f;

        private Transform _root;

        /// <summary>Where the shed ended up. Only meaningful once <see cref="Built"/> is true.</summary>
        public Vector3 Center { get; private set; }

        public bool Built { get; private set; }

        private void Awake()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null) return;

            MapData map = path.Map;
            if (map == null)
            {
                Debug.LogWarning("[BackyardHomeShed] no map loaded — nothing for Max to have left.");
                return;
            }

            Vector3 center = PlaceFor(map);

            if (!Validate(map, center, out string reason))
            {
                Debug.LogWarning($"[BackyardHomeShed] not built: {reason}");
                return;
            }

            Center = center;

            _root = new GameObject("MaxsShed").transform;
            _root.SetParent(transform, false);
            _root.gameObject.AddComponent<KeepsOwnMaterial>();

            BuildShed(_root, center);

            StaticBatchingUtility.Combine(_root.gameObject);
            Built = true;
        }

        /// <summary>
        /// Where the shed stands: centred on <see cref="HoseDirector.StartTapPosition"/>'s X, so the
        /// tap Max plugs into at spawn sits directly in front of its door and the hose reads as coming
        /// from the shed rather than from the middle of the lawn. Its front wall is
        /// <see cref="ExtraSetback"/> beyond the clearance the arena already keeps past the patio's
        /// back wall (<see cref="MapData.Bounds"/>'s Z minimum).
        /// </summary>
        public static Vector3 PlaceFor(MapData map)
        {
            float pad = map.wallThickness + BackyardBackdrop.MinClearance;
            float frontZ = map.Bounds().yMin - pad - ExtraSetback;
            float centerZ = frontZ - Depth * 0.5f;
            return new Vector3(HoseDirector.StartTapPosition.x, 0f, centerZ);
        }

        /// <summary>True when the shed's footprint is genuinely outside every room, by the arena's own
        /// clearance rule — reusing <see cref="BackyardBackdrop.Rooms"/> so the shed is held to exactly
        /// the same "not level, not cover, never reachable" line as the rest of the backdrop.</summary>
        public static bool Validate(MapData map, Vector3 center, out string reason)
        {
            var footprint = new Rect(center.x - Width * 0.5f, center.z - Depth * 0.5f, Width, Depth);

            foreach (Rect room in BackyardBackdrop.Rooms(map))
            {
                if (!room.Overlaps(footprint)) continue;
                reason = $"shed at {center} reaches into the arena";
                return false;
            }

            reason = null;
            return true;
        }

        // --- building it -------------------------------------------------------------------------

        private static void BuildShed(Transform root, Vector3 center)
        {
            Material plank = Flat("home_shed_plank", Plank);
            Material plankDark = Flat("home_shed_plank_dark", PlankDark);
            Material roof = Flat("home_shed_roof", Roof);

            Part(root, "Walls", PrimitiveType.Cube,
                center + new Vector3(0f, WallHeight * 0.5f, 0f),
                new Vector3(Width, WallHeight, Depth), plank);

            // The door, on the FRONT wall — facing the patio, where Max just walked out of.
            float doorZ = center.z + Depth * 0.5f + 0.05f;
            Part(root, "Door", PrimitiveType.Cube,
                new Vector3(center.x, WallHeight * 0.42f, doorZ),
                new Vector3(1.6f, WallHeight * 0.84f, 0.12f), plankDark);

            // Two tipped slabs meeting at a ridge — the same box-and-wedge every other roof in this
            // yard is built from (BackyardBackdrop's houses, the intro's own exterior shed).
            float slope = Width * 0.62f;
            const float Pitch = 32f;
            foreach (float s in new[] { -1f, 1f })
            {
                Part(root, "RoofSlab", PrimitiveType.Cube,
                    center + new Vector3(s * Width * 0.25f, WallHeight + RoofRise * 0.5f, 0f),
                    new Vector3(slope, 0.24f, Depth + 0.6f), roof,
                    Quaternion.Euler(0f, 0f, s * Pitch));
            }
        }

        private static void Part(Transform root, string name, PrimitiveType shape, Vector3 at,
                                 Vector3 scale, Material mat, Quaternion? rot = null)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.isStatic = true;

            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // --- the look — matched to the intro's exterior shed (IntroPalette), duplicated locally so
        // this backdrop doesn't reach into the cinematic's namespace for a handful of colours ---------

        private static readonly Color Plank = new Color(0.40f, 0.30f, 0.20f);
        private static readonly Color PlankDark = new Color(0.28f, 0.21f, 0.14f);
        private static readonly Color Roof = new Color(0.42f, 0.24f, 0.19f);

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        private static Material Flat(string key, Color c)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var shader = MaterialLibrary.SurfaceShader;
            if (shader == null) return null;

            var m = new Material(shader)
            {
                name = key,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);

            Cache[key] = m;
            return m;
        }
    }
}
