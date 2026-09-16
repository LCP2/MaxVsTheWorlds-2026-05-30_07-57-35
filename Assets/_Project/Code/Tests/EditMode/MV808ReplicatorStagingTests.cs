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
    /// MV-808 — every beat of the Replicator's Lure/Intake/Cycle/Output theatre already existed as
    /// timing, but none of it was legible: <c>TickIntake</c> lerped straight to <c>HatchPosition</c>
    /// with no ramp to visibly climb, the doubled pair emerged wherever <c>EnemySpawner.SpawnKind</c>'s
    /// own door/mouth placement put them (not from the box), and the LED only ever signalled capacity,
    /// never a busy state. Fails on base commit f89c4e7 (pre-MV-808): <c>Replicator.IntakeSeconds</c>
    /// is 0.5 there (not 1.0), and <c>Replicator.OutRampFootPosition</c>/<c>OutputPosition</c> don't
    /// exist at all — this test fails to COMPILE on that commit, the same "no such member" base-commit
    /// failure <see cref="ReplicatorTests"/>'s own MV-706 doc comment names for its base commit. Quoted
    /// in this ticket's fix comment.
    ///
    /// Tier 2 (resolved values): every assertion reads a resolved Transform position, a resolved
    /// shader property block colour, or a live Collider count — never an authored constant asserted
    /// back at itself, never a mere presence check.
    /// </summary>
    public sealed class MV808ReplicatorStagingTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test uses — EditMode
        // tests share one physics scene for the whole cc-verify run with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(-12481f, 0f, 63920f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo LateUpdateMethod =
            typeof(Replicator).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LedField =
            typeof(Replicator).GetField("_led", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, value);

        private GameObject _playerGo;
        private GameObject _replicatorGo;
        private GameObject _rusherGo;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = RigOrigin + new Vector3(0f, 0f, 50f); // outside the 7 m melee exclusion
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_replicatorGo != null) Object.DestroyImmediate(_replicatorGo);
            if (_rusherGo != null) Object.DestroyImmediate(_rusherGo);
        }

        private RobotEnemy NewRusher(Vector3 position)
        {
            _rusherGo = new GameObject("Rusher");
            var cc = _rusherGo.AddComponent<CharacterController>();
            var e = _rusherGo.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            // OnEnable never runs as a side effect of AddComponent outside Play mode — same quirk every
            // other Replicator/RobotEnemy EditMode test in this suite works around.
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        private static void AssertLed(Renderer led, MaterialPropertyBlock mpb, Color expected, string context)
        {
            led.GetPropertyBlock(mpb);
            Color actual = mpb.GetColor("_BaseColor");
            Assert.AreEqual(expected.r, actual.r, 0.001f, $"{context}: LED red channel");
            Assert.AreEqual(expected.g, actual.g, 0.001f, $"{context}: LED green channel");
            Assert.AreEqual(expected.b, actual.b, 0.001f, $"{context}: LED blue channel");
        }

        [Test]
        public void ReplicatorTheatre_RampsTheWalkUp_LitsTheLedBusy_EmitsTheTwinsFromTheOutRamp()
        {
            LogAssert.ignoreFailingMessages = true; // BuildBody's collider-strip [Error], every Replicator test carries this

            DevTuning.GlobalRobotBudget = 20f; // plenty of headroom so room checks never block this test

            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            _replicatorGo.transform.localScale = new Vector3(2f, 1.5f, 2f); // the ticket's own authored footprint

            // --- The ramps (and the rest of the generated body) must never add a Collider — dressing
            // only, per the ticket's own "no collider" requirement. Captured before Build() so this is
            // a real before/after comparison, not the same call read twice. ---
            int collidersBefore = _replicatorGo.GetComponentsInChildren<Collider>(true).Length;

            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build(); // AddComponent's own Awake never runs outside Play mode
            replicator.Configure(2); // capacity 2, so one full cycle leaves it > 0 (idle-green, not spent)
            Set(_replicatorGo.GetComponent<EnemySpawner>(), "startingRobots", 6);

            int collidersAfter = _replicatorGo.GetComponentsInChildren<Collider>(true).Length;
            Assert.AreEqual(collidersBefore, collidersAfter,
                "building the generated body (hull, hatch, ramps) must never add a Collider — the ramps " +
                "are dressing only and must never be able to alter navigation");

            var led = (Renderer)LedField.GetValue(replicator);
            var ledMpb = new MaterialPropertyBlock();
            Color green = new Color(0.30f, 0.95f, 0.35f);
            Color red = new Color(1.00f, 0.18f, 0.14f);

            LateUpdateMethod.Invoke(replicator, null);
            AssertLed(led, ledMpb, green, "idle, before anything is lured, the LED must read green");

            RobotEnemy rusher = NewRusher(RigOrigin + new Vector3(5f, 0f, 0f)); // 5 m from the box
            replicator.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, rusher.Current,
                "within lure radius, capacity > 0, clear of Max's melee exclusion — this Rusher must be lured");

            // --- Move it to its queue slot and drive the Intake beat in small steps, sampling the
            // ascent, until it despawns. MV-823 rebuilt the draw-in as a walk-speed-derived AnimSequence
            // (walk to the hatch, then pass through and shrink) rather than a flat IntakeSeconds lerp,
            // so this polls to the despawn rather than assuming a fixed duration. ---
            rusher.transform.position = rusher.ReplicatorSeekTarget;

            var ys = new System.Collections.Generic.List<float>();
            int guard = 0;
            while (rusher.IsAlive && guard++ < 300)
            {
                replicator.TickConsumption(0.02f);
                LateUpdateMethod.Invoke(replicator, null);
                ys.Add(rusher.transform.position.y);
                AssertLed(led, ledMpb, red, $"mid-ascent (sample {ys.Count - 1}), the LED must read red");
            }
            Assert.IsFalse(rusher.IsAlive, "fully drawn in, the Rusher must be despawned into the Cycle beat");
            AssertLed(led, ledMpb, red, "the instant Intake completes (still mid-Cycle), the LED must read red");

            for (int i = 1; i < ys.Count; i++)
                Assert.GreaterOrEqual(ys[i], ys[i - 1] - 0.0001f,
                    $"the intake robot's Y must rise (or hold, once past the hatch) monotonically across " +
                    $"the ascent — sample {i} ({ys[i]:F3}) fell below sample {i - 1} ({ys[i - 1]:F3})");
            Assert.Greater(ys[ys.Count - 1], ys[0] + 0.05f,
                "the ascent from ground level to the hatch lip must be a real rise, not a flat line within noise");

            var spawner = _replicatorGo.GetComponent<EnemySpawner>();
            Assert.AreEqual(0, spawner.LiveCountOf(EnemyKind.Rusher),
                "the Cycle beat hasn't elapsed yet — nothing should have emerged");

            // --- Cross CycleSeconds only (never CycleSeconds + EmitStaggerSeconds in the same call) —
            // polled rather than a hand-computed dt sum, since MV-823 changed CycleSeconds 0.9 -> 2.0
            // and a fixed-jump sum tied to the old value is exactly what broke this test on that change. ---
            guard = 0;
            while (spawner.LiveCountOf(EnemyKind.Rusher) < 1 && guard++ < 300)
            {
                replicator.TickConsumption(0.02f);
                LateUpdateMethod.Invoke(replicator, null);
            }
            Assert.AreEqual(1, spawner.LiveCountOf(EnemyKind.Rusher),
                "the FIRST of the doubled pair must emerge once the Cycle beat completes");
            AssertLed(led, ledMpb, red, "between the first and second emission, the LED must still read red");

            // --- Cross CycleSeconds + EmitStaggerSeconds too. ---
            guard = 0;
            while (spawner.LiveCountOf(EnemyKind.Rusher) < 2 && guard++ < 300)
            {
                replicator.TickConsumption(0.02f);
                LateUpdateMethod.Invoke(replicator, null);
            }
            Assert.AreEqual(2, spawner.LiveCountOf(EnemyKind.Rusher),
                "the SECOND of the doubled pair must emerge once the stagger has elapsed");
            Assert.AreEqual(1, replicator.Capacity, "one doubling must spend exactly one of the two starting capacity");
            AssertLed(led, ledMpb, green, "once both twins have emerged, with capacity still > 0, the LED must read green again");

            // --- Both twins must be at the out-ramp foot, not wherever the ordinary emergence path
            // (EnemySpawner.SpawnKind's own door/mouth placement) would otherwise have put them. ---
            var live = (IList)typeof(EnemySpawner)
                .GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(spawner);
            RobotEnemy twinA = null, twinB = null;
            foreach (RobotEnemy r in live)
            {
                if (r.Kind != EnemyKind.Rusher) continue;
                if (twinA == null) twinA = r; else twinB = r;
            }
            Assert.IsNotNull(twinA, "the first emitted twin must be live and findable");
            Assert.IsNotNull(twinB, "the second emitted twin must be live and findable");

            Vector3 outFoot = replicator.OutRampFootPosition;
            Vector3 inFoot = replicator.QueueSlotPosition(0);
            Assert.LessOrEqual(Vector3.Distance(twinA.transform.position, outFoot), 1.2f,
                "the first twin must be placed within 1.2 m of the out-ramp foot");
            Assert.LessOrEqual(Vector3.Distance(twinB.transform.position, outFoot), 1.2f,
                "the second twin must be placed within 1.2 m of the out-ramp foot");
            Assert.Greater(Vector3.Distance(twinA.transform.position, inFoot), 1.0f,
                "the first twin must be well clear of the in-ramp foot, not merely near the hatch face");
            Assert.Greater(Vector3.Distance(twinB.transform.position, inFoot), 1.0f,
                "the second twin must be well clear of the in-ramp foot, not merely near the hatch face");
        }
    }
}
