using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Sizing for world-space bars that hang over a scaled body (YT-71).
    ///
    /// The bug this exists to prevent: a world-space Canvas parented to a body inherits that body's
    /// scale. The Mower Hutch is a cube scaled (3, 2, 3), so its health bar's authored 0.02 canvas
    /// scale silently became (0.06, 0.04, 0.06) — a 220 px bar rendered 13.2 METRES wide, with a
    /// 19.2 m name label over it, in an arena only 24 m across. It also came out non-uniform
    /// (0.06 wide vs 0.04 tall), so it was stretched as well as enormous.
    ///
    /// Nothing about the authored numbers looked wrong; the scale was inherited. So the fix is to
    /// cancel the parent's scale rather than to hand-pick smaller numbers that happen to survive it.
    /// </summary>
    public static class WorldBar
    {
        /// <summary>Local scale that cancels a parent's world scale, so a child's units are metres
        /// again no matter what the body it hangs on is scaled to.</summary>
        public static Vector3 Unscale(Vector3 parentLossyScale)
        {
            return new Vector3(
                1f / SafeAxis(parentLossyScale.x),
                1f / SafeAxis(parentLossyScale.y),
                1f / SafeAxis(parentLossyScale.z));
        }

        /// <summary>The local offset that lands <paramref name="worldMetres"/> above the parent's
        /// origin. Local positions are scaled by the parent too — a body scaled 2× on Y moves its
        /// children twice as far as the number says.</summary>
        public static float LocalOffsetY(float worldMetres, float parentScaleY)
            => worldMetres / SafeAxis(parentScaleY);

        /// <summary>Canvas scale that renders a <paramref name="pixelWidth"/>-wide UI rect at
        /// <paramref name="worldWidth"/> metres. Uniform by construction — a bar must never be
        /// stretched by the body it belongs to.</summary>
        public static float CanvasScaleFor(float worldWidth, float pixelWidth)
            => pixelWidth > 0f ? worldWidth / pixelWidth : 1f;

        private static float SafeAxis(float v) => Mathf.Abs(v) < 1e-4f ? 1e-4f : v;
    }
}
