using NUnit.Framework;
using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-252's crop math: <see cref="SplashScreen.CoverSize"/> is the "background-size: cover"
    /// sizing used to show Splash.png full-bleed on any device aspect without distortion or
    /// letterbox bars, while keeping the art's top-left corner (the open teal-sky safe area the
    /// ticket calls out) pinned in frame — <see cref="SplashScreen.FitCover"/> always anchors that
    /// corner, so what has to be pinned here is that the SIZE this returns never leaves a gap.
    /// </summary>
    public sealed class SplashScreenTests
    {
        // Splash.png's real dimensions (Assets/_Project/Resources/Art/Splash.png).
        private const float TexW = 1456f;
        private const float TexH = 816f;

        [Test]
        public void OnAWiderScreenThanTheArt_ItMatchesWidthAndOverflowsHeight()
        {
            // A very wide/short frame relative to the art's ~1.78:1 — width must be pinned exactly to
            // the frame, and height must be AT LEAST the frame's (cover, never under-fill).
            Vector2 size = SplashScreen.CoverSize(2000f, 600f, TexW, TexH);

            Assert.AreEqual(2000f, size.x, 1e-2, "cover must match the frame's width exactly");
            Assert.GreaterOrEqual(size.y, 600f, "cropped dimension must never fall short of the frame");
        }

        [Test]
        public void OnATallerScreenThanTheArt_ItMatchesHeightAndOverflowsWidth()
        {
            // A phone-shaped frame, much taller relative to its width than the art — height pinned,
            // width overflows (this is the crop that gets trimmed off the right, not the sky).
            Vector2 size = SplashScreen.CoverSize(1080f, 2340f, TexW, TexH);

            Assert.AreEqual(2340f, size.y, 1e-2, "cover must match the frame's height exactly");
            Assert.GreaterOrEqual(size.x, 1080f, "cropped dimension must never fall short of the frame");
        }

        [Test]
        public void NeverScalesTheArtNonUniformly()
        {
            // The one failure mode that would stretch Max's face: check the returned box has the
            // same aspect ratio as the source art, for a spread of frame shapes.
            float texAspect = TexW / TexH;
            foreach (var frame in new[] { new Vector2(2000f, 600f), new Vector2(1080f, 2340f), new Vector2(1456f, 816f), new Vector2(800f, 800f) })
            {
                Vector2 size = SplashScreen.CoverSize(frame.x, frame.y, TexW, TexH);
                Assert.AreEqual(texAspect, size.x / size.y, 1e-3,
                    $"frame {frame} produced a non-uniform scale — the art would look stretched");
            }
        }

        [Test]
        public void ExactlyMatchingAspectRatiosFillTheFrameWithNoOverflow()
        {
            Vector2 size = SplashScreen.CoverSize(TexW, TexH, TexW, TexH);
            Assert.AreEqual(TexW, size.x, 1e-2);
            Assert.AreEqual(TexH, size.y, 1e-2);
        }

        [Test]
        public void FitCoverPinsTheArtsTopLeftCornerToTheFramesTopLeftCorner()
        {
            // This is what actually keeps the open teal-sky safe area (top-left of Splash.png, per
            // the ticket) on screen: the art's anchor/pivot must be top-left with zero offset, so any
            // overflow computed above is trimmed from the right/bottom, never the top/left.
            var frameGo = new GameObject("Frame", typeof(RectTransform));
            var artGo = new GameObject("Art", typeof(RectTransform));
            try
            {
                var frame = (RectTransform)frameGo.transform;
                frame.sizeDelta = new Vector2(1080f, 2340f);
                var art = (RectTransform)artGo.transform;
                art.SetParent(frame, false);

                var sprite = Sprite.Create(new Texture2D((int)TexW, (int)TexH), new Rect(0, 0, TexW, TexH), Vector2.zero);
                SplashScreen.FitCover(frame, art, sprite);

                Assert.AreEqual(new Vector2(0f, 1f), art.anchorMin);
                Assert.AreEqual(new Vector2(0f, 1f), art.anchorMax);
                Assert.AreEqual(new Vector2(0f, 1f), art.pivot);
                Assert.AreEqual(Vector2.zero, art.anchoredPosition);
            }
            finally
            {
                Object.DestroyImmediate(artGo);
                Object.DestroyImmediate(frameGo);
            }
        }
    }
}
