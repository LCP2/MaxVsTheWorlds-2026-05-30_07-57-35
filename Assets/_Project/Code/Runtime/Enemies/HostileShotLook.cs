using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-1428: the one visibility rule for every enemy shot in a world whose
    /// <c>brightHostileProjectiles</c> dial is on (the City): a core of at least
    /// <see cref="MinCoreDiameter"/>, a colour from {<see cref="Orange"/>, <see cref="Red"/>,
    /// <see cref="BlueWhite"/>} drawn additive/emissive (never the firing robot's body colour), and a
    /// trail of at least <see cref="MinTrailLength"/>. With the dial off nothing here is consulted.
    /// </summary>
    public static class HostileShotLook
    {
        public const float MinCoreDiameter = 0.25f;
        public const float MinTrailLength = 1.0f;
        public const float MinBeamWidth = 0.08f;

        /// <summary>#FF8A1E — Launcher missile, Turret glob.</summary>
        public static readonly Color Orange = new Color(1f, 0x8A / 255f, 0x1E / 255f);
        /// <summary>#FF3B2F — Gunner beam.</summary>
        public static readonly Color Red = new Color(1f, 0x3B / 255f, 0x2F / 255f);
        /// <summary>#BFE3FF — Orbital missile.</summary>
        public static readonly Color BlueWhite = new Color(0xBF / 255f, 0xE3 / 255f, 0xFF / 255f);

        /// <summary>Grey the Launcher's trail fades to (its smoke).</summary>
        public static readonly Color Smoke = new Color(0.62f, 0.62f, 0.64f);

        /// <summary>Core diameter every bright projectile is built with (a margin over the minimum).</summary>
        public const float CoreDiameter = 0.3f;

        /// <summary>Trail lifetime (s) for a projectile flying at <paramref name="speed"/> m/s such that the
        /// trail is comfortably over <see cref="MinTrailLength"/> metres long.</summary>
        public static float TrailTimeFor(float speed) =>
            Mathf.Max(0.3f, MinTrailLength * 1.3f / Mathf.Max(0.5f, speed));

        /// <summary>Launcher salvo (MV-1428): from this area index on a Launcher fires two missiles.</summary>
        public const int SalvoFirstArea = 10;
        public const float SalvoGapSeconds = 0.25f;

        public static bool SalvoApplies(bool bright, int areaIndex) => bright && areaIndex >= SalvoFirstArea;

        /// <summary>The resolved look of a built projectile, read back off its real components.</summary>
        public readonly struct Resolved
        {
            public readonly float CoreDiameter;
            public readonly Color Color;
            public readonly float TrailLength;

            public Resolved(float coreDiameter, Color color, float trailLength)
            {
                CoreDiameter = coreDiameter;
                Color = color;
                TrailLength = trailLength;
            }
        }

        /// <summary>Colour a renderer's material is actually drawn with (additive tint property).</summary>
        public static Color MaterialColor(Material m)
        {
            if (m == null) return Color.clear;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.clear;
        }
    }
}
