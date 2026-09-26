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
    [MaxWorlds.Core.PerfSection("sludge")]
    public sealed class SludgeFlow : MonoBehaviour
    {
        /// <summary>UV units per second the tile's texture scrolls — resolved onto the component at
        /// bind time by <see cref="MapRuntime.BuildSludge"/>, never zero for a built sludge tile.</summary>
        public Vector2 ScrollSpeed { get; private set; }

        private Material _material;

        /// <summary>MV-978: the tile's own zone-tagged renderer, when this tile has one (a map-authored
        /// sludge lane, or the per-zone combined mesh <c>CombineZoneGeometry</c> folds it into) — the
        /// gating signal, same "already zone-tagged, just read it" idiom <see cref="DeckVisibility"/>
        /// uses. Null for a flow attached to something the MV-972 gate was never meant to reach (a
        /// Sludgequeen boss-fight flood plane spanning the whole arena, a Sludge Drone's own free-flying
        /// puddle) — <see cref="Update"/> always ticks in that case, unchanged from before this ticket.</summary>
        private Renderer _gateRenderer;

        /// <summary>Call counter for MV-978's own EditMode test — how many times <see cref="Update"/>
        /// has actually run its per-frame body (not counting an early-out while gated invisible).</summary>
        public int UpdateCallCount { get; private set; }

        public void Configure(Material material, Vector2 scrollSpeed) => Configure(null, material, scrollSpeed);

        public void Configure(Renderer gateRenderer, Material material, Vector2 scrollSpeed)
        {
            _gateRenderer = gateRenderer;
            _material = material;
            ScrollSpeed = scrollSpeed;
        }

        private void Update()
        {
            if (_gateRenderer != null && !_gateRenderer.enabled) return;
            UpdateCallCount++;
            if (_material == null) return;
            _material.mainTextureOffset += ScrollSpeed * Time.deltaTime;
        }
    }
}
