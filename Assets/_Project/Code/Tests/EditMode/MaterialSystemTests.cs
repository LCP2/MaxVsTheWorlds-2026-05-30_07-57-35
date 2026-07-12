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
            var tex = StylizedTextures.Ground(64);

            // Opposite edges must agree, or the ground shows a grid of seams once it's tiled.
            for (int i = 0; i < 64; i++)
            {
                Assert.That(tex.GetPixel(0, i).r, Is.EqualTo(tex.GetPixel(63, i).r).Within(0.08f),
                    $"vertical seam at row {i}");
                Assert.That(tex.GetPixel(i, 0).r, Is.EqualTo(tex.GetPixel(i, 63).r).Within(0.08f),
                    $"horizontal seam at column {i}");
            }
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
