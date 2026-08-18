using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Dev;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-421: the ui-screens job captures THE RIG and the HUD at two explicit pixel sizes (1920x1080
    /// and 1728x1080) rather than whatever the ambient Game View window happens to be. CanvasScaler's
    /// own "Scale With Screen Size" always reads Screen.width/Screen.height regardless of a canvas's
    /// render mode or its camera's target texture, and Screen.SetResolution is a no-op in Editor Play
    /// Mode — so <see cref="PressKitDirector.ComputeScaleFactor"/> reimplements CanvasScaler's own
    /// match-width-or-height formula against an explicit size instead. It's a pure function and the one
    /// piece of this job's logic verifiable without an actual capture.
    /// </summary>
    public sealed class PressKitDirectorTests
    {
        private static CanvasScaler NewScaler(float refW, float refH, float matchWidthOrHeight)
        {
            var go = new GameObject("scaler-test");
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(refW, refH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = matchWidthOrHeight;
            return scaler;
        }

        [Test]
        public void MatchByHeight_AtTheCanvasReferenceFrame_ScalesOneToOne()
        {
            // WeaponsScreen/HomeScreen/ResultScreen all use matchWidthOrHeight=1 against a 1920x1080
            // reference — captured at that exact size, one reference unit must be exactly one pixel.
            var scaler = NewScaler(1920f, 1080f, 1f);
            try
            {
                Assert.That(PressKitDirector.ComputeScaleFactor(scaler, 1920, 1080), Is.EqualTo(1f).Within(1e-5f));
            }
            finally { Object.DestroyImmediate(scaler.gameObject); }
        }

        [Test]
        public void MatchByHeight_AtTheNarrowestBrowserAspect_StillScalesOneToOne()
        {
            // The MV-421 arithmetic this job exists to catch: at 1.6:1 (1728x1080), match-by-height
            // still resolves to scaleFactor 1 because the height is unchanged, so the visible reference
            // width collapses to exactly the pixel width — 1728, not the full 1920 reference frame.
            var scaler = NewScaler(1920f, 1080f, 1f);
            try
            {
                float scaleFactor = PressKitDirector.ComputeScaleFactor(scaler, 1728, 1080);
                Assert.That(scaleFactor, Is.EqualTo(1f).Within(1e-5f));
                float visibleRefWidth = 1728 / scaleFactor;
                Assert.That(visibleRefWidth, Is.EqualTo(1728f).Within(1e-3f));
            }
            finally { Object.DestroyImmediate(scaler.gameObject); }
        }

        [Test]
        public void GeometricMean_MatchesTheHudsOwnMatchWidthOrHeight()
        {
            // HudController uses matchWidthOrHeight=0.5, a different curve from the Rig's pure
            // match-by-height — pins that the formula actually reads the scaler's own setting rather
            // than assuming 1. matchWidthOrHeight=0.5 is exactly the geometric mean of the two axes'
            // direct ratios, sqrt(w/refW * h/refH) — a genuinely independent way to derive the same
            // number, not a copy of the log-lerp implementation.
            var scaler = NewScaler(1920f, 1080f, 0.5f);
            try
            {
                float scaleW = 1728f / 1920f;
                float scaleH = 1080f / 1080f;
                float expected = Mathf.Sqrt(scaleW * scaleH);
                Assert.That(PressKitDirector.ComputeScaleFactor(scaler, 1728, 1080), Is.EqualTo(expected).Within(1e-5f));
            }
            finally { Object.DestroyImmediate(scaler.gameObject); }
        }

        [Test]
        public void ArmedFlags_StayFalseInANormalTestSession()
        {
            // The process running this suite was launched with neither -presskit/-uiscreens nor a
            // Temp/*.arm marker, so both capture jobs must stay dormant — PressKitDirector.Install()
            // never fires and the whole system never touches the game.
            Assert.That(PressKitDirector.Armed(), Is.False, "the gameplay press-kit must stay dormant in a normal session");
            Assert.That(PressKitDirector.UiScreensArmed(), Is.False, "the ui-screens job must stay dormant in a normal session");
        }
    }
}
