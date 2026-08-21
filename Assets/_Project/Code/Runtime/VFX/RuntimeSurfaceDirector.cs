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
    /// Rather than patch each spawn site by hand, this used to sweep continuously — anything undressed
    /// got a real material within a frame of appearing. MV-527: that generality was mostly theoretical.
    /// Everything the sweep excludes (<see cref="SurfaceSkinned"/>, <see cref="CharacterSkin"/>,
    /// <see cref="SelfDrivenTint"/>, anything under an <see cref="IDamageable"/>, <see cref="GroundRing"/>,
    /// anything under <see cref="KeepsOwnMaterial"/>) covers every character body and every
    /// runtime-spawned VFX prop already in this codebase — <see cref="DamageZone"/>'s own visual, for
    /// instance, is a <see cref="GroundRing"/> built with a real material at creation (see that class's
    /// own doc comment), so it was never actually reachable here despite once being the reason this
    /// class exists. What's left is world SCENERY built once, synchronously, while the map assembles
    /// (<c>BackyardPath.Awake</c> → <c>MapRuntime.Build</c>) — cover pieces, backdrop, dressing, the
    /// home shed. One sweep in <see cref="Start"/>, guaranteed by Unity to run after every object's
    /// Awake has already fired this scene, catches all of it at a one-time cost instead of a per-frame
    /// one. A future spawn site that creates a raw, unmaterialled primitive DURING gameplay (after this
    /// sweep has already run) needs to dress itself explicitly — the way <c>Pickup</c> and every
    /// character rig in this file's own exclusion list already do — rather than relying on this class
    /// to find it.
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

        // MV-527: Start, not Update — see the class doc comment for why one sweep is enough.
        private void Start() => Sweep();

        private void Sweep()
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.GetComponent<SurfaceSkinned>() != null) continue;   // done already
                if (r.GetComponent<CharacterSkin>() != null) continue;    // a body; CharacterSkinDirector owns it
                if (r.GetComponent<SelfDrivenTint>() != null) continue;   // gameplay drives this block (MV-350)
                // includeInactive: true (MV-350) — a pooled robot is SetActive(false) between lives, and
                // GetComponentInParent does not search inactive GameObjects unless told to. Without it, the
                // instant a robot despawns this guard silently stops seeing its IDamageable and the sweep
                // claims the robot's own renderers as if they were unclaimed world scenery — permanently,
                // because SurfaceSkinned below makes it a one-way door. That was the tan-robot bug.
                if (r.GetComponentInParent<IDamageable>(true) != null) continue;
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
