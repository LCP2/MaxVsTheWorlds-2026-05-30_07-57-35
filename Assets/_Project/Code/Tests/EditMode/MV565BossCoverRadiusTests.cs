using System.Collections.Generic;
using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-565: <see cref="MapValidation.Cover"/> used to refuse ANY cover anywhere in a
    /// <see cref="ZoneKind.Boss"/> zone. World 1 v4's boss arenas are full combat rooms (up to 56x44 m,
    /// 45 robots), so that blanket ban meant "no cover in the room the player spends the longest in."
    /// The fix narrows the ban to a clearance round each boss (<see cref="MapValidation.MinBossCoverClearance"/>)
    /// instead of the whole room — this pins the three shapes that clearance test has to handle.
    /// Clearance values below are footprint-to-footprint, not centre-to-centre (MV-649).
    /// </summary>
    public sealed class MV565BossCoverRadiusTests
    {
        /// <summary>An entry room and a big boss arena joined by a doorway. <paramref name="extra"/>
        /// supplies whatever boss/cover entities the case under test needs.</summary>
        private static MapData Map(MapEntity[] extra)
        {
            var entities = new List<MapEntity>
            {
                new MapEntity { id = "start", kind = "playerSpawn", x = 0f, z = -10f },
            };
            entities.AddRange(extra);

            return new MapData
            {
                name = "MV-565 Boss Radius",
                wallHeight = 3f,
                wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "a", type = "entry", x = 0f, z = -10f, width = 20f, depth = 20f },
                    new MapZone { id = "b", type = "boss",  x = 0f, z = 20f,  width = 40f, depth = 40f },
                },
                links = new[] { new MapLink { from = "a", to = "b", doorway = 4f } },
                entities = entities.ToArray(),
            };
        }

        [Test]
        public void Validation_ScopesTheBossCoverBanToARadiusRoundEachBoss()
        {
            // --- AC2: cover clear of the boss's footprint (13.5 m past its edge here) validates.
            MapData clear = Map(new[]
            {
                new MapEntity { id = "boss1", kind = "boss", x = 0f, z = 20f },
                new MapEntity { id = "farCover", kind = "cover", x = 0f, z = 35f, width = 2f, height = 2f, depth = 2f },
            });
            Assert.IsTrue(MapValidation.Validate(clear, out string clearWhy), clearWhy);

            // --- AC3: cover 0.5 m from the boss's footprint fails, naming the cover and the boss it
            // crowds (boss defaults to a 1x1 m footprint here; closeCover's own footprint reaches
            // x=1..3, 0.5 m clear of the boss's x=0.5 edge — inside the 2 m minimum).
            MapData close = Map(new[]
            {
                new MapEntity { id = "boss1", kind = "boss", x = 0f, z = 20f },
                new MapEntity { id = "closeCover", kind = "cover", x = 2f, z = 20f, width = 2f, height = 2f, depth = 2f },
            });
            Assert.IsFalse(MapValidation.Validate(close, out string closeWhy));
            StringAssert.Contains("closeCover", closeWhy);
            StringAssert.Contains("boss1", closeWhy);

            // --- AC4: a boss zone with no boss entity at all still refuses every piece of cover in it,
            // exactly as before this ticket — a malformed/legacy boss room stays fully protected.
            MapData noBoss = Map(new[]
            {
                new MapEntity { id = "orphanCover", kind = "cover", x = 0f, z = 35f, width = 2f, height = 2f, depth = 2f },
            });
            Assert.IsFalse(MapValidation.Validate(noBoss, out string noBossWhy));
            StringAssert.Contains("readability-first", noBossWhy);
        }

        /// <summary>MV-649: clearance is measured footprint-to-footprint against the boss's authored
        /// size, not from its centre point — a 6x6 boss must not eat its own clearance radius.</summary>
        [Test]
        public void Validation_MeasuresBossCoverClearanceFromTheBossFootprintNotItsCentre()
        {
            // --- 2.5 m clear of the boss's 6x6 footprint validates. Boss footprint spans z=17..23
            // (6 m box centred on z=20); cover is a 2x2 box centred on z=26.5, so its footprint spans
            // z=25.5..27.5 — 2.5 m clear of the boss's z=23 edge.
            MapData clear = Map(new[]
            {
                new MapEntity { id = "boss1", kind = "boss", x = 0f, z = 20f, width = 6f, depth = 6f },
                new MapEntity { id = "farCover", kind = "cover", x = 0f, z = 26.5f, width = 2f, height = 2f, depth = 2f },
            });
            Assert.IsTrue(MapValidation.Validate(clear, out string clearWhy), clearWhy);

            // --- 1.5 m clear of the same boss's footprint fails, naming the boss. Cover footprint
            // spans z=24.5..26.5 — 1.5 m clear of the boss's z=23 edge.
            MapData close = Map(new[]
            {
                new MapEntity { id = "boss1", kind = "boss", x = 0f, z = 20f, width = 6f, depth = 6f },
                new MapEntity { id = "closeCover", kind = "cover", x = 0f, z = 25.5f, width = 2f, height = 2f, depth = 2f },
            });
            Assert.IsFalse(MapValidation.Validate(close, out string closeWhy));
            StringAssert.Contains("boss1", closeWhy);
        }
    }
}
