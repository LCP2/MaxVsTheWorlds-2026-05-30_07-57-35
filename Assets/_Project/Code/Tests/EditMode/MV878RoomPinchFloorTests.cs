using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-878: <see cref="MapValidation.Cover"/>'s room-pinch sweep refused any room whose widest
    /// crossing at some depth fell below <see cref="MapValidation.MinFreeChannel"/>, hard-coded to 3 m.
    /// That is a readability floor — twice Max's 1.0 m <c>CharacterController</c> body width, not a
    /// physical-fit check — and it refused four authored World 2 area a13 depths whose widest crossing
    /// is exactly 2.0 m, deliberate maze geometry from Lee's design workbook, not corrupted data.
    /// <see cref="MapValidation.MinFreeChannel"/> drops to 2 m, and the sweep's comparison at that floor
    /// is now tolerant of floating-point equality (<c>MinFreeChannel - 1e-3f</c>) so an authored 2 m gap
    /// on the 1 m grid does not fail on a rounding hair.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2 — the validator's own verdict and reason string, never <see cref="MapValidation.MinFreeChannel"/>
    /// itself): a zone whose widest crossing is exactly 2.0 m now validates, and — in the same test, so
    /// the sweep is proven to still catch a genuine pinch rather than merely "disabled" — a zone whose
    /// widest crossing is 1.5 m is still refused, naming the pinch.
    /// </summary>
    public sealed class MV878RoomPinchFloorTests
    {
        /// <summary>A single 10x6 m Entry zone (tight rooms are allowed for Entry — only Open/Dense/Boss
        /// enforce <see cref="MapValidation.MinFightRoomWidth"/>) with a player spawn, and two cover
        /// slabs spanning its whole depth that leave a <paramref name="gap"/> m wide channel centred on
        /// x=0.</summary>
        private static MapData Map(float gap)
        {
            const float zoneHalfWidth = 5f; // zone width 10 → XMin -5, XMax 5
            float halfGap = gap * 0.5f;
            float sideWidth = zoneHalfWidth - halfGap;
            float leftCenter = -(zoneHalfWidth + halfGap) * 0.5f;
            float rightCenter = (zoneHalfWidth + halfGap) * 0.5f;

            return new MapData
            {
                name = "MV-878 Room Pinch",
                wallHeight = 3f,
                wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "z", type = "entry", x = 0f, z = 0f, width = 10f, depth = 6f },
                },
                entities = new[]
                {
                    new MapEntity { id = "start", kind = "playerSpawn", x = 0f, z = 0f },
                    new MapEntity { id = "left", kind = "cover", x = leftCenter, z = 0f, width = sideWidth, height = 1.5f, depth = 8f },
                    new MapEntity { id = "right", kind = "cover", x = rightCenter, z = 0f, width = sideWidth, height = 1.5f, depth = 8f },
                },
            };
        }

        [Test]
        public void Validation_AcceptsAnExactlyTwoMetreChannel_ButStillRefusesAOnePointFiveMetrePinch()
        {
            // Widest crossing exactly 2.0 m — the World 2 a13 case (four depths, cover 2.000 m apart).
            // Under the old 3 m floor this failed; at 2 m with the tolerant comparison it must ACCEPT.
            Assert.IsTrue(MapValidation.Validate(Map(2.0f), out string acceptWhy), acceptWhy);

            // Widest crossing 1.5 m — a genuine pinch, below even the new floor. The sweep must still
            // catch it and name the pinch, proving the floor change didn't gut the rule.
            Assert.IsFalse(MapValidation.Validate(Map(1.5f), out string refuseWhy));
            StringAssert.Contains("pinches", refuseWhy);
        }
    }
}
