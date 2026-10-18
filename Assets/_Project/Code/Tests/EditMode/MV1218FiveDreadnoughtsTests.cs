using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1218 — the Reef's five authored Dreadnought garrison slots (a6/a12/a18/a24/a28), added as pure
    /// data once MV-1217 (the kind), MV-1220 (<see cref="WorldComposition.blendWithDial"/>), MV-1221
    /// (<see cref="AreaSpawnQueue.FillExact"/>'s Dreadnought enqueue) and MV-1234
    /// (<see cref="WorldRunner.ResolveShedComposition"/>'s blend) all landed.
    ///
    /// One EditMode test (MV-465 Rule 1), asserting the RESOLVED garrison seed a6 actually produces
    /// through the same production path <see cref="AreaAccumulationDirector"/> itself uses —
    /// <see cref="WorldCatalog.IndexOf"/> → <see cref="WorldLibrary.Load"/> → <see cref="Garrison.SeedCount"/>
    /// + <see cref="Garrison.SeedSlots(WorldArea, int, WorldConfig)"/> — never an authored constant
    /// (Tier 1) and never a rendered pixel (Tier 3).
    /// </summary>
    public sealed class MV1218FiveDreadnoughtsTests
    {
        [Test]
        public void ReefConfig_A6Garrison_ResolvesExactlyOneDreadnoughtAtAuthoredPosition()
        {
            int worldIndex = WorldCatalog.IndexOf(WorldIds.Reef);
            Assert.AreNotEqual(-1, worldIndex, "the Reef must be a registered world");

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.KeyForIndex(worldIndex));
            Assert.IsNotNull(cfg, "world3_config.json must load cleanly through the production loader");

            WorldArea a6 = cfg.Area("a6");
            Assert.IsNotNull(a6, "area a6 must exist in the Reef");

            int seedCount = Garrison.SeedCount(a6.index, cfg);
            Garrison.Seed[] slots = Garrison.SeedSlots(a6, seedCount, cfg);

            int dreadnoughtCount = 0;
            Vector3 dreadnoughtPos = default;
            foreach (Garrison.Seed slot in slots)
            {
                if (slot.Kind != EnemyKind.Dreadnought) continue;
                dreadnoughtCount++;
                dreadnoughtPos = slot.Position;
            }

            Assert.AreEqual(1, dreadnoughtCount, "a6 must resolve exactly one Dreadnought garrison entry");
            Assert.AreEqual(152.5f, dreadnoughtPos.x, 0.01f, "the authored Dreadnought's resolved x");
            Assert.AreEqual(22.5f, dreadnoughtPos.z, 0.01f, "the authored Dreadnought's resolved z");
        }
    }
}
