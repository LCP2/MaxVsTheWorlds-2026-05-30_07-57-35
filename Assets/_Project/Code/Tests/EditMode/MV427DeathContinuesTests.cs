using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Pure-logic coverage for MV-427 ("death continues the run"): the respawn/restore decision
    /// (<see cref="RespawnPlanner"/>), the once-ever award guard (<see cref="DeathRunState"/>), the
    /// per-area queue-drop used when an arena resets (<see cref="AreaSpawnQueue.RemoveQueued"/>), and
    /// the one-shot health primitive a destroyed shed's persistence relies on
    /// (<see cref="DestructibleHealth"/>).
    ///
    /// The live scene wiring (WorldRunner's actual teleport/gate-reclose/area-restore orchestration,
    /// AreaGate.Reclose's hinge/collider state, RobotEnemy.Despawn) is PlayMode-shaped and is
    /// deliberately NOT covered here — this worker never authors or runs PlayMode tests (Unity
    /// PlayMode batch mode hangs indefinitely; see CC_AUTONOMY.md). See the MV-427 fix comment for
    /// what still needs a human play-check.
    /// </summary>
    public sealed class MV427DeathContinuesTests
    {
        [TearDown]
        public void TearDown() => DeathRunState.Reset();

        // ------------------------------------------------------------ RespawnPlanner

        [Test]
        public void OrdinaryMidRunArea_RespawnsOneAreaBack_AndReclosesItsGate()
        {
            RespawnPlan plan = RespawnPlanner.Resolve(deathAreaIndex: 6, areaCount: 18);

            Assert.That(plan.RespawnAreaIndex, Is.EqualTo(5));
            Assert.That(plan.RestoreAreaIndex, Is.EqualTo(6));
            Assert.That(plan.RecloseGate, Is.True);
        }

        [Test]
        public void DeathInArea1_FallsBackToTheEntryStub_AndStillReclosesItsGate()
        {
            RespawnPlan plan = RespawnPlanner.Resolve(deathAreaIndex: 1, areaCount: 18);

            Assert.That(plan.RespawnAreaIndex, Is.EqualTo(0), "no previous arena — the entry stub");
            Assert.That(plan.RestoreAreaIndex, Is.EqualTo(1));
            Assert.That(plan.RecloseGate, Is.True);
        }

        [Test]
        public void DeathInTheBossRoom_RespawnsInTheLastNormalArea_AndNeverReclosesTheBossGate()
        {
            RespawnPlan plan = RespawnPlanner.Resolve(deathAreaIndex: 19, areaCount: 18);

            Assert.That(plan.RespawnAreaIndex, Is.EqualTo(18));
            Assert.That(plan.RestoreAreaIndex, Is.EqualTo(19));
            Assert.That(plan.RecloseGate, Is.False,
                "the boss gate opens on a condition, not combat — re-closing it would softlock the run");
        }

        // ------------------------------------------------------------ DeathRunState

        [Test]
        public void AnAreasPart_IsGrantedAtMostOnce_EvenAcrossARestore()
        {
            Assert.That(DeathRunState.TryGrantAreaPart(6), Is.True, "the first grant for this area must succeed");
            Assert.That(DeathRunState.TryGrantAreaPart(6), Is.False,
                "a second grant for the same area — e.g. after the area's robots are wiped and " +
                "respawned by a death — must never succeed, or suicide-farming becomes optimal");
            Assert.That(DeathRunState.HasGrantedAreaPart(6), Is.True);
        }

        [Test]
        public void DifferentAreas_GrantIndependently()
        {
            Assert.That(DeathRunState.TryGrantAreaPart(3), Is.True);
            Assert.That(DeathRunState.TryGrantAreaPart(4), Is.True, "a different area's part is unrelated");
        }

        [Test]
        public void RecordDeath_IncrementsAndFiresChanged()
        {
            int? lastReported = null;
            void OnChanged(int v) => lastReported = v;

            DeathRunState.DeathsChanged += OnChanged;
            try
            {
                Assert.That(DeathRunState.DeathsTaken, Is.EqualTo(0));
                DeathRunState.RecordDeath();
                DeathRunState.RecordDeath();
                Assert.That(DeathRunState.DeathsTaken, Is.EqualTo(2));
                Assert.That(lastReported, Is.EqualTo(2));
            }
            finally
            {
                DeathRunState.DeathsChanged -= OnChanged;
            }
        }

        [Test]
        public void Reset_ClearsGrantedAreasAndTheDeathCount()
        {
            DeathRunState.TryGrantAreaPart(2);
            DeathRunState.RecordDeath();

            DeathRunState.Reset();

            Assert.That(DeathRunState.HasGrantedAreaPart(2), Is.False, "a fresh run must not inherit a past run's granted areas");
            Assert.That(DeathRunState.DeathsTaken, Is.EqualTo(0));
            Assert.That(DeathRunState.TryGrantAreaPart(2), Is.True, "area 2's part must be grantable again on a fresh run");
        }

        // ------------------------------------------------------------ AreaSpawnQueue.RemoveQueued

        [Test]
        public void RemoveQueued_DropsOnlyTheNamedAreasBacklog()
        {
            var queue = new AreaSpawnQueue(maxActive: 0); // nothing releases; everything stays queued
            queue.Fill(largeCount: 2, smallCount: 0, areaIndex: 3);
            queue.Fill(largeCount: 2, smallCount: 0, areaIndex: 4);
            Assert.That(queue.QueuedCount, Is.EqualTo(4));

            queue.RemoveQueued(3);

            Assert.That(queue.QueuedCount, Is.EqualTo(2), "only area 3's backlog should have been dropped");
        }

        [Test]
        public void RemoveQueued_LeavesActiveCountUntouched()
        {
            var queue = new AreaSpawnQueue(maxActive: 5);
            queue.Fill(largeCount: 0, smallCount: 3, areaIndex: 1);
            queue.TryRelease(out _);
            queue.TryRelease(out _);

            queue.RemoveQueued(1);

            Assert.That(queue.ActiveCount, Is.EqualTo(2),
                "RemoveQueued only drops the not-yet-released backlog; active robots are the caller's own job");
        }

        // ------------------------------------------------------------ DestructibleHealth (what a
        // destroyed shed's persistence through death relies on)

        [Test]
        public void OnceDestroyed_HealthNeverRevives()
        {
            var health = new DestructibleHealth(100f);
            Assert.That(health.TakeDamage(100f), Is.True, "the killing hit should report true");
            Assert.That(health.IsAlive, Is.False);

            health.Heal(999f);
            Assert.That(health.IsAlive, Is.False, "a destroyed structure must never revive — a shed stays destroyed through death (MV-427)");
            Assert.That(health.Current, Is.EqualTo(0f));

            bool destroyedFiredAgain = false;
            health.Destroyed += () => destroyedFiredAgain = true;
            health.TakeDamage(1f);
            Assert.That(destroyedFiredAgain, Is.False, "TakeDamage on an already-dead structure must be a no-op");
        }
    }
}
