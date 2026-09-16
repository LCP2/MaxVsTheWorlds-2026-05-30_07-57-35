using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-817 — a Replicator's doubled twins were emitted through <see cref="EnemySpawner.SpawnExact"/>,
    /// gated on <c>_live.Count &lt; EffectiveMaxLiveEnemies</c> as well as the field-wide budget.
    /// <c>EffectiveMaxLiveEnemies</c> ramps from a Replicator's own <c>startingRobots</c> (0, the
    /// component default <see cref="MaxWorlds.Arena.Map.MapRuntime"/> hands every real box) up to
    /// <c>maxLiveEnemies</c> (8) as <see cref="DifficultyDirector.Normalized"/> climbs — it reads 0 for
    /// roughly the first 6% of a run. A robot fed in during that window was consumed and never replaced:
    /// <c>SpawnExact</c> silently returned an empty list, and the pending emission was dropped outright.
    /// This also let the box shred its own guarantee whenever the global budget happened to close over
    /// the freed slot before the Cycle beat's guaranteed replacement spent its MV-809 reservation.
    ///
    /// Fails on base commit 3255d43: at <see cref="DifficultyDirector.Normalized"/> == 0, the FIRST
    /// assertion below (a twin must emerge within 2 s) fails outright — nothing is ever emitted, because
    /// <c>EffectiveMaxLiveEnemies</c> is 0 and <c>SpawnExact</c> had no way to be told to ignore it.
    /// Quoted in this ticket's fix comment.
    ///
    /// MV-820 update: this file originally covered two angles — the per-factory ramp cap wrongly gating
    /// a Replicator (Part 1, kept below) and the first twin's own guarantee resolving by RETRYING once
    /// room freed rather than emitting immediately (Part 2). MV-820 Change 6 replaced the retry with an
    /// unconditional bypass of the global budget for the guaranteed first twin, so Part 2's own
    /// assertions (the twin must NOT spawn on the first attempt, only once an intruder frees a slot) now
    /// assert exactly the behaviour MV-820 removed — the same shape MV-820's own AC1(e) test covers,
    /// with the opposite (now correct) expectation. Culled per the testing policy's own redundancy rule
    /// rather than kept failing.
    ///
    /// Tier 2 (resolved values): every assertion reads <see cref="EnemySpawner.LiveCountOf"/>,
    /// <see cref="Replicator.Capacity"/>, a resolved out-ramp <see cref="Transform.position"/>, or a
    /// resolved LED <see cref="MaterialPropertyBlock"/> colour — never an authored constant asserted
    /// back at itself.
    /// </summary>
    public sealed class MV817ReplicatorAlwaysGivesBackTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test in this suite
        // uses — EditMode tests share one physics scene for the whole cc-verify run with no per-test
        // reset.
        private static readonly Vector3 RigOriginPart1 = new Vector3(84213f, 0f, -46110f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LiveField =
            typeof(EnemySpawner).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            DifficultyDirector.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            DifficultyDirector.Reset();
        }

        private static RobotEnemy NewLiveRobot(Vector3 position)
        {
            var go = new GameObject("MV817-robot");
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            // OnEnable never runs as a side effect of AddComponent outside Play mode — same quirk every
            // other Replicator/RobotEnemy EditMode test in this suite works around.
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        private static Replicator NewReplicator(Vector3 position, int capacity)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "MV817-replicator";
            go.transform.position = position;
            go.transform.localScale = new Vector3(2f, 1.5f, 2f);
            var replicator = go.AddComponent<Replicator>();
            replicator.Build(); // AddComponent's own Awake never runs outside Play mode
            replicator.Configure(capacity);
            return replicator;
        }

        [Test]
        public void ReplicatorAlwaysGivesBack_AtDifficultyZero()
        {
            LogAssert.ignoreFailingMessages = true; // BuildBody's collider-strip [Error], every Replicator test carries this

            // === Part 1 (root cause): DifficultyDirector at level 0, global count well under budget.
            // The Replicator's own EnemySpawner is left at UNTOUCHED component defaults (maxLiveEnemies
            // 8, startingRobots 0) — exactly what MapRuntime hands every real box — so
            // EffectiveMaxLiveEnemies reads 0 here unless SpawnExact is told to ignore it. ===
            Assert.AreEqual(0f, DifficultyDirector.Normalized, 0.0001f,
                "setup failure: Normalized must read 0 at a freshly reset run clock");

            Replicator replicatorA = NewReplicator(RigOriginPart1, capacity: 2);
            EnemySpawner spawnerA = replicatorA.GetComponent<EnemySpawner>();
            RobotEnemy rusherA = NewLiveRobot(RigOriginPart1 + new Vector3(5f, 0f, 0f)); // within the 16 m lure radius

            replicatorA.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, rusherA.Current,
                "setup failure: the robot must be lured before this test can drive a cycle");
            rusherA.transform.position = rusherA.ReplicatorSeekTarget;

            // MV-823 rebuilt the draw-in as a walk-speed-derived AnimSequence rather than a flat
            // IntakeSeconds lerp, and changed CycleSeconds 0.9 -> 2.0 — polled to each milestone rather
            // than a hand-computed dt sum tied to the old numbers, and against a wider time ceiling
            // (MV-823 deliberately lengthened one cycle to ~3 s for the replication light).
            float elapsedA = 0f;
            const float dtA = 0.02f;
            int guardA = 0;
            while (rusherA.IsAlive && guardA++ < 300) { replicatorA.TickConsumption(dtA); elapsedA += dtA; }
            Assert.IsFalse(rusherA.IsAlive, "setup failure: the robot must be despawned into the Cycle beat by now");
            Assert.AreEqual(0, spawnerA.LiveCountOf(EnemyKind.Rusher), "the Cycle beat hasn't completed yet — nothing should have emerged");

            guardA = 0;
            while (spawnerA.LiveCountOf(EnemyKind.Rusher) < 1 && guardA++ < 300) { replicatorA.TickConsumption(dtA); elapsedA += dtA; }
            Assert.LessOrEqual(elapsedA, 4.5f, "must land well under the pre-MV-812 4.4 s ceiling");
            Assert.AreEqual(1, spawnerA.LiveCountOf(EnemyKind.Rusher),
                "MV-817: at DifficultyDirector level 0 (EffectiveMaxLiveEnemies == 0), the Replicator must " +
                "still give back its guaranteed first twin — the per-factory ramp cap must never gate a " +
                "Replicator's own emission");

            guardA = 0;
            while (spawnerA.LiveCountOf(EnemyKind.Rusher) < 2 && guardA++ < 300) { replicatorA.TickConsumption(dtA); elapsedA += dtA; }
            Assert.LessOrEqual(elapsedA, 4.5f, "must land well under the pre-MV-812 4.4 s ceiling");
            Assert.AreEqual(2, spawnerA.LiveCountOf(EnemyKind.Rusher),
                "well under the global budget, the opportunistic second twin must also emerge");
            Assert.AreEqual(1, replicatorA.Capacity,
                "the box's own state must stay consistent: a cycle that gives back the full pair spends exactly one capacity");

            var liveA = (IList)LiveField.GetValue(spawnerA);
            RobotEnemy twinA = null;
            foreach (RobotEnemy r in liveA) if (r.Kind == EnemyKind.Rusher) { twinA = r; break; }
            Assert.IsNotNull(twinA, "setup failure: an emitted twin must be findable in the spawner's own live list");
            Assert.LessOrEqual(Vector3.Distance(twinA.transform.position, replicatorA.OutRampFootPosition), 1.2f,
                "the emitted twin must land at the Replicator's own out-ramp, not wherever the ordinary spawn path would place it");

            foreach (RobotEnemy r in liveA) if (r != null) Object.DestroyImmediate(r.gameObject);
            Object.DestroyImmediate(rusherA.gameObject);
            Object.DestroyImmediate(replicatorA.gameObject);
        }
    }
}
