using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Puts the stylised materials onto the greybox (YT-50). Self-installing, so the scene file
    /// stays untouched and CI/WebGL get the same surfaces as the editor.
    ///
    /// It deliberately only dresses *world* surfaces — ground, walls, props. Anything that can be
    /// damaged (Max, enemies, the boss, the Mower Hutch) is left alone: those renderers are tinted
    /// at runtime by gameplay to show hit flashes, tells and damage state, and re-skinning them
    /// here would mean this system and the gameplay code fighting over the same colour every frame.
    /// They keep their existing look until they get real models.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldMaterials : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<WorldMaterials>() != null) return;
            new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
        }

        [SerializeField] private BiomePalette palette = BiomePalette.Backyard;

        private void Awake() => Apply(palette);

        /// <summary>Dress every world surface in the biome. Returns how many renderers it touched.</summary>
        public int Apply(BiomePalette p)
        {
            palette = p;
            MaterialLibrary.Palette = p;

            int dressed = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!IsWorldSurface(r)) continue;

                var mat = MaterialLibrary.Surface(KindOf(r));
                if (mat == null) continue;

                r.sharedMaterial = mat;
                dressed++;
            }
            return dressed;
        }

        /// <summary>World surfaces are the things gameplay never recolours, and that don't already
        /// have a look of their own. Anything damageable is owned by gameplay's tint logic (see the
        /// class summary); anything marked <see cref="KeepsOwnMaterial"/> is imported art that
        /// arrived with its materials attached, and repainting a tree in flat lawn-green is the
        /// opposite of what the art pass is for.</summary>
        public static bool IsWorldSurface(Renderer r)
        {
            if (r == null) return false;
            if (r.GetComponentInParent<KeepsOwnMaterial>() != null) return false;
            return r.GetComponentInParent<IDamageable>() == null;
        }

        /// <summary>Classify by shape rather than by name: a flat plane is ground, a tall box is a
        /// wall, anything else is a prop. Name-matching would break the moment the arena is
        /// generated rather than scaffolded.</summary>
        public static SurfaceKind KindOf(Renderer r)
        {
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null && mesh.name.StartsWith("Plane")) return SurfaceKind.Ground;

            Vector3 size = r.bounds.size;
            bool flat = size.y < 0.25f && (size.x > 4f || size.z > 4f);
            if (flat) return SurfaceKind.Ground;

            return size.y >= 2f ? SurfaceKind.Wall : SurfaceKind.Prop;
        }
    }
}
