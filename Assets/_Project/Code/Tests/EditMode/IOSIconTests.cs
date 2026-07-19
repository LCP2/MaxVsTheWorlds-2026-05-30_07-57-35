using MaxWorlds.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-104. Apple rejects the upload — after a perfectly good archive — if the App Store icon is
    /// missing, not square, or carries an alpha channel. Those rules are only enforced by a real
    /// TestFlight upload, so pin them here instead of discovering them a 10-minute archive at a time.
    /// </summary>
    public class IOSIconTests
    {
        [TestCase(29)]
        [TestCase(180)]
        [TestCase(1024)] // the App Store slot that was empty and blocked the upload
        public void PlaceholderIcon_IsSquareAtRequestedSize(int size)
        {
            Texture2D icon = IOSBuild.MakePlaceholderIcon(size);

            Assert.That(icon.width, Is.EqualTo(size));
            Assert.That(icon.height, Is.EqualTo(size));
        }

        [Test]
        public void PlaceholderIcon_HasNoAlphaChannel()
        {
            // RGB24 has no alpha to strip; RGBA32 would export a transparent-capable PNG, which is
            // an outright App Store rejection even when every pixel is opaque.
            Texture2D icon = IOSBuild.MakePlaceholderIcon(1024);

            Assert.That(icon.format, Is.EqualTo(TextureFormat.RGB24));
        }

        [Test]
        public void PlaceholderIcon_IsFullyOpaque()
        {
            Texture2D icon = IOSBuild.MakePlaceholderIcon(64);

            foreach (Color pixel in icon.GetPixels())
            {
                Assert.That(pixel.a, Is.EqualTo(1f).Within(0.001f));
            }
        }

        [Test]
        public void PlaceholderIcon_ReadsAsAMarkNotAFlatSquare()
        {
            // The centred block has to actually differ from the ground, or the icon is a solid
            // colour and unrecognisable at phone size. Corner vs centre is the whole design.
            Texture2D icon = IOSBuild.MakePlaceholderIcon(100);

            Color corner = icon.GetPixel(2, 2);
            Color centre = icon.GetPixel(50, 50);

            Assert.That(centre, Is.Not.EqualTo(corner));
            // Dark mark on a light ground — the contrast direction the Craft Bible asks for.
            Assert.That(centre.grayscale, Is.LessThan(corner.grayscale));
        }
    }
}
