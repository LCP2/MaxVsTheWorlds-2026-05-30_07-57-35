using System.IO;
using MaxWorlds.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-104 / YT-115. Apple rejects the upload — after a perfectly good archive — if the App Store
    /// icon is missing, not square, or carries an alpha channel. Those rules are only enforced by a
    /// real TestFlight upload, so pin them here instead of discovering them a 10-minute archive at a
    /// time.
    ///
    /// YT-115 replaced the greybox with a real mark and added the last test in this file, which is
    /// the one that closes the hole the others left: everything above it tests the DRAWING CODE,
    /// while the thing that actually ships is the committed PNG. Those were free to drift apart.
    /// </summary>
    public class IOSIconTests
    {
        [TestCase(29)]   // the Settings row — the smallest the mark ever has to survive
        [TestCase(180)]
        [TestCase(1024)] // the App Store slot that was empty and blocked the upload
        public void AppIcon_IsSquareAtRequestedSize(int size)
        {
            Texture2D icon = IOSBuild.MakeAppIcon(size);

            Assert.That(icon.width, Is.EqualTo(size));
            Assert.That(icon.height, Is.EqualTo(size));
        }

        [Test]
        public void AppIcon_HasNoAlphaChannel()
        {
            // RGB24 has no alpha to strip; RGBA32 would export a transparent-capable PNG, which is
            // an outright App Store rejection even when every pixel is opaque.
            Texture2D icon = IOSBuild.MakeAppIcon(1024);

            Assert.That(icon.format, Is.EqualTo(TextureFormat.RGB24));
        }

        [Test]
        public void AppIcon_IsFullyOpaque()
        {
            Texture2D icon = IOSBuild.MakeAppIcon(64);

            foreach (Color pixel in icon.GetPixels())
            {
                Assert.That(pixel.a, Is.EqualTo(1f).Within(0.001f));
            }
        }

        [Test]
        public void AppIcon_ReadsAsAMarkNotAFlatSquare()
        {
            // The figure has to actually differ from the field, or the icon is a solid colour and
            // unrecognisable at phone size.
            //
            // Sampled on the CROWN, not the dead centre: the centre of this mark is Max's face,
            // which is deliberately light. The Craft Bible's "dark figure on a light field" is a
            // claim about the silhouette, so the silhouette is what gets measured.
            Texture2D icon = IOSBuild.MakeAppIcon(100);

            Color corner = icon.GetPixel(2, 2);
            Color crown = icon.GetPixel(50, 80);

            Assert.That(crown, Is.Not.EqualTo(corner));
            Assert.That(crown.grayscale, Is.LessThan(corner.grayscale - 0.2f),
                "the silhouette is not clearly darker than the field it sits on");
        }

        [Test]
        public void AppIcon_KeepsItsGogglesLit_EvenAtSettingsSize()
        {
            // The goggles are the mark's one bright accent and the first thing lost to a bad
            // downscale. At 29 px they are about four pixels across, so if the antialiasing is
            // wrong they wash into the silhouette and the icon becomes an anonymous dark blob.
            Texture2D icon = IOSBuild.MakeAppIcon(29);

            float brightest = 0f;
            float darkest = 1f;
            foreach (Color p in icon.GetPixels())
            {
                // Look only at cool pixels — the orange field is bright too, so plain luminance
                // would just find the background.
                if (p.b > p.r) brightest = Mathf.Max(brightest, p.grayscale);
                darkest = Mathf.Min(darkest, p.grayscale);
            }

            Assert.That(brightest, Is.GreaterThan(0.5f),
                "the goggles vanished at Settings size — the mark is an anonymous blob");
            Assert.That(brightest - darkest, Is.GreaterThan(0.35f),
                "not enough contrast between the lenses and the silhouette");
        }

        [Test]
        public void AppIcon_IsNoLongerTheGreybox()
        {
            // The greybox was two flat colours with a hard edge between them: a dark square on plain
            // orange, no gradient and no antialiasing. Rather than pin a coordinate — which is
            // brittle the moment the artwork is nudged — assert the two properties it could not
            // possibly have.
            Texture2D icon = IOSBuild.MakeAppIcon(100);

            // 1. The field is a ramp, so the top corner is not the bottom corner.
            Color bottomCorner = icon.GetPixel(2, 2);
            Color topCorner = icon.GetPixel(2, 97);
            Assert.That(Mathf.Abs(topCorner.grayscale - bottomCorner.grayscale), Is.GreaterThan(0.1f),
                "the field is flat — this is still the greybox's plain orange");

            // 2. Real drawing means many tones: a gradient, feathered edges, and more than one
            // element. Two-colour art cannot produce this.
            var tones = new System.Collections.Generic.HashSet<int>();
            foreach (Color p in icon.GetPixels()) tones.Add(Mathf.RoundToInt(p.grayscale * 255f));

            Assert.That(tones.Count, Is.GreaterThan(40),
                $"only {tones.Count} distinct tones — this is flat, hard-edged placeholder art");
        }

        [Test]
        public void CommittedIcon_MatchesWhatTheCodeDraws()
        {
            // THE important one. PlayerSettings references the icon by asset GUID, so the committed
            // PNG is what ships — not IOSBuild.MakeAppIcon. Before YT-115 these were two independent
            // drawings and nothing compared them, which meant a checkout whose LFS pull had not run
            // would regenerate the OLD art and ship it with every test above still green.
            var committed = AssetDatabase.LoadAssetAtPath<Texture2D>(IOSBuild.IconAssetPath);
            Assert.IsNotNull(committed,
                $"{IOSBuild.IconAssetPath} is missing — the icon that actually ships is not in the repo");

            Assert.That(committed.width, Is.EqualTo(IOSBuild.MarketingIconSize));
            Assert.That(committed.height, Is.EqualTo(IOSBuild.MarketingIconSize));

            // Compare structurally rather than pixel-for-pixel: the asset has been through PNG
            // encode and Unity's importer, so exact equality would fail on rounding. An 8x8 average
            // is immune to that and still catches "this is a completely different picture".
            Texture2D fresh = IOSBuild.MakeAppIcon(IOSBuild.MarketingIconSize);
            const int Grid = 8;

            for (int gy = 0; gy < Grid; gy++)
            {
                for (int gx = 0; gx < Grid; gx++)
                {
                    Color a = CellAverage(committed, gx, gy, Grid);
                    Color b = CellAverage(fresh, gx, gy, Grid);

                    Assert.That(a.r, Is.EqualTo(b.r).Within(0.06f), $"cell {gx},{gy} red");
                    Assert.That(a.g, Is.EqualTo(b.g).Within(0.06f), $"cell {gx},{gy} green");
                    Assert.That(a.b, Is.EqualTo(b.b).Within(0.06f),
                        $"cell {gx},{gy} blue — the committed PNG has drifted from the drawing code. " +
                        "Run MaxWorlds/iOS/Regenerate App Icon and commit the result.");
                }
            }
        }

        [Test]
        public void CommittedIcon_IsARealFile_NotAnUnpulledLfsPointer()
        {
            // Git LFS leaves a ~130-byte text pointer in place of the real file when it has not been
            // pulled. Unity imports that as nothing useful, and the failure shows up as a blank icon
            // at the end of a CI archive rather than here.
            var info = new FileInfo(IOSBuild.IconAssetPath);

            Assert.IsTrue(info.Exists, $"{IOSBuild.IconAssetPath} does not exist on disk");
            Assert.That(info.Length, Is.GreaterThan(1024),
                "the icon file is pointer-sized — LFS content was never pulled");
        }

        private static Color CellAverage(Texture2D tex, int gx, int gy, int grid)
        {
            int w = tex.width / grid;
            int h = tex.height / grid;
            Color[] block = tex.GetPixels(gx * w, gy * h, w, h);

            float r = 0f, g = 0f, b = 0f;
            foreach (Color c in block) { r += c.r; g += c.g; b += c.b; }

            return new Color(r / block.Length, g / block.Length, b / block.Length, 1f);
        }
    }
}
