using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The wind (YT-78). Two shaders carry it, for two different reasons, and both reasons are worth
    /// pinning down because both are counter-intuitive.
    /// </summary>
    public sealed class WindTests
    {
        [TearDown]
        public void Reset()
        {
            MaterialLibrary.Palette = BiomePalette.Backyard;
            MaterialLibrary.Clear();
        }

        /// <summary>
        /// Only plants bend.
        ///
        /// The yard's WALLS wear the same shader as its bushes — the greybox walls are its fence line,
        /// so they are timber and they are triplanar and they are, as far as the shader is concerned,
        /// exactly the same kind of thing as a hedge. The only thing standing between a breathing
        /// fence and the player is that the wind strength is a per-material number and wood's is zero.
        /// </summary>
        [Test]
        [TestCase(SurfaceKind.Wood, false)]
        [TestCase(SurfaceKind.Wall, false)]     // the fence line — must NEVER move
        [TestCase(SurfaceKind.Stone, false)]
        [TestCase(SurfaceKind.Dirt, false)]
        [TestCase(SurfaceKind.Metal, false)]    // the factory is a machine, not a plant
        [TestCase(SurfaceKind.Foliage, true)]
        public void OnlyPlantsBendInTheWind(SurfaceKind kind, bool shouldMove)
        {
            var m = MaterialLibrary.Surface(kind);
            Assert.IsTrue(m.HasProperty("_WindStrength"), $"{kind} has no wind control at all.");

            float wind = m.GetFloat("_WindStrength");

            if (shouldMove)
            {
                Assert.Greater(wind, 0f, $"{kind} doesn't move. It is a plant; the yard is supposed " +
                                         "to look like it has weather in it.");
            }
            else
            {
                Assert.AreEqual(0f, wind, 1e-5f,
                    $"{kind} SWAYS. A fence that breathes is not ambience, it is a bug — and the " +
                    "arena's walls, the shed and the factory all wear this same shader.");
            }
        }

        /// <summary>
        /// The sway has to be big enough to see and small enough to ignore.
        ///
        /// This test used to hold the wind between 6 and 20 CENTIMETRES, and it passed for a wind
        /// nobody could see. Metres are the wrong unit for the question: what decides whether a plant
        /// moves is how many PIXELS it crosses at the play camera, and that depends on how tall the
        /// plant is as much as on the number here. The range is kept as a sanity rail — see
        /// WindVisibilityTests for the test that actually answers the question.
        /// </summary>
        [Test]
        public void TheFoliageWind_IsAGentleOne()
        {
            float wind = MaterialLibrary.Surface(SurfaceKind.Foliage).GetFloat("_WindStrength");

            Assert.That(wind, Is.InRange(0.1f, 0.4f),
                $"the wind is {wind:0.00} m at full bend. Well under this and no plant in the yard " +
                "moves a pixel; well over it and the bushes are waving, and ambience that pulls the " +
                "eye off a telegraph has stopped being ambience.");
        }

        /// <summary>
        /// The LAWN has a wind too, and it is the one that matters.
        ///
        /// The lawn is a flat plane with almost no vertices in it — there is nothing there for a vertex
        /// shader to bend — and it is also most of what is on the screen. Swaying only the props left
        /// the yard visibly dead, because the things that CAN bend cover about a tenth of the frame. So
        /// the ground shader leans the BLADES instead, by moving where its grass texture is sampled.
        /// </summary>
        [Test]
        public void TheLawnHasAWindOfItsOwn()
        {
            var ground = MaterialLibrary.Surface(SurfaceKind.Ground);

            Assert.IsTrue(ground.HasProperty("_WindStrength"), "the lawn has no wind control.");
            Assert.Greater(ground.GetFloat("_WindStrength"), 0f,
                "the lawn doesn't move. It is most of the screen — if it is still, the yard is still, " +
                "however hard the bushes are working.");
            Assert.Greater(ground.GetFloat("_WindSpeed"), 0f, "the lawn's gust never arrives.");
        }

        [Test]
        public void TheLawnsWind_LeansTheGrass_ItDoesNotSlideTheGround()
        {
            float lean = MaterialLibrary.Surface(SurfaceKind.Ground).GetFloat("_WindStrength");

            Assert.That(lean, Is.InRange(0.1f, 0.3f),
                $"the grass leans {lean:0.00} m. Much under this and the lawn — most of the screen — " +
                "is a photograph; much past it and the texture is no longer a blade bending, it is " +
                "the whole lawn sliding sideways under the player's feet.");
        }

        /// <summary>The wind is a look, so it lives with the rest of the biome's look — one struct,
        /// one place to turn the weather off for a world that hasn't got any.</summary>
        [Test]
        public void TheWindIsTunedOnTheBiome_NotBuriedInTheShader()
        {
            var p = BiomePalette.Backyard;
            p.GroundWindLean = 0f;
            MaterialLibrary.Palette = p;

            Assert.AreEqual(0f, MaterialLibrary.Surface(SurfaceKind.Ground).GetFloat("_WindStrength"), 1e-5f,
                "turning the biome's wind off left the lawn blowing about anyway — the shader's own " +
                "default is overriding the palette, so there is nowhere to turn the weather off.");
        }
    }
}
