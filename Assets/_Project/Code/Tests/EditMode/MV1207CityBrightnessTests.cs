using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1207: Lee, 17 Oct, on a TestFlight screenshot of the City — "the whole world could do with
    /// brightening a little bit. There are lots of really interesting structures and detail which are
    /// getting lost because everything's in too much of a dark shade of colour." Night stays night
    /// (the ticket's own "Do not re-raise"); only the City's ambient, key light and wall/cover/ground
    /// palette values move.
    ///
    /// Asserts RESOLVED values only (Rule 2, Tier 2): <see cref="RenderSettings"/>' own ambient colours
    /// after <see cref="BackyardLighting.Apply"/>, and <see cref="BiomePalette.ColorFor"/>'s own output —
    /// never an authored constant compared to itself.
    ///
    /// Fails on base commit 5d36ce5: that commit's ambient resolves to a mean channel level of
    /// ~0.1867 (BaseAmbientLevel below) and the City's ground palette colour resolves to a peak
    /// channel (V) of 0.282 (BaseGroundV below) — short of the 1.35x/1.15x this test demands.
    /// </summary>
    public sealed class MV1207CityBrightnessTests
    {
        private GameObject _go;

        // Recorded from BackyardLook.City on base commit 5d36ce5 (pre-fix):
        //   AmbientSky = (0.20, 0.22, 0.32), AmbientEquator = (0.17, 0.17, 0.21),
        //   AmbientGround = (0.12, 0.12, 0.15) -> mean of all 9 channels.
        private const float BaseAmbientLevel = (0.74f + 0.55f + 0.39f) / 9f;

        // Recorded from BiomePalette.City.GroundBase on base commit 5d36ce5: (0.204, 0.220, 0.282).
        // V (HSV value) of an RGB colour with no negative component is just its peak channel.
        private const float BaseGroundV = 0.282f;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            RenderSettings.fog = false;
        }

        [Test]
        public void CityAmbientAndGroundAreBrighterThanBase()
        {
            _go = new GameObject("mv1207-city-brightness-test");
            var lighting = _go.AddComponent<BackyardLighting>();
            lighting.Apply(BackyardLook.City);

            float resolvedAmbient = MeanChannel(RenderSettings.ambientSkyColor)
                + MeanChannel(RenderSettings.ambientEquatorColor)
                + MeanChannel(RenderSettings.ambientGroundColor);
            resolvedAmbient /= 3f;

            Assert.That(resolvedAmbient, Is.GreaterThanOrEqualTo(BaseAmbientLevel * 1.35f - 1e-4f),
                $"the City's resolved ambient ({resolvedAmbient:F4}) must be >= 1.35x the base commit's " +
                $"({BaseAmbientLevel:F4}) — Lee's own 'everything's too dark' note.");

            Color groundResolved = BiomePalette.City.ColorFor(SurfaceKind.Ground);
            float groundV = PeakChannel(groundResolved);

            Assert.That(groundV, Is.GreaterThanOrEqualTo(BaseGroundV * 1.15f - 1e-4f),
                $"the City's resolved Ground palette colour V ({groundV:F4}) must be >= 1.15x the base " +
                $"commit's ({BaseGroundV:F4}).");
        }

        private static float MeanChannel(Color c) => (c.r + c.g + c.b) / 3f;
        private static float PeakChannel(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));
    }
}
