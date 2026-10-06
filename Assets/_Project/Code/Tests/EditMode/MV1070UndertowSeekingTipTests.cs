using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1070 — World 3's UNDERTOW beam's tip must genuinely SEEK a robot (magnetic pull, visible
    /// overshoot, snap-on within <see cref="Undertow.LatchDistance"/>) rather than only latching when
    /// Max is already aimed dead-on inside the lance's own 3-degree fire cone (MV-1064's shape).
    ///
    /// A standalone <see cref="Undertow"/> with no <see cref="MaxWorlds.Player.PlayerController"/> aim
    /// source (same "fresh probe, aimSource stays null, SetFiring drives it directly" idiom
    /// <c>CaptureDirector.BuildMv1064UndertowLatchCheck</c> already uses) so Max's own facing never moves
    /// during the test — spec's own "Max still points it in the general direction... Max not rotating"
    /// framing — ticked through <c>Undertow.Tick</c>'s own explicit-<c>dt</c> method, the same
    /// reflection-driven, real-entry-point idiom every other Undertow EditMode test already uses.
    ///
    /// Fails on b2589a1 (the commit before this ticket): a robot 25 degrees off aim sits outside
    /// <c>Undertow</c>'s old 3-degree acquire cone, so it is never latched and never damaged at all (the
    /// first "reaches within 0.4m" assertion below times out).
    ///
    /// MV-1121 removed this test's own former closing block: it asserted three forked "prong" child
    /// LineRenderers at the tip (MV-1070's deliberate finish), which MV-1121 explicitly deleted outright
    /// ("no jaw, prong, cross-bar, ball, orb, dot or sprite at the tip" — Lee, 2026-10-07) and replaced
    /// with a plain cut end; that removal — and the finish itself — is covered by
    /// <c>MV1121UndertowBeamRefinementTests</c> instead. MV-1121 also widened the free-aim sway (Change
    /// 3), which is why this test's own stay-near-rest budget below reads 1.5m rather than its original
    /// 0.6m.
    /// </summary>
    public sealed class MV1070UndertowSeekingTipTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo UndertowAwake =
            typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick =
            typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly FieldInfo RobotEnemyCcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo RobotEnemyHealthField =
            typeof(RobotEnemy).GetField("_health", NonPublicInstance);

        private GameObject _undertowGo;
        private GameObject _robotAGo;
        private GameObject _robotBGo;
        private GameObject _robotCGo;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            RobotEnemy.ResetRegistry();
            if (_undertowGo != null) Object.DestroyImmediate(_undertowGo);
            if (_robotAGo != null) Object.DestroyImmediate(_robotAGo);
            if (_robotBGo != null) Object.DestroyImmediate(_robotBGo);
            if (_robotCGo != null) Object.DestroyImmediate(_robotCGo);
        }

        private static void InvokeTick(Undertow undertow, float dt) =>
            UndertowTick.Invoke(undertow, new object[] { dt });

        private static RobotEnemy BuildRobot(Vector3 position, out GameObject go)
        {
            go = new GameObject("MV1070 Robot");
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            RobotEnemyCcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(robot, null); // seeds Active/ResetState -- Awake/OnEnable don't run outside Play mode
            go.transform.position = position;
            RobotEnemyHealthField.SetValue(robot, 100000f); // survives every tick in this test
            return robot;
        }

        [Test]
        public void TipSeeksOvershootsAndLatches_IgnoresOutOfConeRobot_SwaysWithNoTarget_WhipsToNextOnDeath_FinishIsProngsNotBall_MV1070()
        {
            WeaponSystemState.ApplyWorldLoadout(2); // World 3 -> UNDERTOW is the active primary
            DevMode.Enabled = true;
            DevMode.InfiniteEnergy = true; // isolate seek/latch timing from tank depletion

            _undertowGo = new GameObject("MV1070 Undertow");
            var undertow = _undertowGo.AddComponent<Undertow>();
            UndertowAwake.Invoke(undertow, null); // Awake doesn't run for AddComponent outside Play mode
            undertow.SetFiring(true); // no PlayerController/aimSource -- Max never rotates during this test

            Vector3 origin = _undertowGo.transform.position;
            Vector3 aimDir = _undertowGo.transform.forward; // world forward, fixed for the whole test

            // --- AC1 (no-candidate half): before any robot exists, the tip must sway rather than sit
            // dead still, and stay close to the aim point at full Range.
            for (int i = 0; i < 10; i++) InvokeTick(undertow, 0.05f); // settle into the steady sway first
            Vector3 swayA = undertow.StreamEndPoint;
            for (int i = 0; i < 6; i++) InvokeTick(undertow, 0.05f); // 0.3s later
            Vector3 swayB = undertow.StreamEndPoint;

            Assert.GreaterOrEqual(Vector3.Distance(swayA, swayB), 0.1f,
                $"with no candidate the tip must sway >=0.1m over 0.3s, moved only {Vector3.Distance(swayA, swayB):0.000}m");

            // MV-1121 widened the sway from "0.35m across" to "1.25m each way" (Undertow.SwayWidth 0.35 ->
            // 2.5) so the free end reads as visibly searching -- the stay-near-rest budget widens with it.
            Vector3 restPoint = origin + aimDir * undertow.Range;
            Assert.LessOrEqual(Vector3.Distance(swayA, restPoint), 1.5f, "the sway must stay within 1.5m of the aim point at Range");
            Assert.LessOrEqual(Vector3.Distance(swayB, restPoint), 1.5f, "the sway must stay within 1.5m of the aim point at Range");

            // --- AC1 (seek half): Robot A, 6m out, 25 degrees off aim -- outside the lance's own 3-degree
            // fire cone but well inside the tip's 35-degree acquire cone. Robot B, 5m out, 50 degrees off
            // aim -- outside EVEN the latch's 45-degree break angle -- must never be sought or damaged.
            Vector3 robotADir = Quaternion.AngleAxis(25f, Vector3.up) * aimDir;
            Vector3 robotAPos = origin + robotADir * 6f;
            RobotEnemy robotA = BuildRobot(robotAPos, out _robotAGo);

            Vector3 robotBDir = Quaternion.AngleAxis(50f, Vector3.up) * aimDir;
            Vector3 robotBPos = origin + robotBDir * 5f;
            RobotEnemy robotB = BuildRobot(robotBPos, out _robotBGo);
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

            float robotBHealthStart = robotB.HealthCurrent;

            // Signed sideways offset from the straight muzzle->robotA line (AC1's own "changes sign" test):
            // a horizontal axis perpendicular to that line, fixed for the whole approach since robot A
            // never moves in this test.
            Vector3 toRobotA = (robotAPos - origin); toRobotA.y = 0f;
            Vector3 lineDir = toRobotA.normalized;
            Vector3 sideAxis = Vector3.Cross(Vector3.up, lineDir);

            bool sawPositiveSide = false, sawNegativeSide = false;
            float elapsed = 0f;
            const float dt = 0.05f;
            const float arriveBudget = 0.6f;
            const float observeWindow = 1.0f; // generous over arriveBudget -- captures the full overshoot
                                               // even though distance can dip under LatchDistance slightly
                                               // BEFORE the sideways deviation finishes its own swing back
            float? elapsedAtLatch = null;
            float robotAHealthBeforeLatch = robotA.HealthCurrent;

            while (elapsed < observeWindow)
            {
                InvokeTick(undertow, dt);
                elapsed += dt;

                Vector3 tip = undertow.StreamEndPoint;
                float side = Vector3.Dot(tip - origin, sideAxis);
                if (side > 0.02f) sawPositiveSide = true;
                if (side < -0.02f) sawNegativeSide = true;

                if (elapsedAtLatch == null)
                {
                    if (undertow.IsLatched)
                    {
                        elapsedAtLatch = elapsed;
                        Assert.AreSame(robotA.gameObject, undertow.LatchedTransform.gameObject,
                            "the first thing latched must be robot A, the only in-cone candidate");
                    }
                    else
                    {
                        Assert.AreEqual(robotAHealthBeforeLatch, robotA.HealthCurrent, 0.01f,
                            $"t={elapsed:0.00}s: robot A must take NO damage before the tip actually arrives (AC1)");
                    }
                }
            }

            Assert.IsTrue(elapsedAtLatch.HasValue, $"the tip never latched onto robot A within {observeWindow}s");
            Assert.LessOrEqual(elapsedAtLatch.Value, arriveBudget,
                $"the tip took {elapsedAtLatch.Value:0.00}s to reach robot A -- over the {arriveBudget}s budget (AC1)");
            Assert.IsTrue(sawPositiveSide && sawNegativeSide,
                "the tip's signed sideways offset from the muzzle->robotA line must change sign at least " +
                "once during the approach -- it must visibly overshoot (AC1 spec #4)");
            Assert.IsTrue(undertow.IsLatched, "the latch must still hold at the end of the observe window");

            // --- Damage cadence: a tick every 0.1s once latched; robot B (50 degrees off, never sought)
            // must take none of it.
            for (int i = 0; i < 3; i++)
            {
                float before = robotA.HealthCurrent;
                InvokeTick(undertow, 0.1f);
                Assert.Less(robotA.HealthCurrent, before, $"latched tick {i}: robot A must take a damage tick every 0.1s");
            }
            Assert.AreEqual(robotBHealthStart, robotB.HealthCurrent, 0.01f,
                "robot B sits 50 degrees off aim -- outside even the 45-degree latch break angle -- and must never be damaged (AC1)");

            // --- AC2: kill the latched robot A; robot C, 3m to its side and still inside the acquire
            // cone, must pick up the latch within 0.6s, without the tip passing back through the muzzle.
            Vector3 robotCPos = robotAPos - Vector3.right * 3f;
            RobotEnemy robotC = BuildRobot(robotCPos, out _robotCGo);
            Physics.SyncTransforms();

            RobotEnemyHealthField.SetValue(robotA, 0f); // "kill the latched robot" -- direct state, not a damage race

            float pierceElapsed = 0f;
            bool pierced = false;
            float minDistFromMuzzle = float.MaxValue;
            while (pierceElapsed < 0.6f + 0.2f && !pierced)
            {
                InvokeTick(undertow, dt);
                pierceElapsed += dt;
                minDistFromMuzzle = Mathf.Min(minDistFromMuzzle, Vector3.Distance(undertow.StreamEndPoint, origin));

                if (undertow.IsLatched && ReferenceEquals(undertow.LatchedTransform.gameObject, robotC.gameObject))
                    pierced = true;
            }

            Assert.IsTrue(pierced, "robot C never picked up the latch after robot A died (AC2)");
            Assert.LessOrEqual(pierceElapsed, 0.6f, $"robot C picked up the latch after {pierceElapsed:0.00}s -- over AC2's 0.6s budget");
            Assert.Greater(minDistFromMuzzle, 1f,
                $"the tip must whip straight across to robot C, never passing back through the muzzle -- " +
                $"closest approach to the muzzle during the hand-off was {minDistFromMuzzle:0.000}m");

            // --- AC3 (resolved values, never the authored constants themselves): let the beam's own
            // visual whip-lag (MV-1046: the centreline eases toward its target with a 0.12s lag at the
            // very tip) catch up after that whip-across hand-off before reading renderer geometry --
            // otherwise the eased VISUAL tip can still be mid-catch-up with the actual (unsmoothed)
            // combat tip this same test just asserted on above.
            for (int i = 0; i < 20; i++) InvokeTick(undertow, dt); // 1s, many lag time-constants

            // The core stays full width to the tip (MV-1121: a plain cut end, no prong/jaw/ball — see
            // MV1121UndertowBeamRefinementTests for that removal's own coverage).
            Transform coreT = _undertowGo.transform.Find("UndertowCore");
            Assert.IsNotNull(coreT, "UndertowVfx.Init must build a child named UndertowCore");
            var core = coreT.GetComponent<LineRenderer>();
            Assert.GreaterOrEqual(core.widthMultiplier, 0.22f * 0.85f,
                "the core must keep >=85% of its own width all the way to the tip (spec #8)");
        }
    }
}
