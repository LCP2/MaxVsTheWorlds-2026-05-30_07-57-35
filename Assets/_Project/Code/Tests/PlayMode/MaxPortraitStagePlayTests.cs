using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The Upgrade screen's Max portrait (YT-176), rendered live instead of a 2D painted headshot. The
    /// look is Lee's call off a render; the structure has to hold — it renders to a texture the screen
    /// can show, and, being a pile of runtime primitives, it never ships magenta and never carries a
    /// collider that would foul the world it's hidden inside.
    /// </summary>
    public sealed class MaxPortraitStagePlayTests
    {
        private MaxPortraitStage _stage;

        [TearDown]
        public void TearDown()
        {
            if (_stage != null) Object.Destroy(_stage.gameObject);
        }

        [UnityTest]
        public IEnumerator RendersToATexture_WithNoColliderAndNoMagenta()
        {
            _stage = MaxPortraitStage.Create(null);
            Assert.IsNotNull(_stage.Texture, "the stage has no render texture for the screen to show.");
            Assert.IsTrue(_stage.Texture.IsCreated(), "the render texture was never created.");
            yield return null;

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

        [UnityTest]
        public IEnumerator ShowEnablesTheCamera_AndHideDisablesIt()
        {
            _stage = MaxPortraitStage.Create(null);
            var cam = _stage.GetComponentInChildren<Camera>(true);
            Assert.IsNotNull(cam, "the stage has no camera rendering the bust.");
            Assert.IsFalse(cam.enabled, "the camera should start disabled — it only runs while the screen is up.");

            _stage.Show();
            Assert.IsTrue(cam.enabled, "Show() should start the live render.");

            _stage.Hide();
            Assert.IsFalse(cam.enabled, "Hide() should stop the live render.");
            yield return null;
        }
    }
}
