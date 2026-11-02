using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1340: Lee, iPhone TestFlight v0.24.0, World 4, 2 Nov — "W4 is just way too dark," measured
    /// median luminance 52/255 and hue 23deg (muddy brown) against a navy target, because
    /// <see cref="BackyardLook.Reef"/>'s hazard-orange rim light (authored at intensity 2.65) overpowers
    /// the cool key/fill/ambient, and MV-1339's wall-base/drum neon
    /// (<see cref="WorldMaterials.M_Circuit_Cyan"/>, <see cref="WorldMaterials.M_Hazard"/> et al) is built
    /// via <c>WorldMaterials.ReefMaterial</c>'s runtime <c>EnableKeyword("_EMISSION")</c> path — the exact
    /// pattern MV-1316 found invisible on WebGL/iPhone (Metal/GLES), because Unity's build-time
    /// shader-variant stripper has no SERIALIZED material to discover the _EMISSION variant from.
    ///
    /// Two RESOLVED-value checks (testing policy Tier 2), neither a restated authored constant: (a) the
    /// Rim light's own resolved <see cref="Light.intensity"/> after
    /// <see cref="BackyardLighting.Apply(BackyardLook)"/> — not <c>BackyardLook.Reef.RimIntensity</c> read
    /// back at itself; (b) <see cref="WorldMaterials.M_Circuit_Cyan"/>/<see cref="WorldMaterials.M_Hazard"/>'s
    /// resolved keyword/float state against the MV-1316 <c>M_ReefDeckKeepEmission.mat</c> template — loaded
    /// FRESH via <c>Resources.Load</c> in this test, not a hardcoded literal — which only a material cloned
    /// from that template carries; a bare <c>new Material(shader)</c> plus a runtime
    /// <c>EnableKeyword("_EMISSION")</c> call (the pre-fix path) never sets either.
    ///
    /// Fails on main (pre-fix): the resolved Rim light intensity is 2.65 (not &lt;= 0.8), and
    /// M_Circuit_Cyan/M_Hazard's _SPECULARHIGHLIGHTS_OFF keyword and _Smoothness never match the
    /// template's (quoted failure output in the fix comment).
    /// </summary>
    public sealed class MV1340ReefBrightnessTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            RenderSettings.fog = false;
        }

        [Test]
        public void ReefRimLight_IsSubtle_AndNeonMaterials_UseTheEmissionKeepingAuthoredPath()
        {
            // --- Rim light: resolved intensity after Apply, not the authored field read back at itself. ---
            _go = new GameObject("mv1340-reef-rim-test");
            var lighting = _go.AddComponent<BackyardLighting>();
            lighting.Apply(BackyardLook.Reef);

            Light rim = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.gameObject.name == "Rim") rim = l;

            Assert.IsNotNull(rim, "BackyardLighting.Apply must build a light named \"Rim\"");
            Assert.That(rim.intensity, Is.LessThanOrEqualTo(0.8f),
                $"the Reef's resolved Rim light intensity ({rim.intensity:F2}) must read as a subtle " +
                "accent (<= 0.8), not the mood-lighting warm cast Lee's screenshot measured hue 23deg from.");

            // --- Neon materials: resolved state must match the MV-1316 emission-keeping template. ---
            Material template = Resources.Load<Material>("Materials/M_ReefDeckKeepEmission");
            Assert.IsNotNull(template, "precondition: the MV-1316 authored emission-keeping template must exist");

            void AssertClonedFromTemplate(Material m, string label)
            {
                Assert.IsNotNull(m, $"{label} must resolve");
                Assert.AreEqual(template.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"), m.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"),
                    $"{label} must inherit the authored template's _SPECULARHIGHLIGHTS_OFF keyword state " +
                    "(proving it was cloned from the MV-1316 emission-keeping asset), not a bare " +
                    "`new Material(shader)` with only _EMISSION turned on at runtime.");
                Assert.AreEqual(template.GetFloat("_Smoothness"), m.GetFloat("_Smoothness"), 0.0001f,
                    $"{label}'s resolved _Smoothness must match the authored template's, not the shader's own default.");
            }

            AssertClonedFromTemplate(WorldMaterials.M_Circuit_Cyan, "M_Circuit_Cyan");
            AssertClonedFromTemplate(WorldMaterials.M_Hazard, "M_Hazard");
        }
    }
}
