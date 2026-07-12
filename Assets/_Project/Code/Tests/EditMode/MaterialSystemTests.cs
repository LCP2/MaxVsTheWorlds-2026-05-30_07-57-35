using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-50 — stylised materials, the biome tint, and the elemental recolour system.
    /// </summary>
    public sealed class MaterialSystemTests
    {
        [TearDown]
        public void TearDown()
        {
            MaterialLibrary.Palette = BiomePalette.Backyard;
        }

        // --- elemental recolour ---

        [Test]
        public void Recolor_PreservesLuminance()
        {
            var dark = new Color(0.15f, 0.15f, 0.15f);
            var light = new Color(0.85f, 0.85f, 0.85f);

            var darkIce = ElementPalette.Recolor(dark, Element.Ice);
            var lightIce = ElementPalette.Recolor(light, Element.Ice);

            // Ice is a very pale colour. A naive lerp would drag the dark panel toward white and
            // flatten the model; preserving luminance is what keeps shading intact.
            Assert.That(ElementPalette.Luminance(darkIce),
                Is.EqualTo(ElementPalette.Luminance(dark)).Within(0.06f),
                "a dark surface must stay dark after recolouring to a pale element");
            Assert.That(ElementPalette.Luminance(lightIce),
                Is.GreaterThan(ElementPalette.Luminance(darkIce)),
                "light and shade must survive the recolour, or the variant reads as a flat blob");
        }

        [Test]
        public void Recolor_ShiftsHueTowardTheElement()
        {
            var grey = new Color(0.5f, 0.5f, 0.5f);

            var water = ElementPalette.Recolor(grey, Element.Water);
            var fire = ElementPalette.Recolor(grey, Element.Fire);

            Assert.That(water.b, Is.GreaterThan(water.r), "the water variant should read blue");
            Assert.That(fire.r, Is.GreaterThan(fire.b), "the fire variant should read red");
        }

        [Test]
        public void Recolor_NeutralAndZeroStrengthAreNoOps()
        {
            var c = new Color(0.3f, 0.6f, 0.2f);
            Assert.AreEqual(c, ElementPalette.Recolor(c, Element.Neutral));
            Assert.AreEqual(c, ElementPalette.Recolor(c, Element.Fire, strength: 0f));
        }

        [Test]
        public void Recolor_StaysInGamut()
        {
            var bright = new Color(1f, 1f, 1f);
            var v = ElementPalette.Recolor(bright, Element.Fire);
            Assert.That(v.r, Is.InRange(0f, 1f));
            Assert.That(v.g, Is.InRange(0f, 1f));
            Assert.That(v.b, Is.InRange(0f, 1f));
        }

        // --- material library ---

        [Test]
        public void Surface_ResolvesToASupportedShader_WithATexture()
        {
            var mat = MaterialLibrary.Surface(SurfaceKind.Ground);

            Assert.IsNotNull(mat);
            Assert.IsTrue(mat.shader.isSupported);
            Assert.That(mat.shader.name, Does.Not.Contain("InternalErrorShader"));
            Assert.IsNotNull(mat.GetTexture("_BaseMap"),
                "the ground needs surface variation — a flat albedo is what made it read as dead grey");
        }

        [Test]
        public void Variants_AreCachedPerKindAndElement()
        {
            var a = MaterialLibrary.Variant(SurfaceKind.Ground, Element.Fire);
            var b = MaterialLibrary.Variant(SurfaceKind.Ground, Element.Fire);
            var c = MaterialLibrary.Variant(SurfaceKind.Ground, Element.Ice);

            Assert.AreSame(a, b, "a repeated variant must be a lookup, not a new material");
            Assert.AreNotSame(a, c);
        }

        [Test]
        public void BiomeTint_IsASingleKnobOverEverySurface()
        {
            var neutral = MaterialLibrary.Surface(SurfaceKind.Ground).GetColor("_BaseColor");

            var darker = BiomePalette.Backyard;
            darker.Tint = new Color(0.5f, 0.5f, 0.5f);
            MaterialLibrary.Palette = darker;   // one value, whole biome

            var tinted = MaterialLibrary.Surface(SurfaceKind.Ground).GetColor("_BaseColor");

            Assert.That(ElementPalette.Luminance(tinted),
                Is.LessThan(ElementPalette.Luminance(neutral)),
                "the biome tint must actually move every surface");
        }

        // --- YT-69: the floor must read as grass, not as bile ---

        [Test]
        public void BothGroundTones_AreUnambiguouslyGreen()
        {
            var p = BiomePalette.Backyard;

            foreach (var (name, c) in new[] { ("base", p.GroundBase), ("accent", p.GroundAccent) })
            {
                Assert.That(c.g, Is.GreaterThan(c.r + 0.1f),
                    $"the {name} tone is not clearly green. When red creeps up to meet green you get " +
                    "mustard, and a whole floor of mustard is what got called 'vomit'.");
                Assert.That(c.g, Is.GreaterThan(c.b + 0.1f), $"the {name} tone should read green, not teal");
            }
        }

        [Test]
        public void TheGroundVariesByValue_NotByASwingIntoYellow()
        {
            var p = BiomePalette.Backyard;

            // Sunlit turf must be brighter than shaded turf...
            Assert.That(ElementPalette.Luminance(p.GroundAccent),
                Is.GreaterThan(ElementPalette.Luminance(p.GroundBase) + 0.08f),
                "the mottling needs real value contrast or the floor reads flat");

            // ...but it must not achieve that by turning yellow. Yellow = red catching up with green.
            float baseGap = p.GroundBase.g - p.GroundBase.r;
            float accentGap = p.GroundAccent.g - p.GroundAccent.r;
            Assert.That(accentGap, Is.GreaterThan(0.12f),
                "the bright tone must stay green-dominant; letting red close the gap is exactly how " +
                "the old accent became khaki");
            Assert.That(baseGap, Is.GreaterThan(0.12f));
        }

        [Test]
        public void TheFloorLeavesRoomForMaxAndTheRobotsToPop()
        {
            float floor = ElementPalette.Luminance(BiomePalette.Backyard.GroundBase);
            float sunlit = ElementPalette.Luminance(BiomePalette.Backyard.GroundAccent);

            // Mid-value: bright enough to be a sunny lawn, dark enough that a red hero and steel
            // enemies read against it instead of fighting it.
            Assert.That(floor, Is.InRange(0.10f, 0.35f), "shaded turf should sit in the lower mids");
            Assert.That(sunlit, Is.LessThan(0.70f), "sunlit turf must not blow out to near-white");
        }

        [Test]
        public void Textures_TileSeamlessly()
        {
            // Tested at the size actually shipped, not a toy 64px. Seamlessness here means the noise
            // is PERIODIC, and a periodic function only *looks* continuous once the texture resolves
            // its lattice: at 64px the grass layers land only a third of a cell from the wrap, so
            // opposite edges legitimately differ even though the tiling is exact. At 512 they land
            // within a twentieth of a cell and converge, which is the case the ground actually runs in.
            const int size = 512;
            var tex = StylizedTextures.Ground(size);

            // Opposite edges must agree, or the ground shows a grid of seams once it's tiled.
            for (int i = 0; i < size; i++)
            {
                Assert.That(tex.GetPixel(0, i).r, Is.EqualTo(tex.GetPixel(size - 1, i).r).Within(0.08f),
                    $"vertical seam at row {i}");
                Assert.That(tex.GetPixel(i, 0).r, Is.EqualTo(tex.GetPixel(i, size - 1).r).Within(0.08f),
                    $"horizontal seam at column {i}");
            }
        }

        // --- YT-69 (second pass): the floor must read as a SURFACE, not as a picture of one ---

        [Test]
        public void TheGroundHasRelief_NotJustColour()
        {
            var mat = MaterialLibrary.Surface(SurfaceKind.Ground);

            // The whole diagnosis of "flat and textureless, artificial" in one assertion. An albedo
            // on a flat plane gives the key light nothing to break across, so every pixel of the lawn
            // shades identically and the eye calls it a fill. Relief is the fix; lose the normal map
            // and the floor goes straight back to looking painted on.
            Assert.IsNotNull(mat.GetTexture("_BumpMap"),
                "the ground has no normal map — it is a flat plane wearing a picture of grass");
        }

        [Test]
        public void TheGroundNormalMap_ActuallyPerturbsTheLight()
        {
            var n = StylizedTextures.GroundNormal(64);

            // A normal map that is entirely (0,0,1) is a normal map that does nothing — it would pass
            // "is not null" and still leave the ground dead flat.
            float maxTilt = 0f;
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                var c = n.GetPixel(x, y);
                float tilt = Mathf.Abs(c.r - 0.5f) + Mathf.Abs(c.g - 0.5f);
                maxTilt = Mathf.Max(maxTilt, tilt);
            }

            Assert.That(maxTilt, Is.GreaterThan(0.08f),
                "the normal map is flat — the grass would have no light response at all");
        }

        [Test]
        public void TheTiledTexture_CarriesGrain_NotBlotches()
        {
            const int size = 512, block = 64;   // blocks an eighth of a tile across
            var tex = StylizedTextures.Ground(size);
            var px = tex.GetPixels();

            // Total variation across the tile...
            float mean = 0f;
            foreach (var c in px) mean += c.r;
            mean /= px.Length;
            float total = 0f;
            foreach (var c in px) total += (c.r - mean) * (c.r - mean);
            total = Mathf.Sqrt(total / px.Length);

            // ...versus the variation that survives blurring it down to eighth-of-a-tile blocks,
            // which is the low-frequency content.
            int blocks = size / block;
            float lowVar = 0f;
            for (int by = 0; by < blocks; by++)
            for (int bx = 0; bx < blocks; bx++)
            {
                float bm = 0f;
                for (int y = 0; y < block; y++)
                for (int x = 0; x < block; x++)
                    bm += px[(by * block + y) * size + (bx * block + x)].r;
                bm /= block * block;
                lowVar += (bm - mean) * (bm - mean);
            }
            float low = Mathf.Sqrt(lowVar / (blocks * blocks));

            // This is the anti-wallpaper invariant. The eye cannot see a 4 cm speckle repeating, but
            // it spots a metre-wide blotch appearing on a grid immediately — so the LOW frequencies
            // are the ones that betray a tiled texture, and they belong in the shader's world-space
            // passes (which never repeat), not in the tile. Let big soft blobs back into this texture
            // and the lawn goes back to reading as wallpaper, however nice the grain is.
            Assert.That(low / total, Is.LessThan(0.4f),
                $"the tile's low-frequency content is {low / total:P0} of its variation — too blotchy; " +
                "those blobs will repeat visibly across the yard");
        }

        [Test]
        public void TheDryTone_MayGoStrawy_ButNotMustard()
        {
            var dry = BiomePalette.Backyard.GroundDry;

            // The macro pass drifts the lawn toward this tone across the yard. It is *meant* to be
            // warmer and paler — sun-bleached grass is. But "warmer" is the exact road that ended in
            // vomit last time: mustard is what you get when red catches up with green. The dry tone
            // is allowed to walk toward it and not allowed to arrive.
            Assert.That(dry.g, Is.GreaterThan(dry.r + 0.1f),
                "the dry tone has gone mustard — this is precisely how the floor became 'vomit'");
            Assert.That(dry.g, Is.GreaterThan(dry.b + 0.1f), "the dry tone should read green, not teal");
        }

        [Test]
        public void TheMacroPass_VariesTheYard_WithoutRepaintingIt()
        {
            var p = BiomePalette.Backyard;

            // Long wavelength: this is the "not one uniform slab" knob, and it has to be measured in
            // metres. Push it up and the yard stops reading as a lawn and starts reading as a texture.
            Assert.That(1f / p.GroundMacroScale, Is.InRange(8f, 40f),
                "the across-the-yard variation should cycle over metres, not centimetres");

            // But it must not be so strong that it undoes the colour verdict that was already signed
            // off, or repaints the floor light enough to stop Max and the robots popping against it.
            Assert.That(p.GroundMacroStrength, Is.InRange(0.05f, 0.6f));
            Assert.That(p.GroundLushShade, Is.InRange(0.6f, 1f));
        }

        [Test]
        public void Textures_ActuallyVary()
        {
            var tex = StylizedTextures.Ground(64);
            float min = 1f, max = 0f;
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float v = tex.GetPixel(x, y).r;
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
            Assert.That(max - min, Is.GreaterThan(0.25f),
                "a near-constant texture is the same as no texture — the ground would stay flat");
        }

        // --- what gets dressed ---

        [Test]
        public void Damageables_AreLeftAlone_SoGameplayTintsDontFight()
        {
            var enemy = new GameObject("enemy", typeof(MeshRenderer), typeof(MeshFilter));
            var dummy = enemy.AddComponent<DamageableDummy>();
            var wall = new GameObject("wall", typeof(MeshRenderer), typeof(MeshFilter));
            try
            {
                Assert.IsFalse(WorldMaterials.IsWorldSurface(enemy.GetComponent<MeshRenderer>()),
                    "enemies are tinted at runtime by gameplay (hit flashes) — re-skinning them here " +
                    "would mean two systems fighting over _BaseColor");
                Assert.IsTrue(WorldMaterials.IsWorldSurface(wall.GetComponent<MeshRenderer>()));
                Assert.IsNotNull(dummy);
            }
            finally
            {
                Object.DestroyImmediate(enemy);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void KindOf_ReadsShape_NotName()
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                plane.name = "not-called-ground";
                box.transform.localScale = new Vector3(1f, 4f, 1f);

                Assert.AreEqual(SurfaceKind.Ground, WorldMaterials.KindOf(plane.GetComponent<Renderer>()));
                Assert.AreEqual(SurfaceKind.Wall, WorldMaterials.KindOf(box.GetComponent<Renderer>()));
            }
            finally
            {
                Object.DestroyImmediate(plane);
                Object.DestroyImmediate(box);
            }
        }
    }
}
