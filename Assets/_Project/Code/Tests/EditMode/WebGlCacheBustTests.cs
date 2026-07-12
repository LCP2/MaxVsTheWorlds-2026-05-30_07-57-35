using NUnit.Framework;
using MaxWorlds.Editor;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-62 — the WebGL cache-buster.
    ///
    /// Unity's loader fetches Build/*.data, *.wasm and *.js by filenames that never change between
    /// builds, so a browser that has seen the play link before can serve the WHOLE GAME from cache
    /// and show a weeks-old build with no error and no sign. That makes every "the fix isn't there"
    /// report unfalsifiable — and it cost us a review cycle.
    ///
    /// This rewrite runs at build time, where a silent no-op would be invisible. Hence tests.
    /// </summary>
    public sealed class WebGlCacheBustTests
    {
        /// <summary>A faithful slice of Unity's real WebGL template.</summary>
        private const string Template = @"
      var buildUrl = ""Build"";
      var loaderUrl = buildUrl + ""/WebGL.loader.js"";
      var config = {
        dataUrl: buildUrl + ""/WebGL.data"",
        frameworkUrl: buildUrl + ""/WebGL.framework.js"",
        codeUrl: buildUrl + ""/WebGL.wasm"",
        streamingAssetsUrl: ""StreamingAssets"",
      };";

        [Test]
        public void EveryBuildUrl_GetsTheVersionQuery()
        {
            string patched = WebGlCacheBust.Patch(Template, "abc1234-0712-1530");

            Assert.That(patched, Does.Contain("/WebGL.loader.js?v=abc1234-0712-1530"));
            Assert.That(patched, Does.Contain("/WebGL.data?v=abc1234-0712-1530"));
            Assert.That(patched, Does.Contain("/WebGL.framework.js?v=abc1234-0712-1530"));
            Assert.That(patched, Does.Contain("/WebGL.wasm?v=abc1234-0712-1530"));
        }

        [Test]
        public void ItDoesNotTouchAnythingThatIsNotABuildUrl()
        {
            string patched = WebGlCacheBust.Patch(Template, "v1");

            Assert.That(patched, Does.Contain(@"streamingAssetsUrl: ""StreamingAssets"""),
                "only the Build/* URLs are cached by the loader — leave the rest alone");
            Assert.That(patched, Does.Contain(@"var buildUrl = ""Build"""));
        }

        [Test]
        public void RunningItTwice_DoesNotDoubleUpTheQuery()
        {
            string once = WebGlCacheBust.Patch(Template, "v1");
            string twice = WebGlCacheBust.Patch(once, "v1");

            Assert.AreEqual(once, twice, "a URL that already carries a query must be left alone");
            Assert.That(twice, Does.Not.Contain("?v=v1?v=v1"));
        }

        [Test]
        public void ATemplateItCannotUnderstand_IsLeftUntouched_SoTheBuildStillWorks()
        {
            const string alien = "<html><body>nothing to see</body></html>";
            Assert.AreEqual(alien, WebGlCacheBust.Patch(alien, "v1"),
                "if the template changes shape, corrupting index.html would be far worse than " +
                "failing to cache-bust — the caller logs loudly instead");
        }

        [Test]
        public void BuildStamp_IsAlwaysNonEmpty_SoAVersionIsAlwaysVisible()
        {
            string stamp = BuildStamp.Compose();

            Assert.That(stamp, Is.Not.Null.And.Not.Empty);
            Assert.That(stamp, Does.Contain("-"), "expected <sha>-<timestamp>");
        }
    }
}
