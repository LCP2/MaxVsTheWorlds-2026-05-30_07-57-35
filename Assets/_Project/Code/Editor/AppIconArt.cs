using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// The MAX vs THE WORLDS app icon (YT-115), drawn in code.
    ///
    /// What it replaces was a dark square on orange — a greybox that existed only to stop Apple
    /// rejecting the archive (YT-104). This is the first thing anyone sees of the game, on a
    /// TestFlight row and then on a home screen, so it gets a real mark.
    ///
    /// THE MARK: Max, hood up and goggles on, head-on — a dark silhouette with a lit face and two
    /// burning cyan lenses, on his own orange. Three reasons it is this and not a monogram:
    ///
    ///   * It is a CHARACTER, and the game is named after him. An "M" would be readable and mean
    ///     nothing; the pillar is "loud, readable, BRANDED".
    ///   * It survives being tiny. On a home screen this is about forty pixels across, which is
    ///     roughly the size the robots are drawn at in-game — the whole art direction is already
    ///     built around reading a character at that size, and the answer there was the same one as
    ///     here: one bold silhouette plus one bright pair of eyes.
    ///   * The colour hierarchy is the game's own, inverted for the frame. In-game Max is the warm
    ///     figure against a cool ground (CharacterSkin). An icon has no ground to speak of, so the
    ///     FIELD takes his orange and the figure goes dark — and the goggles keep the one cool
    ///     accent, cyan against orange, which is the hardest contrast available and the reason the
    ///     eyes still read when the icon is a thumbnail in a list.
    ///
    /// Drawn with signed-distance fields rather than by filling pixel rectangles. It costs a few
    /// more lines and buys the thing a hand-drawn PNG would have: real antialiasing at any size, so
    /// the same code produces a clean 1024 for the App Store and a clean 29 for a Settings row
    /// without a resampling step that turns the goggles to mush.
    ///
    /// No alpha, ever — see <see cref="Build"/>.
    /// </summary>
    public static class AppIconArt
    {
        // The field. Max's hot orange-red (CharacterSkin.PlayerBody) is the bottom of the ramp; the
        // top lifts toward the Backyard's afternoon gold. A vertical ramp rather than a flat fill
        // because a flat orange square reads as a placeholder no matter what is drawn on it.
        private static readonly Color FieldTop = new Color(0.98f, 0.68f, 0.24f);
        private static readonly Color FieldBottom = new Color(0.90f, 0.34f, 0.10f);

        /// <summary>The silhouette. Not pure black — a blue-dark, so it sits in the same family as
        /// the boss's near-black chassis instead of looking like a hole cut in the icon.</summary>
        private static readonly Color Figure = new Color(0.09f, 0.10f, 0.14f);

        /// <summary>The face, inside the hood. Warm and in shadow — the hood is over it, so it is a
        /// step down from the field rather than a bright spot competing with the goggles.
        ///
        /// NOTE FOR LEE: this is the one value here that is a character decision rather than a
        /// graphic one, and it was mine by default. Say the word and it changes.</summary>
        private static readonly Color Face = new Color(0.91f, 0.70f, 0.52f);

        /// <summary>The goggle bridge and strap ends — a step up from the silhouette, so the goggles
        /// read as one object worn over the face instead of two loose discs.</summary>
        private static readonly Color Strap = new Color(0.17f, 0.18f, 0.23f);

        /// <summary>The lenses. Cyan-white, the one cool thing in the frame: against this orange it
        /// is the strongest contrast on the wheel, which is what keeps the eyes alive at 40 px.</summary>
        private static readonly Color LensHot = new Color(0.82f, 0.99f, 1f);
        private static readonly Color LensDeep = new Color(0.25f, 0.72f, 0.95f);

        // ---------------------------------------------------------------- layout
        //
        // Everything below is in a square normalised to [-1, 1] with +y up. The whole mark is kept
        // inside a radius of about 0.8: iOS masks the icon to a rounded rectangle and then shrinks
        // it, so anything near a corner is either clipped or crowded.

        // The hood is built from two shapes, not one: a crown circle and a jaw box below it. A single
        // circle is what the first attempt used and it read as a bald head in a balaclava — a hood
        // is TALLER than a skull and it flares where it gathers around the face, and those are
        // exactly the two things the second shape adds.
        private static readonly Vector2 CrownAt = new Vector2(0f, 0.25f);
        private const float CrownRadius = 0.44f;

        private static readonly Vector2 JawAt = new Vector2(0f, 0.07f);
        private static readonly Vector2 JawHalf = new Vector2(0.40f, 0.28f);
        private const float JawRound = 0.22f;

        /// <summary>The hood's crown peak, high and just off-centre. The same trick the rusher's
        /// antenna plays — an asymmetric fleck is what a silhouette reads as character. Kept small
        /// and near the top: lower down and larger, it stops reading as a hood and starts reading as
        /// a lump.</summary>
        private static readonly Vector2 PeakAt = new Vector2(0.20f, 0.55f);
        private const float PeakRadius = 0.09f;

        // Shoulders: WIDE and FLAT, and wider than the hood. This is what stops the whole thing
        // being one continuous gourd — a kid in a hoodie has a shoulder line, and a silhouette
        // without one reads as a cloak, or worse, a monk.
        private static readonly Vector2 ShouldersAt = new Vector2(0f, -0.42f);
        private static readonly Vector2 ShouldersHalf = new Vector2(0.60f, 0.17f);
        private const float ShouldersRound = 0.15f;

        /// <summary>How softly the hood melts into the shoulders — the neck, in one number.
        ///
        /// It is a narrow window and both walls are visible from here. Too loose and the hood and
        /// shoulders swell into a single gourd that reads as a cloaked monk; too tight and the
        /// shoulders detach into a bar floating under a head. The shapes have to nearly touch and
        /// this has to be about the size of the gap left between them.</summary>
        private const float NeckBlend = 0.16f;

        // The face sits LOW in the hood, which is the whole hood read: the band of dark above it is
        // the hood over his head, and without that gap this is just a face on a blob.
        private static readonly Vector2 FaceAt = new Vector2(0f, 0.13f);
        private const float FaceRadius = 0.255f;

        private const float LensX = 0.132f;
        private const float LensY = 0.21f;
        private const float LensRadius = 0.103f;

        /// <summary>
        /// The icon at any size, always RGB24 and always fully opaque.
        ///
        /// The format is not a detail: Apple rejects an App Store icon that carries an alpha channel
        /// even when every pixel of it is opaque, and that rejection arrives at the END of a long
        /// archive-and-upload. RGB24 has no channel to give away.
        /// </summary>
        public static Texture2D Build(int size)
        {
            size = Mathf.Max(1, size);
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var px = new Color[size * size];

            // One pixel, in the normalised space above — the width the edges are feathered over.
            // Scaling the feather with the icon is what makes a 29 px icon as clean as a 1024.
            float aa = 1.3f * (2f / size);

            for (int y = 0; y < size; y++)
            {
                // +0.5 samples pixel centres; the flip puts +y up, so the ramp runs the way it reads.
                float ny = ((y + 0.5f) / size) * 2f - 1f;

                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size) * 2f - 1f;
                    px[y * size + x] = Shade(new Vector2(nx, ny), aa);
                }
            }

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>One pixel: the field, then the figure, then the strap, then the lenses — painted
        /// back to front, each one feathered over its own edge.</summary>
        private static Color Shade(Vector2 p, float aa)
        {
            // The field, plus a soft corner falloff. The vignette is very light: it is there to stop
            // the orange reading as a flat swatch, not to be seen.
            Color c = Color.Lerp(FieldBottom, FieldTop, Smooth01(p.y * 0.5f + 0.5f));
            c *= Mathf.Lerp(1f, 0.90f, Mathf.Clamp01(p.magnitude - 0.55f));

            // The hood: crown over jaw, with one fold to break the symmetry.
            float hood = SmoothUnion(Circle(p, CrownAt, CrownRadius),
                                     RoundBox(p, JawAt, JawHalf, JawRound), 0.16f);
            hood = SmoothUnion(hood, Circle(p, PeakAt, PeakRadius), 0.14f);

            float figure = SmoothUnion(hood,
                                       RoundBox(p, ShouldersAt, ShouldersHalf, ShouldersRound),
                                       NeckBlend);
            c = Over(c, Figure, Coverage(figure, aa));

            // The face, cut into the hood. Clipped to the hood so it can never bleed past the
            // silhouette's edge and break the outline.
            float face = Mathf.Max(Circle(p, FaceAt, FaceRadius), hood + 0.055f);
            c = Over(c, Face, Coverage(face, aa));

            // The bridge between the lenses, so the goggles are one worn object rather than two
            // loose discs. Clipped to the face for the same reason the face is clipped to the hood.
            float bridge = Mathf.Max(RoundBox(p, new Vector2(0f, LensY), new Vector2(LensX, 0.032f), 0.03f),
                                     Circle(p, FaceAt, FaceRadius));
            c = Over(c, Strap, Coverage(bridge, aa));

            for (int i = 0; i < 2; i++)
            {
                var at = new Vector2((i == 0 ? -1f : 1f) * LensX, LensY);
                float lens = Circle(p, at, LensRadius);

                // A dark rim around each lens, so the cyan never touches the face directly — the
                // rim is what stops the goggles reading as painted-on eyes.
                c = Over(c, Strap, Coverage(lens - 0.022f, aa));

                // A lit lens rather than a flat disc: bright at the top-left, deeper at the bottom,
                // so at large sizes it reads as glass catching the light instead of a printed dot.
                float lit = Mathf.Clamp01(0.5f - (p - at).y / (LensRadius * 2.2f)
                                               - (p - at).x / (LensRadius * 3.5f));
                c = Over(c, Color.Lerp(LensHot, LensDeep, lit), Coverage(lens, aa));
            }

            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }

        // ---------------------------------------------------------------- distance fields

        /// <summary>Negative inside, positive outside, zero on the edge — the convention every
        /// helper here shares, and what lets <see cref="Coverage"/> antialias all of them the same
        /// way.</summary>
        private static float Circle(Vector2 p, Vector2 at, float r) => (p - at).magnitude - r;

        private static float RoundBox(Vector2 p, Vector2 at, Vector2 half, float round)
        {
            float dx = Mathf.Abs(p.x - at.x) - (half.x - round);
            float dy = Mathf.Abs(p.y - at.y) - (half.y - round);
            float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - round;
        }

        /// <summary>Union of two shapes with a fillet where they meet, rather than a crease. The
        /// fillet is the hoodie's neck.</summary>
        private static float SmoothUnion(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        /// <summary>How much of this pixel the shape covers, 0..1, feathered over one pixel.</summary>
        private static float Coverage(float distance, float aa) => 1f - Smooth01((distance + aa) / (2f * aa));

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static Color Over(Color under, Color over, float alpha) => Color.Lerp(under, over, alpha);
    }
}
