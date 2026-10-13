using System.Collections.Generic;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1181: <see cref="MapValidation.Cover"/>'s flat 1 m cover-height floor refused Market
    /// Street's authored street furniture (cafe table 0.8 m, planter box 0.6 m, bollard 0.95 m) with
    /// no exemption mechanism. The fix narrows the floor to <see cref="MapValidation.MinLowProfileCoverHeight"/>
    /// for the specific low-profile dressings in <see cref="MapValidation.LowProfileCoverDressings"/>,
    /// leaving ordinary tactical cover's <see cref="MapValidation.MinCoverHeight"/> floor untouched.
    /// </summary>
    public sealed class MV1181LowProfileCoverHeightTests
    {
        private static MapData Map(MapEntity cover)
        {
            return new MapData
            {
                name = "MV-1181 Low Profile Cover",
                wallHeight = 3f,
                wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "a", type = "entry", x = 0f, z = 0f, width = 20f, depth = 20f },
                },
                entities = new[]
                {
                    new MapEntity { id = "start", kind = "playerSpawn", x = 0f, z = 0f },
                    cover,
                },
            };
        }

        [Test]
        public void Validation_ExemptsAuthoredLowProfileStreetFurnitureFromTheOrdinaryCoverHeightFloor()
        {
            // --- AC1: Market Street's own measured values (cafeTable 0.8 m, planterBox 0.6 m,
            // bollard 0.95 m) all validate once dressed with a LowProfileCoverDressings entry.
            MapData cafeTable = Map(new MapEntity
            {
                id = "a1_c13", kind = "cover", x = 0f, z = 0f, width = 1.8f, height = 0.8f, depth = 0.9f,
                dressing = "cafeTable",
            });
            Assert.IsTrue(MapValidation.Validate(cafeTable, out string cafeWhy), cafeWhy);

            MapData planterBox = Map(new MapEntity
            {
                id = "a1_c17", kind = "cover", x = 0f, z = 0f, width = 1.6f, height = 0.6f, depth = 0.5f,
                dressing = "planterBox",
            });
            Assert.IsTrue(MapValidation.Validate(planterBox, out string planterWhy), planterWhy);

            MapData bollard = Map(new MapEntity
            {
                id = "a1_c27", kind = "cover", x = 0f, z = 0f, width = 0.4f, height = 0.95f, depth = 0.4f,
                dressing = "bollard",
            });
            Assert.IsTrue(MapValidation.Validate(bollard, out string bollardWhy), bollardWhy);

            // --- AC2 (narrowing, not a removal): the SAME 0.8 m height with no low-profile dressing
            // still fails exactly as it always has — ordinary tactical cover keeps its 1 m floor.
            MapData undressed = Map(new MapEntity
            {
                id = "plainCover", kind = "cover", x = 0f, z = 0f, width = 1.8f, height = 0.8f, depth = 0.9f,
            });
            Assert.IsFalse(MapValidation.Validate(undressed, out string undressedWhy));
            StringAssert.Contains("plainCover", undressedWhy);

            // --- Even a low-profile dressing has a floor: an authoring mistake near zero height still
            // fails, naming the lower threshold.
            MapData tooFlat = Map(new MapEntity
            {
                id = "a1_c99", kind = "cover", x = 0f, z = 0f, width = 0.4f, height = 0.1f, depth = 0.4f,
                dressing = "bollard",
            });
            Assert.IsFalse(MapValidation.Validate(tooFlat, out string tooFlatWhy));
            StringAssert.Contains("a1_c99", tooFlatWhy);
        }
    }
}
