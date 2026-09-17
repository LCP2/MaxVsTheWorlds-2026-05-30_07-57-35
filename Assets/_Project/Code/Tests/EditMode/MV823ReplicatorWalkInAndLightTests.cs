using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-823 — Lee: "I want to see robots GO IN TO THE REPLICATORS, not just disappear. I want to see
    /// a CLEAR BRIGHT LIGHT switch on when a replication is occurring." Fails on the commit this ticket
    /// is cut from: the hatch swung about its own local Y, which after its Euler(90,0,0) build rotation
    /// resolved to the face NORMAL (the door spun in its own plane, never actually opening — see
    /// <c>Replicator.LateUpdate</c>'s old <c>Quaternion.AngleAxis(_hatchOpenAmount * HatchOpenAngleDeg,
    /// Vector3.up)</c>), a consumed robot was lerped straight to the hatch face and despawned there
    /// (never continuing through, never shrinking), and there was no roof beacon field to find at all.
    /// Tier 2 (resolved values): every assertion below reads a resolved transform/renderer state after
    /// driving the beat with a synthetic dt, never an authored constant.
    ///
    /// MV-834: the floor-pool assertions this test originally carried (a resolved "ReplicationPool"
    /// renderer's active window and colour) are culled — Lee rejected the pool outright ("a stupid,
    /// basic big yellow circle around it that is illogical") and MV-834 deletes it from
    /// <c>FactoryBodies.BuildReplicator</c> entirely, so a test asserting its resolved state would now
    /// be asserting a part that no longer exists. The beacon's own window/colour assertions are kept,
    /// updated for the beacon's new green (was warm-white).
    /// </summary>
    public sealed class MV823ReplicatorWalkInAndLightTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(-71340f, 0f, 63012f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

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

        private readonly struct Sample
        {
            public readonly float Elapsed;
            public readonly bool BeaconActive;
            public Sample(float elapsed, bool beaconActive)
            {
                Elapsed = elapsed; BeaconActive = beaconActive;
            }
        }

        [Test]
        public void MV823_RobotWalksThroughHatchAndReplicationLightBracketsTheCycle()
        {
            // Same BuildBody collider-strip [Error], see ReplicatorTests' own note on this.
            LogAssert.ignoreFailingMessages = true;

            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            _replicatorGo.transform.localScale = new Vector3(2f, 1.5f, 2f); // the ticket's own authored footprint
            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build();
            replicator.Configure(1);
            Set(_replicatorGo.GetComponent<EnemySpawner>(), "startingRobots", 4); // room for the doubled pair

            RobotEnemy rusher = NewRusher(RigOrigin + new Vector3(5f, 0f, 0f));
            replicator.TickLure();
            rusher.transform.position = rusher.ReplicatorSeekTarget; // at its queue slot, ready for Intake

            Transform hatch = Get<Transform>(replicator, "_hatch");
            Renderer beacon = Get<Renderer>(replicator, "_replicationBeacon");
            Assert.IsNotNull(hatch, "FactoryBodies.BuildReplicator must produce a Hatch transform");
            Assert.IsNotNull(beacon, "FactoryBodies.BuildReplicator must produce a replication beacon renderer (MV-823)");

            Assert.IsFalse(beacon.gameObject.activeSelf, "the beacon must start OFF — dark until a replication actually runs");

            Quaternion closedWorldRotation = hatch.rotation;
            Vector3 hullFaceNormal = Vector3.forward; // the box is authored unrotated; hatch sits on the -Z face

            var spawner = _replicatorGo.GetComponent<EnemySpawner>();
            var samples = new List<Sample>(600);

            bool sawHatchSwungOpen = false;
            bool sawPassThroughDeepEnoughAndShrunk = false;
            float minRobotY = float.PositiveInfinity;
            float rampSurfaceFloor = RigOrigin.y - 0.75f; // GroundY: box centre Y minus half the authored 1.5 m height
            float intakeCompleteElapsed = -1f;
            float secondTwinElapsed = -1f;
            Color? capturedBeaconColor = null;

            const float dt = 0.02f;
            float elapsed = 0f;

            for (int i = 0; i < 600; i++) // up to 12 s — generous margin either side of the ~3.5 s cycle
            {
                replicator.TickConsumption(dt);
                elapsed += dt;

                // (a) hatch swings open about an axis parallel to the hull face, not the face normal.
                Quaternion delta = hatch.rotation * Quaternion.Inverse(closedWorldRotation);
                delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
                if (angleDeg > 180f) angleDeg = 360f - angleDeg;
                if (angleDeg >= 70f && Mathf.Abs(Vector3.Dot(axis.normalized, hullFaceNormal)) < 0.2f)
                    sawHatchSwungOpen = true;

                if (rusher.gameObject.activeSelf)
                {
                    minRobotY = Mathf.Min(minRobotY, rusher.transform.position.y);

                    // (b) how far the robot's position has passed beyond the hatch plane, into the hull.
                    float depthPastHatch = Vector3.Dot(rusher.transform.position - replicator.HatchPosition, hullFaceNormal);
                    if (depthPastHatch >= 0.4f && rusher.transform.localScale.x <= 0.05f)
                        sawPassThroughDeepEnoughAndShrunk = true;
                }
                else if (intakeCompleteElapsed < 0f)
                {
                    intakeCompleteElapsed = elapsed; // the instant the robot itself is fully inside
                }

                if (secondTwinElapsed < 0f && spawner.LiveCountOf(EnemyKind.Rusher) >= 2)
                    secondTwinElapsed = elapsed;

                samples.Add(new Sample(elapsed, beacon.gameObject.activeSelf));

                if (capturedBeaconColor == null && beacon.gameObject.activeSelf)
                {
                    var mpb = new MaterialPropertyBlock();
                    beacon.GetPropertyBlock(mpb);
                    capturedBeaconColor = mpb.GetColor("_BaseColor");
                }

                // Once the second twin has emitted, keep sampling only far enough to also cover the
                // 0.4 s linger plus the AC's own 0.5 s "and inactive ... after" margin, then stop.
                if (secondTwinElapsed >= 0f && elapsed >= secondTwinElapsed + 0.4f + 0.5f + 0.05f) break;
            }

            Assert.IsTrue(sawHatchSwungOpen,
                "the hatch transform's world rotation must, at some mid-intake tick, differ from closed by " +
                "at least 70 degrees about an axis parallel to the hull face — not the face normal (the " +
                "original MV-706 bug: swinging about local Y resolved, after the panel's own Euler(90,0,0) " +
                "build rotation, to the face normal, so the door spun in its own plane instead of opening)");

            Assert.IsTrue(sawPassThroughDeepEnoughAndShrunk,
                "the robot must pass at least 0.4 m beyond the hatch plane into the hull, shrunk to <= 5% " +
                "scale, before it despawns — not stop dead at the hatch face and vanish");

            Assert.GreaterOrEqual(minRobotY, rampSurfaceFloor - 0.05f,
                "the robot's Y must never dip below the ramp surface's own floor level while it's being drawn in");

            Assert.Greater(intakeCompleteElapsed, 0f, "the robot must actually despawn into the box during this run");
            Assert.Greater(secondTwinElapsed, intakeCompleteElapsed, "the second twin must emit after intake completes");

            // Asserted with a small buffer either side of the exact linger cutoff (windowEnd) — the
            // linger timer decrements by dt per tick, so the precise tick it crosses zero is a
            // discretisation detail, not something the AC cares about pinning to the millisecond.
            float windowEnd = secondTwinElapsed + 0.4f;
            foreach (Sample s in samples)
            {
                if (s.Elapsed < intakeCompleteElapsed - 0.001f)
                {
                    Assert.IsFalse(s.BeaconActive, $"beacon must be OFF before intake completes (t={s.Elapsed:F2})");
                }
                else if (s.Elapsed <= windowEnd - 0.03f)
                {
                    Assert.IsTrue(s.BeaconActive, $"beacon must be ON between intake completion and second-twin + 0.4s (t={s.Elapsed:F2})");
                }
                else if (s.Elapsed >= windowEnd + 0.5f)
                {
                    Assert.IsFalse(s.BeaconActive, $"beacon must be OFF 0.5s after the second-twin + 0.4s window closes (t={s.Elapsed:F2})");
                }
            }

            // (e) MV-834: the beacon's resolved colour must read green, not MV-823's warm-white —
            // Lee's own "the light is green, not white or yellow".
            Assert.IsTrue(capturedBeaconColor.HasValue, "the beacon must have been active at least once to sample its colour");
            Assert.Greater(capturedBeaconColor.Value.g, capturedBeaconColor.Value.r * 2f,
                "the beacon's resolved colour must be green-dominant (g > 2x r) while a replication is running");
        }
    }
}
