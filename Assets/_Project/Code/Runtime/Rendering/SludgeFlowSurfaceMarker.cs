using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Empty marker (MV-938) on a sludge tile's "Flow" GameObject — see
    /// <see cref="StormdrainKit.DressSludgeTile"/>'s own build call for why it needs one:
    /// <c>MaxWorlds.Arena.MapStaticBatchRoot.CombineZoneGeometry</c> would otherwise fold a
    /// tile's one-renderer flow mesh into a "Combined ..." mesh for zero benefit (its material is
    /// already unique per tile) while relocating it out from under "Stormdrain Dressing" to the map
    /// root, which the dressing census tests (MV-755, MV-906) rely on it staying under.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SludgeFlowSurfaceMarker : MonoBehaviour
    {
    }
}
