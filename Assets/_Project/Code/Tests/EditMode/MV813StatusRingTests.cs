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
    /// MV-813 — the Replicator's busy/idle state was only ever carried by a 10 cm status LED: ~5 px
    /// at the play camera's ~48 px/m (26 m, 60°), legible up close, invisible at play scale (Lee, on
    /// build 9751529: "no indication that anything is happening"). This adds a 0.9 m additive status
    /// ring on the box's top face, taking the exact resolved colour the LED already does and pulsing
    /// its emissive strength 1.0x-2.2x at 2 Hz while busy. Fails on base commit 9751529 (pre-MV-813):
    /// <c>FactoryBodies.BuildReplicator</c> never builds a "StatusRing" part at all, so
    /// <c>Find("Body/StatusRing")</c> resolves null and the very first real assertion throws.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) asserting RESOLVED state only (Rule 2,
    /// Tier 2): the ring's resolved renderer bounds and world position, its resolved
    /// MaterialPropertyBlock colour across one full lure/intake/cycle/output loop (green, red x3,
    /// green), and its resolved emissive strength sampled 10 times across 1 second both busy and idle
    /// — never an authored constant asserted back at itself, never a mere presence check.
    /// </summary>
    public sealed class MV813StatusRingTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test in this suite
        // uses — EditMode tests share one physics scene for the whole cc-verify run with no per-test
        // reset.
        private static readonly Vector3 RigOrigin = new Vector3(53021f, 0f, -77410f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo LateUpdateMethod =
            typeof(Replicator).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, value);

        private GameObject _playerGo;
        private GameObject _replicatorGo;
        private GameObject _rusherGo;
        private GameObject _rusher2Go;

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
            if (_rusher2Go != null) Object.DestroyImmediate(_rusher2Go);
        }

        private RobotEnemy NewRusher(ref GameObject slot, string name, Vector3 position)
        {
            slot = new GameObject(name);
            var cc = slot.AddComponent<CharacterController>();
            var e = slot.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            // OnEnable never runs as a side effect of AddComponent outside Play mode — same quirk
            // every other Replicator/RobotEnemy EditMode test in this suite works around.
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        private static Color SampleRing(Renderer ring, MaterialPropertyBlock mpb)
        {
            ring.GetPropertyBlock(mpb);
            return mpb.GetColor("_BaseColor");
        }

        [Test]
        public void StatusRing_ReadsAtPlayScale_TracksLedColour_AndPulsesOnlyWhenBusy()
        {
            LogAssert.ignoreFailingMessages = true; // BuildBody's collider-strip [Error], every Replicator test carries this

            DevTuning.GlobalRobotBudget = 20f; // plenty of headroom so room checks never block this test

            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            _replicatorGo.transform.localScale = new Vector3(2f, 1.5f, 2f); // the ticket's own authored footprint
            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build(); // AddComponent's own Awake never runs outside Play mode
            // Capacity 3: one cycle for the red/green loop below, a second for the pulse/steadiness
            // sampling, and still > 0 (idle green, never spent) after both.
            replicator.Configure(3);
            Set(_replicatorGo.GetComponent<EnemySpawner>(), "startingRobots", 8);

            // --- AC1: the ring's resolved bounds and placement ---
            Transform ringT = _replicatorGo.transform.Find("Body/StatusRing");
            Assert.IsNotNull(ringT, "BuildReplicator must build a StatusRing part under the metre-space Body root");
            var ring = ringT.GetComponent<Renderer>();
            Assert.IsNotNull(ring, "StatusRing must carry a renderer");

            Bounds bounds = ring.bounds;
            Assert.GreaterOrEqual(bounds.size.x, 0.85f, "the ring's resolved world bounds must measure at least 0.85 m across (X)");
            Assert.GreaterOrEqual(bounds.size.z, 0.85f, "the ring's resolved world bounds must measure at least 0.85 m across (Z)");

            float topFaceY = _replicatorGo.transform.position.y + _replicatorGo.transform.localScale.y * 0.5f;
            Assert.Greater(ringT.position.y, topFaceY - 0.001f,
                "the ring's centre must sit at or above the Replicator body's own top face");

            var mpb = new MaterialPropertyBlock();
            Color green = new Color(0.30f, 0.95f, 0.35f);

            // --- Green before an intake ---
            LateUpdateMethod.Invoke(replicator, null);
            Color before = SampleRing(ring, mpb);
            Assert.AreEqual(green.r, before.r, 0.01f, "before any intake, the ring must read the idle green (r)");
            Assert.AreEqual(green.g, before.g, 0.01f, "before any intake, the ring must read the idle green (g)");
            Assert.AreEqual(green.b, before.b, 0.01f, "before any intake, the ring must read the idle green (b)");

            // --- Lure and drive the first robot to its queue slot ---
            RobotEnemy rusher = NewRusher(ref _rusherGo, "Rusher1", RigOrigin + new Vector3(5f, 0f, 0f));
            replicator.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, rusher.Current,
                "within lure radius, capacity > 0, clear of Max's melee exclusion — this Rusher must be lured");
            rusher.transform.position = rusher.ReplicatorSeekTarget;

            // --- Red at three samples spread across the intake-to-second-emission window: Intake
            // (0.35 s) + Cycle (0.9 s) + stagger (0.2 s) = 1.45 s total. ---
            replicator.TickConsumption(0.2f); // mid-Intake
            LateUpdateMethod.Invoke(replicator, null);
            Color redSample1 = SampleRing(ring, mpb);

            replicator.TickConsumption(0.5f); // past Intake, mid-Cycle (0.7 s total)
            LateUpdateMethod.Invoke(replicator, null);
            Color redSample2 = SampleRing(ring, mpb);

            replicator.TickConsumption(0.55f); // just past the first emission (1.25 s total)
            LateUpdateMethod.Invoke(replicator, null);
            Color redSample3 = SampleRing(ring, mpb);

            AssertRedFamily(redSample1, "1");
            AssertRedFamily(redSample2, "2");
            AssertRedFamily(redSample3, "3");

            // --- Finish the cycle: second twin emits, capacity drops from 3 to 2 (still > 0) ---
            replicator.TickConsumption(0.2f); // cross the stagger too
            LateUpdateMethod.Invoke(replicator, null);
            Assert.AreEqual(2, replicator.Capacity, "one doubling must spend exactly one of the three starting capacity");

            // --- Green again ---
            Color after = SampleRing(ring, mpb);
            Assert.AreEqual(green.r, after.r, 0.01f, "once both twins have emerged, with capacity still > 0, the ring must read idle green again (r)");
            Assert.AreEqual(green.g, after.g, 0.01f, "...idle green again (g)");
            Assert.AreEqual(green.b, after.b, 0.01f, "...idle green again (b)");

            // --- Pulsing while busy: 10 samples across 1 s, drawn from a second full cycle. ---
            RobotEnemy rusher2 = NewRusher(ref _rusher2Go, "Rusher2", RigOrigin + new Vector3(5f, 0f, 0f));
            replicator.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, rusher2.Current, "the second Rusher must also be lured");
            rusher2.transform.position = rusher2.ReplicatorSeekTarget;
            replicator.TickConsumption(0.05f); // crosses into Intake — busy from here on
            LateUpdateMethod.Invoke(replicator, null);
            AssertRedFamily(SampleRing(ring, mpb), "busy-start");

            var busySamples = new float[10];
            for (int i = 0; i < 10; i++)
            {
                replicator.TickConsumption(0.1f); // 10 x 0.1 s = 1.0 s, still short of the 1.45 s busy window
                LateUpdateMethod.Invoke(replicator, null);
                busySamples[i] = SampleRing(ring, mpb).r; // red's dominant channel (base 1.0) tracks the multiplier directly
            }
            float busyMax = Mathf.Max(busySamples);
            float busyMin = Mathf.Min(busySamples);
            Assert.GreaterOrEqual(busyMax, busyMin * 1.8f,
                $"while busy, the ring's emissive strength must swing at least 1.8x peak-to-trough over 1 s " +
                $"(max {busyMax:F3}, min {busyMin:F3}) — it must visibly pulse, not sit still");

            // --- Finish this second cycle: capacity 2 -> 1 (still > 0, idle green, never spent). ---
            replicator.TickConsumption(2f);
            LateUpdateMethod.Invoke(replicator, null);
            Assert.AreEqual(1, replicator.Capacity, "the second doubling must spend exactly one more of the starting capacity");
            Color idleCheck = SampleRing(ring, mpb);
            Assert.AreEqual(green.r, idleCheck.r, 0.01f, "the ring must be back to idle green before the steadiness sampling");

            // --- Steady while idle: 10 samples across 1 s, under 5% variation. ---
            var idleSamples = new float[10];
            for (int i = 0; i < 10; i++)
            {
                replicator.TickConsumption(0.1f);
                LateUpdateMethod.Invoke(replicator, null);
                idleSamples[i] = SampleRing(ring, mpb).g; // idle green's dominant, nonzero channel (base 0.95)
            }
            float idleMax = Mathf.Max(idleSamples);
            float idleMin = Mathf.Min(idleSamples);
            Assert.LessOrEqual((idleMax - idleMin) / idleMax, 0.05f,
                $"while idle, the ring's emissive strength must vary by under 5% over 1 s (max {idleMax:F4}, min {idleMin:F4})");
        }

        /// <summary>The pulse multiplier changes overall brightness but never the hue — a red sample
        /// must stay red-dominant (r much greater than g) at every point across the 1.0x-2.2x swing,
        /// never drift toward the idle green.</summary>
        private static void AssertRedFamily(Color sample, string label) =>
            Assert.Greater(sample.r, sample.g * 2f, $"sample '{label}' must be red-dominant (r >> g), not the idle green");
    }
}
