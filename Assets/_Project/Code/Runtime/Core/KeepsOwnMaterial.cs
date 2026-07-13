using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// "This renderer already has the material it wants — leave it alone."
    ///
    /// The rendering layer dresses every world surface it finds, and the runtime sweep re-dresses
    /// anything that appears mid-run, because a greybox primitive with Unity's built-in material
    /// draws magenta in a player build (YT-58). Both are blanket rules, and they have to be: the
    /// alternative is remembering to dress each new spawn site, which is how the magenta got in.
    ///
    /// Imported art breaks the assumption. A kit prop arrives with its own materials, and dressing
    /// it would repaint a tree, a fence and a flower bed in one flat stylised colour — the exact
    /// look the art pass exists to get rid of. So a prop that brings its own material says so, once,
    /// on its root, and both systems skip it and everything under it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeepsOwnMaterial : MonoBehaviour
    {
    }
}
