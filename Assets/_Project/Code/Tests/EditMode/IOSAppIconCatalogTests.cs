using MaxWorlds.Editor;
using NUnit.Framework;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-104. The App Store icon entry is the one thing Apple blocks the upload on, and the only
    /// way to find out the hard way costs a ~15-minute archive plus an upload. Pin the catalog
    /// rewrite here instead.
    /// </summary>
    public class IOSAppIconCatalogTests
    {
        // Verbatim from the Xcode project Unity generated in run 29682706214 — five device slots
        // and no ios-marketing entry, which is exactly what Apple rejected.
        private const string UnityGenerated = @"{
	""images"" : [
		{
			""filename"" : ""Icon-iPhone-120.png"",
			""idiom"" : ""iphone"",
			""scale"" : ""2x"",
			""size"" : ""60x60""
		},
		{
			""filename"" : ""Icon-iPad-167.png"",
			""idiom"" : ""ipad"",
			""scale"" : ""2x"",
			""size"" : ""83.5x83.5""
		}
	],
	""info"" : {
		""author"" : ""xcode"",
		""version"" : 1
	}
}";

        [Test]
        public void AddsMarketingEntry_WhenUnityOmittedIt()
        {
            string result = IOSAppIconPostprocessor.AddMarketingIconEntry(UnityGenerated, "Icon-1024.png");

            Assert.That(result, Does.Contain("ios-marketing"));
            Assert.That(result, Does.Contain("1024x1024"));
            Assert.That(result, Does.Contain("Icon-1024.png"));
        }

        [Test]
        public void KeepsTheExistingSlots()
        {
            string result = IOSAppIconPostprocessor.AddMarketingIconEntry(UnityGenerated, "Icon-1024.png");

            Assert.That(result, Does.Contain("Icon-iPhone-120.png"));
            Assert.That(result, Does.Contain("Icon-iPad-167.png"));
        }

        [Test]
        public void IsIdempotent_SoARebuildDoesNotDuplicateTheEntry()
        {
            string once = IOSAppIconPostprocessor.AddMarketingIconEntry(UnityGenerated, "Icon-1024.png");
            string twice = IOSAppIconPostprocessor.AddMarketingIconEntry(once, "Icon-1024.png");

            Assert.That(twice, Is.EqualTo(once));
        }

        [Test]
        public void ProducesBalancedJson_WithNoTrailingComma()
        {
            string result = IOSAppIconPostprocessor.AddMarketingIconEntry(UnityGenerated, "Icon-1024.png");

            Assert.That(CountOf(result, '{'), Is.EqualTo(CountOf(result, '}')));
            Assert.That(CountOf(result, '['), Is.EqualTo(CountOf(result, ']')));
            Assert.That(result.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", ""),
                Does.Not.Contain(",]"));
        }

        [Test]
        public void HandlesAnEmptyImagesArray_WithoutAStrayComma()
        {
            const string empty = "{\n\t\"images\" : [],\n\t\"info\" : { \"version\" : 1 }\n}";

            string result = IOSAppIconPostprocessor.AddMarketingIconEntry(empty, "Icon-1024.png");

            Assert.That(result, Does.Contain("ios-marketing"));
            Assert.That(result.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", ""),
                Does.Not.Contain(",]"));
        }

        private static int CountOf(string text, char c)
        {
            int n = 0;
            foreach (char ch in text)
            {
                if (ch == c) n++;
            }
            return n;
        }
    }
}
