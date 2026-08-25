using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Shed roadmap stage 1 (MV-541): an area's sheds go from "one, maybe" to "however many are
    /// authored". Two subsystems still assumed one-per-area before this ticket even though MV-475
    /// already gave <see cref="WorldArea"/> a resolved <see cref="WorldArea.Sheds"/> list — both read
    /// the legacy single <see cref="WorldArea.shed"/> field directly instead: the loader's authored
    /// body size (fixed here, MV-541 change 2) and <see cref="Garrison"/>'s spawn-ring dodge (MV-541
    /// change 1). This one test pins both fixes against the one property every multi-shed area must
    /// have: EVERY authored shed is real, sized correctly, and dodged — not just the first one.
    /// </summary>
    public sealed class MV541MultiShedTests
    {
        /// <summary>A minimal three-area world (entry stub / shed area / boss) whose shed area
        /// carries two sheds, spaced to clear both <see cref="MapValidation.MinShedWallMargin"/> and
        /// <see cref="MapValidation.MinShedSeparation"/> — proving a real, validating multi-shed world,
        /// not just an isolated area object.</summary>
        private static WorldConfig TwoShedWorld() => new WorldConfig
        {
            world = "Test World",
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", role = "entry",
                    origin = new WorldAreaOrigin { x = -2f, z = -6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", role = "shed", hasShed = true,
                    origin = new WorldAreaOrigin { x = -15f, z = 0f },
                    size = new WorldAreaSize { w = 30f, d = 30f },
                    sheds = new[]
                    {
                        new WorldShed { x = -6f, z = 10f },
                        new WorldShed { x = 6f, z = 20f },
                    },
                },
                new WorldArea
                {
                    id = "boss", role = "boss+exit",
                    origin = new WorldAreaOrigin { x = -15f, z = 30f },
                    size = new WorldAreaSize { w = 30f, d = 20f },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                    from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                },
            },
        };

        [Test]
        public void TwoShedsInOneArea_LoadAtTheReducedSize_AndAreBothDodgedByGarrison()
        {
            // --- AC1 + AC3: the loader builds one factory entity per authored shed, each at 0.75x
            // the pre-541 authored body size (was 3x2x3; MapValidation must accept both without error).
            Assert.IsTrue(WorldMapLoader.TryLoad(TwoShedWorld(), out MapData map, out string reason), reason);

            List<MapEntity> sheds = MapValidation.Kind(map, EntityKind.Factory);
            Assert.AreEqual(2, sheds.Count, "both authored sheds should register as factory/spawn-source entities");

            MapEntity shed1 = map.Entity("a1_shed1");
            MapEntity shed2 = map.Entity("a1_shed2");
            Assert.IsNotNull(shed1, "the first authored shed did not round-trip into the map");
            Assert.IsNotNull(shed2, "the second authored shed did not round-trip into the map");

            foreach (MapEntity shed in new[] { shed1, shed2 })
            {
                Assert.AreEqual(2.25f, shed.width, 1e-3f, $"'{shed.id}' width should be 0.75x the pre-541 3 m body");
                Assert.AreEqual(1.5f, shed.height, 1e-3f, $"'{shed.id}' height should be 0.75x the pre-541 2 m body");
                Assert.AreEqual(2.25f, shed.depth, 1e-3f, $"'{shed.id}' depth should be 0.75x the pre-541 3 m body");
            }

            // --- Garrison/spawn-queue wiring: an area's garrison ring must dodge EVERY shed it
            // carries, not just WorldArea.shed (the legacy single field, left null here on purpose —
            // this area only authors WorldArea.sheds, so a dodge that still reads .shed sees nothing
            // and a seed lands dead-centre on a shed).
            var dodgeArea = new WorldArea
            {
                id = "dodge-fixture", index = 1, role = "normal", garrisonDensity = "light",
                origin = new WorldAreaOrigin { x = 0f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                hasShed = true,
                sheds = new[]
                {
                    new WorldShed { x = 10f, z = 16f }, // dead on seed#1's ring angle (90 deg)
                    new WorldShed { x = 10f, z = 4f },  // dead on seed#3's ring angle (270 deg)
                },
            };

            Vector3[] seeds = Garrison.SeedPositions(dodgeArea, 4);
            float needed = MapValidation.SpawnRadius + MapValidation.SpawnClearance;

            foreach (WorldShed shed in dodgeArea.Sheds())
            {
                var shedXz = new Vector2(shed.x, shed.z);
                foreach (Vector3 p in seeds)
                {
                    float toShed = Vector2.Distance(new Vector2(p.x, p.z), shedXz);
                    Assert.GreaterOrEqual(toShed, needed,
                        $"seed ({p.x}, {p.z}) is {toShed:0.###} m from shed ({shed.x}, {shed.z}) — needs {needed} m");
                }
            }
        }
    }
}
