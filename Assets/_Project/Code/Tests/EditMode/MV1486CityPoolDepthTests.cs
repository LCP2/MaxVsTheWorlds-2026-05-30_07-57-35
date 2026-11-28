using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1486: City lamp pools were too bright (cream cores), and the tarmac still read brown. Built the real way
    /// (<see cref="MapRuntime.Build"/> + <see cref="CityDressing.Dress"/> with <see cref="BackyardLook.City"/> applied).
    /// Asserts resolved values: each lamp-pool material's ZTest / render queue / peak colour, each pool's world Y, and every
    /// colour in the City's resolved post volume and fog.
    /// </summary>
    public sealed class MV1486CityPoolDepthTests
    {
        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // The pool material colour on main (151467c): PoolAmber (255,160,58) desaturated to S 0.59, V 1.0 = (1.000, 0.715, 0.410).
        private static readonly float TodayPoolPeakLum = Lum(new Color(1.000f, 0.715f, 0.410f));

        [Test]
        public void CityLampPoolsAreDimDeepAmberDepthTestedOnTheGroundAndTheGradeIsNeutral()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(cfg, "the City's shipped config must load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1486 City Pool Probe Root");
            var lightingGo = new GameObject("MV1486 City Look Probe");
            try
            {
                lightingGo.AddComponent<BackyardLighting>().Apply(BackyardLook.City);
                MapBuild build = MapRuntime.Build(map, host.transform);
                host.AddComponent<WorldMaterials>().Apply(BiomePalette.City);
                CityDressing.Dress(host.transform, map, cfg, build.Cover);

                int pools = 0;
                foreach (MeshRenderer r in host.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name != "Pool" || r.transform.parent == null || r.transform.parent.name != "Street Lamp") continue;
                    pools++;
                    Material m = r.sharedMaterial;
                    if (m.HasProperty("_ZTest"))
                        Assert.That(m.GetFloat("_ZTest"), Is.EqualTo((float)CompareFunction.LessEqual), "pool ZTest must be LessEqual, not Always");
                    Assert.That(m.renderQueue, Is.LessThanOrEqualTo((int)RenderQueue.Transparent), "pool render queue must be no later than Transparent");
                    Assert.That(r.bounds.center.y, Is.LessThan(0.05f), "pool quad world Y");
                    Color peak = m.GetColor("_BaseColor");
                    Assert.That(Lum(peak), Is.LessThanOrEqualTo(TodayPoolPeakLum * 0.65f),
                        $"pool peak colour {peak} luminance {Lum(peak):F3} must be <= 65% of today's {TodayPoolPeakLum:F3}");
                }
                Assert.That(pools, Is.GreaterThan(0), "the City must build lamp pools");

                // ---- every grade / fog / vignette colour is neutral: |R - B| <= 0.02 ----
                AssertNeutral("fog", RenderSettings.fogColor);
                var volume = lightingGo.GetComponent<Volume>();
                Assert.IsNotNull(volume, "the City look must resolve a post volume");
                VolumeProfile p = volume.sharedProfile;
                Assert.IsTrue(p.TryGet(out ColorAdjustments ca));
                AssertNeutral("colorFilter", ca.colorFilter.value);
                Assert.IsTrue(p.TryGet(out Vignette vig));
                AssertNeutral("vignette", vig.color.value);
                Assert.IsTrue(p.TryGet(out Bloom bloom));
                AssertNeutral("bloom tint", bloom.tint.value);
                Assert.IsTrue(p.TryGet(out ShadowsMidtonesHighlights smh));
                AssertNeutral("shadows", smh.shadows.value);
                AssertNeutral("midtones", smh.midtones.value);
                AssertNeutral("highlights", smh.highlights.value);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lightingGo);
                RenderSettings.fog = false;
            }
        }

        private static void AssertNeutral(string what, Color c) =>
            Assert.That(Mathf.Abs(c.r - c.b), Is.LessThanOrEqualTo(0.02f), $"{what} {c}: |R - B| must be <= 0.02");

        private static void AssertNeutral(string what, Vector4 v) =>
            Assert.That(Mathf.Abs(v.x - v.z), Is.LessThanOrEqualTo(0.02f), $"{what} {v}: |R - B| must be <= 0.02");
    }
}
