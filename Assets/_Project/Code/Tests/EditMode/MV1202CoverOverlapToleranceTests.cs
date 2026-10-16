using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1202: <see cref="MapValidation.Cover"/>'s cover-vs-cover check called bare <c>Rect.Overlaps</c>
    /// (via <see cref="ArenaCover.Overlaps"/>) with no tolerance at all — unlike every other geometric
    /// comparison in the file, all of which carry a <see cref="MapGeometry.Epsilon"/>-scale tolerance.
    /// Two cover boxes authored face-to-face (an end-to-end barrier wall, an L-corner) have a zero-area
    /// abutment in exact arithmetic, but float32 rounding on their authored centres can push that a few
    /// microns either side of zero depending on where in the world they sit — the same pattern passed at
    /// one translation and failed at another in MV-1200's city_config.json. The fix measures the overlap
    /// extent on both axes and only reports a violation once it exceeds <see cref="MapValidation.MinCoverOverlap"/>
    /// (1 cm), not at the first sign of a strict-comparison rounding artefact.
    /// </summary>
    public sealed class MV1202CoverOverlapToleranceTests
    {
        [Test]
        public void Validation_IgnoresAZeroAreaCoverAbutmentButStillCatchesARealOverlap()
        {
            // --- AC1: two 1.7 x 0.6 m barriers authored face-to-face, same geometry and separation as
            // MV-1200's a5_c27/a5_c28 pair, which float32 rounds to a ~1.9 µm "overlap" at this
            // translation. This is the half that FAILS on the base commit — see the fix comment for the
            // quoted failure output.
            var touching = new MapData
            {
                name = "MV-1202 Touching Cover",
                wallHeight = 3f,
                wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "a", type = "entry", x = 20f, z = 0f, width = 40f, depth = 20f },
                },
                entities = new[]
                {
                    new MapEntity { id = "start", kind = "playerSpawn", x = 5f, z = -5f },
                    new MapEntity
                    {
                        id = "a5_c27", kind = "cover", x = 17.45f, z = 0f,
                        width = 1.7f, height = 1.2f, depth = 0.6f,
                    },
                    new MapEntity
                    {
                        id = "a5_c28", kind = "cover", x = 19.15f, z = 0f,
                        width = 1.7f, height = 1.2f, depth = 0.6f,
                    },
                },
            };
            Assert.IsTrue(MapValidation.Validate(touching, out string touchingWhy), touchingWhy);

            // --- AC2: the same tolerance must not swallow a real overlap. Two 1x1 m boxes overlapping
            // by 0.2 m on both axes still fails, naming the pair and the measured extent.
            var overlapping = new MapData
            {
                name = "MV-1202 Overlapping Cover",
                wallHeight = 3f,
                wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "b", type = "entry", x = 0f, z = 0f, width = 20f, depth = 20f },
                },
                entities = new[]
                {
                    new MapEntity { id = "start", kind = "playerSpawn", x = -8f, z = -8f },
                    new MapEntity
                    {
                        id = "ov_a", kind = "cover", x = 0f, z = 0f,
                        width = 1f, height = 1.2f, depth = 1f,
                    },
                    new MapEntity
                    {
                        id = "ov_b", kind = "cover", x = 0.8f, z = 0.8f,
                        width = 1f, height = 1.2f, depth = 1f,
                    },
                },
            };
            Assert.IsFalse(MapValidation.Validate(overlapping, out string overlapWhy));
            StringAssert.Contains("ov_a", overlapWhy);
            StringAssert.Contains("ov_b", overlapWhy);
            StringAssert.Contains("0.2", overlapWhy);
        }
    }
}
