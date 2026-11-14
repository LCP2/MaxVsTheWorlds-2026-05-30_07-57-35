using NUnit.Framework;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1376 — the capture harness boots a WebGL build straight into World N area aK from the URL
    /// alone. Asserts the resolved spec: <c>?world=4&amp;area=a1</c> maps to world 4 / area "a1" (and the
    /// 0-based override BackyardPath consumes), a malformed value is rejected.
    /// </summary>
    public sealed class MV1376UrlBootSpecTests
    {
        [Test]
        public void WorldAndAreaParams_ResolveToRequestedSpec_AndMalformedIsRejected()
        {
            Assert.IsTrue(DevModeController.TryParseBootSpec(
                "http://localhost:8080/index.html?hud=0&world=4&area=A1", out int world, out string area));
            Assert.AreEqual(4, world);
            Assert.AreEqual("a1", area);

            Assert.IsFalse(DevModeController.TryParseBootSpec("http://x/?world=banana&area=a1", out world, out area));
            Assert.AreEqual(0, world);
            Assert.IsNull(area);
            Assert.IsFalse(DevModeController.TryParseBootSpec("http://x/?world=0", out world, out _));
            Assert.IsFalse(DevModeController.TryParseBootSpec("http://x/?world=4&area=zzz", out world, out _));
        }
    }
}
