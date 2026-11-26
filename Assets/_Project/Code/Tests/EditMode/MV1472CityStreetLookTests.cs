using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1472: the City is a warm streetlight night - dark neutral tarmac with real road detail and amber
    /// lamp pools - not a blue/purple floor. Guards the live defect Lee reported on 26 Nov: only a1's
    /// authored road paint was ever built (a2..a30 had none), the tarmac tint was blue-grey (B &gt; R), and
    /// the look's ambient / fog / split-tone were violet.
    ///
    /// Built the real way: <see cref="WorldLibrary.Load"/> -> <see cref="MapRuntime.Build"/> -> the City's
    /// own material sweep -> <see cref="CityDressing.Dress"/>, with <see cref="BackyardLook.City"/> applied.
    /// Asserts resolved values on built objects (Tier 2): the floor's baked base-map mean colour, the
    /// RenderSettings the look resolves to, and what each area actually contains.
    /// </summary>
    public sealed class MV1472CityStreetLookTests
    {
        [Test]
        public void EveryCityAreaIsADarkWarmTarmacStreetWithLampsAndLanePaint()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(cfg, "the City's shipped config must load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1472 City Street Probe Root");
            var lightingGo = new GameObject("MV1472 City Look Probe");
            try
            {
                var lighting = lightingGo.AddComponent<BackyardLighting>();
                lighting.Apply(BackyardLook.City);

                MapBuild build = MapRuntime.Build(map, host.transform);
                host.AddComponent<WorldMaterials>().Apply(BiomePalette.City);
                CityDressing.Dress(host.transform, map, cfg, build.Cover);

                // ---- the look: no violet in ambient, fog or the grade (R >= B each) ----
                AssertWarm("ambient sky", RenderSettings.ambientSkyColor);
                AssertWarm("ambient equator", RenderSettings.ambientEquatorColor);
                AssertWarm("ambient ground", RenderSettings.ambientGroundColor);
                AssertWarm("fog", RenderSettings.fogColor);
                AssertWarm("colour filter", BackyardLook.City.ColorFilter);
                AssertWarm("shadow split-tone", BackyardLook.City.ShadowTint);

                // ---- the floor: tarmac material whose resolved base map is dark and not blue ----
                Renderer floor = null;
                foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Map Floor") { floor = t.GetComponent<Renderer>(); break; }
                Assert.IsNotNull(floor, "the City must build its map floor");
                Assert.That(floor.sharedMaterial.name, Does.Contain("Asphalt"),
                    $"the floor must wear the tarmac material, not '{floor.sharedMaterial.name}'");
                Color mean = MeanColour(floor.sharedMaterial.GetTexture("_BaseMap") as Texture2D);
                Assert.That(mean.r, Is.GreaterThanOrEqualTo(mean.b - 0.002f),
                    $"tarmac mean colour {mean} must not lean blue/violet (R >= B)");
                Assert.That((mean.r + mean.g + mean.b) / 3f, Is.LessThan(0.2f),
                    $"tarmac mean colour {mean} must be dark before lighting");

                // ---- every area: >= 2 lamp pools, >= 1 lane-marking mesh ----
                var pools = new Dictionary<string, int>();
                var paint = new Dictionary<string, int>();
                int cubeMeshes = 0;
                foreach (Transform dressing in host.transform)
                {
                    if (dressing.name != "City Dressing") continue;
                    foreach (Transform street in dressing)
                    {
                        if (!street.name.StartsWith("Street ")) continue;
                        string id = street.name.Substring("Street ".Length);
                        foreach (MeshRenderer r in street.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            if (r.name == "Pool" && r.transform.parent != null && r.transform.parent.name == "Street Lamp")
                                pools[id] = pools.TryGetValue(id, out int p) ? p + 1 : 1;
                            if (r.name.StartsWith("Lane Paint"))
                                paint[id] = paint.TryGetValue(id, out int q) ? q + 1 : 1;
                            var mf = r.GetComponent<MeshFilter>();
                            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Cube") cubeMeshes++;
                        }
                    }
                }

                int areas = 0;
                foreach (WorldArea a in cfg.areas)
                {
                    if (a == null || a.id == "stub") continue;
                    areas++;
                    Assert.That(pools.TryGetValue(a.id, out int pc) ? pc : 0, Is.GreaterThanOrEqualTo(2),
                        $"area {a.id} must have at least 2 lamp pools");
                    Assert.That(paint.TryGetValue(a.id, out int lc) ? lc : 0, Is.GreaterThanOrEqualTo(1),
                        $"area {a.id} must have at least one lane-marking mesh");
                }
                Assert.That(areas, Is.EqualTo(30), "the City has 30 areas");
                Assert.That(cubeMeshes, Is.EqualTo(0), "no street mesh may use the built-in Cube mesh");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(lightingGo);
                RenderSettings.fog = false;
            }
        }

        private static void AssertWarm(string what, Color c) =>
            Assert.That(c.r, Is.GreaterThanOrEqualTo(c.b - 1e-4f), $"{what} {c} must have R >= B (no violet)");

        private static Color MeanColour(Texture2D tex)
        {
            Assert.IsNotNull(tex, "the floor must wear a real base map");
            Color32[] px = tex.GetPixels32();
            double r = 0, g = 0, b = 0;
            foreach (Color32 p in px) { r += p.r; g += p.g; b += p.b; }
            float n = px.Length * 255f;
            return new Color((float)(r / n), (float)(g / n), (float)(b / n));
        }
    }
}
