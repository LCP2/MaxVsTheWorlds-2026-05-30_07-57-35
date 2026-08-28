using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-601 — before this ticket, <see cref="Garrison.SeedSlots"/> truncated
    /// <see cref="WorldArea.garrison"/> to the first <c>count</c> entries, where <c>count</c> is
    /// <see cref="Garrison.SeedCount"/> capped by <see cref="Garrison.DensityShare"/> at 0.85. That
    /// meant no density dial could ever place every authored entry: in a3, 9 authored entries against
    /// a normal-density count of 5 saw the other 4 silently become random arrivals — the live "bolters
    /// wandering" bug. This proves every authored entry now lands, even past <c>count</c>.
    /// </summary>
    public sealed class MV601GarrisonSeedSlotsTests
    {
        [Test]
        public void SeedSlots_PlacesEveryAuthoredEntry_EvenBeyondSeedCount()
        {
            // 9 authored entries, count 5 (what density 'normal' on a 9-robot area yields) -> all 9
            // authored entries must be placed, at their authored positions and kinds; none dropped.
            var area = new WorldArea
            {
                id = "a3", index = 3,
                origin = new WorldAreaOrigin { x = 0f, z = 0f }, size = new WorldAreaSize { w = 20f, d = 20f },
                garrison = new[]
                {
                    new WorldGarrisonEntry { kind = "bolter", x = 1f, z = 1f },
                    new WorldGarrisonEntry { kind = "bolter", x = 2f, z = 1f },
                    new WorldGarrisonEntry { kind = "bolter", x = 3f, z = 1f },
                    new WorldGarrisonEntry { kind = "bolter", x = 4f, z = 1f },
                    new WorldGarrisonEntry { kind = "bolter", x = 5f, z = 1f },
                    new WorldGarrisonEntry { kind = "bolter", x = 6f, z = 1f },
                    new WorldGarrisonEntry { kind = "bolter", x = 7f, z = 1f },
                    new WorldGarrisonEntry { kind = "bolter", x = 8f, z = 1f },
                    new WorldGarrisonEntry { kind = "bolter", x = 9f, z = 1f },
                },
            };

            Garrison.Seed[] slots = Garrison.SeedSlots(area, count: 5);

            Assert.AreEqual(9, slots.Length, "every authored entry must be placed, even past SeedCount");
            for (int i = 0; i < 9; i++)
            {
                Assert.AreEqual(EnemyKind.Bolter, slots[i].Kind, $"slot {i} must carry its authored kind");
                Assert.AreEqual(i + 1f, slots[i].Position.x, 1e-3f, $"slot {i} must land on its authored x");
                Assert.AreEqual(1f, slots[i].Position.z, 1e-3f, $"slot {i} must land on its authored z");
            }
        }
    }
}
