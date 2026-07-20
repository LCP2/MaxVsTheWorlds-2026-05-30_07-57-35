using System;
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

        // ---------------------------------------------------------------------
        // YT-117 — CFBundleVersion. Apple rejects a build whose number does not
        // strictly increase within a marketing-version train, and silently drops
        // a duplicate during async processing. These pin the three properties
        // that keep uploads acceptable.
        // ---------------------------------------------------------------------

        /// <summary>The build number already live in App Store Connect. Anything we generate from
        /// here on must exceed it, or Apple rejects the upload as non-increasing.</summary>
        private const long AlreadyUploaded = 2607191034L;

        [Test]
        public void ComposeIosBuildNumber_IncreasesWithTime()
        {
            string earlier = BuildStamp.ComposeIosBuildNumber(new DateTime(2026, 7, 19, 10, 34, 0, DateTimeKind.Utc));
            string later = BuildStamp.ComposeIosBuildNumber(new DateTime(2026, 7, 19, 10, 35, 0, DateTimeKind.Utc));

            Assert.Less(long.Parse(earlier), long.Parse(later));
        }

        [Test]
        public void ComposeIosBuildNumber_ExceedsTheBuildAlreadyInAppStoreConnect()
        {
            // A run number (11, 12, ...) would fail this — which is exactly why YT-117's proposed
            // GITHUB_RUN_NUMBER fix would have broken uploads instead of fixing them.
            string now = BuildStamp.ComposeIosBuildNumber(new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc));

            Assert.Greater(long.Parse(now), AlreadyUploaded);
        }

        [Test]
        public void ComposeIosBuildNumber_StaysWithinApplesNumericLimits()
        {
            // Must be all digits, <= 18 chars, and below 2^32 (App Store Connect rejects larger).
            // A seconds-resolution stamp (yyMMddHHmmss) would blow the 2^32 ceiling.
            string latest = BuildStamp.ComposeIosBuildNumber(new DateTime(2041, 12, 31, 23, 59, 0, DateTimeKind.Utc));

            Assert.That(latest, Does.Match(@"^\d+$"));
            Assert.LessOrEqual(latest.Length, 18);
            Assert.Less(long.Parse(latest), 4294967296L);
        }
    }
}
