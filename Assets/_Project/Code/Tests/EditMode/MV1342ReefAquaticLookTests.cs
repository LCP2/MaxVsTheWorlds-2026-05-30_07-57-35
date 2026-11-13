using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1342: Lee, iPhone TestFlight v0.25.0, 3 Nov — "I don't think W4 is especially good. Still
    /// dark and it looks too similar to W5 because of that." Both worlds were dark navy with neon, so
    /// they read as the same place. This re-grades the Reef from dark navy to a bright, teal-green
    /// "underwater" look (<see cref="BackyardLook.Reef"/>, the deck albedo panel tones) and adds a
    /// drifting floor-wide caustics overlay (removed again by MV-1350 for a static dapple baked into the deck atlas); World 5 is untouched.
    ///
    /// Three RESOLVED-value checks (testing policy Tier 2), none a restated authored constant:
    /// (a) the Reef's own resolved Key light colour after <see cref="BackyardLighting.Apply"/>, and the
    /// baked deck albedo's own actual pixel colour multiplied back through its base — not
    /// <see cref="BackyardLook.Reef"/>'s fields or <see cref="WorldMaterials.ReefHullPanelToneLight"/>
    /// read back at themselves; (b) withdrawn by MV-1350 (the animated caustics overlay no longer exists); (c) a built
    /// <see cref="ReefKit.BuildDrum"/>'s rim material's resolved keyword/float state against the
    /// MV-1316 <c>M_ReefDeckKeepEmission.mat</c> template, same technique
    /// <c>MV1340ReefBrightnessTests</c> already uses.
    ///
    /// Fails on main (pre-fix): the resolved Key light hue sits at ~206deg (navy) and the baked deck
    /// panel's own resolved hue sits at ~210deg — both outside this test's 155-200deg teal band — .
    /// </summary>
    public sealed class MV1342ReefAquaticLookTests
    {
        private GameObject _lightingGo;
        private GameObject _drumProbeGo;

        [TearDown]
        public void TearDown()
        {
            if (_lightingGo != null) Object.DestroyImmediate(_lightingGo);
            if (_drumProbeGo != null) Object.DestroyImmediate(_drumProbeGo);
            RenderSettings.fog = false;
        }

        [Test]
        public void ReefReadsAquaticTeal_AndDrumRimsKeepEmission()
        {
            // --- (a1) resolved Key light colour must read teal-green, not the old navy band. ---
            _lightingGo = new GameObject("mv1342-reef-look-test");
            var lighting = _lightingGo.AddComponent<BackyardLighting>();
            lighting.Apply(BackyardLook.Reef);

            // RenderSettings.sun, not a name match -- BackyardLighting.Apply reuses whatever
            // pre-existing directional light it finds as the key (FindExistingSun), which keeps ITS OWN
            // name rather than being renamed "Sun (Key)", so a name-based lookup is not reliable here.
            Light key = RenderSettings.sun;
            Assert.IsNotNull(key, "BackyardLighting.Apply must build/reuse a Key (sun) light and set RenderSettings.sun");

            Color.RGBToHSV(key.color, out float keyHue01, out _, out _);
            float keyHueDeg = keyHue01 * 360f;
            Assert.That(keyHueDeg, Is.InRange(155f, 200f),
                $"the Reef's resolved Key light hue ({keyHueDeg:F0}deg) must read teal-green (155-200deg), " +
                "not the pre-MV-1342 navy band (~206deg) this struct used to sit in.");

            // --- (a2) resolved deck albedo colour (baked ratio texture multiplied back through its own
            // base, the same maths the shader performs via _BaseMap * _BaseColor) must also read teal.
            // Sampled at a light panel's own plate-1 cell centre (gx=1, gy=1 of the 6-cell supertile —
            // same "cell centre, never a seam" technique MV1339ReefHullPanelFloorTests already uses),
            // so neither the faint plate seam nor the heavy panel seam contaminates the sample. ---
            Texture2D albedo = StylizedTextures.ReefDeckAlbedo();
            int cellSize = albedo.width / WorldMaterials.ReefDeckSuperTileCells;
            Color32 bakedPixel = albedo.GetPixel(1 * cellSize + cellSize / 2, 1 * cellSize + cellSize / 2);
            Color floorBase = WorldMaterials.ReefShipFloor;
            var resolvedPanel = new Color(
                bakedPixel.r / 255f * floorBase.r,
                bakedPixel.g / 255f * floorBase.g,
                bakedPixel.b / 255f * floorBase.b);
            Color.RGBToHSV(resolvedPanel, out float deckHue01, out _, out _);
            float deckHueDeg = deckHue01 * 360f;
            Assert.That(deckHueDeg, Is.InRange(155f, 200f),
                $"the Reef deck's own resolved panel colour ({resolvedPanel}) hue ({deckHueDeg:F0}deg) " +
                "must read teal-green (155-200deg).");

            // --- (c) the drum's rim material still resolves through the MV-1316 emission-keeping
            // template -- unchanged by this ticket, re-asserted here because AC6 names it explicitly. ---
            Material template = Resources.Load<Material>("Materials/M_ReefDeckKeepEmission");
            Assert.IsNotNull(template, "precondition: the MV-1316 authored emission-keeping template must exist");

            _drumProbeGo = new GameObject("mv1342-drum-probe");
            GameObject drum = ReefKit.BuildDrum(_drumProbeGo.transform, Vector3.zero, new Vector3(3f, 2f, 3f));
            Transform rim = FindChildNamed(drum.transform, "Rim");
            Assert.IsNotNull(rim, "the drum must build a top-edge rim so it reads as lit, not a dark disc");

            Material rimMat = rim.GetComponent<Renderer>().sharedMaterial;
            Assert.AreEqual(template.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"), rimMat.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"),
                "the drum rim material must inherit the authored template's _SPECULARHIGHLIGHTS_OFF keyword state " +
                "(proving it was cloned from the MV-1316 emission-keeping asset), not a bare " +
                "`new Material(shader)` with only _EMISSION turned on at runtime.");
            Assert.AreEqual(template.GetFloat("_Smoothness"), rimMat.GetFloat("_Smoothness"), 0.0001f,
                "the drum rim material's resolved _Smoothness must match the authored template's, not the shader's own default.");
        }

        private static Transform FindChildNamed(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
