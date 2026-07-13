using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Bosses;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>Marks a renderer as already dressed, so it is only ever processed once.</summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceSkinned : MonoBehaviour { }

    /// <summary>
    /// Dresses anything that appears at RUNTIME and isn't a character (YT-61).
    ///
    /// THIS IS THE MAGENTA. Every one of these is created with GameObject.CreatePrimitive:
    ///
    ///     DamageZone      the boss's grass/blade AoEs   <- spawned mid-fight, never dressed
    ///     BackyardPath    the arena path blocks
    ///     EnemySpawner    the robot bodies              <- covered by CharacterSkin
    ///
    /// CreatePrimitive hands the object Unity's BUILT-IN default material. That material has no URP
    /// subshader, so in a player build URP draws it with the magenta error shader. It looks correct
    /// in the editor, where Unity substitutes the pipeline's default — which is why this survived
    /// every editor check and only ever showed up on the deployed link.
    ///
    /// WorldMaterials dresses the scene once, at load. The boss's damage zones are created DURING
    /// the fight, so they were never dressed at all: a bright flat magenta wedge on the ground every
    /// time Big Bermuda charges. That is the magenta QA kept reporting and I kept failing to
    /// reproduce — I was never fighting the boss.
    ///
    /// Rather than patch each spawn site (three today, and the next one will be forgotten), this
    /// sweeps continuously: anything undressed gets a real material within a frame of appearing. The
    /// sweep is cheap — a handful of renderers — and it closes the whole class of bug, including the
    /// ones nobody has written yet.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeSurfaceDirector : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<RuntimeSurfaceDirector>() != null) return;
            new GameObject("RuntimeSurfaces").AddComponent<RuntimeSurfaceDirector>();
        }

        private void Update() => Sweep();

        private void Sweep()
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.GetComponent<SurfaceSkinned>() != null) continue;   // done already
                if (r.GetComponent<CharacterSkin>() != null) continue;    // a body; CharacterSkinDirector owns it
                if (r.GetComponentInParent<IDamageable>() != null) continue;
                if (r.GetComponent<GroundRing>() != null) continue;       // brings its own material
                if (r.GetComponentInParent<KeepsOwnMaterial>() != null) continue;   // imported art (YT-75)

                var mat = MaterialFor(r);
                if (mat == null) continue;

                r.sharedMaterial = mat;
                r.gameObject.AddComponent<SurfaceSkinned>();
            }
        }

        private static Material MaterialFor(Renderer r)
        {
            // The boss's damage zones colour themselves through a MaterialPropertyBlock, alpha and
            // all, so they want a transparent material that lets _BaseColor do the talking. An
            // opaque one would stamp a solid disc over the arena.
            if (r.GetComponentInParent<DamageZone>() != null)
            {
                return VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            }

            return MaterialLibrary.Surface(WorldMaterials.KindOf(r));
        }
    }
}
