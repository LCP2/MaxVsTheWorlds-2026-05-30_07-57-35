using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// Pure pan/zoom clamp maths for the full-screen map (MV-563) — no MonoBehaviour, no Input System
    /// dependency, so the clamps are pinned by an EditMode test without building a canvas or simulating
    /// touch. <see cref="MapScreen"/> is the only caller: it feeds live screen-space pointer deltas in
    /// and reads a clamped zoom/pan pair back out every frame a gesture is in progress.
    /// </summary>
    public static class MapPanZoomModel
    {
        /// <summary>The scale-to-fit factor that letterboxes <paramref name="contentSize"/> entirely
        /// inside <paramref name="viewportSize"/> — the map's own opening state (AC3: "starts zoomed to
        /// fit the whole world"). Degenerate content returns 1 rather than dividing by zero.</summary>
        public static float FitScale(Vector2 viewportSize, Vector2 contentSize)
        {
            if (contentSize.x <= 0f || contentSize.y <= 0f) return 1f;
            return Mathf.Min(viewportSize.x / contentSize.x, viewportSize.y / contentSize.y);
        }

        /// <summary>The zoom multiplier (on top of <see cref="FitScale"/>, so 1 = the opening fit state)
        /// at which one area's own on-screen height — <paramref name="typicalAreaWorldHeight"/> scaled by
        /// <paramref name="fitScale"/> — reaches half of <paramref name="viewportHeight"/> (the ticket's
        /// "a maximum where a single area fills roughly half the screen height"). Never below 1: a world
        /// whose typical area is already that tall at the fit scale clamps to no extra zoom at all rather
        /// than a max below the minimum.</summary>
        public static float MaxZoomMultiplier(float viewportHeight, float fitScale, float typicalAreaWorldHeight)
        {
            if (viewportHeight <= 0f || fitScale <= 0f || typicalAreaWorldHeight <= 0f) return 1f;
            float areaHeightAtFit = typicalAreaWorldHeight * fitScale;
            if (areaHeightAtFit <= 0f) return 1f;
            return Mathf.Max(1f, (viewportHeight * 0.5f) / areaHeightAtFit);
        }

        /// <summary>Clamps a zoom multiplier between the opening fit state (1, the minimum) and
        /// <paramref name="maxZoom"/>.</summary>
        public static float ClampZoom(float zoom, float maxZoom) => Mathf.Clamp(zoom, 1f, Mathf.Max(1f, maxZoom));

        /// <summary>Adjusts <paramref name="pan"/> (the content's own anchored-position offset from
        /// viewport centre, in screen pixels) so that <paramref name="pivotViewportLocal"/> — a point
        /// expressed relative to viewport centre, in the same screen-pixel space <paramref name="pan"/>
        /// lives in — stays under the same screen position while the zoom multiplier changes from
        /// <paramref name="oldZoom"/> to <paramref name="newZoom"/> (both full multipliers, i.e. already
        /// including <see cref="FitScale"/>). This is what makes a pinch zoom about its own midpoint
        /// rather than the viewport centre.</summary>
        public static Vector2 ZoomAboutPoint(Vector2 pan, float oldZoom, float newZoom, Vector2 pivotViewportLocal)
        {
            if (oldZoom <= 0f) return pan;

            // The content point currently sitting under the pivot, in the content's own unscaled space:
            Vector2 contentLocal = (pivotViewportLocal - pan) / oldZoom;
            // Re-solve pan so that same content point renders at the same pivot under the new scale:
            return pivotViewportLocal - contentLocal * newZoom;
        }

        /// <summary>Clamps <paramref name="pan"/> so the zoomed content can never be dragged entirely off
        /// screen: on an axis where the zoomed content exceeds the viewport, pan is clamped to at most
        /// half the excess; on an axis where it doesn't (true of both axes at the opening fit zoom, by
        /// construction of <see cref="FitScale"/>), pan on that axis is pinned to 0 — centred, not
        /// draggable.</summary>
        public static Vector2 ClampPan(Vector2 pan, Vector2 viewportSize, Vector2 contentSize, float zoom)
        {
            Vector2 scaledContent = contentSize * zoom;
            float maxX = Mathf.Max(0f, (scaledContent.x - viewportSize.x) * 0.5f);
            float maxY = Mathf.Max(0f, (scaledContent.y - viewportSize.y) * 0.5f);
            return new Vector2(Mathf.Clamp(pan.x, -maxX, maxX), Mathf.Clamp(pan.y, -maxY, maxY));
        }
    }
}
