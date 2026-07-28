using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Models;
using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Builds the Backyard's set dressing out of the imported garden kit (YT-75) — the fence line,
    /// the trees over it, the flower beds, the stepping-stone path, the shed's plank walls, and real
    /// foliage standing where the greybox cover blocks used to.
    ///
    /// Self-installing off the back of <see cref="BackyardPath"/>, exactly like the material and
    /// ambience layers: no scene edit, so a fresh clone, CI and the WebGL link all dress the same
    /// yard with nothing hand-wired. If there's no path in the scene there's no yard to dress and
    /// this does nothing, which is what keeps it out of the test fixtures that build a bare arena.
    ///
    /// Two things it never does, and both are the point:
    ///
    ///   * It adds no colliders. Every prop here is scenery. What stops the player is still the
    ///     greybox — the walls, the gate, the three cover blocks — and it is untouched. So the yard
    ///     can be dressed as densely as it likes without a single re-tuned fight.
    ///
    ///   * It doesn't build ANYTHING if the set fails <see cref="BackyardDressingSet.Validate"/>.
    ///     An undressed yard is a cosmetic problem; a hedge across the mission line is a broken run.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardDressing : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BackyardDressing>() != null) return;
            if (FindFirstObjectByType<BackyardPath>() == null) return;   // no yard, nothing to dress

            new GameObject("Backyard Dressing").AddComponent<BackyardDressing>();
        }

        /// <summary>How far the ground extends past the fence, so the trees beyond it are standing on
        /// something instead of hanging over the void.</summary>
        private const float SurroundMargin = 26f;

        /// <summary>Cover pieces are dressed to the height of the box that blocks you. Round kit
        /// foliage is always wider than it is tall, so filling a tall, thin hedge means stretching it
        /// — but only this far. Past that a bush stops reading as a bush.</summary>
        private const float MaxStretch = 2.6f;

        private Transform _props;

        /// <summary>How many kit props were placed. 0 means the set was rejected — see the log.</summary>
        public int PropCount { get; private set; }

        private void Awake()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null) return;

            MapData map = path.Map;
            if (map == null)
            {
                // The dressing follows the map's own walls now. With no map there is nothing to
                // follow, and falling back to the old corridor would fence a level that isn't there.
                Debug.LogWarning("[BackyardDressing] no map loaded — the yard stays undressed.");
                return;
            }

            var set = BackyardDressingSet.Build(map);

            var cover = new List<ArenaCover>(path.CoverPieces.Count);
            foreach (CoverPiece piece in path.CoverPieces) cover.Add(piece.Cover);

            if (!BackyardDressingSet.Validate(map, set, cover, out string reason))
            {
                Debug.LogWarning($"[BackyardDressing] yard left undressed: {reason}");
                return;
            }

            Surround(map);

            _props = new GameObject("Kit Props").transform;
            _props.SetParent(transform, false);
            _props.gameObject.AddComponent<KeepsOwnMaterial>();   // covers everything below it

            foreach (DressingProp prop in set) Place(prop);
            foreach (CoverPiece piece in path.CoverPieces) DressCover(piece);

            // Timber gets wood grain, paving gets stone, the beds get turned soil (YT-77). Strictly
            // before the batch below: batching bakes each renderer's material into the combined mesh,
            // so a material swapped in afterwards is a swap that doesn't land.
            int surfaced = KitSurfaces.Dress(_props);

            // One draw call per material instead of one per prop. The props never move, so there is
            // nothing to give up by baking them together.
            StaticBatchingUtility.Combine(_props.gameObject);

            Debug.Log($"[BackyardDressing] {PropCount} kit props placed, {surfaced} re-surfaced.");
        }

        /// <summary>The ground beyond the fence, cut to the MAP's bounds rather than to a corridor's
        /// three bands — the yard turns now, and a rectangle around the old straight path would leave
        /// the nook's trees hanging over the void. Not a kit model and deliberately not marked as
        /// bringing its own material: it wants the biome's ground surface, same as the lawn, so the
        /// yard doesn't end in a hard edge with the skybox showing under the neighbours' trees.</summary>
        private void Surround(MapData map)
        {
            Rect b = map.Bounds();
            float margin = map.wallThickness + SurroundMargin;
            float width = b.width + margin * 2f;
            float depth = b.height + margin * 2f;

            // A Plane, not a Cube: the material layer classifies a surface by its mesh, and a mesh
            // called "Plane" is ground by definition — no guessing from its proportions. Unity's Plane
            // is 10 m across at unit scale, hence the tenths.
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Yard Surround";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(b.center.x, -0.05f, b.center.y);
            go.transform.localScale = new Vector3(width * 0.1f, 1f, depth * 0.1f);
            go.isStatic = true;

            // It sits under the arena floor purely to be looked at. A collider here would be a second,
            // invisible floor just below the real one.
            Strip(go);
        }

        private GameObject Place(DressingProp prop)
        {
            var go = ModelLibrary.Instantiate(PropCatalog.ResourceKey(prop.Key), _props);
            if (go == null)
            {
                // The kit isn't imported in this project. Greybox stands, nothing breaks — same
                // contract as ModelSwap. Silent per-prop; the count in the log tells the story.
                return null;
            }

            go.name = prop.Key;
            go.transform.localPosition = prop.Position;
            go.transform.localRotation = Quaternion.Euler(0f, prop.YawDeg, 0f);
            go.transform.localScale = prop.Scale;
            go.isStatic = true;

            Strip(go);
            PropCount++;
            return go;
        }

        // ---------------------------------------------------------------- cover

        /// <summary>Puts real foliage where a cover block stands. The block keeps its collider and
        /// keeps blocking exactly what it blocked before — only its renderer goes dark, with a tree
        /// or a hedge standing in the same footprint. Cover the player has learned to kite around
        /// does not move because the art landed.</summary>
        private void DressCover(CoverPiece piece)
        {
            if (piece.Cover.Dressing == CoverDressing.None || piece.Body == null) return;

            ArenaCover c = piece.Cover;
            Vector2 at = c.CenterXz;

            switch (c.Dressing)
            {
                case CoverDressing.Tree:
                    // Sized off the block's height and left uniform: a stretched tree reads as a bug.
                    // Its crown lands inside the collider, so the player never stops short of thin air.
                    Fill(PropCatalog.TreeDefault, at, c.Size.y, Mathf.Min(c.Size.x, c.Size.z), 0f);
                    Fill(PropCatalog.BushDetailed, at + new Vector2(0.55f, 0.45f), 0.6f, 1.1f, 40f);
                    break;

                case CoverDressing.Hedge:
                {
                    float depth = Mathf.Min(c.Size.x, c.Size.z);
                    int n = Mathf.Max(2, Mathf.RoundToInt(c.Size.x / depth));
                    for (int i = 0; i < n; i++)
                    {
                        float t = (i + 0.5f) / n - 0.5f;
                        Fill(PropCatalog.BushDetailed, at + new Vector2(c.Size.x * t, 0f),
                             c.Size.y, depth, i * 63f);
                    }
                    break;
                }

                case CoverDressing.Planter:
                {
                    float cell = Mathf.Min(c.Size.x, c.Size.z) * 0.5f;
                    for (int i = 0; i < 4; i++)
                    {
                        var offset = new Vector2((i % 2 == 0 ? -1f : 1f) * cell * 0.5f,
                                                 (i < 2 ? -1f : 1f) * cell * 0.5f);
                        Fill(PropCatalog.BushDetailed, at + offset, c.Size.y, cell, i * 51f);
                    }
                    Fill(PropCatalog.PotLarge, at + new Vector2(0f, -c.Size.z * 0.35f), 0.5f, 1.2f, 0f);
                    break;
                }

                case CoverDressing.Shed:
                    // No kit model for this — the garden kit never shipped a shed — so it's built the
                    // same primitive way as BackyardHomeShed's backdrop, just sized to fill THIS block
                    // rather than a fixed backdrop footprint, so the plank walls and the collider that
                    // stops the player always agree.
                    BuildShedCover(c);
                    break;
            }

            var renderer = piece.Body.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
        }

        /// <summary>Plank walls, a door and a pitched roof filling a cover block's own footprint and
        /// height (YT-172) — the block stays the collider, this only ever paints inside it, the same
        /// contract every other case in <see cref="DressCover"/> keeps.</summary>
        private void BuildShedCover(ArenaCover c)
        {
            Material plank = ShedMaterial("cover_shed_plank", ShedPlank);
            Material plankDark = ShedMaterial("cover_shed_plank_dark", ShedPlankDark);
            Material roof = ShedMaterial("cover_shed_roof", ShedRoof);

            Vector2 at = c.CenterXz;
            float wallHeight = c.Size.y * 0.75f;
            float roofRise = c.Size.y - wallHeight;

            Part("Walls", PrimitiveType.Cube,
                new Vector3(at.x, wallHeight * 0.5f, at.y),
                new Vector3(c.Size.x, wallHeight, c.Size.z), plank);

            // The door faces south, back toward the patio — the direction Max approaches from.
            float doorZ = at.y - c.Size.z * 0.5f - 0.05f;
            Part("Door", PrimitiveType.Cube,
                new Vector3(at.x, wallHeight * 0.42f, doorZ),
                new Vector3(Mathf.Min(1.2f, c.Size.x * 0.4f), wallHeight * 0.84f, 0.12f), plankDark);

            // Two tipped slabs meeting at a ridge, capped at the block's own height so the roofline
            // never pokes out past the collider that actually stops the player.
            float slope = c.Size.x * 0.62f;
            const float Pitch = 32f;
            foreach (float s in new[] { -1f, 1f })
            {
                Part("RoofSlab", PrimitiveType.Cube,
                    new Vector3(at.x + s * c.Size.x * 0.25f, wallHeight + roofRise * 0.5f, at.y),
                    new Vector3(slope, 0.2f, c.Size.z + 0.4f), roof,
                    Quaternion.Euler(0f, 0f, s * Pitch));
            }
        }

        private void Part(string name, PrimitiveType shape, Vector3 at, Vector3 scale, Material mat,
                          Quaternion? rot = null)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.SetParent(_props, false);
            go.transform.localPosition = at;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.isStatic = true;

            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            Strip(go);
            PropCount++;
        }

        // The look — matched to BackyardHomeShed's plank/roof palette, duplicated locally so this
        // dressing pass doesn't reach into that backdrop's namespace for a handful of colours.
        private static readonly Color ShedPlank = new Color(0.40f, 0.30f, 0.20f);
        private static readonly Color ShedPlankDark = new Color(0.28f, 0.21f, 0.14f);
        private static readonly Color ShedRoof = new Color(0.42f, 0.24f, 0.19f);

        private static readonly Dictionary<string, Material> ShedMaterials = new Dictionary<string, Material>();

        private static Material ShedMaterial(string key, Color c)
        {
            if (ShedMaterials.TryGetValue(key, out var cached) && cached != null) return cached;

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

            ShedMaterials[key] = m;
            return m;
        }

        /// <summary>A kit model sized to fill a slot: as wide as <paramref name="width"/> allows, then
        /// stretched up to <paramref name="height"/> — but only up to <see cref="MaxStretch"/>, past
        /// which we accept a shorter prop rather than a smeared one.</summary>
        private void Fill(string key, Vector2 at, float height, float width, float yaw)
        {
            Vector3 kit = PropCatalog.Size(key);
            if (kit == Vector3.zero) return;

            float uniform = width / Mathf.Max(kit.x, kit.z);
            float stretch = Mathf.Clamp(height / (kit.y * uniform), 1f, MaxStretch);

            Place(new DressingProp(key, at, new Vector3(uniform, uniform * stretch, uniform), yaw));
        }

        /// <summary>Dressing is scenery. Whatever the prefab or the primitive came with, it leaves
        /// here with nothing that can be walked into, shot, or steered around.</summary>
        private static void Strip(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }
    }
}
