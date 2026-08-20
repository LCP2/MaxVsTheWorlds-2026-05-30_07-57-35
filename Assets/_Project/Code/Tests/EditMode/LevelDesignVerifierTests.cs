using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-487: the level-design constraints (2 m robot-to-cover gap, 11 m shed separation, 18 m minimum
    /// room dimension, the E/W gate-axis-alternation principle) used to be enforced only by a throwaway
    /// verifier hand-coded into a chat session. This pins <see cref="LevelDesignVerifier"/> against a
    /// single fixture that deliberately breaks all four constraints at once, proving the verifier reports
    /// every violation in one pass (AC2) rather than stopping at the first the way
    /// <see cref="MapValidation.Cover"/> does.
    /// </summary>
    public sealed class LevelDesignVerifierTests
    {
        [Test]
        public void Violations_ReportsEveryBrokenConstraint_NotJustTheFirst()
        {
            var constraints = new LevelDesignConstraints
            {
                minRobotToCoverGapMetres = 2f,
                minShedSeparationMetres = 11f,
                minRoomDimensionMetres = 18f,
                gateAxisAlternation = new GateAxisAlternationConstraint { principle = "test fixture" },
            };

            // a1: a 10x10 room (breaks the 18 m minimum) with a single authored robot and a cover box
            // that blankets its entire garrison ring, so wherever Garrison.SeedPositions lands its one
            // seed, it is guaranteed to sit inside the cover (breaks the 2 m robot-to-cover gap).
            var a1 = new WorldArea
            {
                id = "a1",
                index = 1,
                role = "normal",
                origin = new WorldAreaOrigin { x = 0, z = 10 },
                size = new WorldAreaSize { w = 10, d = 10 },
                hasShed = false,
                garrisonDensity = "heavy",
                composition = new WorldComposition { bruiser = 1 },
                cover = new[]
                {
                    new WorldCover { id = "a1_blanket", x = 5, z = 15, width = 10, height = 1.8f, depth = 10 },
                },
            };

            // a2 and a3: two sheds 5 m apart (breaks the 11 m minimum separation).
            var a2 = new WorldArea
            {
                id = "a2",
                index = 2,
                role = "shed",
                origin = new WorldAreaOrigin { x = 20, z = 10 },
                size = new WorldAreaSize { w = 20, d = 20 },
                hasShed = true,
                shed = new WorldShed { x = 30, z = 20 },
                garrisonDensity = "none",
                composition = new WorldComposition { rusher = 1 },
            };
            var a3 = new WorldArea
            {
                id = "a3",
                index = 3,
                role = "shed",
                origin = new WorldAreaOrigin { x = 20, z = 40 },
                size = new WorldAreaSize { w = 20, d = 20 },
                hasShed = true,
                shed = new WorldShed { x = 34, z = 23 },
                garrisonDensity = "none",
                composition = new WorldComposition { rusher = 1 },
            };

            var stub = new WorldArea
            {
                id = "stub",
                index = 0,
                role = "entry",
                origin = new WorldAreaOrigin { x = -4, z = 0 },
                size = new WorldAreaSize { w = 4, d = 4 },
                garrisonDensity = "none",
            };

            // Gates in authored order: stub->a1 (vertical), a1->a2 (vertical again — breaks the
            // alternation principle), a2->a3 (horizontal — does not).
            var gates = new[]
            {
                new WorldGate
                {
                    id = "g0",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                    width = 3,
                },
                new WorldGate
                {
                    id = "g1",
                    from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a2", wall = "S", pos = 0.5f },
                    width = 3,
                },
                new WorldGate
                {
                    id = "g2",
                    from = new WorldGateEndpoint { area = "a2", wall = "E", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a3", wall = "W", pos = 0.5f },
                    width = 3,
                },
            };

            var cfg = new WorldConfig
            {
                areas = new[] { stub, a1, a2, a3 },
                gates = gates,
                dials = new WorldDials
                {
                    areaCount = 3,
                    baseThreat = 1f,
                    threatGrowth = 0f,
                    band = new WorldBand { up = 0f, down = 0f },
                    pacingRhythm = new[] { 1f },
                    toughnessCurve = new WorldToughnessCurve(),
                    powerupCadence = 1,
                },
            };

            List<string> violations = LevelDesignVerifier.Violations(cfg, constraints);

            Assert.AreEqual(4, violations.Count,
                "expected exactly one violation per broken constraint (room dimension, shed separation, " +
                "robot-to-cover gap, gate-axis alternation) - a different count means either a real " +
                "violation was missed or the verifier stopped short, quoting: " + string.Join(" | ", violations));

            Assert.IsTrue(violations.Any(v => v.Contains("a1") && v.Contains("18")),
                "expected a room-dimension violation naming area a1 and the 18 m minimum");
            Assert.IsTrue(violations.Any(v => v.Contains("a2") && v.Contains("a3")),
                "expected a shed-separation violation naming both a2 and a3");
            Assert.IsTrue(violations.Any(v => v.Contains("a1") && v.Contains("cover")),
                "expected a robot-to-cover-gap violation naming area a1's cover");
            Assert.IsTrue(violations.Any(v => v.Contains("g1")),
                "expected a gate-axis-alternation violation naming gate g1, which repeats g0's N/S axis");
        }
    }
}
