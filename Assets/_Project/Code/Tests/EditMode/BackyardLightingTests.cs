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

        // ------------------------------------------------------------------ MV-350: bloom vs the ceiling

        /// <summary>
        /// MV-350's root cause. BloomThreshold and SunlitAlbedo.Ceiling are two independently
        /// authored constants, and nothing previously checked that they stayed compatible — unlike
        /// Ceiling and KeyIntensity, which SunlitAlbedo's own doc says the tests hold together. The
        /// old threshold (0.92) sat BELOW the exact worst case a ceiling-compliant surface reaches
        /// under the key alone (0.6 x 1.8 = 1.08), so bloom self-triggered on ordinary, correctly
        /// -exposed bodies and surfaces — not highlights, not VFX — and its warm tint desaturated
        /// them toward tan/cream on the unshaded side of the yard, independent of the colour
        /// actually painted. This is the same guarantee SunlitAlbedo.Ceiling's own doc promises for
        /// KeyIntensity, extended to the threshold that decides whether an ordinary lit surface
        /// stays a colour or gets bloomed toward the tint.
        /// </summary>
        [Test]
        public void BloomThreshold_ClearsTheCeilingCompliantPeakUnderTheKeyAlone()
        {
            var look = BackyardLook.Default;
            float worstCasePeak = SunlitAlbedo.Ceiling * look.KeyIntensity;

            Assert.Greater(look.BloomThreshold, worstCasePeak,
                $"BloomThreshold ({look.BloomThreshold:0.00}) does not clear the brightest a " +
                $"ceiling-compliant surface reaches under the key alone ({worstCasePeak:0.00}) — " +
                "any archetype or surface authored right up to the ceiling will self-bloom and " +
                "wash toward the warm bloom tint (MV-350).");
        }

        /// <summary>
        /// Pins the regression rather than just asserting the new state: proves the fix closes a
        /// real, previously-failing case — the Bruiser, at the ceiling-compliant colour MV-328
        /// shipped — instead of being a no-op that would pass whether or not the bug existed.
        /// </summary>
        [Test]
        public void TheOldThresholdWouldHaveSelfBloomedTheBruiser_TheShippedOneDoesNot()
        {
            Color bruiser = MaxWorlds.VFX.CharacterSkin.BaseColorFor(MaxWorlds.VFX.CharacterRole.Bruiser);
            float key = BackyardLook.Default.KeyIntensity;

            Assert.IsTrue(SunlitAlbedo.ClipsBloomUnderKey(bruiser, key, 0.92f),
                "the old threshold no longer reproduces MV-350's bug against the shipped Bruiser " +
                "colour — this test's premise has drifted and needs re-checking, not just leaving green.");
            Assert.IsFalse(SunlitAlbedo.ClipsBloomUnderKey(bruiser, key, BackyardLook.Default.BloomThreshold),
                "the shipped threshold still lets the Bruiser self-bloom under the key alone.");
        }
    }
}
