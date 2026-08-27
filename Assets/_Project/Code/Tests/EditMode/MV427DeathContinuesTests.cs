using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Pure-logic coverage for MV-427 ("death continues the run"): the respawn/restore decision
    /// (<see cref="RespawnPlanner"/>, table-driven over the real world config since MV-575), the
    /// once-ever award guard (<see cref="DeathRunState"/>), the
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

        // ------------------------------------------------------------ RespawnPlanner (MV-575)

        /// <summary>MV-575: dying to ANY of World 1's three bosses (areas 12, 20, 30) used to respawn
        /// at area 30 regardless of which boss it was — <c>ResolveDeathArea</c>/<c>RespawnPlanner</c>
        /// both assumed a single boss room synthesized at <c>areaCount + 1</c> (31), an assumption the
        /// real shipped config broke the moment a boss became a real numbered area. Table-driven over
        /// the actual <c>world1_config.json</c> so this can never again drift from what ships: every
        /// area death — ordinary or boss — must fall back exactly one area and restore the area
        /// actually died in (AC1/AC2), RecloseGate must be false for every boss area and true for every
        /// ordinary one (AC3), no plan value may ever land outside 0..areaCount (AC4), a boss death
        /// must resolve a real <see cref="WorldArea"/> for the overlay to name (AC5), and every
        /// expectation is read from the config's own <c>IsBossRole</c>/<c>index</c>, never a hardcoded
        /// 12/20/30/31 (AC6).</summary>
        [Test]
        public void DeathInAnyWorld1Area_FallsBackOneArea_AndReclosesOnlyNonBossGates()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "the shipped world1_config.json failed to load — see the error log above");

            int areaCount = cfg.dials.areaCount;
            int bossAreasChecked = 0;

            for (int deathArea = 1; deathArea <= areaCount; deathArea++)
            {
                WorldArea area = cfg.AreaByIndex(deathArea);
                Assert.IsNotNull(area, $"world1_config.json has no authored area at index {deathArea}");

                bool isBoss = area.IsBossRole;
                if (isBoss) bossAreasChecked++;

                RespawnPlan plan = RespawnPlanner.Resolve(deathArea, deathGateIsConditionGated: isBoss);

                Assert.That(plan.RestoreAreaIndex, Is.EqualTo(deathArea),
                    $"area {deathArea}: must always restore the area actually died in");
                Assert.That(plan.RespawnAreaIndex, Is.EqualTo(Mathf.Max(0, deathArea - 1)),
                    $"area {deathArea}: must always fall back exactly one area (0 for area 1's entry stub)");
                Assert.That(plan.RecloseGate, Is.EqualTo(!isBoss),
                    $"area {deathArea} ('{area.name}', role='{area.role}'): RecloseGate must be false for " +
                    "every boss area (its gate opens on a shed condition, not combat — re-closing it would " +
                    "softlock the run) and true for every ordinary one");

                Assert.That(plan.RespawnAreaIndex, Is.InRange(0, areaCount), $"area {deathArea}: respawn index out of range");
                Assert.That(plan.RestoreAreaIndex, Is.InRange(0, areaCount), $"area {deathArea}: restore index out of range");

                if (isBoss)
                {
                    WorldArea restoreArea = cfg.AreaByIndex(plan.RestoreAreaIndex);
                    Assert.IsNotNull(restoreArea,
                        $"AC5: a boss death at area {deathArea} must resolve a real WorldArea for the death " +
                        "overlay to name — never fall back to the 'Area {N}' placeholder");
                }
            }

            Assert.That(bossAreasChecked, Is.GreaterThan(0),
                "world1_config.json must author at least one boss area for this test to mean anything");
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
