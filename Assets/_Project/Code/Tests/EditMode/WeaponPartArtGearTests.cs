using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-430 — the gear's teeth used to float 0.070 world units clear of the disc rim (a diameter, not
    /// a radius, treated as the disc's world radius — the same trap <see cref="WeaponPartArt.BuildPowerCell"/>'s
    /// CasingRadius doc already called out) and the machine-internals pool was ten designs that all
    /// collapsed to noise at the fixed 72° camera. This pins the fix: one design in the pool, its teeth
    /// seated against the disc with the ring radius derived from the disc's own scale, and a dark
    /// (not chrome) plinth on every part builder.
    ///
    /// MV-498 — the pool is back down to one design (Lee asked for exactly one symbol, 2026-08-20), and
    /// that one design's disc/teeth (the largest mass) now carry an actually-colourful accent instead of
    /// neutral Steel.
    /// </summary>
    public sealed class WeaponPartArtGearTests
    {
        /// <summary>MV-430 pinned the pool at one design; MV-454 widened it to ten; MV-498 collapses it
        /// back to one, permanently this time — Lee decided, not a shape-vs-colour argument to re-run.
        /// </summary>
        [Test]
        public void MachineInternalsKeys_HoldsExactlyGear()
        {
            Assert.AreEqual(1, WeaponPartArt.MachineInternalsKeys.Length,
                "the machine-internals pool should hold exactly one design (MV-498).");
            Assert.AreEqual(WeaponPartArt.Keys.Gear, WeaponPartArt.MachineInternalsKeys[0]);
        }

        /// <summary>
        /// AC2/AC3/AC4 (MV-498). The tone lives in a baked albedo texture, not <c>_BaseColor</c> — see
        /// <c>MaterialLibrary.Build</c> — so this reads the actual <c>_BaseMap</c> pixels off the built
        /// object's Disc/Tooth renderers, exactly like <c>SurfaceMaterialTests.ARe_SurfacedKitProp_KeepsTheColourTheKitWasGiven</c>
        /// does for the kit-recolour path, rather than asserting against the <see cref="WeaponPartArt.PartGold"/>
        /// constant directly. Fails on 64ed46b: the disc/teeth wore Steel (0.55, 0.58, 0.63), whose baked
        /// average has HSV saturation ~0.09 (nowhere near the 0.55 floor) and sits within 0.05/channel of
        /// the forbidden Steel entry itself.
        /// </summary>
        [Test]
        public void Gear_DiscAndTeeth_AreSaturatedAndWarm_PlinthStaysRecessive()
        {
            var root = WeaponPartArt.BuildGear();
            try
            {
                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                var bodyRenderers = renderers.Where(r => r.name == "Disc" || r.name.StartsWith("Tooth")).ToArray();
                Assert.GreaterOrEqual(bodyRenderers.Length, 2, "expected the Disc and at least one Tooth renderer.");

                var bodyMaterials = bodyRenderers.Select(r => r.sharedMaterial).Distinct().ToArray();
                Assert.AreEqual(1, bodyMaterials.Length,
                    "the disc and teeth should share one material — that's what 'the largest mass carries the hue' means.");

                Color avg = AverageAlbedo(bodyMaterials[0]);
                float luminance = 0.2126f * avg.r + 0.7152f * avg.g + 0.0722f * avg.b;
                Color.RGBToHSV(avg, out _, out float saturation, out _);

                Assert.That(luminance, Is.InRange(0.45f, 0.60f),
                    $"disc/teeth luminance {luminance:0.000} is outside the ticket's colourful-not-just-lit range.");
                Assert.GreaterOrEqual(saturation, 0.55f,
                    $"disc/teeth saturation {saturation:0.000} reads as grey, not colourful.");

                var forbidden = new (string name, Color c)[]
                {
                    ("Steel", WeaponPartArt.Steel), ("Chrome", WeaponPartArt.Chrome), ("DarkSteel", WeaponPartArt.DarkSteel),
                    ("PowerBlue", WeaponPartArt.PowerBlue), ("HarnessGreen", WeaponPartArt.HarnessGreen),
                    ("BeamCyan", WeaponPartArt.BeamCyan), ("EngineOrange", WeaponPartArt.EngineOrange),
                    ("ModuleRed", WeaponPartArt.ModuleRed), ("ModuleGlow", WeaponPartArt.ModuleGlow),
                    ("CellCyan", WeaponPartArt.CellCyan),
                };
                foreach (var (name, c) in forbidden)
                {
                    float maxDiff = Mathf.Max(Mathf.Abs(avg.r - c.r), Mathf.Max(Mathf.Abs(avg.g - c.g), Mathf.Abs(avg.b - c.b)));
                    Assert.Greater(maxDiff, 0.05f,
                        $"disc/teeth colour is within 0.05/channel of {name} — a cosmetic drop would misread as it.");
                }

                var plinth = renderers.Single(r => r.name == "Plinth");
                Assert.AreSame(MaterialLibrary.Tinted(SurfaceKind.Metal, WeaponPartArt.DarkSteel), plinth.sharedMaterial,
                    "the plinth must stay DarkSteel and recessive (MV-430) — the colour comes from the part, not the base.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Color AverageAlbedo(Material m)
        {
            var tex = (Texture2D)m.GetTexture("_BaseMap");
            var px = tex.GetPixels32();
            double r = 0, g = 0, b = 0;
            foreach (var p in px) { r += p.r; g += p.g; b += p.b; }
            int n = px.Length;
            return new Color((float)(r / n) / 255f, (float)(g / n) / 255f, (float)(b / n) / 255f);
        }
    }
}
