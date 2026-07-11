using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-49 — the Backyard lighting + post stack. Awake doesn't run in edit mode, so the
    /// tests drive <see cref="BackyardLighting.Apply"/> directly (which is why it's public).
    /// </summary>
    public sealed class BackyardLightingTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            RenderSettings.fog = false;
        }

        private BackyardLighting Build()
        {
            _go = new GameObject("lighting-test");
            var lighting = _go.AddComponent<BackyardLighting>();
            lighting.Apply(BackyardLook.Default);
            return lighting;
        }

        [Test]
        public void Apply_BuildsTheFullPostStack()
        {
            var profile = Build().Profile;

            Assert.IsNotNull(profile, "no volume profile was built");
            Assert.IsTrue(profile.Has<Tonemapping>(), "no tonemapping — the image stays raw and flat");
            Assert.IsTrue(profile.Has<ColorAdjustments>());
            Assert.IsTrue(profile.Has<ShadowsMidtonesHighlights>());
            Assert.IsTrue(profile.Has<Bloom>());
            Assert.IsTrue(profile.Has<Vignette>());
            Assert.IsTrue(profile.Has<FilmGrain>());
        }

        [Test]
        public void Apply_AttachesAGlobalVolumeBoundToThatProfile()
        {
            var lighting = Build();

            var volume = _go.GetComponent<Volume>();
            Assert.IsNotNull(volume, "no Volume — the profile would be built and then ignored");
            Assert.IsTrue(volume.isGlobal, "the volume must be global or it only grades inside a collider");
            Assert.AreSame(lighting.Profile, volume.sharedProfile);
        }

        [Test]
        public void Apply_OverridesAreActive_NotJustPresent()
        {
            var profile = Build().Profile;

            // A VolumeComponent that exists but has no overridden parameters does nothing at
            // all — the default state of Add<T>() — so "present" is not the same as "applied".
            Assert.IsTrue(profile.TryGet<Bloom>(out var bloom));
            Assert.IsTrue(bloom.intensity.overrideState, "bloom intensity is not overridden — it would stay at 0");
            Assert.That(bloom.intensity.value, Is.GreaterThan(0f));

            Assert.IsTrue(profile.TryGet<ColorAdjustments>(out var grade));
            Assert.IsTrue(grade.saturation.overrideState);
            Assert.That(grade.saturation.value, Is.GreaterThan(0f), "the look asks for saturated colour");
        }

        [Test]
        public void Apply_BuildsAKeyLightThatCastsShadows_PlusFillAndRim()
        {
            Build();

            Light key = null;
            int directionals = 0;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                directionals++;
                if (l.shadows != LightShadows.None) key = l;
            }

            Assert.That(directionals, Is.GreaterThanOrEqualTo(3), "expected a key, a fill and a rim");
            Assert.IsNotNull(key, "nothing casts shadows — depth at a fixed top-down angle comes from shadows");
            Assert.That(key.shadowStrength, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
        }

        [Test]
        public void Apply_OnlyOneLightCastsShadows()
        {
            Build();

            int casters = 0;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional && l.shadows != LightShadows.None) casters++;
            }

            Assert.AreEqual(1, casters,
                "a second shadow-casting directional doubles the shadow cost and muddies the first");
        }

        [Test]
        public void Apply_SetsAnAmbientGradient_NotFlatGrey()
        {
            Build();

            Assert.AreEqual(AmbientMode.Trilight, RenderSettings.ambientMode,
                "flat ambient is exactly what makes the greybox read as dead grey");
            Assert.AreNotEqual(RenderSettings.ambientSkyColor, RenderSettings.ambientGroundColor,
                "sky and ground bounce must differ, or the gradient is pointless");
        }

        [Test]
        public void Apply_IsIdempotent_AndDoesNotStackLightsOnRepeat()
        {
            var lighting = Build();
            lighting.Apply(BackyardLook.Default);
            lighting.Apply(BackyardLook.Default);

            int owned = 0;
            foreach (Transform child in _go.transform) owned++;
            Assert.AreEqual(2, owned, "re-applying should reuse the fill and rim, not spawn new ones each time");
        }
    }
}
