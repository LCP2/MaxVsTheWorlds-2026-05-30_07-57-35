using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1483: the City is a darker, NEUTRAL streetlight night. Lee, 28 Nov: "a little bit too light... too brown on
    /// the floor", the pylon read cyan (moon key doubled to 1.75 by MV-1472), and the lamp pools showed stepped rings.
    /// Built the real way (<see cref="MapRuntime.Build"/> + <see cref="CityDressing.Dress"/> with
    /// <see cref="BackyardLook.City"/> applied); asserts resolved values: RenderSettings ambient, the key Light's
    /// intensity, and each built lamp pool's mesh, material texture and world bounds.
    /// </summary>
    public sealed class MV1483CityMoodTests
    {
        // MV-1472's City ambient, recorded on bb08aca: sky (0.48, 0.46, 0.44), equator (0.40, 0.39, 0.37), ground (0.32, 0.31, 0.30).
        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        private static readonly float TodaySky = Lum(new Color(0.48f, 0.46f, 0.44f));
        private static readonly float TodayEquator = Lum(new Color(0.40f, 0.39f, 0.37f));
        private static readonly float TodayGround = Lum(new Color(0.32f, 0.31f, 0.30f));

        [Test]
        public void CityIsDarkNeutralNightLitByOneSmoothQuadPoolPerLamp()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(cfg, "the City's shipped config must load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1483 City Mood Probe Root");
            var lightingGo = new GameObject("MV1483 City Look Probe");
            try
            {
                lightingGo.AddComponent<BackyardLighting>().Apply(BackyardLook.City);
                MapBuild build = MapRuntime.Build(map, host.transform);
                host.AddComponent<WorldMaterials>().Apply(BiomePalette.City);
                CityDressing.Dress(host.transform, map, cfg, build.Cover);

                // ---- ambient: <= 70% of today's luminance, neutral (R - B in [0, 0.01]) ----
                AssertAmbient("sky", RenderSettings.ambientSkyColor, TodaySky);
                AssertAmbient("equator", RenderSettings.ambientEquatorColor, TodayEquator);
                AssertAmbient("ground", RenderSettings.ambientGroundColor, TodayGround);

                // ---- key: the resolved directional light ----
                Light key = RenderSettings.sun;
                Assert.IsNotNull(key, "the City look must resolve a key light");
                Assert.That(key.intensity, Is.LessThanOrEqualTo(0.875f), "the City key intensity must be back at or under 0.875");

                // ---- pools: one smooth quad per lamp ----
                int lamps = 0, pools = 0;
                foreach (Transform dressing in host.transform)
                {
                    if (dressing.name != "City Dressing") continue;
                    foreach (MeshRenderer r in dressing.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (r.name == "Street Lamp") continue;
                        if (r.name != "Pool" || r.transform.parent == null || r.transform.parent.name != "Street Lamp") continue;
                        pools++;
                        var mf = r.GetComponent<MeshFilter>();
                        Assert.That(mf.sharedMesh.vertexCount, Is.EqualTo(4), "a lamp pool is ONE quad, not stacked discs");
                        Assert.That(r.sharedMaterial.GetTexture("_BaseMap"), Has.Property("name").EqualTo("CityPoolGradient"),
                            "a lamp pool must wear the shared smooth radial-gradient texture");
                        Assert.That(r.bounds.size.x, Is.InRange(4.5f, 5.5f), "pool diameter (X)");
                        Assert.That(r.bounds.size.z, Is.InRange(4.5f, 5.5f), "pool diameter (Z)");
                    }
                    foreach (Transform t in dressing.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name != "Street Lamp") continue;
                        lamps++;
                        int own = 0;
                        foreach (Transform c in t) if (c.name == "Pool") own++;
                        Assert.That(own, Is.LessThanOrEqualTo(1), "no more than one pool mesh per lamp");
                    }
                }
                Assert.That(lamps, Is.GreaterThan(0), "the City must build street lamps");
                Assert.That(pools, Is.LessThanOrEqualTo(lamps), "pool renderer count must not exceed the lamp count");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lightingGo);
                RenderSettings.fog = false;
            }
        }

        private static void AssertAmbient(string what, Color c, float todayLum)
        {
            Assert.That(Lum(c), Is.LessThanOrEqualTo(todayLum * 0.70f), $"ambient {what} {c} luminance must be <= 70% of {todayLum:F3}");
            Assert.That(c.r - c.b, Is.LessThanOrEqualTo(0.01f), $"ambient {what} {c} R - B must be <= 0.01");
        }
    }
}
