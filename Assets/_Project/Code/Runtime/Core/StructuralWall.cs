using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// "This renderer is a boundary wall — treat it as one, whatever its height." (MV-742)
    ///
    /// <see cref="MaxWorlds.Rendering.WorldMaterials.KindOf"/> classifies most world surfaces by
    /// shape alone: a plane is ground, a box tall enough to read as a wall is a wall, anything else
    /// is a prop. That works everywhere the Backyard's own ~3.5 m fence line set the assumption —
    /// but World 2's Stormdrain conduits are authored with a 1.5 m ceiling
    /// (<c>world2_config.json</c>'s <c>wallHeight</c>), under the shape check's height cutoff, and
    /// some of its cover pieces are authored taller (1.6 m) than that. No single height threshold can
    /// ever separate the two for this data, in either direction.
    ///
    /// <see cref="MapRuntime"/> knows which box IS a boundary wall at the moment it builds one — it
    /// never has to guess from shape — so it says so once, here, the same way imported art says
    /// "leave my material alone" via <see cref="KeepsOwnMaterial"/>. <c>KindOf</c> checks for this
    /// marker before it ever falls back to the height heuristic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StructuralWall : MonoBehaviour
    {
    }
}
