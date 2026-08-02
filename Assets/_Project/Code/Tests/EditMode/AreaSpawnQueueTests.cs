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
    }
}
