using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-834 — Lee: "a big green light on the replicator" while it replicates, not MV-823's "stupid,
    /// basic big yellow circle around it that is illogical". Fails on the commit this ticket is cut
    /// from: <c>FactoryBodies.BuildReplicator</c> still builds a 4 m-radius "ReplicationPool" floor disc,
    /// and both the idle LED and the replication beacon/status-ring/hatch-glow are the old warm-white
    /// (1.00, 0.85, 0.45) rather than green. Tier 2 (resolved values): the hull/hazard-band renderer
    /// widths after the metre-space container's scale-cancel, the beacon's resolved bounds and
    /// MaterialPropertyBlock colour after driving a real replication with a synthetic dt, and the idle
    /// LED's resolved MaterialPropertyBlock colour — never an authored constant asserted back at itself.
    /// </summary>
    public sealed class MV834ReplicationLightTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(19204f, 0f, -35871f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo LateUpdateMethod =
            typeof(Replicator).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, value);

        private static T Get<T>(object o, string field) =>
            (T)o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);

        private GameObject _playerGo;
        private GameObject _replicatorGo;
        private GameObject _rusherGo;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = RigOrigin + new Vector3(0f, 0f, 20f);
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
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
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        [Test]
        public void MV834_NoFloorPoolRemains_BeaconIsGreenAndWideEnough_IdleLedIsAmber()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] every Replicator test carries

            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            _replicatorGo.transform.localScale = new Vector3(2f, 1.5f, 2f); // the ticket's own authored footprint
            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build();
            replicator.Configure(2); // > 1 so capacity is still > 0 (idle-amber, not spent) after one cycle
            Set(_replicatorGo.GetComponent<EnemySpawner>(), "startingRobots", 4); // room for the doubled pair

            // --- AC1: no floor pool of any colour, no renderer reading as one. ---
            Transform body = _replicatorGo.transform.Find("Body");
            Assert.IsNotNull(body, "Build() must generate a Body container");
            Assert.IsNull(body.Find("ReplicationPool"), "no child named ReplicationPool may exist anywhere on a Replicator (MV-834)");

            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = r.bounds;
                bool lowAndWide = b.size.y < 0.1f && (b.size.x > 2.2f || b.size.z > 2.2f);
                Assert.IsFalse(lowAndWide,
                    $"renderer '{r.gameObject.name}' is below 0.1 m tall and wider than 2.2 m " +
                    "(the box plus its ramps) — this is what a floor pool/disc would look like, and none may exist");
            }

            // --- AC2: drive a replication; the beacon must be wide enough and read green while it runs. ---
            RobotEnemy rusher = NewRusher(RigOrigin + new Vector3(5f, 0f, 0f));
            replicator.TickLure();
            rusher.transform.position = rusher.ReplicatorSeekTarget;

            Renderer beacon = Get<Renderer>(replicator, "_replicationBeacon");
            Assert.IsNotNull(beacon, "FactoryBodies.BuildReplicator must produce a replication beacon renderer");
            Assert.IsFalse(beacon.gameObject.activeSelf, "the beacon must start OFF");

            bool sampledWhileActive = false;
            const float dt = 0.02f;
            for (int i = 0; i < 600 && !sampledWhileActive; i++) // up to 12 s — generous margin either side of the cycle
            {
                replicator.TickConsumption(dt);
                if (!beacon.gameObject.activeSelf) continue;

                Bounds bounds = beacon.bounds;
                Assert.GreaterOrEqual(bounds.size.x, 1.2f - 0.01f, "the beacon's resolved XZ width (X) must be at least 1.2 m");
                Assert.GreaterOrEqual(bounds.size.z, 1.2f - 0.01f, "the beacon's resolved XZ width (Z) must be at least 1.2 m");

                var mpb = new MaterialPropertyBlock();
                beacon.GetPropertyBlock(mpb);
                Color emission = mpb.GetColor("_EmissionColor");
                Assert.Greater(emission.g, emission.r * 2f,
                    "the beacon's resolved emission colour's green channel must be more than 2x its red channel while active");

                sampledWhileActive = true;
            }
            Assert.IsTrue(sampledWhileActive, "the beacon must have gone active at least once while driving a real replication");

            // Drain the rest of the cycle so the beacon goes OFF again (past the second twin + 0.4 s linger).
            for (int i = 0; i < 300; i++) replicator.TickConsumption(dt);
            Assert.IsFalse(beacon.gameObject.activeSelf, "the beacon must be OFF again once the replication window has fully closed");

            // --- AC3: the idle LED reads the new dim amber, not the old idle green. ---
            LateUpdateMethod.Invoke(replicator, null);
            Renderer led = Get<Renderer>(replicator, "_led");
            var ledMpb = new MaterialPropertyBlock();
            led.GetPropertyBlock(ledMpb);
            Color ledColor = ledMpb.GetColor("_BaseColor");
            Assert.AreEqual(0.60f, ledColor.r, 0.01f, "idle LED red channel must equal 0.60");
            Assert.AreEqual(0.45f, ledColor.g, 0.01f, "idle LED green channel must equal 0.45");
            Assert.AreEqual(0.15f, ledColor.b, 0.01f, "idle LED blue channel must equal 0.15");
        }
    }
}
