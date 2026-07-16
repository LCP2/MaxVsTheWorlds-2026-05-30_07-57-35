using NUnit.Framework;
using MaxWorlds.Editor;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-99 — the build-SHA stamp (YT-62) writes a version like "60c7413-0512" into
    /// PlayerSettings.bundleVersion, which is fine for WebGL/Windows but ILLEGAL as an iOS
    /// CFBundleShortVersionString (digits and dots only). That failed the iOS CI build. These
    /// guard the iOS-version selection so the pipeline can't regress into that again.
    /// </summary>
    public sealed class BuildStampTests
    {
        [TestCase("0.0.110", true)]   // GameCI's semantic version
        [TestCase("1.0.0", true)]
        [TestCase("0", true)]
        [TestCase("12.34.56", true)]
        [TestCase("60c7413-0512", false)] // the SHA stamp — letters + dash
        [TestCase("local-0716-0359", false)]
        [TestCase("1.0.0-beta", false)]
        [TestCase("1.", false)]        // must end with a number
        [TestCase(".1", false)]        // must begin with a number
        [TestCase("1..0", false)]      // no empty components
        [TestCase("1234567890.1234567890", false)] // > 18 chars
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsValidIosVersion_MatchesApplesRule(string input, bool expected)
        {
            Assert.AreEqual(expected, BuildStamp.IsValidIosVersion(input));
        }

        [Test]
        public void ComposeIosVersion_UsesGameCiVersionWhenValid()
        {
            Assert.AreEqual("0.0.110", BuildStamp.ComposeIosVersion("0.0.110"));
        }

        [Test]
        public void ComposeIosVersion_FallsBackWhenTheStampIsIllegal()
        {
            // The SHA stamp and an empty env both fall back to the stable marketing version.
            Assert.AreEqual("0.1.0", BuildStamp.ComposeIosVersion("60c7413-0512"));
            Assert.AreEqual("0.1.0", BuildStamp.ComposeIosVersion(null));
            Assert.AreEqual("0.1.0", BuildStamp.ComposeIosVersion(""));
        }
    }
}
