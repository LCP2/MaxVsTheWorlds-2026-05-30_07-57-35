using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// MV-1398: resolution-aware shape sprites for THE RIG. Drop-in for the <see cref="HudTextures"/>
    /// generators THE RIG calls — same signatures, but the texture is generated at the live pixel scale
    /// the sprite will be drawn at (<see cref="Resolution"/>: 2x supersampled, rounded up to a
    /// power-of-two bucket, cached per bucket by <see cref="HudTextures"/>), with mipmaps and trilinear
    /// filtering. The sprite's pixelsPerUnit is scaled by the same multiplier, so its nominal canvas-unit
    /// size (and every authored sizeDelta that stretches it) is unchanged. MV-1405 adds the text half.
    /// </summary>
    public static class CrispUi
    {
        /// <summary>Largest texture edge a crisp sprite may have; a bigger sprite drops to a smaller
        /// bucket rather than spending WebGL texture memory on it.</summary>
        public const int MaxTextureEdge = 1536;
        public const int MaxResolution = 4;

        /// <summary>Current texel-per-canvas-unit bucket (1, 2 or 4). Set by <see cref="WeaponsScreen"/>
        /// from its live canvas scale before it builds shapes.</summary>
        public static int Resolution = 1;

        /// <summary>The power-of-two bucket for an effective on-screen scale (device pixels per canvas
        /// unit, canvas scaleFactor x any board scale): the smallest 1/2/4 that is at least 2x it.</summary>
        public static int BucketFor(float effectiveScale)
        {
            float want = Mathf.Max(effectiveScale, 0.01f) * 2f;
            int k = 1;
            while (k < MaxResolution && k < want - 0.0001f) k *= 2;
            return k;
        }

        private static Sprite With(int maxEdge, System.Func<Sprite> make)
        {
            int k = Resolution;
            while (k > 1 && maxEdge * k > MaxTextureEdge) k /= 2;
            int prev = HudTextures.Res;
            HudTextures.Res = k;
            try { return make(); }
            finally { HudTextures.Res = prev; }
        }

        public static Sprite Solid() => HudTextures.Solid();

        public static Sprite Arrow(int size = 64) => With(size, () => HudTextures.Arrow(size));

        public static Sprite Disc(int size = 128) => With(size, () => HudTextures.Disc(size));

        public static Sprite Ring(int size, float strokeWidth, bool dashed = false, int dashCount = 24) =>
            With(size, () => HudTextures.Ring(size, strokeWidth, dashed, dashCount));

        public static Sprite RoundedBox(int size = 64, float cornerFraction = 0.28f) =>
            With(size, () => HudTextures.RoundedBox(size, cornerFraction));

        public static Sprite RoundedBoxOutline(int size, float cornerFraction, float strokeWidth) =>
            With(size, () => HudTextures.RoundedBoxOutline(size, cornerFraction, strokeWidth));

        public static Sprite Polygon(int sides, float rotationDeg, int width, int height) =>
            With(Mathf.Max(width, height), () => HudTextures.Polygon(sides, rotationDeg, width, height));

        public static Sprite PolygonOutline(int sides, float rotationDeg, int width, int height,
            float strokeWidth, bool dashed = false, float dashLength = 14f, float gapLength = 10f) =>
            With(Mathf.Max(width, height), () => HudTextures.PolygonOutline(sides, rotationDeg, width, height,
                strokeWidth, dashed, dashLength, gapLength));

        public static Sprite PolygonGlow(int sides, float rotationDeg, int width, int height,
            float shapeRadius, float blurPx) =>
            With(Mathf.Max(width, height), () => HudTextures.PolygonGlow(sides, rotationDeg, width, height,
                shapeRadius, blurPx));

        public static Sprite BezierStroke(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float strokeWidth,
            int width, int height) =>
            With(Mathf.Max(width, height), () => HudTextures.BezierStroke(p0, p1, p2, p3, strokeWidth, width, height));

        public static Sprite VectorIcon(string svgFragment, int size) =>
            With(size, () => HudTextures.VectorIcon(svgFragment, size));
    }
}
