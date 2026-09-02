using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-655 — the flat 1 m <see cref="MapValidation.MinGarrisonCoverGap"/> rejected a Bolter
    /// (0.4 m collider radius) drawn dead-centre in a 3x3 hedge pen, because a 1 m design-grid cell
    /// sits only 0.5 m from an adjacent hedge face. The gap is now per-robot — collider radius + 0.1 m
    /// (0.5 m for a Bolter) — so the centred placement passes and only a genuine too-close placement
    /// still fails, quoting the actual and required gap.
    /// </summary>
    public sealed class MV655GarrisonCoverGapTests
    {
        // Four 1x1 m hedge blocks forming a ring round (15, 15) — a 3x3 m pen with a 1x1 m interior,
        // so the centre cell sits exactly 0.5 m clear of every face.
        private static WorldArea PenArea(float entryX, float entryZ) => new WorldArea
        {
            id = "a1", index = 1, role = "normal",
            origin = new WorldAreaOrigin { x = 0f, z = 0f },
            size = new WorldAreaSize { w = 30f, d = 30f },
            composition = new WorldComposition { bolter = 1 },
            garrison = new[] { new WorldGarrisonEntry { kind = "bolter", x = entryX, z = entryZ } },
            cover = new[]
            {
                new WorldCover { id = "hedge_n", x = 15f, z = 16f, width = 1f, height = 1f, depth = 1f },
                new WorldCover { id = "hedge_s", x = 15f, z = 14f, width = 1f, height = 1f, depth = 1f },
                new WorldCover { id = "hedge_e", x = 16f, z = 15f, width = 1f, height = 1f, depth = 1f },
                new WorldCover { id = "hedge_w", x = 14f, z = 15f, width = 1f, height = 1f, depth = 1f },
            },
        };

        private static WorldConfig OneAreaWorld(WorldArea area) => new WorldConfig
        {
            world = "Test World",
            dials = new WorldDials { areaCount = 1, baseThreat = 1f, threatGrowth = 0f, pacingRhythm = new[] { 1f } },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", role = "entry",
                    origin = new WorldAreaOrigin { x = 13f, z = -6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                area,
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
            },
        };

        [Test]
        public void BolterAtPenCentre_ClearsThePerRobotGap_ButNudgedTowardAFaceFails()
        {
            Assert.IsTrue(
                MapValidation.ValidateWorldConfig(OneAreaWorld(PenArea(15f, 15f)), out string centredReason),
                centredReason);

            Assert.IsFalse(
                MapValidation.ValidateWorldConfig(OneAreaWorld(PenArea(15f, 15.2f)), out string nudgedReason));
            Assert.IsTrue(nudgedReason.Contains("0.3") && nudgedReason.Contains("0.5"),
                $"expected the failure to quote the actual 0.3 m gap and the required 0.5 m, got: {nudgedReason}");
        }
    }
}
