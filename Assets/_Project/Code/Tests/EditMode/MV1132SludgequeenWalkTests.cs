using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1132 (MV-465 Rule 1, one new test carrying every acceptance sub-check): the Sludgequeen's six
    /// legs must actually WALK — planted feet that hold their world position while the body moves over
    /// them, stepping in two alternating tripods, never sliding.
    ///
    /// Fails on the base commit (1ce1aa2): <c>SludgequeenRig</c> there carries no <c>Tick(float)</c> —
    /// reflection finds no such method, so invoking it throws a <see cref="System.NullReferenceException"/>
    /// before any assertion below even runs. Quoted failure output: see the fix comment on the ticket.
    ///
    /// EditMode only, reflection-driven for <c>Awake</c>/<c>OnEnable</c>/<c>Tick</c> (repo convention —
    /// a plain MonoBehaviour never runs these outside Play mode, and <c>Tick</c> takes an explicit
    /// <c>dt</c> precisely so a test can drive it at a controlled 60 Hz rather than depending on
    /// <see cref="Time.deltaTime"/> — the same idiom <c>MV1083BossClosesToContactTests</c> already uses
    /// for <c>Approach(dt)</c>). Builds the real <see cref="SludgequeenRig"/> on a real
    /// <see cref="SludgequeenBoss"/> at the authored body width of 6 (scale 1), same fixture shape
    /// <c>MV1124SludgequeenBodyTests</c> already uses, then drives the boss's own transform directly
    /// (the rig's "real follow path" is <see cref="SludgequeenRig"/> reading that transform every tick —
    /// exactly what AC1 asks to exercise, not the boss's own pursuit AI, which is untouched by this
    /// ticket).
    /// </summary>
    public sealed class MV1132SludgequeenWalkTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const float Dt = 1f / 60f;
        private const float MoveSpeed = 0.9f;   // BossTuning.MoveSpeed
        private const float FootRestHeight = 0.12f;   // SludgequeenRig's authored FootY at scale 1
        private const float HomeRadius = 3.85f;        // SludgequeenRig's authored FootRadius at scale 1
        private const float GroundedEpsilon = 0.01f;

        private GameObject _bossGo;
        private SludgequeenRig _rig;
        private MethodInfo _tickMethod;

        [SetUp]
        public void SetUp() => SludgequeenBoss.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            if (_rig != null) Object.DestroyImmediate(_rig.gameObject);
            if (_bossGo != null) Object.DestroyImmediate(_bossGo);
            SludgequeenBoss.ResetRegistry();
            BossCensus.Reset();

            // Rings this test's walk spawned are real scene objects (GroundRing.Create is not parented
            // to the rig) — EditMode shares one scene for the whole cc-verify run, so any that outlived
            // this test's own 0.5s-later check must not leak into the next fixture.
            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include))
                if (ring != null && ring.name == "SludgequeenFootfallRing") Object.DestroyImmediate(ring.gameObject);
        }

        private void Tick(float dt) => _tickMethod.Invoke(_rig, new object[] { dt });

        private static float FootHeightAboveRest(SludgequeenRig rig, int leg) =>
            rig.LegFoot(leg).position.y - FootRestHeight;

        private static Vector3 HomeFor(int leg, Vector3 bodyPos, float bodyYawDeg)
        {
            float angleDeg = bodyYawDeg + 30f + leg * 60f;
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            Vector3 home = bodyPos + dir * HomeRadius;
            home.y = bodyPos.y + FootRestHeight;
            return home;
        }

        /// <summary>Perpendicular distance from <paramref name="p"/> to the infinite line through
        /// <paramref name="a"/>/<paramref name="b"/> — AC1e's "knee at least 0.3 m above the straight
        /// line from hip to foot".</summary>
        private static float DistanceToLine(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float lenSq = d.sqrMagnitude;
            if (lenSq < 1e-8f) return Vector3.Distance(p, a);
            float t = Vector3.Dot(p - a, d) / lenSq;
            Vector3 closest = a + d * t;
            return Vector3.Distance(p, closest);
        }

        [Test]
        public void SixLegsPlantLiftAndStep_WithNoSlidingFeet()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            go.transform.localScale = Vector3.one * 6f;   // authored body width -> rig scale 1
            var boss = go.AddComponent<SludgequeenBoss>();
            _bossGo = go;

            typeof(SludgequeenBoss).GetMethod("Awake", NonPublicInstance).Invoke(boss, null);
            boss.SetArenaBounds(new Rect(-50f, -50f, 100f, 100f));

            _rig = SludgequeenRig.CreateFor(boss);
            Assert.IsNotNull(_rig, "CreateFor must build a rig for a live boss");
            typeof(SludgequeenRig).GetMethod("OnEnable", NonPublicInstance).Invoke(_rig, null);

            _tickMethod = typeof(SludgequeenRig).GetMethod("Tick", NonPublicInstance);
            Assert.IsNotNull(_tickMethod, "SludgequeenRig must expose a dt-driven Tick for this test to drive");

            // ---------------------------------------------------------------- AC1 a-e, i: walk 6 m straight

            int[] liftCount = new int[SludgequeenRig.LegCount];
            float[] episodeMax = new float[SludgequeenRig.LegCount];
            bool[] wasAirborne = new bool[SludgequeenRig.LegCount];
            Vector3[] prevFootXZ = new Vector3[SludgequeenRig.LegCount];
            for (int i = 0; i < SludgequeenRig.LegCount; i++)
                prevFootXZ[i] = Flatten(_rig.LegFoot(i).position);

            GroundRing firstLandingRing = null;
            Vector3 firstLandingFootPos = default;
            bool sawFirstLanding = false;

            float distanceWalked = 0f;
            int walkTicks = Mathf.CeilToInt((6f / MoveSpeed) / Dt);
            for (int tick = 0; tick < walkTicks; tick++)
            {
                boss.transform.position += new Vector3(0f, 0f, MoveSpeed * Dt);
                distanceWalked += MoveSpeed * Dt;
                Tick(Dt);

                bool[] airborne = new bool[SludgequeenRig.LegCount];
                for (int i = 0; i < SludgequeenRig.LegCount; i++)
                    airborne[i] = FootHeightAboveRest(_rig, i) > GroundedEpsilon;

                // AC1b: at most three feet off the floor, never two neighbours together.
                int airborneCount = 0;
                for (int i = 0; i < SludgequeenRig.LegCount; i++) if (airborne[i]) airborneCount++;
                Assert.LessOrEqual(airborneCount, 3, $"tick {tick}: more than three feet off the floor at once");
                for (int i = 0; i < SludgequeenRig.LegCount; i++)
                {
                    int right = (i + 1) % SludgequeenRig.LegCount;
                    Assert.IsFalse(airborne[i] && airborne[right],
                        $"tick {tick}: neighbouring feet {i} and {right} are both off the floor");
                }

                for (int i = 0; i < SludgequeenRig.LegCount; i++)
                {
                    float h = FootHeightAboveRest(_rig, i);

                    // AC1d: never below resting height.
                    Assert.GreaterOrEqual(h, -0.001f, $"tick {tick}, leg {i}: foot sank below its resting height");

                    if (airborne[i])
                    {
                        if (!wasAirborne[i]) liftCount[i]++;
                        episodeMax[i] = Mathf.Max(episodeMax[i], h);
                    }
                    else
                    {
                        // AC1c: a foot that was ALSO on the floor last tick must not have slid, even
                        // though the body moved underneath it.
                        if (!wasAirborne[i])
                        {
                            float slid = Vector3.Distance(Flatten(_rig.LegFoot(i).position), prevFootXZ[i]);
                            Assert.LessOrEqual(slid, 0.01f,
                                $"tick {tick}, leg {i}: planted foot moved {slid:F4} m horizontally while the body walked");
                        }
                        else
                        {
                            // Just landed -- AC1d's per-step peak check.
                            Assert.GreaterOrEqual(episodeMax[i], 0.5f, $"leg {i}: step peak {episodeMax[i]:F2} m was under 0.5 m");
                            Assert.LessOrEqual(episodeMax[i], 0.9f, $"leg {i}: step peak {episodeMax[i]:F2} m was over 0.9 m");
                            episodeMax[i] = 0f;

                            // AC1i: a ring exists within 0.1 m of the foot the tick it lands.
                            if (!sawFirstLanding)
                            {
                                sawFirstLanding = true;
                                firstLandingFootPos = _rig.LegFoot(i).position;
                                foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Exclude))
                                {
                                    if (ring.name != "SludgequeenFootfallRing") continue;
                                    if (Vector3.Distance(ring.transform.position, firstLandingFootPos) <= 0.1f)
                                    {
                                        firstLandingRing = ring;
                                        break;
                                    }
                                }
                                Assert.IsNotNull(firstLandingRing,
                                    $"no footfall ring within 0.1 m of the landed foot at {firstLandingFootPos}");
                            }
                        }
                    }
                    wasAirborne[i] = airborne[i];
                    prevFootXZ[i] = Flatten(_rig.LegFoot(i).position);

                    // AC1e: segment lengths preserved, knee elevated off the hip-foot line.
                    Vector3 hip = _rig.LegHip(i).position;
                    Vector3 knee = _rig.LegKnee(i).position;
                    Vector3 foot = _rig.LegFoot(i).position;
                    float upperLen = Vector3.Distance(hip, knee);
                    float lowerLen = Vector3.Distance(knee, foot);
                    Assert.AreEqual(1.82f, upperLen, 0.03f, $"tick {tick}, leg {i}: upper segment length drifted to {upperLen:F3} m");
                    Assert.AreEqual(3.0f, lowerLen, 0.03f, $"tick {tick}, leg {i}: lower segment length drifted to {lowerLen:F3} m");
                    float kneeHeight = DistanceToLine(knee, hip, foot);
                    Assert.GreaterOrEqual(kneeHeight, 0.3f, $"tick {tick}, leg {i}: knee only {kneeHeight:F2} m off the hip-foot line");
                }
            }

            Assert.IsTrue(sawFirstLanding, "the walk never produced a single footfall -- the whole gait never engaged");
            for (int i = 0; i < SludgequeenRig.LegCount; i++)
                Assert.GreaterOrEqual(liftCount[i], 3, $"leg {i} left the floor only {liftCount[i]} time(s) over a 6 m walk");

            // AC1i continued: 0.5 s (well past the ring's own 0.45 s life) after that first landing, the
            // ring must be gone or invisible. The walk above already ran several seconds past it.
            Assert.IsTrue(firstLandingRing == null || !firstLandingRing.Visible,
                "the footfall ring from the first landing is still visible long after its own life");

            // ---------------------------------------------------------------- AC1f: the body stops

            int settleTicks = Mathf.CeilToInt(0.5f / Dt);
            for (int tick = 0; tick < settleTicks; tick++) Tick(Dt);

            for (int i = 0; i < SludgequeenRig.LegCount; i++)
                Assert.LessOrEqual(FootHeightAboveRest(_rig, i), GroundedEpsilon,
                    $"leg {i} still airborne {0.5f:F1}s after the body stopped");

            Vector3[] settledFootPos = new Vector3[SludgequeenRig.LegCount];
            for (int i = 0; i < SludgequeenRig.LegCount; i++) settledFootPos[i] = _rig.LegFoot(i).position;

            int holdTicks = Mathf.CeilToInt(2f / Dt);
            for (int tick = 0; tick < holdTicks; tick++)
            {
                Tick(Dt);
                for (int i = 0; i < SludgequeenRig.LegCount; i++)
                    Assert.LessOrEqual(Vector3.Distance(_rig.LegFoot(i).position, settledFootPos[i]), 0.001f,
                        $"tick {tick} after stopping, leg {i} moved while the body stood still");
            }

            // ---------------------------------------------------------------- AC1g: turn 90 degrees on the spot

            bool[] steppedDuringTurn = new bool[SludgequeenRig.LegCount];
            float turnTicks = Mathf.CeilToInt(2f / Dt);
            float degPerTick = 90f / turnTicks;
            for (int tick = 0; tick < turnTicks; tick++)
            {
                boss.transform.rotation = Quaternion.Euler(0f, boss.transform.eulerAngles.y + degPerTick, 0f);
                Tick(Dt);
                for (int i = 0; i < SludgequeenRig.LegCount; i++)
                    if (FootHeightAboveRest(_rig, i) > GroundedEpsilon) steppedDuringTurn[i] = true;
            }

            for (int i = 0; i < SludgequeenRig.LegCount; i++)
                Assert.IsTrue(steppedDuringTurn[i], $"leg {i} never stepped while the body turned 90 degrees on the spot");

            Vector3 bodyPosAfterTurn = boss.transform.position;
            float yawAfterTurn = boss.transform.eulerAngles.y;
            for (int i = 0; i < SludgequeenRig.LegCount; i++)
            {
                Vector3 home = HomeFor(i, bodyPosAfterTurn, yawAfterTurn);
                float d = Vector3.Distance(_rig.LegFoot(i).position, home);
                Assert.LessOrEqual(d, 0.8f, $"leg {i} ended {d:F2} m from its home after the 90-degree turn");
            }

            // ---------------------------------------------------------------- AC1h: a 10 m jump in one tick

            boss.transform.position += new Vector3(10f, 0f, 0f);
            Tick(Dt);

            Vector3 bodyPosAfterJump = boss.transform.position;
            float yawAfterJump = boss.transform.eulerAngles.y;
            for (int i = 0; i < SludgequeenRig.LegCount; i++)
            {
                Vector3 home = HomeFor(i, bodyPosAfterJump, yawAfterJump);
                float d = Vector3.Distance(_rig.LegFoot(i).position, home);
                Assert.LessOrEqual(d, 0.1f, $"leg {i} is {d:F2} m from home the same tick as a 10 m jump");
            }
        }

        private static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
