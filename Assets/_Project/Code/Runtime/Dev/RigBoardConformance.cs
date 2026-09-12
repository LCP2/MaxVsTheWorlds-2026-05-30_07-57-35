using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MaxWorlds.Dev
{
    /// <summary>
    /// MV-463 Part 2 — the ui-screens harness's own conformance pass: the pixel-measurement primitives
    /// <see cref="UiScreensDirector"/> uses to assert a just-captured THE RIG PNG actually matches
    /// <c>rig_board.json</c>, instead of only ever taking a picture nobody automated ever looks at
    /// again. Every method here is a pure function over a <see cref="Texture2D"/> and plain data — no
    /// reach into <c>WeaponsScreen</c>, <c>RigState</c> or the scene — so an EditMode test can paint a
    /// synthetic texture and assert a check's own math directly, without a play-mode capture.
    /// </summary>
    public static class RigBoardConformance
    {
        /// <summary>MV-480: maps a board-JSON (x, y) — always authored in the SAME unscaled coordinate
        /// space every node's own <c>rig_board.json</c> entry uses, regardless of which captured aspect
        /// is being measured — onto the actual pixel a given capture rendered it at. Every existing
        /// check was written assuming an implicit identity transform (<see cref="Identity"/>), true only
        /// for <c>rig-16x9</c> where json space and pixel space happen to coincide 1:1. The other two
        /// captured aspects (<c>rig-ipad-mini</c>, standard-mode geometry scaled by
        /// <c>WeaponsScreen.ComputeBoardScale</c> and cropped by <c>WeaponsScreen.VisibleRefXWindow</c>;
        /// <c>rig-phone</c>, phone-mode geometry inside <c>WeaponsScreen.BuildPhoneScrollViewport</c>,
        /// never scaled but offset by the viewport's own top) need this explicit mapping instead.
        ///
        /// The transform is a single affine map per axis — <c>scaled = Offset + json * Scale</c> — built
        /// once per capture by the caller (<c>UiScreensDirector.RunConformanceChecks</c>) from whichever
        /// of <c>WeaponsScreen</c>'s own scale-to-fit constants apply to that capture's aspect/mode, then
        /// reused for every node on the board. Composing two json-space points through the SAME transform
        /// before differencing them (as every ray/annulus/rect check below already does, by construction)
        /// reproduces exactly the on-screen pixel distance between them — so no check needs to separately
        /// premultiply a radius by <see cref="Scale"/>; passing raw, unscaled json-space radii/offsets
        /// straight through, same as every check already did for <c>rig-16x9</c>, is already correct at
        /// every aspect once the transform itself is right.</summary>
        public readonly struct BoardPixelTransform
        {
            public readonly float Scale;
            public readonly float OffsetX;
            public readonly float OffsetY;
            public readonly float WindowMinX;
            public readonly float PixelsPerRefUnit;

            public BoardPixelTransform(float scale, float offsetX, float offsetY, float windowMinX, float pixelsPerRefUnit)
            {
                Scale = scale; OffsetX = offsetX; OffsetY = offsetY; WindowMinX = windowMinX; PixelsPerRefUnit = pixelsPerRefUnit;
            }

            /// <summary>The no-op transform every pre-MV-480 check implicitly assumed: json space IS
            /// pixel space, 1:1, no crop. Still exactly what <c>rig-16x9</c> needs (scale 1, no window
            /// crop, 1 pixel per ref unit at 1920x1080) — <see cref="GetJsonPixel"/>'s default when no
            /// transform is supplied, so every pre-existing call site (all of them <c>rig-16x9</c>-only
            /// before this ticket) needs no change at all.</summary>
            public static readonly BoardPixelTransform Identity = new BoardPixelTransform(1f, 0f, 0f, 0f, 1f);

            public float PixelX(float jsonX) => (OffsetX + jsonX * Scale - WindowMinX) * PixelsPerRefUnit;
            public float PixelY(float jsonY) => (OffsetY + jsonY * Scale) * PixelsPerRefUnit;
        }

        /// <summary>Texture2D is bottom-left origin; rig_board.json's canvas coordinates are top-left,
        /// y-down (the same convention <c>UiScreensDirector</c>'s own pre-existing probe 6 already
        /// uses) — this is the one place that conversion happens so every check above it can just think
        /// in json coordinates. <paramref name="transform"/> defaults to <see cref="BoardPixelTransform.Identity"/>
        /// (MV-480) — see that field's own doc comment for why that's still exactly right for the one
        /// caller (<c>rig-16x9</c>) that predates it.</summary>
        public static Color GetJsonPixel(Texture2D tex, float jsonX, float jsonY, BoardPixelTransform? transform = null)
        {
            var t = transform ?? BoardPixelTransform.Identity;
            float px = t.PixelX(jsonX);
            float py = t.PixelY(jsonY);
            int x = Mathf.Clamp(Mathf.RoundToInt(px), 0, tex.width - 1);
            int y = Mathf.Clamp(tex.height - 1 - Mathf.RoundToInt(py), 0, tex.height - 1);
            return tex.GetPixel(x, y);
        }

        /// <summary>Sum of the absolute per-channel RGB difference — cheap, monotonic, and all every
        /// check here needs: "does this pixel read as meaningfully different from that one."</summary>
        public static float ColorDistance(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        /// <summary>Walks INWARD from <paramref name="maxDist"/> toward (<paramref name="cx"/>,
        /// <paramref name="cy"/>) along (<paramref name="dx"/>, <paramref name="dy"/>), returning the
        /// first (furthest-out) distance whose pixel differs from <paramref name="background"/> by more
        /// than <paramref name="tolerance"/> — i.e. the outer edge of whatever ink is out there.
        /// Deliberately outside-in, not centre-out: a node's own icon is stroke art with plenty of
        /// transparent gaps in the middle of its bounding box (confirmed live running this exact
        /// ticket — a centre-out walk along RANGE's own ray hit exactly such a gap and returned early),
        /// so searching from the centre is unreliable. Approaching from outside the hex+glow, the first
        /// hit found actually is the edge, regardless of what's transparent closer to the middle.</summary>
        public static float RayInkDistance(Texture2D tex, float cx, float cy, float dx, float dy,
            float maxDist, Color background, float tolerance, BoardPixelTransform? transform = null)
        {
            for (float d = maxDist; d >= 1f; d -= 1f)
            {
                Color px = GetJsonPixel(tex, cx + dx * d, cy + dy * d, transform);
                if (ColorDistance(px, background) > tolerance) return d;
            }
            return 0f;
        }

        /// <summary>Does ANY pixel in the (2*<paramref name="halfBlock"/>+1) square centred on
        /// (<paramref name="jsonX"/>, <paramref name="jsonY"/>) read as ink? A single centre pixel is
        /// exactly as fragile as <see cref="RayInkDistance"/>'s old centre-out walk was — icon stroke
        /// art routinely has a transparent gap at its own mathematical centre — so "is a node here at
        /// all" needs a small neighbourhood, not one sample.</summary>
        public static bool BlockHasInk(Texture2D tex, float jsonX, float jsonY, int halfBlock,
            Color background, float tolerance, BoardPixelTransform? transform = null)
        {
            for (int dy = -halfBlock; dy <= halfBlock; dy++)
                for (int dx = -halfBlock; dx <= halfBlock; dx++)
                    if (ColorDistance(GetJsonPixel(tex, jsonX + dx, jsonY + dy, transform), background) > tolerance)
                        return true;
            return false;
        }

        /// <summary>Mean perceptual luminance (Rec. 709) over the json-space rect
        /// [<paramref name="xMin"/>, <paramref name="xMax"/>) x [<paramref name="yMin"/>, <paramref name="yMax"/>),
        /// sampled every <paramref name="step"/> px — a coarse grid is plenty for a column-band mean and
        /// keeps a whole-region scan cheap.</summary>
        public static float MeanLuminance(Texture2D tex, float xMin, float xMax, float yMin, float yMax, int step = 4,
            BoardPixelTransform? transform = null)
        {
            double sum = 0;
            int count = 0;
            for (float y = yMin; y < yMax; y += step)
                for (float x = xMin; x < xMax; x += step)
                {
                    Color c = GetJsonPixel(tex, x, y, transform);
                    sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    count++;
                }
            return count > 0 ? (float)(sum / count) : 0f;
        }

        /// <summary>Fraction of sampled points in the annulus [<paramref name="rInner"/>, <paramref name="rOuter"/>]
        /// around (<paramref name="cx"/>, <paramref name="cy"/>) that read as "ink" — differ from
        /// <paramref name="background"/> by more than <paramref name="tolerance"/>. This is the glow
        /// containment measure: a node's halo should fade out well before the outer radius, so a high
        /// fraction here means the glow (or something else) is bleeding too far.
        ///
        /// Restricted to two 2*<paramref name="sectorHalfWidthDeg"/>-wide sectors centred on
        /// <paramref name="sectorCenterDeg"/> and its opposite side (json-space angle, 0 = +x/right,
        /// 90 = +y/down) — every tree connector runs roughly vertically out of its own node (down to a
        /// child, up from a parent), so a full-circle annulus double-counts real, intentional connector
        /// ink as glow bleed (confirmed live running this exact ticket: several owned nodes read
        /// ~100% until this was scoped down). Left/right (0/180) dodges that for every node on this
        /// board — no connector on THE RIG ever leaves a node sideways.
        ///
        /// MV-499: also clamped to [<paramref name="xMin"/>, <paramref name="xMax"/>] in absolute
        /// json-space x. MV-472 made column width content-driven, so a node sitting near the wide end of
        /// its own family's spread (its checked annulus reaching outward toward the NEXT family's own
        /// column) can have rOuter=1.95r cross the column boundary — confirmed live: p_rng/e_cel/
        /// u_dmg (each the outermost sibling in a multi-child spread) read 35-51% ink not from their own
        /// halo but from sampling straight into the neighbouring family's differently-tinted panel, which
        /// was never what this check meant to police. Every other sibling closer to its own column's
        /// centre never reaches that boundary and is unaffected by the clamp.</summary>
        public static float AnnulusInkFraction(Texture2D tex, float cx, float cy, float rInner, float rOuter,
            Color background, float tolerance, float sectorCenterDeg = 0f, float sectorHalfWidthDeg = 40f, int step = 3,
            float xMin = float.NegativeInfinity, float xMax = float.PositiveInfinity, BoardPixelTransform? transform = null)
        {
            int hit = 0, total = 0;
            for (float y = -rOuter; y <= rOuter; y += step)
                for (float x = -rOuter; x <= rOuter; x += step)
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    if (d < rInner || d > rOuter) continue;
                    float ang = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
                    bool inSector = Mathf.Abs(Mathf.DeltaAngle(sectorCenterDeg, ang)) <= sectorHalfWidthDeg
                                 || Mathf.Abs(Mathf.DeltaAngle(sectorCenterDeg + 180f, ang)) <= sectorHalfWidthDeg;
                    if (!inSector) continue;
                    float sampleX = cx + x;
                    if (sampleX < xMin || sampleX > xMax) continue;
                    total++;
                    if (ColorDistance(GetJsonPixel(tex, sampleX, cy + y, transform), background) > tolerance) hit++;
                }
            return total > 0 ? (float)hit / total : 0f;
        }

        /// <summary>Hue-direction distance between two colours — each normalised to its own RGB sum so
        /// only the ratio between channels counts, not overall brightness. rig_board.json's own
        /// comments (regionRect, lockedFusion) document that this project's Linear colour space makes a
        /// low-alpha wash display several times brighter than a naive sRGB-space alpha blend predicts —
        /// confirmed live running this exact check: an unlit category's actual fill measured brighter
        /// than <see cref="Color.Lerp"/> against a known alpha predicted, by roughly the same multiple
        /// those comments describe. Comparing hue direction instead of predicting exact composited
        /// brightness sidesteps needing to reverse-engineer that gamma curve here.</summary>
        public static float HueDistance(Color a, Color b)
        {
            Vector3 an = NormalizeHue(a), bn = NormalizeHue(b);
            return Vector3.Distance(an, bn);
        }

        private static Vector3 NormalizeHue(Color c)
        {
            float sum = c.r + c.g + c.b;
            return sum > 0.02f ? new Vector3(c.r, c.g, c.b) / sum : Vector3.zero;
        }

        public static string ColorHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public static string PassFailLine(string checkName, bool pass, string detail) =>
            $"{(pass ? "PASS" : "FAIL")} {checkName}: {detail}";
    }

    /// <summary>
    /// MV-777 — the gate that should have caught every World 2 visual failure: every ticket that
    /// touched the Stormdrain kit passed its own acceptance criteria while the actual frame collapsed
    /// to 10 luma of usable contrast, because every one of those criteria counted objects instead of
    /// measuring the image. This is a pure function over a <see cref="Texture2D"/> — no scene, no
    /// camera — so it runs identically whether it is checking a synthetic EditMode texture or a real
    /// capture PNG loaded off disk, and can be dropped into any capture preset that renders a play
    /// frame as a blocking check, the same class as <c>cc-verify</c>'s frame-rate assertion.
    /// </summary>
    public static class FrameContrastGate
    {
        /// <summary>MV-790 floor only — a genuinely flat frame still has to fail, but range is no
        /// longer the primary signal: it rewarded exactly the neon-on-near-black failure it was
        /// written to catch (two extremes with nothing between them scored 127 of range against the
        /// approved design's 50). Value tiers and colour dominance below now carry the real weight.</summary>
        public const float MinRange = 28f;

        /// <summary>How close (in luma) to the median a pixel has to be to count as "the same value"
        /// for <see cref="Result.MedianBandShare"/>.</summary>
        public const float MedianBandHalfWidth = 12f;

        /// <summary>Above this share of the frame sitting within <see cref="MedianBandHalfWidth"/> of
        /// the median, the frame is dominated by one value regardless of its overall range. MV-790:
        /// raised from 0.60 to the value the approved design actually reaches (0.70) — 0.60 was a gate
        /// nothing could pass.</summary>
        public const float MaxMedianBandShare = 0.70f;

        /// <summary>Width of one luma tier bucket.</summary>
        public const float TierBandWidth = 20f;

        /// <summary>A tier only counts if it holds at least this share of the sampled frame — a
        /// stray handful of outlier pixels must not manufacture a tier that was never actually there.</summary>
        public const float TierMinShare = 0.04f;

        /// <summary>MV-790: unchanged from MV-777, now the gate's primary signal.</summary>
        public const int MinDistinctTiers = 4;

        /// <summary>MV-790: RGB quantised to this many levels per channel before counting which single
        /// colour dominates the frame.</summary>
        public const int DominantColorQuantizeLevels = 12;

        /// <summary>Above this share of the sampled frame being the single most common quantised
        /// colour, the frame reads as neon-on-flat-background even when its luma range and tier count
        /// both look fine (a two-colour frame can still clear both if a stray gradient adds noise).</summary>
        public const float MaxDominantColorShare = 0.35f;

        public readonly struct Result
        {
            public readonly float RangeP95P5;
            public readonly float MedianBandShare;
            public readonly int DistinctTiers;
            public readonly float DominantColorShare;
            public readonly bool Pass;
            public readonly string FailReason;

            public Result(float rangeP95P5, float medianBandShare, int distinctTiers, float dominantColorShare, bool pass, string failReason)
            {
                RangeP95P5 = rangeP95P5;
                MedianBandShare = medianBandShare;
                DistinctTiers = distinctTiers;
                DominantColorShare = dominantColorShare;
                Pass = pass;
                FailReason = failReason;
            }

            public override string ToString() =>
                $"range={RigBoardConformance.Fmt(RangeP95P5)} medianBandShare={RigBoardConformance.Fmt(MedianBandShare * 100f)}% " +
                $"tiers={DistinctTiers} dominantColorShare={RigBoardConformance.Fmt(DominantColorShare * 100f)}% pass={Pass}" +
                (FailReason == null ? "" : $" ({FailReason})");
        }

        /// <summary>
        /// Computes the play-area luminance histogram of <paramref name="tex"/> — sampled every
        /// <paramref name="step"/> pixels, excluding the outer <paramref name="marginFrac"/> of the
        /// frame on every side (the skybox wedge / HUD margin a real capture would need cropped out) —
        /// and evaluates it against the MV-790 thresholds: distinct value tiers (primary), single-colour
        /// dominance, median-band dominance, and a usable-range floor.
        /// </summary>
        public static Result Check(Texture2D tex, float marginFrac = 0.10f, int step = 4)
        {
            int w = tex.width, h = tex.height;
            int mx = Mathf.RoundToInt(w * marginFrac);
            int my = Mathf.RoundToInt(h * marginFrac);

            var lumas = new List<float>();
            var colorCounts = new Dictionary<int, int>();
            for (int y = my; y < h - my; y += step)
                for (int x = mx; x < w - mx; x += step)
                {
                    Color c = tex.GetPixel(x, y);
                    lumas.Add((0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) * 255f);

                    int rq = Mathf.Clamp(Mathf.FloorToInt(c.r * DominantColorQuantizeLevels), 0, DominantColorQuantizeLevels - 1);
                    int gq = Mathf.Clamp(Mathf.FloorToInt(c.g * DominantColorQuantizeLevels), 0, DominantColorQuantizeLevels - 1);
                    int bq = Mathf.Clamp(Mathf.FloorToInt(c.b * DominantColorQuantizeLevels), 0, DominantColorQuantizeLevels - 1);
                    int key = (rq * DominantColorQuantizeLevels + gq) * DominantColorQuantizeLevels + bq;
                    colorCounts.TryGetValue(key, out int ccount);
                    colorCounts[key] = ccount + 1;
                }

            if (lumas.Count == 0) return new Result(0f, 1f, 0, 1f, false, "no pixels sampled inside the margin");

            lumas.Sort();
            float p5 = Percentile(lumas, 0.05f);
            float p95 = Percentile(lumas, 0.95f);
            float range = p95 - p5;
            float median = Percentile(lumas, 0.5f);

            int withinMedianBand = 0;
            foreach (float l in lumas)
                if (Mathf.Abs(l - median) <= MedianBandHalfWidth) withinMedianBand++;
            float medianShare = (float)withinMedianBand / lumas.Count;

            var bandCounts = new Dictionary<int, int>();
            foreach (float l in lumas)
            {
                int band = Mathf.FloorToInt(l / TierBandWidth);
                bandCounts.TryGetValue(band, out int count);
                bandCounts[band] = count + 1;
            }
            int tiers = 0;
            foreach (var kv in bandCounts)
                if ((float)kv.Value / lumas.Count >= TierMinShare) tiers++;

            int dominantColorCount = 0;
            foreach (var kv in colorCounts)
                if (kv.Value > dominantColorCount) dominantColorCount = kv.Value;
            float dominantColorShare = (float)dominantColorCount / lumas.Count;

            var reasons = new List<string>();
            if (tiers < MinDistinctTiers) reasons.Add($"only {tiers} distinct tiers (need {MinDistinctTiers})");
            if (dominantColorShare > MaxDominantColorShare) reasons.Add($"dominant colour {RigBoardConformance.Fmt(dominantColorShare * 100f)}% > {RigBoardConformance.Fmt(MaxDominantColorShare * 100f)}%");
            if (medianShare > MaxMedianBandShare) reasons.Add($"median-band share {RigBoardConformance.Fmt(medianShare * 100f)}% > {RigBoardConformance.Fmt(MaxMedianBandShare * 100f)}%");
            if (range < MinRange) reasons.Add($"range {RigBoardConformance.Fmt(range)} < {RigBoardConformance.Fmt(MinRange)}");

            bool pass = reasons.Count == 0;
            return new Result(range, medianShare, tiers, dominantColorShare, pass, pass ? null : string.Join("; ", reasons));
        }

        /// <summary>Linear-interpolated percentile over an already-sorted list.</summary>
        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 1) return sorted[0];
            float idx = p * (sorted.Count - 1);
            int lo = Mathf.FloorToInt(idx);
            int hi = Mathf.CeilToInt(idx);
            if (lo == hi) return sorted[lo];
            return Mathf.Lerp(sorted[lo], sorted[hi], idx - lo);
        }
    }
}
