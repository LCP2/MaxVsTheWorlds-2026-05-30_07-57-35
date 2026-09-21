using System.Collections.Generic;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-877, Lee's call (2026-09-21): "ALL we need to do is make sure Max can't take a route that we
    /// don't want him to. So if there's a gap but he can't fit through it then good enough." That settles
    /// what <see cref="MapValidation"/> is for — refusing an unwanted route, never judging readability.
    /// <see cref="MapValidation.Cover"/>'s doorway-clearance branch refused cover within
    /// <see cref="MapValidation.DoorwayClearance"/> (2 m) of a doorway mouth purely so "the way through
    /// stops reading as a way through" — a readability rule, and by Lee's call not validation's job. It
    /// was also refusing correct level data: seven authored World 2 cover pieces sit exactly 2.000 m from
    /// a gate mouth (four-metre corridors on a one-metre grid put the nearest cover exactly on the old
    /// limit), which is what blocked MV-875.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2 — the validator's own verdict and reason string, never the deleted constant): a cover piece
    /// 0.5 m from a doorway's mouth now validates, and — in the same test, so the rest of
    /// <see cref="MapValidation.Cover"/> is proven intact, not merely "the doorway check no longer runs
    /// at all" — two overlapping cover pieces elsewhere in the same map shape are still refused, naming
    /// both.
    /// </summary>
    public sealed class MV877DoorwayClearanceTests
    {
        /// <summary>Two 20x20 m open rooms sharing a 4 m doorway centred on (0, 0) — <paramref name="extra"/>
        /// supplies whatever cover the case under test needs.</summary>
        private static MapData Map(MapEntity[] extra)
        {
            var entities = new List<MapEntity>
            {
                new MapEntity { id = "start", kind = "playerSpawn", x = -8f, z = -15f },
            };
            entities.AddRange(extra);

            return new MapData
            {
                name = "MV-877 Doorway Clearance",
                wallHeight = 3f,
                wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "a", type = "open", x = 0f, z = -10f, width = 20f, depth = 20f },
                    new MapZone { id = "b", type = "open", x = 0f, z = 10f,  width = 20f, depth = 20f },
                },
                links = new[] { new MapLink { from = "a", to = "b", doorway = 4f } },
                entities = entities.ToArray(),
            };
        }

        [Test]
        public void Validation_AcceptsCoverNearADoorway_ButStillRefusesOverlappingCover()
        {
            // Doorway mouth resolves to (0, 0) (rooms share their z=0 edge, doorway centred on the 20 m
            // overlap). Cover footprint is x:[-1,1] z:[0.5,1.5] — nearest point (0, 0.5) is 0.5 m from the
            // mouth, inside the old 2 m DoorwayClearance. This must now ACCEPT.
            MapData nearDoorway = Map(new[]
            {
                new MapEntity { id = "doorwayCover", kind = "cover", x = 0f, z = 1f, width = 2f, height = 1.5f, depth = 1f },
            });
            Assert.IsTrue(MapValidation.Validate(nearDoorway, out string nearWhy), nearWhy);

            // Two cover pieces authored on top of each other, far from any doorway, must still be
            // refused, naming both — proof the rest of Cover() (the overlap check) is untouched.
            MapData overlapping = Map(new[]
            {
                new MapEntity { id = "overlapA", kind = "cover", x = 5f, z = -15f, width = 2f, height = 1.5f, depth = 2f },
                new MapEntity { id = "overlapB", kind = "cover", x = 5f, z = -15f, width = 2f, height = 1.5f, depth = 2f },
            });
            Assert.IsFalse(MapValidation.Validate(overlapping, out string overlapWhy));
            StringAssert.Contains("overlapA", overlapWhy);
            StringAssert.Contains("overlapB", overlapWhy);
        }
    }
}
