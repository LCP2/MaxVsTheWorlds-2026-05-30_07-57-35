using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Supplies Unity's built-in font for the HUD (YT-30). Using legacy uGUI
    /// <see cref="UnityEngine.UI.Text"/> with <c>LegacyRuntime.ttf</c> means the HUD renders
    /// headlessly in CI and in the WebGL build with zero committed art and no "TMP Essential
    /// Resources" import step (TextMeshPro is unusable until those are imported). Greybox
    /// typography — the spec's Lilita One / Nunito faces are a Phase C art pass.
    /// </summary>
    public static class HudFont
    {
        private static Font _cached;

        /// <summary>The shared built-in dynamic font (never null in a normal build).</summary>
        public static Font Get()
        {
            if (_cached == null)
            {
                _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return _cached;
        }
    }
}
