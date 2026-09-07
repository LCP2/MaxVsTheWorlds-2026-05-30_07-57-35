using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Scrolls a sludge tile's material to read as flowing (MV-690) — closing the "[ART] follow-up
    /// gives it a flowing UV scroll" note <see cref="MapRuntime"/> left on <c>SludgeColor</c> when
    /// MV-692 shipped it as a flat colour.
    ///
    /// <see cref="MapRuntime.BuildSludge"/> tints a graded sludge tile with its own
    /// <see cref="MaterialLibrary.Tinted"/> instance (its albedo tone varies with distance to the
    /// outfall, so tiles at different tones are already different material instances) — scrolling that
    /// instance's own offset here, rather than a single shared channel-wide material, is what keeps
    /// each tile's flow moving without one tile's scroll fighting another's different tone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SludgeFlow : MonoBehaviour
    {
        /// <summary>UV units per second the tile's texture scrolls — resolved onto the component at
        /// bind time by <see cref="MapRuntime.BuildSludge"/>, never zero for a built sludge tile.</summary>
        public Vector2 ScrollSpeed { get; private set; }

        private Material _material;

        public void Configure(Material material, Vector2 scrollSpeed)
        {
            _material = material;
            ScrollSpeed = scrollSpeed;
        }

        private void Update()
        {
            if (_material == null) return;
            _material.mainTextureOffset += ScrollSpeed * Time.deltaTime;
        }
    }
}
