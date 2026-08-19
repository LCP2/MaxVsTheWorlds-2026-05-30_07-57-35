using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The Upgrade screen's Max portrait (YT-176), rendered live instead of a 2D painted headshot. The
    /// look is Lee's call off a render; the structure has to hold — it renders to a texture the screen
    /// can show, and, being a pile of runtime primitives, it never ships magenta and never carries a
    /// collider that would foul the world it's hidden inside.
    ///
    /// MV-464: moved from PlayMode. <see cref="MaxPortraitStage.Create"/> calls its own
    /// <c>Build()</c> synchronously rather than relying on Awake (which never runs in edit mode), so
    /// nothing here ever needed a frame to pass.
    /// </summary>
    public sealed class MaxPortraitStageTests
    {
        private MaxPortraitStage _stage;

        [TearDown]
        public void TearDown()
        {
            if (_stage != null) Object.DestroyImmediate(_stage.gameObject);
        }

        [Test]
        public void RendersToATexture_WithNoColliderAndNoMagenta()
        {
            _stage = MaxPortraitStage.Create(null);
            Assert.IsNotNull(_stage.Texture, "the stage has no render texture for the screen to show.");
            Assert.IsTrue(_stage.Texture.IsCreated(), "the render texture was never created.");

            var renderers = _stage.GetComponentsInChildren<MeshRenderer>(true);
            Assert.Greater(renderers.Length, 5, "the bust is nearly empty.");
            foreach (var r in renderers)
            {
                Assert.IsNotNull(r.sharedMaterial, $"'{r.name}' draws nothing.");
                string shader = r.sharedMaterial.shader.name;
                Assert.That(shader,
                    Does.StartWith("Universal Render Pipeline").Or.StartWith("MaxWorlds").Or.StartWith("Sprites"),
                    $"'{r.name}' wears '{shader}' — magenta in the build.");
            }

            Assert.IsEmpty(_stage.GetComponentsInChildren<Collider>(true),
                "the portrait stage carries a collider — it would foul the world it is hidden inside.");
        }

        [Test]
        public void ShowEnablesTheCamera_AndHideDisablesIt()
        {
            _stage = MaxPortraitStage.Create(null);
            var cam = _stage.GetComponentInChildren<Camera>(true);
            Assert.IsNotNull(cam, "the stage has no camera rendering the bust.");
            Assert.IsFalse(cam.enabled, "the camera should start disabled — it only runs while the screen is up.");

            // Show() only actually enables the camera when there's a real graphics device to render
            // with (YT-189) — under a headless -nographics run it stays off, since an enabled camera
            // pointed at a RenderTexture would try to render and log an engine-level error that fails
            // whatever test happens to be running at the time, not just this one.
            bool hasRealDevice = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            _stage.Show();
            Assert.AreEqual(hasRealDevice, cam.enabled, "Show() should start the live render iff a real graphics device exists.");

            _stage.Hide();
            Assert.IsFalse(cam.enabled, "Hide() should stop the live render.");
        }
    }
}
