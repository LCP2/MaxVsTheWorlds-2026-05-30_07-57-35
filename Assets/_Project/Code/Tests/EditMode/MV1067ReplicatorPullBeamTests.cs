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
    /// MV-1067 — Lee (2026-10-02): "I don't want robots just suddenly disappearing." Follow-up to
    /// MV-1066's guaranteed-arrival pull, which fixed arrival but did it with a flat 40 m/s glide and no
    /// visible tell at all — exactly a "sudden disappear and reappear" to anyone watching, just over a
    /// shorter distance. Must fail on d0ed9e6 (MV-1066's own merge commit): that commit's glide speed is
    /// a fixed 40 m/s (not this robot's own ~1.4 m/s walk speed) and <c>FactoryBodies.ReplicatorParts</c>
    /// carries no pull-beam field at all, so both the speed-cap and beam assertions below fail against it.
    ///
    /// ONE new test (testing policy MV-465, Rule 1). Tier 2 throughout (Rule 2): every assertion reads a
    /// resolved transform/renderer/flag after real per-tick simulation — robot position deltas, the pull
    /// beam's own resolved active state, the hatch's own resolved rotation, the robot's own resolved
    /// scale/depth-past-hatch — never an authored constant, never a rendered pixel. // Guards MV-1067
    /// </summary>
    public sealed class MV1067ReplicatorPullBeamTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(-81340f, 0f, 73012f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo VerticalVelField =
            typeof(RobotEnemy).GetField("_verticalVel", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private static T Get<T>(object o, string field) =>
            (T)o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, value);

        private GameObject _playerGo;
        private GameObject _replicatorGo;
        private GameObject _sludgerGo;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset(); // a stale RobotMoveSpeed override would break this test's own speed-cap math
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset(); // no real map in this scene — Waypoint must fall back to a beeline
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = RigOrigin + new Vector3(0f, 0f, 50f);
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_replicatorGo != null) Object.DestroyImmediate(_replicatorGo);
            if (_sludgerGo != null) Object.DestroyImmediate(_sludgerGo);
            DevTuning.Reset();
        }

        private RobotEnemy NewSludger(Vector3 position)
        {
            _sludgerGo = new GameObject("Sludger");
            var cc = _sludgerGo.AddComponent<CharacterController>();
            var e = _sludgerGo.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Sludger)); // 1.4 m/s, and exempt from MapSlowZones — deterministic
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        /// <summary>Same "Tick, then zero the vertical velocity" idiom as MV1066ReplicatorLureReproTests'
        /// own InvokeTick — there is no real floor under this robot in an isolated scene, so letting
        /// ordinary gravity (and MV-946/MV-952's own fall-recovery) run unchecked across the many real
        /// seconds this test simulates would fight the seek instead of measuring it.</summary>
        private static void InvokeTick(RobotEnemy e, float dt)
        {
            e.Tick(dt);
            VerticalVelField.SetValue(e, 0f);
        }

        [Test]
        public void SixSecondFallbackPull_MovesAtWalkSpeedWithBeamOn_ThenRunsIntakeToCompletion()
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

            // 5 m out along the same line the lure steers along — far enough to sample several glide
            // ticks before arrival, without the robot ever needing to close actual ground on its own.
            Vector3 slot0 = replicator.QueueSlotPosition(0);
            Vector3 outward = (slot0 - replicator.HatchPosition).normalized;
            Vector3 farStart = slot0 + outward * 5f;
            farStart.y = 0f;

            RobotEnemy sludger = NewSludger(farStart);
            replicator.TickLure();
            Assert.AreEqual(slot0, sludger.ReplicatorSeekTarget, "setup failure: TickLure must assign this robot to queue slot 0");

            // Seed _seekElapsedSeconds straight past the 6s threshold (reflection — same "Set" idiom
            // every other test in this suite uses for internal state) rather than spending 6 real
            // simulated seconds on the ordinary pre-fallback walk: there is no real floor in this
            // isolated scene, and letting gravity/fall-recovery (MV-946/MV-952) run for that long before
            // the glide branch's own gravity-skip kicks in risks exactly the "recovery undoes progress"
            // fight TickReplicatorSeeking's own doc comment already warns about — ApplyGravity/fall-
            // recovery are skipped for the WHOLE run this way, same as a real pull would get once it
            // actually engages. This also means every tick in this run is a pull tick, which is exactly
            // what the ticket's own AC is about.
            typeof(RobotEnemy).GetField("_seekElapsedSeconds", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(sludger, 6f);

            Assert.IsFalse(replicator.PullBeamActive, "the pull beam must start OFF — nothing has run past the 6s fallback yet");

            const float dt = 0.02f;
            const int maxSteps = 1000; // 20s — generous margin for ~3.6s glide to arrive + ~2s worst-case Intake
            float elapsed = 0f;
            bool sawGlideTick = false;
            bool sawHatchSwungOpen = false;
            bool sawPassThroughDeepEnoughAndShrunk = false;
            bool despawned = false;
            Transform hatch = Get<Transform>(replicator, "_hatch");
            Quaternion closedHatchRotation = hatch.rotation;
            Vector3 hullFaceNormal = Vector3.forward; // box authored unrotated; hatch sits on the -Z face
            float walkCapPerTick = sludger.EffectiveMoveSpeed * dt + 0.01f; // small float-noise margin

            for (int i = 0; i < maxSteps && !despawned; i++)
            {
                Vector3 before = sludger.transform.position;
                InvokeTick(sludger, dt);
                Vector3 after = sludger.transform.position;
                // Read BEFORE TickConsumption — this tick's movement (InvokeTick, above) is what the
                // speed cap judges, regardless of whether this same TickConsumption call below is the
                // one that happens to hand the robot off to Intake.
                bool wasGliding = sludger.IsInGuaranteedArrivalGlide;

                replicator.TickConsumption(dt);
                elapsed += dt;

                // Read AGAIN, AFTER TickConsumption — on the exact tick arrival hands the robot to
                // Intake (BeginReplicatorIntake sets IsBeingDrawnIn inside that same call), the pull is
                // legitimately over by the time this line runs even though it was still live when
                // InvokeTick ran a moment earlier; the beam's own resolved state reflects that same
                // end-of-tick snapshot, so it must be judged against it, not the pre-TickConsumption flag.
                bool stillGliding = sludger.IsInGuaranteedArrivalGlide;

                if (wasGliding)
                {
                    sawGlideTick = true;
                    float step = Vector3.Distance(before, after);
                    Assert.LessOrEqual(step, walkCapPerTick,
                        $"MV-1067: a robot in the 6s fallback pull must move no more than its own " +
                        $"walking speed x dt per tick (cap={walkCapPerTick:F4}m, actual={step:F4}m, t={elapsed:F2}s) " +
                        "— never a teleport/snap (the pre-fix 40 m/s glide).");
                }

                if (stillGliding)
                {
                    Assert.IsTrue(replicator.PullBeamActive,
                        $"MV-1067: the beam renderer must be enabled for every tick of the pull (t={elapsed:F2}s)");
                }

                Quaternion delta = hatch.rotation * Quaternion.Inverse(closedHatchRotation);
                delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
                if (angleDeg > 180f) angleDeg = 360f - angleDeg;
                if (angleDeg >= 70f && Mathf.Abs(Vector3.Dot(axis.normalized, hullFaceNormal)) < 0.2f)
                    sawHatchSwungOpen = true;

                if (sludger.gameObject.activeSelf)
                {
                    float depthPastHatch = Vector3.Dot(sludger.transform.position - replicator.HatchPosition, hullFaceNormal);
                    if (depthPastHatch >= 0.4f && sludger.transform.localScale.x <= 0.05f)
                        sawPassThroughDeepEnoughAndShrunk = true;
                }
                else
                {
                    despawned = true;
                    // MV-1067: "No robot may ever vanish without that Intake beat playing" — the beat
                    // must have already run to completion BY the exact tick the robot disappears, not
                    // merely at some later point.
                    Assert.IsTrue(sawPassThroughDeepEnoughAndShrunk,
                        "MV-1067: the robot despawned before the Intake pass-through-and-shrink beat ever completed — a vanish.");
                }
            }

            Assert.IsTrue(sawGlideTick, "setup failure: the robot never crossed into the guaranteed-arrival pull — widen the distance or the step budget");
            Assert.IsTrue(sawHatchSwungOpen, "the hatch must still swing open through the normal Intake beat after a fallback pull");
            Assert.IsTrue(despawned, "the robot must actually reach the hatch and complete Intake within this run");
            Assert.IsFalse(replicator.PullBeamActive, "the pull beam must be OFF again once the pull has handed off to Intake");
        }
    }
}
