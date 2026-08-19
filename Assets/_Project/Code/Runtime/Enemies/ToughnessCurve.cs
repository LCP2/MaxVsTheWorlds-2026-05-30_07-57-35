using System;
using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// A world's <c>toughnessCurve</c> dial (Confluence MVW 34439170 §5/§8.6) — when the Heavy/Brute
    /// tiers unlock and how far the budget's tank-share (Heavy+Brute's fraction of Σ THV) drifts by
    /// the world's last area. Mirrors <see cref="AreaPopulation.ToughSplitForArea"/>'s intro-area
    /// idiom, but drives a THV-budget split rather than a raw large-slot count.
    /// </summary>
    [Serializable]
    public sealed class ToughnessCurve
    {
        /// <summary>The area the Heavy tier — and tank-share — starts appearing in.</summary>
        public int heavyFromArea = 5;

        /// <summary>The area the Brute tier joins Heavy in the tank share.</summary>
        public int bruteFromArea = 8;

        /// <summary>Tank-share at <see cref="heavyFromArea"/> (typically 0 — nothing tanky before
        /// then).</summary>
        public float tankShareAtHeavyIntro = 0f;

        /// <summary>Tank-share at <paramref name="lastArea"/> — "drifting toward ~70% by world's
        /// end" (spec §9).</summary>
        public float tankShareAtEnd = 0.70f;

        /// <summary>The world's final area — where <see cref="tankShareAtEnd"/> is reached.</summary>
        public int lastArea = 8;

        /// <summary>The area Gunner (ranged laser) starts appearing in the ambient arena population
        /// (MV-310). Mirrors <see cref="heavyFromArea"/>'s intro-area idiom.</summary>
        public int gunnerFromArea = 2;

        /// <summary>The area Launcher (homing missile) joins Gunner (MV-310).</summary>
        public int launcherFromArea = 3;

        /// <summary>The area Blinker (teleport-flank) joins Gunner/Launcher (MV-310).</summary>
        public int blinkerFromArea = 4;

        /// <summary>The Σ-THV share [0,1×100] each unlocked special kind (Gunner/Launcher/Blinker) draws
        /// off an area's total budget once its own intro area is reached — independently of the other
        /// two, so once all three are live they stack, the same "each tier substitutes its own slice"
        /// idiom <see cref="AreaPopulation.ToughSplitForArea"/> uses for Heavy/Brute (MV-310).</summary>
        public float specialSharePct = 12f;

        /// <summary>The budget fraction [0,1] that should go to tanky types (Heavy+Brute) at
        /// <paramref name="areaIndex"/>: 0 before <see cref="heavyFromArea"/>, then a linear drift
        /// from <see cref="tankShareAtHeavyIntro"/> to <see cref="tankShareAtEnd"/> across the
        /// remaining areas to <see cref="lastArea"/>.</summary>
        public float TankShareForArea(int areaIndex)
        {
            if (areaIndex < heavyFromArea) return 0f;

            float span = Mathf.Max(1, lastArea - heavyFromArea);
            float t = Mathf.Clamp01((areaIndex - heavyFromArea) / span);
            return Mathf.Clamp01(Mathf.Lerp(tankShareAtHeavyIntro, tankShareAtEnd, t));
        }

        public bool HeavyUnlockedAt(int areaIndex) => areaIndex >= heavyFromArea;
        public bool BruteUnlockedAt(int areaIndex) => areaIndex >= bruteFromArea;
        public bool GunnerUnlockedAt(int areaIndex) => areaIndex >= gunnerFromArea;
        public bool LauncherUnlockedAt(int areaIndex) => areaIndex >= launcherFromArea;
        public bool BlinkerUnlockedAt(int areaIndex) => areaIndex >= blinkerFromArea;
    }
}
