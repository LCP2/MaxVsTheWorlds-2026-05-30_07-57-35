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

        // --- Per-area cap (MV-417) — the cap used to be shared field-wide, so a robot alive in one -----
        // --- area could block release into a different area entirely; it is now checked per-area. ------

        [Test]
        public void AnAreaAtItsOwnCapNeverBlocksAnotherAreasRelease()
        {
            var queue = new AreaSpawnQueue(maxActive: 1);
            queue.Fill(largeCount: 0, smallCount: 2, areaIndex: 1);
            queue.Fill(largeCount: 0, smallCount: 2, areaIndex: 2);

            Assert.IsTrue(queue.TryRelease(out int area1, out _));
            Assert.AreEqual(1, area1, "area 1's own release fills its 1-robot cap");

            Assert.IsTrue(queue.TryRelease(out int area2, out _),
                "area 2 must release its own robot even though area 1 is already at its cap - " +
                "before MV-417 a single shared cap would have blocked this");
            Assert.AreEqual(2, area2);

            Assert.IsFalse(queue.TryRelease(out _, out _), "both areas are now at their own 1-robot cap");
            Assert.AreEqual(2, queue.ActiveCount);
            Assert.AreEqual(1, queue.ActiveCountForArea(1));
            Assert.AreEqual(1, queue.ActiveCountForArea(2));
        }

        [Test]
        public void TryReleaseArea_TargetsOneAreaOnly_EvenOutOfFifoOrder()
        {
            var queue = new AreaSpawnQueue(maxActive: 5);
            queue.Fill(largeCount: 0, smallCount: 1, areaIndex: 1);
            queue.Fill(largeCount: 0, smallCount: 1, areaIndex: 2);

            Assert.IsTrue(queue.TryReleaseArea(2, out _), "must find area 2's entry despite area 1 sitting at the FIFO front");
            Assert.AreEqual(1, queue.ActiveCountForArea(2));
            Assert.AreEqual(0, queue.ActiveCountForArea(1));
            Assert.AreEqual(1, queue.QueuedCount, "area 1's entry must still be queued, untouched");

            Assert.IsFalse(queue.TryReleaseArea(2, out _), "nothing left queued for area 2");
        }

        [Test]
        public void TryTakeForGarrison_IgnoresTheCapEntirely()
        {
            var queue = new AreaSpawnQueue(maxActive: 1);
            queue.Fill(largeCount: 0, smallCount: 3, areaIndex: 1);

            Assert.IsTrue(queue.TryTakeForGarrison(1, out _));
            Assert.IsTrue(queue.TryTakeForGarrison(1, out _));
            Assert.IsTrue(queue.TryTakeForGarrison(1, out _));
            Assert.IsFalse(queue.TryTakeForGarrison(1, out _), "nothing left to take");

            Assert.AreEqual(3, queue.ActiveCount, "all 3 taken despite a cap of 1 - garrison bypasses it entirely");
            Assert.AreEqual(0, queue.QueuedCount);
        }

        [Test]
        public void Requeue_PutsAnEntryBackAndFreesItsAreasCapSlot()
        {
            var queue = new AreaSpawnQueue(maxActive: 1);
            queue.Fill(largeCount: 0, smallCount: 1, areaIndex: 1);

            Assert.IsTrue(queue.TryRelease(out EnemyKind kind));
            Assert.AreEqual(1, queue.ActiveCountForArea(1));
            Assert.AreEqual(0, queue.QueuedCount);

            queue.Requeue(1, kind);

            Assert.AreEqual(0, queue.ActiveCountForArea(1), "the cap slot must free up again");
            Assert.AreEqual(1, queue.QueuedCount, "the entry must be back on the queue, not lost");
            Assert.IsTrue(queue.TryRelease(out _), "it must be releasable again");
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
