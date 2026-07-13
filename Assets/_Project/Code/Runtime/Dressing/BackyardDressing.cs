using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Dressing
{
    /// <summary>
    /// Dresses the Backyard (YT-75): a fence line, a shed, trees, beds, planting and clutter, built
    /// from a free CC0 low-poly garden kit — so the yard reads as somebody's actual backyard rather
    /// than as a grey box with a robot in it.
    ///
    /// It installs itself after scene load, like the lighting and the materials do, so the scene
    /// file is untouched and CI/WebGL get the dressed yard with nothing hand-placed. It reads the
    /// arena from <see cref="BackyardPath"/> — the same layout the walls and cover were built from —
    /// so reshaping the arena re-dresses it instead of leaving a fence hanging in mid-air.
    ///
    /// It touches nothing gameplay owns. The props carry no colliders, so movement, spawning and
    /// pathing are the same yard they were before; the greybox blocks keep their colliders and give
    /// up only their renderers; and the Mower Hutch's own body is left alone, because gameplay tints
    /// it to show damage and two systems writing one colour is a bug waiting for a boss fight.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardDressing : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BackyardDressing>() != null) return;
            if (FindFirstObjectByType<BackyardPath>() == null) return;   // not the Backyard: nothing to dress
            new GameObject("BackyardDressing").AddComponent<BackyardDressing>();
        }

        [Tooltip("Same yard every run, on every machine. Change it to re-roll the planting.")]
        [SerializeField] private int seed = 75;

        private Transform _root;

        /// <summary>How many props ended up in the yard. Zero means the kit didn't load.</summary>
        public int PropCount { get; private set; }

        private void Awake() => Build();

        private void Build()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null) return;

            BackyardPathLayout layout = path.Layout;
            ArenaCover[] cover = BackyardCover.Default;

            // The cover props only exist if the gameplay stream's own invariants held. If they
            // didn't, BackyardPath skipped them — so there is nothing to stand a tree on, and the
            // dressing must not stand one there anyway.
            if (!BackyardCover.Validate(layout, cover, path.ShedZ, 3.5f, out _))
                cover = System.Array.Empty<ArenaCover>();

            List<DressProp> plan = DressingPlan.Build(layout, path.ShedZ, cover, seed);

            if (!DressingPlan.Validate(layout, plan, path.ShedZ, cover, out string reason))
            {
                // An undressed yard is ugly. A yard with a hedge across the doorway is unplayable.
                Debug.LogWarning($"[BackyardDressing] yard left undressed: {reason}");
                return;
            }

            _root = new GameObject("Backyard Dressing").transform;
            _root.SetParent(transform, false);

            foreach (var p in plan)
                if (GardenKit.Spawn(p, _root) != null) PropCount++;

            ReskinCover(path, cover);
            ReskinShedPosts(path);
            ReskinWalls(path);

            Debug.Log($"[BackyardDressing] {PropCount} props placed (plan of {plan.Count}).");
        }

        /// <summary>
        /// The walls become the timber BEHIND the fence, not a wall in their own right.
        ///
        /// Every wall in the yard now has a fence panel standing against its inner face, and the
        /// greybox wall was still painted its own mid-brown — so the boundary read as a picket stuck
        /// to a chocolate slab, with a metre-wide cap on top of it. Repainted dark, the same mass
        /// reads as the shadowed backing boards behind the palings, and the lighter fence in front
        /// of it pops. The geometry, and therefore the fight, is untouched: this is a colour.
        /// </summary>
        private void ReskinWalls(BackyardPath path)
        {
            // Dark enough to sit behind the palings, light enough that a wall's vertical face — which
            // catches far less of the key than the lawn does — still reads as timber and not as a
            // charcoal border drawn round the yard.
            var backing = KitMaterials.For("woodBark");
            if (backing == null || PropCount == 0) return;

            foreach (var t in path.GetComponentsInChildren<Transform>())
            {
                if (!t.name.Contains("Wall") && !t.name.Contains("Shoulder")) continue;

                var r = t.GetComponent<MeshRenderer>();
                if (r == null) continue;

                r.sharedMaterial = backing;
                if (t.GetComponent<SurfaceSkinned>() == null) t.gameObject.AddComponent<SurfaceSkinned>();
                if (t.GetComponent<DressingSkin>() == null) t.gameObject.AddComponent<DressingSkin>();
            }
        }

        /// <summary>Hide the cover blocks' greybox, keep their colliders. The tree/planter/hedge the
        /// plan stood on top of them is what you see; the block is what you hide behind.</summary>
        private void ReskinCover(BackyardPath path, IReadOnlyList<ArenaCover> cover)
        {
            if (PropCount == 0) return;      // the kit didn't load; don't hide the only geometry left

            foreach (var c in cover)
            {
                var block = Find(path.transform, c.Name);
                if (block == null) continue;

                var r = block.GetComponent<MeshRenderer>();
                if (r != null) r.enabled = false;
            }
        }

        /// <summary>The two posts the hutch stands between are timber, not concrete. Repainted in
        /// place: they keep their colliders and their position, and the material directors are told
        /// to leave them alone.</summary>
        private void ReskinShedPosts(BackyardPath path)
        {
            var timber = KitMaterials.For("woodDark");
            if (timber == null) return;

            foreach (string name in new[] { "Shed Post L", "Shed Post R" })
            {
                var post = Find(path.transform, name);
                if (post == null) continue;

                var r = post.GetComponent<MeshRenderer>();
                if (r == null) continue;

                r.sharedMaterial = timber;
                if (post.GetComponent<SurfaceSkinned>() == null) post.gameObject.AddComponent<SurfaceSkinned>();
                if (post.GetComponent<DressingSkin>() == null) post.gameObject.AddComponent<DressingSkin>();
            }
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                if (t.name == name) return t;
            return null;
        }
    }
}
