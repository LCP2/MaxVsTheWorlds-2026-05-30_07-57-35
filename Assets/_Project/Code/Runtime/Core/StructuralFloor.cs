using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// "This renderer is the map's own floor slab — treat it as ground, whatever its height." (MV-954)
    ///
    /// <see cref="MaxWorlds.Rendering.WorldMaterials.KindOf"/> classifies most world surfaces by shape:
    /// thin and wide reads as ground, tall reads as a wall. That broke the moment
    /// <see cref="MaxWorlds.Arena.MapGeometry.FloorThickness"/> grew from 0.1 m to 2.0 m (a floor
    /// collider thick enough that a stall-inflated <see cref="MaxWorlds.Core.CharacterControllerMotion"/>
    /// sub-step can never clear it) — a 2 m-thick slab no longer LOOKS flat, so the shape heuristic
    /// started dressing it in wall material instead of floor material (MV742StormdrainPaletteTests,
    /// MV745World3HullDressingTests). Same fix <see cref="StructuralWall"/> already applies to walls
    /// for the same reason (MV-742): the one place that knows for certain what a box is
    /// (<c>MapRuntime.Build</c>) says so once, here, instead of leaving shape to guess. <c>KindOf</c>
    /// checks for this marker before it ever falls back to the height heuristic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StructuralFloor : MonoBehaviour
    {
    }
}
