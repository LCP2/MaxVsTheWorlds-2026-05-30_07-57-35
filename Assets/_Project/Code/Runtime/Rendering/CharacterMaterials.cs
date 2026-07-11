using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Puts the stylised character shader (YT-57) on the things that fight — Max, the robots, the
    /// boss, the factory. The counterpart to <see cref="WorldMaterials"/>, which dresses the world
    /// and deliberately skips these.
    ///
    /// The outline and rim are what make a character read at the fixed ~72° camera: from above, a
    /// silhouette is most of what you can see of a body, so lighting its edge and drawing a line
    /// around it is what separates it from the ground it's standing on.
    ///
    /// CRITICAL: the shader keeps <c>_BaseColor</c> and <c>_EmissionColor</c>, because gameplay
    /// tints these very renderers through a MaterialPropertyBlock — hit flashes, enemy wind-up
    /// tells, the factory's damage state. Swapping the material must not break a single one of
    /// them, which is why the property names are preserved rather than "improved".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterMaterials : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<CharacterMaterials>() != null) return;
            new GameObject("CharacterMaterials").AddComponent<CharacterMaterials>();
        }

        private float _rescanTimer;

        private void Update()
        {
            // Enemies are pooled and spawn continuously through a run, so new bodies keep arriving.
            // A periodic sweep is far cheaper than a per-spawn hook into the spawner (which is
            // gameplay-owned anyway).
            _rescanTimer -= Time.deltaTime;
            if (_rescanTimer > 0f) return;
            _rescanTimer = 1f;

            Apply();
        }

        /// <summary>Dress every character renderer. Returns how many were newly dressed.</summary>
        public int Apply()
        {
            var mat = MaterialLibrary.Character();
            if (mat == null) return 0;

            int dressed = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!IsCharacter(r)) continue;
                if (r.sharedMaterial == mat) continue;   // already done

                r.sharedMaterial = mat;
                dressed++;
            }
            return dressed;
        }

        /// <summary>A character is anything that can be damaged. That's the same test WorldMaterials
        /// uses to decide what to leave alone, so the two can never both claim a renderer.</summary>
        public static bool IsCharacter(Renderer r)
        {
            return r != null && r.GetComponentInParent<IDamageable>() != null;
        }
    }
}
