using System.IO;
using MaxWorlds.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-104 / MV-1139. Apple rejects the upload — after a perfectly good archive — if the App
    /// Store icon is missing, not square, or carries an alpha channel. Those rules are only enforced
    /// by a real TestFlight upload, so pin them here instead of discovering them a 10-minute archive
    /// at a time.
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
        public void AppIcon_IsTheApprovedArt()
        {
            // MV-1139: the drawn-in-code mark is gone; this is the committed painted art, loaded and
            // scaled by IOSBuild.MakeAppIcon — not the file on disk and not a constant. On base
            // (the old code-drawn silhouette) this fails, because that drawing has no white at
            // (170, 800).
            Texture2D icon = IOSBuild.MakeAppIcon(1024);

            AssertPixel(icon, 40, 40, new Color32(253, 186, 58, 255), 0.04f, "gold top of the field");
            AssertPixel(icon, 40, 984, new Color32(227, 114, 29, 255), 0.04f, "orange bottom of the field");
            AssertPixel(icon, 170, 800, new Color32(255, 255, 255, 255), 0.04f, "inside the M");
            AssertPixel(icon, 860, 700, new Color32(255, 255, 255, 255), 0.04f, "inside the X");
            AssertPixel(icon, 535, 755, new Color32(15, 20, 26, 255), 0.04f, "the dark ring of the robot eye");
            AssertPixel(icon, 535, 790, new Color32(255, 176, 32, 255), 0.04f, "the gold centre of the robot eye");

            // Proves the small sizes are a scaled copy, not a blank: same gold top corner at 60px.
            Texture2D small = IOSBuild.MakeAppIcon(60);
            AssertPixel(small, 2, 2, new Color32(253, 186, 58, 255), 0.06f, "gold top at Settings size");
        }

        /// <summary>Samples at (x, row from the TOP); GetPixel counts rows from the bottom.</summary>
        private static void AssertPixel(Texture2D icon, int x, int rowFromTop, Color32 expected, float tolerance, string what)
        {
            Color actual = icon.GetPixel(x, icon.height - 1 - rowFromTop);

            Assert.That(actual.r, Is.EqualTo(expected.r / 255f).Within(tolerance), $"{what} — red");
            Assert.That(actual.g, Is.EqualTo(expected.g / 255f).Within(tolerance), $"{what} — green");
            Assert.That(actual.b, Is.EqualTo(expected.b / 255f).Within(tolerance), $"{what} — blue");
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
    }
}
