using NUnit.Framework;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Unit tests for the concurrent-cap spawn queue (v0.5 recut spec §2, MV-223) — the anti-flood
    /// production rule that keeps a big area population from dumping onto the field all at once.
    /// </summary>
    public sealed class AreaSpawnQueueTests
    {
        [Test]
        public void ReleaseRefusesPastTheConcurrentCap()
        {
            var queue = new AreaSpawnQueue(maxActive: 2);
            queue.Fill(largeCount: 3, smallCount: 3);

            Assert.IsTrue(queue.TryRelease(out _));
            Assert.IsTrue(queue.TryRelease(out _));
            Assert.IsFalse(queue.TryRelease(out _), "already at the cap of 2");
            Assert.AreEqual(2, queue.ActiveCount);
            Assert.AreEqual(4, queue.QueuedCount);
        }

        [Test]
        public void DestroyingAnActiveRobotFreesASlotForTheNextRelease()
        {
            var queue = new AreaSpawnQueue(maxActive: 1);
            queue.Fill(largeCount: 0, smallCount: 2);

            Assert.IsTrue(queue.TryRelease(out _));
            Assert.IsFalse(queue.TryRelease(out _), "still at the cap of 1");

            queue.ReportDestroyed();

            Assert.IsTrue(queue.TryRelease(out _), "a slot freed up when the active robot died");
            Assert.AreEqual(1, queue.ActiveCount);
            Assert.AreEqual(0, queue.QueuedCount);
        }

        [Test]
        public void ReleaseFailsOnceTheQueueIsEmptyEvenWithRoomUnderTheCap()
        {
            var queue = new AreaSpawnQueue(maxActive: 10);
            queue.Fill(largeCount: 1, smallCount: 0);

            Assert.IsTrue(queue.TryRelease(out _));
            Assert.IsFalse(queue.TryRelease(out _), "nothing left queued");
        }

        [Test]
        public void FillQueuesExactlyTheRequestedCounts()
        {
            var queue = new AreaSpawnQueue(maxActive: 100);
            queue.Fill(largeCount: 7, smallCount: 3);

            Assert.AreEqual(10, queue.TotalRemaining);

            int large = 0, small = 0;
            while (queue.TryRelease(out EnemyKind kind))
            {
                if (kind == EnemyKind.Bruiser) large++;
                else small++;
            }

            Assert.AreEqual(7, large);
            Assert.AreEqual(3, small);
        }

        [Test]
        public void FillInterleavesLargeAndSmallRatherThanBlockingThemTogether()
        {
            var queue = new AreaSpawnQueue(maxActive: 100);
            queue.Fill(largeCount: 4, smallCount: 4);

            // An even 1:1 split releases as a perfect alternation.
            EnemyKind[] expected =
            {
                EnemyKind.Bruiser, EnemyKind.Rusher, EnemyKind.Bruiser, EnemyKind.Rusher,
                EnemyKind.Bruiser, EnemyKind.Rusher, EnemyKind.Bruiser, EnemyKind.Rusher,
            };

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.IsTrue(queue.TryRelease(out EnemyKind kind));
                Assert.AreEqual(expected[i], kind, $"release #{i}");
            }
        }

        [Test]
        public void ClearDropsBothQueuedAndActiveRobots()
        {
            var queue = new AreaSpawnQueue(maxActive: 5);
            queue.Fill(largeCount: 2, smallCount: 2);
            queue.TryRelease(out _);

            queue.Clear();

            Assert.AreEqual(0, queue.ActiveCount);
            Assert.AreEqual(0, queue.QueuedCount);
            Assert.IsFalse(queue.TryRelease(out _));
        }

        [Test]
        public void MaxActiveIsNeverLessThanOne()
        {
            var queue = new AreaSpawnQueue(maxActive: 0);

            Assert.AreEqual(1, queue.MaxActive);
        }

        // --- FillForArea (v0.5 recut spec §2-3, MV-224) -----------------------------------------

        [Test]
        public void FillForArea_BeforeIntroAreas_QueuesOnlyBruiserForLargeSlots()
        {
            var queue = new AreaSpawnQueue(maxActive: 100);
            queue.FillForArea(areaIndex: 1, largeCount: 8, smallCount: 0,
                heavyIntroArea: 5f, bruteIntroArea: 8f, toughSubstitutionPct: 25f);

            int bruiser = 0;
            while (queue.TryRelease(out EnemyKind kind))
            {
                Assert.AreEqual(EnemyKind.Bruiser, kind);
                bruiser++;
            }

            Assert.AreEqual(8, bruiser);
        }

        [Test]
        public void FillForArea_PastBothIntroAreas_QueuesTheSubstitutedTiers()
        {
            var queue = new AreaSpawnQueue(maxActive: 100);
            queue.FillForArea(areaIndex: 8, largeCount: 12, smallCount: 0,
                heavyIntroArea: 5f, bruteIntroArea: 8f, toughSubstitutionPct: 25f);

            int bruiser = 0, heavy = 0, brute = 0;
            while (queue.TryRelease(out EnemyKind kind))
            {
                if (kind == EnemyKind.Bruiser) bruiser++;
                else if (kind == EnemyKind.Heavy) heavy++;
                else if (kind == EnemyKind.Brute) brute++;
            }

            Assert.AreEqual(6, bruiser);
            Assert.AreEqual(3, heavy);
            Assert.AreEqual(3, brute);
        }

        [Test]
        public void FillForArea_StillInterleavesAgainstSmallSlots()
        {
            var queue = new AreaSpawnQueue(maxActive: 100);
            queue.FillForArea(areaIndex: 8, largeCount: 4, smallCount: 4,
                heavyIntroArea: 5f, bruteIntroArea: 8f, toughSubstitutionPct: 25f);

            Assert.AreEqual(8, queue.TotalRemaining);

            int small = 0, large = 0;
            while (queue.TryRelease(out EnemyKind kind))
            {
                if (kind == EnemyKind.Rusher) small++;
                else large++;
            }

            Assert.AreEqual(4, small);
            Assert.AreEqual(4, large, "the tough tiers still count as large slots for the interleave");
        }

        // --- FillExact (MV-268's difficulty engine) — MV-310: Gunner/Bomber/Blinker must actually ---
        // --- reach the arena population queue, not just the factory-production mix. -----------------

        [Test]
        public void FillExact_QueuesTheRangedAndTeleportKindsToo()
        {
            var queue = new AreaSpawnQueue(maxActive: 100);
            var composition = new DifficultyEngine.Composition(
                rusher: 2, bruiser: 1, heavy: 0, brute: 0, gunner: 1, bomber: 1, blinker: 1);
            queue.FillExact(composition);

            Assert.AreEqual(6, queue.TotalRemaining);

            int gunner = 0, bomber = 0, blinker = 0;
            while (queue.TryRelease(out EnemyKind kind))
            {
                if (kind == EnemyKind.Gunner) gunner++;
                else if (kind == EnemyKind.Bomber) bomber++;
                else if (kind == EnemyKind.Blinker) blinker++;
            }

            Assert.AreEqual(1, gunner);
            Assert.AreEqual(1, bomber);
            Assert.AreEqual(1, blinker);
        }
    }
}
