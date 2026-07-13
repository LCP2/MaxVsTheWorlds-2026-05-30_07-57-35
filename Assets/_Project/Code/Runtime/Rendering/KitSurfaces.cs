using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Gives the garden kit's props the surface of the material they're supposed to be made of (YT-77).
    ///
    /// The kit arrived (YT-75) as 16 flat colours: `kit_wood`, `kit_stone`, `kit_dirt`, and so on —
    /// one material per swatch, each a single tone with no texture on it at all. Under a raking sun
    /// that reads as painted cardboard. A fence panel and a paving slab differ only in hue, and the
    /// eye doesn't believe either of them.
    ///
    /// The fix does NOT touch the kit's colours. YT-75 and its follow-up spent real effort on those —
    /// pulling the mint-turquoise foliage back to a garden green, then dragging the whole kit under
    /// the sunlit ceiling when every lit fence panel turned out cream. Those decisions stand. What is
    /// added here is the SURFACE: the kit tells us what each material is (its own name does), and we
    /// hand back the same colour with timber grain, stone chipping or turned soil through it, and a
    /// normal map so the light finally breaks across it.
    ///
    /// The kit's own name is the classifier, which is worth being explicit about: shape can't do this.
    /// <see cref="WorldMaterials.KindOf"/> sees a fence paling and a stepping stone as the same thing
    /// — a box — and always will. Material identity has to come from the thing that knows it.
    /// </summary>
    public static class KitSurfaces
    {
        /// <summary>The prefix KitImport gives every material it writes.</summary>
        private const string Prefix = "kit_";

        /// <summary>
        /// What a kit material is made of, or null to leave it exactly as it is.
        ///
        /// Two categories are deliberately left alone. The FLOWERS (`colorRed`, `colorYellow`, …) are
        /// painted petals a few centimetres across — grain on them would be invisible at best and
        /// grubby at worst, and they are the only strong colours in the yard, so nothing here gets to
        /// dirty them. `_defaultMat` is the kit's catch-all for brackets, rims and edging: it lands on
        /// a dozen unrelated parts, and a material we can't name is a material we shouldn't texture.
        /// </summary>
        public static SurfaceKind? KindOf(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return null;

            string n = materialName;
            int prefix = n.IndexOf(Prefix, System.StringComparison.Ordinal);
            if (prefix < 0) return null;                      // not a kit material — not ours to dress
            n = n.Substring(prefix + Prefix.Length);

            // Unity appends " (Instance)" to a material it has cloned. Fold it away, or a cloned
            // kit_wood would classify as nothing and quietly stay flat.
            int space = n.IndexOf(' ');
            if (space >= 0) n = n.Substring(0, space);

            if (n.StartsWith("wood", System.StringComparison.Ordinal)) return SurfaceKind.Wood;
            if (n.StartsWith("dirt", System.StringComparison.Ordinal)) return SurfaceKind.Dirt;
            if (n.StartsWith("stone", System.StringComparison.Ordinal)) return SurfaceKind.Stone;

            // Leaf mass. `grass` is the kit's name for shrubs, tufts and stems — the standing green
            // things, not the lawn, which is the ground shader's job and always has been.
            if (n.StartsWith("leafs", System.StringComparison.Ordinal)) return SurfaceKind.Foliage;
            if (n.StartsWith("grass", System.StringComparison.Ordinal)) return SurfaceKind.Foliage;

            return null;
        }

        /// <summary>
        /// Re-surface every kit prop under <paramref name="root"/>. Returns how many renderers changed.
        ///
        /// Must run BEFORE the props are static-batched: batching bakes the renderer's material into
        /// the combined mesh, and swapping it afterwards would either be ignored or split the batch
        /// back apart. The count of draw calls is unchanged by this — the kit's 16 shared materials
        /// become at most 16 shared materials, because <see cref="MaterialLibrary.Tinted"/> caches on
        /// (kind, tone) and every prop of a given timber shares one.
        /// </summary>
        public static int Dress(Transform root)
        {
            if (root == null) return 0;

            int dressed = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    var kit = mats[i];
                    if (kit == null) continue;

                    SurfaceKind? kind = KindOf(kit.name);
                    if (kind == null) continue;

                    // The kit's colour is the input, not something to re-decide. Whatever KitImport
                    // painted this material, the re-surfaced one renders at the same mean tone.
                    Color tone = kit.HasProperty("_BaseColor") ? kit.GetColor("_BaseColor")
                               : kit.HasProperty("_Color") ? kit.GetColor("_Color")
                               : Color.white;

                    var surfaced = MaterialLibrary.Tinted(kind.Value, tone);
                    if (surfaced == null || surfaced == kit) continue;

                    mats[i] = surfaced;
                    changed = true;
                }

                if (!changed) continue;

                r.sharedMaterials = mats;
                dressed++;
            }

            return dressed;
        }
    }
}
