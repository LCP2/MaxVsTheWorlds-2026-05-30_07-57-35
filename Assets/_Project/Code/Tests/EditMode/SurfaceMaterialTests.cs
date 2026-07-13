using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The surface pass (YT-77): timber, stone, soil and painted metal.
    ///
    /// Most of these are regression tests with a scar. Each one is a bug that shipped into a build and
    /// was found by rendering the yard and looking at it, and every one of them looked perfectly fine
    /// in code review — which is exactly why they are pinned here.
    /// </summary>
    public sealed class SurfaceMaterialTests
    {
        /// <summary>Every kind that carries a material of its own. Ground has its own shader and its
        /// own tests (YT-69); Prop is the fallback grain and has no identity to assert.</summary>
        private static readonly SurfaceKind[] Materials =
        {
            SurfaceKind.Wood,
            SurfaceKind.Stone,
            SurfaceKind.Dirt,
            SurfaceKind.Metal,
            SurfaceKind.Foliage,
            SurfaceKind.Wall,   // the yard's walls ARE its fences: timber
        };

        [TearDown]
        public void Reset()
        {
            MaterialLibrary.Palette = BiomePalette.Backyard;
            MaterialLibrary.Clear();
        }

        // ------------------------------------------------------------------ the materials exist

        [Test]
        public void EverySurface_ResolvesToARealShader_WithAnAlbedo([ValueSource(nameof(Materials))] SurfaceKind kind)
        {
            var m = MaterialLibrary.Surface(kind);

            Assert.IsNotNull(m, $"{kind} has no material at all.");
            Assert.IsTrue(m.shader.isSupported, $"{kind}'s shader is unsupported — it will render magenta.");
            Assert.That(m.shader.name, Does.Not.Contain("InternalErrorShader"),
                $"{kind} fell through to the error shader (YT-58).");
            Assert.IsNotNull(m.GetTexture("_BaseMap"),
                $"{kind} has no albedo map — it is a flat colour, which is the thing this ticket exists to kill.");
        }

        [Test]
        public void EverySurface_CarriesRelief([ValueSource(nameof(Materials))] SurfaceKind kind)
        {
            var m = MaterialLibrary.Surface(kind);

            Assert.IsNotNull(m.GetTexture("_BumpMap"),
                $"{kind} has no normal map. Relief is the half that makes a surface look like a surface: " +
                "without it the light shades every pixel identically and the eye reads a coloured fill, " +
                "not timber. That was YT-69's finding on the lawn and it is just as true standing up.");
        }

        // ------------------------------------------------------------------ the grain is actually there

        /// <summary>
        /// The height field has to USE its range.
        ///
        /// This is the test for the bug that made the first cut of the timber worthless. The generators
        /// reached for <c>Mathf.SmoothStep(0f, 0.16f, x)</c> expecting HLSL's <c>smoothstep</c> — but
        /// Unity's overload INTERPOLATES BETWEEN its first two arguments, so it returned a number in
        /// 0..0.16 rather than a 0..1 mask. Instead of cutting a groove between two planks it dimmed
        /// every plank by half, uniformly. The fence had no seams, the albedo swung across a sliver of
        /// its range, and the yard looked exactly as flat as it had before the work.
        /// </summary>
        [Test]
        public void EveryMask_UsesItsWholeRange([ValueSource(nameof(Materials))] SurfaceKind kind)
        {
            var px = MaterialLibrary.Surface(kind) != null
                ? StylizedTextures.MaskFor(kind).GetPixels32()
                : null;

            Assert.IsNotNull(px, $"{kind} has no mask.");

            float min = px.Min(p => p.r) / 255f;
            float max = px.Max(p => p.r) / 255f;

            Assert.Less(min, 0.04f,
                $"{kind}'s mask never gets dark (min {min:0.00}). Its grooves, gaps and pits are the " +
                "darkest thing on it — if nothing reaches the bottom of the range, they aren't being cut.");
            Assert.Greater(max, 0.96f,
                $"{kind}'s mask never gets bright (max {max:0.00}). It is only using part of its range, " +
                "so it will only produce part of the contrast and part of the relief it was tuned for.");
        }

        /// <summary>Timber is made of BOARDS, and the join between two boards is a hard, narrow, dark
        /// line. A mask with grain but no grooves is a smear, not a fence.</summary>
        [Test]
        public void Timber_IsCutIntoPlanks()
        {
            var tex = StylizedTextures.MaskFor(SurfaceKind.Wood);
            var px = tex.GetPixels32();
            int size = tex.width;

            // Read one row across the grain. A plank groove is a run of dark pixels; the boards
            // between them are not. Count the transitions into darkness.
            int y = size / 3;
            bool dark = false;
            int grooves = 0;
            for (int x = 0; x < size; x++)
            {
                bool d = px[y * size + x].r / 255f < 0.25f;
                if (d && !dark) grooves++;
                dark = d;
            }

            Assert.GreaterOrEqual(grooves, 3,
                $"only {grooves} plank grooves across the tile. The timber mask is supposed to cut the " +
                "surface into boards — if it doesn't, a fence panel renders as one milled sheet.");
        }

        [Test]
        public void EveryNormalMap_ActuallyTilts([ValueSource(nameof(Materials))] SurfaceKind kind)
        {
            var px = StylizedTextures.NormalFor(kind).GetPixels32();

            float maxTilt = px.Max(p => Mathf.Max(
                Mathf.Abs(p.r / 255f * 2f - 1f),
                Mathf.Abs(p.g / 255f * 2f - 1f)));

            Assert.Greater(maxTilt, 0.08f,
                $"{kind}'s normal map is flat (max tilt {maxTilt:0.000}). It will take the light exactly " +
                "like a plane, which is what it is trying not to look like.");
        }

        /// <summary>
        /// The tiles are projected in world space and repeat forever. A seam would draw a hard line
        /// across the yard every couple of metres.
        ///
        /// A seam is not "the two edges differ" — on a surface with hard edges in it (a plank groove, a
        /// course of paving) the last row and the first row are ADJACENT when tiled, and adjacent rows
        /// are supposed to differ: that is what a running bond IS. A seam is a discontinuity that is
        /// ATYPICAL of the texture's own grain. So the wrap is measured against the texture's internal
        /// step from one row to the next, and only has to be no worse.
        /// </summary>
        [Test]
        public void EveryMask_TilesWithoutASeam([ValueSource(nameof(Materials))] SurfaceKind kind)
        {
            var tex = StylizedTextures.MaskFor(kind);
            var px = tex.GetPixels32();
            int size = tex.width;

            float At(int x, int y) => px[y * size + x].r / 255f;

            float interiorX = 0f, interiorY = 0f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size - 1; x++)
            {
                interiorX += Mathf.Abs(At(x + 1, y) - At(x, y));
                interiorY += Mathf.Abs(At(y, x + 1) - At(y, x));
            }
            interiorX /= size * (size - 1);
            interiorY /= size * (size - 1);

            float wrapX = 0f, wrapY = 0f;
            for (int i = 0; i < size; i++)
            {
                wrapX += Mathf.Abs(At(0, i) - At(size - 1, i));
                wrapY += Mathf.Abs(At(i, 0) - At(i, size - 1));
            }
            wrapX /= size;
            wrapY /= size;

            // Generous, on purpose: this is here to catch a texture that does not wrap AT ALL (which
            // shows up as a wrap step many times the grain), not to police a few percent.
            Assert.LessOrEqual(wrapX, interiorX * 3f + 0.02f,
                $"{kind} does not wrap horizontally: the step across the seam ({wrapX:0.000}) is far " +
                $"bigger than the texture's own grain ({interiorX:0.000}). It will draw a line down the yard.");
            Assert.LessOrEqual(wrapY, interiorY * 3f + 0.02f,
                $"{kind} does not wrap vertically: the step across the seam ({wrapY:0.000}) is far " +
                $"bigger than the texture's own grain ({interiorY:0.000}). It will draw a line across the yard.");
        }

        // ------------------------------------------------------------------ under the sun

        /// <summary>
        /// The albedo that SHIPS is the baked texture, not the tone it was grown from — and the bright
        /// end of a grain crosses the sunlit ceiling long before its base colour does. YT-75's follow-up
        /// found this the hard way with a fence that rendered cream in the sun and brown in the shade.
        /// </summary>
        [Test]
        public void NoSurfaceAlbedoClipsToWhiteInTheSun([ValueSource(nameof(Materials))] SurfaceKind kind)
        {
            float key = BackyardLook.Default.KeyIntensity;
            Assert.Greater(key, 1f, "this test only means anything under a key brighter than 1x.");

            var tex = (Texture2D)MaterialLibrary.Surface(kind).GetTexture("_BaseMap");
            var px = tex.GetPixels32();

            float peak = px.Max(p => Mathf.Max(p.r, Mathf.Max(p.g, p.b))) / 255f;

            Assert.LessOrEqual(peak, SunlitAlbedo.Ceiling + 0.01f,
                $"{kind}'s brightest pixel is {peak:0.00}. Under a {key:0.0}x key that renders past 1.0 " +
                "and clips: it stops being a colour and becomes a highlight.");
        }

        [Test]
        public void BringingAColourUnderTheSun_KeepsItsHue()
        {
            var kitWood = new Color(1.00f, 0.56f, 0.38f);   // what Kenney actually ships
            Color under = SunlitAlbedo.Clamp(kitWood);

            Assert.LessOrEqual(under.maxColorComponent, SunlitAlbedo.Ceiling + 1e-4f, "still clips.");
            Assert.That(under.g / under.r, Is.EqualTo(kitWood.g / kitWood.r).Within(0.01f),
                "the hue moved. Clamping each channel would flatten a warm timber into a muddy olive — " +
                "the whole point is to scale by the peak so the ratios survive.");
            Assert.That(under.b / under.r, Is.EqualTo(kitWood.b / kitWood.r).Within(0.01f), "the hue moved.");
        }

        // ------------------------------------------------------------------ the kit

        [Test]
        [TestCase("kit_wood", SurfaceKind.Wood)]
        [TestCase("kit_woodDark", SurfaceKind.Wood)]
        [TestCase("kit_woodBark", SurfaceKind.Wood)]
        [TestCase("kit_wood (Instance)", SurfaceKind.Wood)]   // Unity renames a cloned material
        [TestCase("kit_dirt", SurfaceKind.Dirt)]
        [TestCase("kit_dirtDark", SurfaceKind.Dirt)]
        [TestCase("kit_stone", SurfaceKind.Stone)]
        [TestCase("kit_stoneDark", SurfaceKind.Stone)]
        [TestCase("kit_leafsGreen", SurfaceKind.Foliage)]
        [TestCase("kit_grass", SurfaceKind.Foliage)]
        public void AKitMaterial_IsClassifiedByWhatItIsMadeOf(string material, SurfaceKind expected)
        {
            Assert.AreEqual(expected, KitSurfaces.KindOf(material),
                $"{material} was not recognised, so it would keep its flat colour.");
        }

        [Test]
        [TestCase("kit_colorRed")]      // painted petals, centimetres across
        [TestCase("kit_colorYellow")]
        [TestCase("kit__defaultMat")]   // the kit's catch-all: brackets, rims, edging
        [TestCase("Stylized_tint:Wood:573D26")]   // already ours — must not be re-dressed
        [TestCase("some_other_material")]
        public void AMaterialWeCannotName_IsLeftAlone(string material)
        {
            Assert.IsNull(KitSurfaces.KindOf(material),
                $"{material} was classified. A material we can't name is a material we shouldn't texture — " +
                "and re-dressing our own output would be a loop.");
        }

        [Test]
        public void ARe_SurfacedKitProp_KeepsTheColourTheKitWasGiven()
        {
            // The tone YT-75 chose for the kit's timber, after dragging it under the sunlit ceiling.
            var tone = new Color(0.34f, 0.24f, 0.15f);
            var m = MaterialLibrary.Tinted(SurfaceKind.Wood, tone);

            var px = ((Texture2D)m.GetTexture("_BaseMap")).GetPixels32();
            float meanR = (float)px.Average(p => p.r) / 255f;
            float meanG = (float)px.Average(p => p.g) / 255f;
            float meanB = (float)px.Average(p => p.b) / 255f;

            // The grain swings either side of the tone, so the MEAN is the tone. Texturing a kit
            // material must not quietly repaint it: YT-75 and its follow-up settled these colours.
            Assert.That(meanR, Is.EqualTo(tone.r).Within(0.06f), "the timber got repainted, not textured.");
            Assert.That(meanG, Is.EqualTo(tone.g).Within(0.06f), "the timber got repainted, not textured.");
            Assert.That(meanB, Is.EqualTo(tone.b).Within(0.06f), "the timber got repainted, not textured.");
        }

        [Test]
        public void TheSameKitTone_SharesOneMaterial()
        {
            var tone = new Color(0.34f, 0.24f, 0.15f);

            Assert.AreSame(MaterialLibrary.Tinted(SurfaceKind.Wood, tone),
                           MaterialLibrary.Tinted(SurfaceKind.Wood, tone),
                "a material per prop is a draw call per prop. 217 props share about 16 tones.");
        }

        // ------------------------------------------------------------------ the magenta

        /// <summary>
        /// Re-asserting the palette a scene is ALREADY using must not destroy the materials in it.
        ///
        /// This is the whole yard going magenta. WorldMaterials and the kit dressing both install
        /// themselves at AfterSceneLoad, in an order neither controls. Whichever ran second re-set the
        /// same Backyard palette; the setter cleared the cache unconditionally; and Clear() destroys
        /// every cached material — including the ones the other pass had just handed to 217 renderers.
        /// A destroyed material renders MAGENTA, and it did, in the player only, while the editor
        /// looked perfect.
        /// </summary>
        [Test]
        public void ReAssertingTheSamePalette_DoesNotDestroyMaterialsAlreadyInUse()
        {
            var before = MaterialLibrary.Tinted(SurfaceKind.Wood, new Color(0.34f, 0.24f, 0.15f));
            Assert.IsNotNull(before);

            MaterialLibrary.Palette = BiomePalette.Backyard;   // the same palette, asserted again

            Assert.IsTrue(before != null,
                "the material was destroyed by a palette change that changed nothing. Every renderer " +
                "still pointing at it now draws magenta.");
            Assert.AreSame(before, MaterialLibrary.Tinted(SurfaceKind.Wood, new Color(0.34f, 0.24f, 0.15f)),
                "the cache was dropped, so every renderer holding the old material is now orphaned.");
        }

        [Test]
        public void ChangingThePaletteForReal_StillRebuilds()
        {
            var before = MaterialLibrary.Surface(SurfaceKind.Wood);

            var other = BiomePalette.Backyard;
            other.Tint = new Color(0.5f, 0.5f, 0.9f);
            MaterialLibrary.Palette = other;

            Assert.AreNotSame(before, MaterialLibrary.Surface(SurfaceKind.Wood),
                "a real palette change has to re-colour the arena — that is what the Tint knob is for.");
        }

        // ------------------------------------------------------------------ the machine

        /// <summary>A stand-in for the Mower Hutch: a damageable body, which is what makes a renderer
        /// the MACHINE rather than one of its children.</summary>
        private sealed class FakeMachine : MonoBehaviour, IDamageable
        {
            public bool IsAlive => true;
            public Team Team => Team.Enemy;
            public void TakeDamage(in DamageInfo info) { }
        }

        [Test]
        public void TheFactory_IsMadeOfPaintedMetal_AndCarriesNoPropertyBlock()
        {
            var go = new GameObject("Hutch", typeof(MeshFilter), typeof(MeshRenderer));
            try
            {
                go.AddComponent<FakeMachine>();
                var skin = go.AddComponent<CharacterSkin>().Bind(CharacterRole.Structure);
                var r = go.GetComponent<MeshRenderer>();

                Assert.AreEqual(SurfaceKind.Metal, KindOfMaterial(r.sharedMaterial),
                    "the Mower Hutch is a machine and this ticket says it is made of worn painted metal.");

                // The factory neither flashes nor telegraphs — nothing routes either to a Structure —
                // so its colour lives in its material. That is not a detail: with a property block on
                // this renderer, the triplanar shader lights it about four times too dark.
                Assert.IsFalse(r.HasPropertyBlock(),
                    "the machine still has a property block. Its colour belongs in its material.");
                Assert.AreEqual(CharacterRole.Structure, skin.Role);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// A Structure's CHILDREN are not the machine.
        ///
        /// CharacterSkinDirector skins every renderer under a damageable, so the Mower Hutch's
        /// VulnerableCore — the pulsing "shoot here" tell (spec §7) — is skinned as a Structure too.
        /// If it were treated as the machine, its property block would be cleared every frame while
        /// MowerHutch.PulseCore wrote to it every frame, and whichever LateUpdate ran second would
        /// decide whether the core glowed. It is not the machine; it keeps what it had.
        /// </summary>
        [Test]
        public void AChildOfTheFactory_IsNotTheFactory()
        {
            var body = new GameObject("Hutch", typeof(MeshFilter), typeof(MeshRenderer));
            try
            {
                body.AddComponent<FakeMachine>();

                var core = new GameObject("VulnerableCore", typeof(MeshFilter), typeof(MeshRenderer));
                core.transform.SetParent(body.transform, false);

                core.AddComponent<CharacterSkin>().Bind(CharacterRole.Structure);
                var r = core.GetComponent<MeshRenderer>();

                Assert.AreEqual(MaterialLibrary.CharacterShaderName, r.sharedMaterial.shader.name,
                    "the glowing core was re-skinned as painted metal. It is a light, not a panel.");
                Assert.IsTrue(r.HasPropertyBlock(),
                    "the core lost the property block gameplay pulses it through — the tell goes dark.");
            }
            finally
            {
                Object.DestroyImmediate(body);
            }
        }

        [Test]
        public void TheThingsThatFight_KeepTheCharacterShader()
        {
            var go = new GameObject("Robot", typeof(MeshFilter), typeof(MeshRenderer));
            try
            {
                go.AddComponent<CharacterSkin>().Bind(CharacterRole.Robot);
                var r = go.GetComponent<MeshRenderer>();

                Assert.AreEqual(MaterialLibrary.CharacterShaderName, r.sharedMaterial.shader.name,
                    "a body lost the character shader — and with it the outline and rim light that are " +
                    "what make it read at a fixed top-down camera (YT-57).");
                Assert.IsTrue(r.HasPropertyBlock(),
                    "a body lost its property block, and with it every hit flash and wind-up tell (YT-61).");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Which of our materials this is, by the key baked into its name.</summary>
        private static SurfaceKind? KindOfMaterial(Material m)
        {
            if (m == null) return null;
            foreach (SurfaceKind k in System.Enum.GetValues(typeof(SurfaceKind)))
            {
                if (m.name.Contains($":{k}:") || m.name.EndsWith($":{k}")) return k;
            }
            return null;
        }
    }
}
