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
    /// One consolidated test (testing policy MV-465, Rule 1) covering both angles of the single root
    /// cause the ticket names: the per-factory ramp cap wrongly gating a Replicator (Part 1) and the
    /// first twin's own guarantee never actually being unconditional (Part 2). Tier 2 (resolved values)
    /// throughout: every assertion reads <see cref="EnemySpawner.LiveCountOf"/>, <see cref="Replicator.Capacity"/>,
    /// a resolved out-ramp <see cref="Transform.position"/>, or a resolved LED
    /// <see cref="MaterialPropertyBlock"/> colour — never an authored constant asserted back at itself.
    /// </summary>
    public sealed class MV817ReplicatorAlwaysGivesBackTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test in this suite
        // uses — EditMode tests share one physics scene for the whole cc-verify run with no per-test
        // reset. Part 2 gets its own origin, well clear of Part 1's.
        private static readonly Vector3 RigOriginPart1 = new Vector3(84213f, 0f, -46110f);
        private static readonly Vector3 RigOriginPart2 = new Vector3(-91007f, 0f, 28455f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnDisableMethod =
            typeof(RobotEnemy).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LiveField =
            typeof(EnemySpawner).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LedField =
            typeof(Replicator).GetField("_led", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo LateUpdateMethod =
            typeof(Replicator).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

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

        private static Color SampleLed(Replicator replicator)
        {
            LateUpdateMethod.Invoke(replicator, null);
            var led = (Renderer)LedField.GetValue(replicator);
            var mpb = new MaterialPropertyBlock();
            led.GetPropertyBlock(mpb);
            return mpb.GetColor("_BaseColor");
        }

        private static void AssertBusy(Replicator replicator, string context)
        {
            Color c = SampleLed(replicator);
            Assert.Greater(c.r, c.g * 2f, $"{context}: the LED must read busy (red-dominant), not idle green");
        }

        [Test]
        public void ReplicatorAlwaysGivesBack_AtDifficultyZero_AndNeverDropsTheGuaranteedFirstTwin()
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

            float elapsedA = 0f;
            replicatorA.TickConsumption(Replicator.IntakeSeconds + 0.01f); elapsedA += Replicator.IntakeSeconds + 0.01f;
            Assert.IsFalse(rusherA.IsAlive, "setup failure: the robot must be despawned into the Cycle beat by now");
            Assert.AreEqual(0, spawnerA.LiveCountOf(EnemyKind.Rusher), "the Cycle beat hasn't completed yet — nothing should have emerged");

            replicatorA.TickConsumption(0.59f); elapsedA += 0.59f; // crosses CycleSeconds (0.9 s total)
            Assert.LessOrEqual(elapsedA, 2f, "must land within 2 s of simulated TickConsumption time");
            Assert.AreEqual(1, spawnerA.LiveCountOf(EnemyKind.Rusher),
                "MV-817: at DifficultyDirector level 0 (EffectiveMaxLiveEnemies == 0), the Replicator must " +
                "still give back its guaranteed first twin — the per-factory ramp cap must never gate a " +
                "Replicator's own emission");

            replicatorA.TickConsumption(0.20f); elapsedA += 0.20f; // crosses CycleSeconds + EmitStaggerSeconds
            Assert.LessOrEqual(elapsedA, 2f, "must land within 2 s of simulated TickConsumption time");
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

            // === Part 2 (never drop the guarantee): hold the global count AT the budget the instant the
            // first twin tries to emit. The first twin must not be dropped — it must stay pending, keep
            // the box reading busy, and spawn on the very next tick once room frees. ===
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.GlobalRobotBudget = 3f;

            RobotEnemy bg1 = NewLiveRobot(RigOriginPart2 + new Vector3(1000f, 0f, 0f));
            RobotEnemy bg2 = NewLiveRobot(RigOriginPart2 + new Vector3(1001f, 0f, 0f));
            Replicator replicatorB = NewReplicator(RigOriginPart2, capacity: 1);
            EnemySpawner spawnerB = replicatorB.GetComponent<EnemySpawner>();
            RobotEnemy rusherB = NewLiveRobot(RigOriginPart2 + new Vector3(5f, 0f, 0f));

            Assert.AreEqual(3, RobotEnemy.ActiveCount, "setup failure: field must sit exactly at the budget (3 of 3) before intake");

            replicatorB.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, rusherB.Current,
                "setup failure: the robot must be lured before this test can drive a cycle");
            rusherB.transform.position = rusherB.ReplicatorSeekTarget;

            replicatorB.TickConsumption(Replicator.IntakeSeconds + 0.01f); // completes Intake, reserves the freed slot
            Assert.IsFalse(rusherB.IsAlive, "setup failure: the robot must be despawned into the Cycle beat by now");
            OnDisableMethod.Invoke(rusherB, null); // sync ActiveCount the instant Despawn() fires, same as MV809's own helper

            // The field grows by one MORE robot in the meantime (an unrelated arrival elsewhere) — the
            // reservation still holds the slot this box vacated, but the field is now genuinely full:
            // ActiveCount (3) + the held reservation (1) exceeds the 3-robot budget.
            RobotEnemy intruder = NewLiveRobot(RigOriginPart2 + new Vector3(2000f, 0f, 2000f));
            Assert.AreEqual(3, RobotEnemy.ActiveCount, "setup failure: two background robots plus the intruder must read 3");

            replicatorB.TickConsumption(0.59f); // crosses CycleSeconds (0.95 s total) — the first-twin attempt, and it must fail
            Assert.AreEqual(0, spawnerB.LiveCountOf(EnemyKind.Rusher),
                "MV-817: with the global count held at the budget, the guaranteed first twin must not spawn yet");
            Assert.AreEqual(1, replicatorB.Capacity, "a first twin that couldn't spawn must not have spent any capacity");
            AssertBusy(replicatorB, "first failed attempt");

            replicatorB.TickConsumption(0.01f); // retried on the very next tick — still full, still must not spawn
            Assert.AreEqual(0, spawnerB.LiveCountOf(EnemyKind.Rusher),
                "MV-817: the guaranteed first twin must never be dropped — it must keep retrying, not vanish");
            AssertBusy(replicatorB, "second failed attempt");

            OnDisableMethod.Invoke(intruder, null); // room frees
            Assert.AreEqual(2, RobotEnemy.ActiveCount, "setup failure: freeing the intruder must drop the field back to 2");

            replicatorB.TickConsumption(0.01f); // the first tick after room frees
            Assert.AreEqual(1, spawnerB.LiveCountOf(EnemyKind.Rusher),
                "MV-817: the guaranteed first twin must spawn on the very first tick after room frees");

            var liveB = (IList)LiveField.GetValue(spawnerB);
            RobotEnemy twinB = null;
            foreach (RobotEnemy r in liveB) if (r.Kind == EnemyKind.Rusher) { twinB = r; break; }
            Assert.IsNotNull(twinB, "setup failure: the freed-room twin must be findable in the spawner's own live list");
            Assert.LessOrEqual(Vector3.Distance(twinB.transform.position, replicatorB.OutRampFootPosition), 1.2f,
                "the freed-room twin must land at the Replicator's own out-ramp");

            foreach (RobotEnemy r in liveA) if (r != null) Object.DestroyImmediate(r.gameObject);
            foreach (RobotEnemy r in liveB) if (r != null) Object.DestroyImmediate(r.gameObject);
            Object.DestroyImmediate(rusherA.gameObject);
            Object.DestroyImmediate(replicatorA.gameObject);
            Object.DestroyImmediate(bg1.gameObject);
            Object.DestroyImmediate(bg2.gameObject);
            Object.DestroyImmediate(rusherB.gameObject);
            Object.DestroyImmediate(replicatorB.gameObject);
            Object.DestroyImmediate(intruder.gameObject);
        }
    }
}
