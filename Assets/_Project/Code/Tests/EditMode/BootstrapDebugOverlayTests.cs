using NUnit.Framework;
using MaxWorlds.Core;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-342 — the on-screen FPS/build readout is a QA smoke-verification tool (YT-32/YT-62), not
    /// player-facing UI. It must disappear from the one build real players actually get (iOS
    /// TestFlight/App Store) while staying up on every dev/QA surface (WebGL Pages link, the
    /// cc-verify Windows standalone, the Editor).
    /// </summary>
    public sealed class BootstrapDebugOverlayTests
    {
        [Test]
        public void IosPlayerBuild_HidesTheOverlay()
        {
            Assert.IsFalse(Bootstrap.ShouldShowDebugOverlay(true, RuntimePlatform.IPhonePlayer, false));
        }

        [Test]
        public void IosInTheEditor_StillShowsTheOverlay()
        {
            // A developer testing iOS-specific behaviour in the Editor is not a real player.
            Assert.IsTrue(Bootstrap.ShouldShowDebugOverlay(true, RuntimePlatform.IPhonePlayer, true));
        }

        [Test]
        public void WebGlBuild_ShowsTheOverlay()
        {
            Assert.IsTrue(Bootstrap.ShouldShowDebugOverlay(true, RuntimePlatform.WebGLPlayer, false));
        }

        [Test]
        public void WindowsStandaloneBuild_ShowsTheOverlay()
        {
            Assert.IsTrue(Bootstrap.ShouldShowDebugOverlay(true, RuntimePlatform.WindowsPlayer, false));
        }

        [Test]
        public void FlagOff_HidesTheOverlayEverywhere()
        {
            Assert.IsFalse(Bootstrap.ShouldShowDebugOverlay(false, RuntimePlatform.WebGLPlayer, false));
            Assert.IsFalse(Bootstrap.ShouldShowDebugOverlay(false, RuntimePlatform.WindowsPlayer, true));
        }
    }
}
