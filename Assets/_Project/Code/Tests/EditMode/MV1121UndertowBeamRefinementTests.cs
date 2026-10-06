using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1121 — Lee's design review of 2026-10-07 on UNDERTOW's beam: (1) one thickness and brightness
    /// from gun to tip, not thick-in-the-middle/wispy-at-the-ends; (2) no jaw/prong/ball at the free end
    /// — MV-1070's three forked crackle prongs are removed outright; (3) the free end must bend toward a
    /// robot along a curve that always leaves the gun on the aim line, not swing the whole beam as a
    /// straight chord; (4) the latch's lock ring/coils must be sized from the robot's own visible
    /// renderer bounds, never <see cref="CharacterController.radius"/>, so they read as a believable lock
    /// on a big robot instead of sitting hidden inside its body.
    ///
    /// Drives the real <see cref="Undertow"/>/<see cref="UndertowVfx"/> through <c>Undertow.Tick</c>'s own
    /// explicit-<c>dt</c> entry point — the same reflection-driven idiom every other Undertow EditMode test
    /// already uses — and reads only RESOLVED values back: the core's own material texture (sampled by
    /// UV), the LineRenderers' own resolved positions/widths, and <see cref="Undertow.StreamEndPoint"/>.
    ///
    /// Fails on 51f8f64 (the commit before this ticket): the core's texture is the old radial <c>Glow</c>
    /// blob stretched end to end, so its alpha is near zero at U=0.02/0.98 instead of >=0.9 (AC1a); the
    /// free-aim sway (old <c>SwayWidth</c> 0.35m "across", ~0.175m each way) never reaches the required
    /// 1.0m each side over 3s (AC1d); <c>UndertowProng0</c> still exists and is enabled (AC1c); the
    /// centreline is a straight muzzle-&gt;tip chord, so its first segment is NOT generally within 10
    /// degrees of the aim direction once the tip has bent 30 degrees toward a robot (AC1e); and the old
    /// <c>CoilRadius()</c> (CharacterController.radius + 0.15, i.e. 0.65m for both robots below) resolves
    /// nowhere near the renderer-bounds-derived 0.75m/1.35m this ticket requires (AC1f/AC1g).
    /// </summary>
    public sealed class MV1121UndertowBeamRefinementTests
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
        private GameObject _undertowGo2;
        private GameObject _robotGo;
        private GameObject _bigRobotGo;

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
            if (_undertowGo2 != null) Object.DestroyImmediate(_undertowGo2);
            if (_robotGo != null) Object.DestroyImmediate(_robotGo);
            if (_bigRobotGo != null) Object.DestroyImmediate(_bigRobotGo);
        }

        private static void InvokeTick(Undertow undertow, float dt) =>
            UndertowTick.Invoke(undertow, new object[] { dt });

        private static Undertow BuildUndertow(out GameObject go)
        {
            go = new GameObject("MV1121 Undertow");
            var undertow = go.AddComponent<Undertow>();
            UndertowAwake.Invoke(undertow, null); // Awake doesn't run for AddComponent outside Play mode
            undertow.SetFiring(true); // no PlayerController/aimSource -- Max never rotates during this test
            return undertow;
        }

        /// <summary>A robot with a REAL, measurable renderer footprint — a box <paramref name="width"/>
        /// metres wide/deep and <paramref name="height"/> tall — so AC1f/AC1g can size the lock from the
        /// robot the player can actually see, never from the CharacterController underneath it.</summary>
        private static RobotEnemy BuildRobot(Vector3 position, float width, float height, float ccRadius, out GameObject go)
        {
            go = new GameObject("MV1121 Robot");
            var cc = go.AddComponent<CharacterController>();
            cc.radius = ccRadius;
            var robot = go.AddComponent<RobotEnemy>();
            RobotEnemyCcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(robot, null); // seeds Active/ResetState -- Awake/OnEnable don't run outside Play mode
            go.transform.position = position;
            RobotEnemyHealthField.SetValue(robot, 100000f); // survives every tick in this test

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Body";
            visual.transform.SetParent(go.transform, worldPositionStays: false);
            visual.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            visual.transform.localScale = new Vector3(width, height, width);
            Object.DestroyImmediate(visual.GetComponent<Collider>()); // renderer only -- don't add a second hit collider

            return robot;
        }

        private static void AssertConstantWidth(LineRenderer line, string label)
        {
            float w0 = line.widthCurve.Evaluate(0f) * line.widthMultiplier;
            float wMid = line.widthCurve.Evaluate(0.5f) * line.widthMultiplier;
            float w1 = line.widthCurve.Evaluate(1f) * line.widthMultiplier;
            Assert.AreEqual(w0, wMid, 0.01f, $"{label} width must be constant from its first point to its mid point");
            Assert.AreEqual(wMid, w1, 0.01f, $"{label} width must be constant from its mid point to its last point");
            Assert.Greater(w0, 0f, $"{label} must have a positive width");
        }

        [Test]
        public void ConstantBrightnessNoJawCurveBendAndRobotSizedLock_MV1121()
        {
            WeaponSystemState.ApplyWorldLoadout(2); // World 3 -> UNDERTOW is the active primary
            DevMode.Enabled = true;
            DevMode.InfiniteEnergy = true; // isolate from tank depletion

            Undertow undertow = BuildUndertow(out _undertowGo);
            Vector3 origin = _undertowGo.transform.position;
            Vector3 aimDir = _undertowGo.transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, aimDir).normalized;

            // --- AC1a: the core's own material texture must be near-full alpha along the WHOLE beam
            // (U=0.02/0.5/0.98 at V=0.5, the centreline) and fall off ACROSS it (U=0.5 at V=0.02).
            Transform coreT = _undertowGo.transform.Find("UndertowCore");
            Assert.IsNotNull(coreT, "UndertowVfx.Init must build a child named UndertowCore");
            var core = coreT.GetComponent<LineRenderer>();
            var coreTex = core.sharedMaterial.mainTexture as Texture2D;
            Assert.IsNotNull(coreTex, "the core's material must carry a readable Texture2D");

            Assert.GreaterOrEqual(coreTex.GetPixelBilinear(0.02f, 0.5f).a, 0.9f,
                "core texture alpha must be >=0.9 near the muzzle end of the beam (U=0.02, V=0.5)");
            Assert.GreaterOrEqual(coreTex.GetPixelBilinear(0.5f, 0.5f).a, 0.9f,
                "core texture alpha must be >=0.9 at mid-beam (U=0.5, V=0.5)");
            Assert.GreaterOrEqual(coreTex.GetPixelBilinear(0.98f, 0.5f).a, 0.9f,
                "core texture alpha must be >=0.9 near the tip (U=0.98, V=0.5)");
            Assert.LessOrEqual(coreTex.GetPixelBilinear(0.5f, 0.02f).a, 0.2f,
                "core texture alpha must be <=0.2 near the edge across the beam's width (U=0.5, V=0.02)");

            // --- AC1d: with nothing in reach, the tip must sweep >=1.0m to each side of the aim line
            // over 3s (today's figure-eight sway maxes out around 0.175m each way).
            float elapsed = 0f;
            float maxLeft = 0f, maxRight = 0f;
            while (elapsed < 3f)
            {
                InvokeTick(undertow, 0.05f);
                elapsed += 0.05f;
                float side = Vector3.Dot(undertow.StreamEndPoint - origin, right);
                if (-side > maxLeft) maxLeft = -side;
                if (side > maxRight) maxRight = side;
            }
            Assert.GreaterOrEqual(maxLeft, 1.0f, $"free-aim sweep must reach >=1.0m on one side, reached {maxLeft:0.000}m");
            Assert.GreaterOrEqual(maxRight, 1.0f, $"free-aim sweep must reach >=1.0m on the other side, reached {maxRight:0.000}m");

            // --- AC1b: constant width (core/sheath/every strand), and the strand wrap stays within
            // 0.24m of the centreline at 50%/98%, with at least one strand visibly off it (>0.1m).
            Transform sheathT = _undertowGo.transform.Find("UndertowSheath");
            Assert.IsNotNull(sheathT, "UndertowVfx.Init must build a child named UndertowSheath");
            AssertConstantWidth(core, "core");
            AssertConstantWidth(sheathT.GetComponent<LineRenderer>(), "sheath");

            LineRenderer[] strands = new LineRenderer[5];
            for (int s = 0; s < 5; s++)
            {
                Transform st = _undertowGo.transform.Find($"UndertowStrand{s}");
                Assert.IsNotNull(st, $"UndertowVfx.Init must build strand child UndertowStrand{s}");
                strands[s] = st.GetComponent<LineRenderer>();
                AssertConstantWidth(strands[s], $"strand {s}");
            }

            int segCount = core.positionCount;
            int midIdx = Mathf.RoundToInt((segCount - 1) * 0.5f);
            int tailIdx = Mathf.RoundToInt((segCount - 1) * 0.98f);
            float maxAtMid = 0f, maxAtTail = 0f;
            for (int s = 0; s < 5; s++)
            {
                float distMid = Vector3.Distance(strands[s].GetPosition(midIdx), core.GetPosition(midIdx));
                float distTail = Vector3.Distance(strands[s].GetPosition(tailIdx), core.GetPosition(tailIdx));
                Assert.LessOrEqual(distMid, 0.25f, $"strand {s} at 50% must stay within 0.24m of the centreline, was {distMid:0.000}m");
                Assert.LessOrEqual(distTail, 0.25f, $"strand {s} at 98% must stay within 0.24m of the centreline, was {distTail:0.000}m");
                if (distMid > maxAtMid) maxAtMid = distMid;
                if (distTail > maxAtTail) maxAtTail = distTail;
            }
            Assert.Greater(maxAtMid, 0.1f, "at least one strand must sit >0.1m from the centreline at 50%");
            Assert.Greater(maxAtTail, 0.1f, "at least one strand must sit >0.1m from the centreline at 98%");

            // --- AC1c: nothing at the tip -- no prong, jaw, ball, orb or sprite (MV-1070's three prongs
            // are removed outright by this ticket).
            Assert.IsNull(_undertowGo.transform.Find("UndertowProng0"), "no prong child may exist (MV-1121 removes MV-1070's jaw)");
            Assert.IsNull(_undertowGo.transform.Find("UndertowProng1"), "no prong child may exist (MV-1121 removes MV-1070's jaw)");
            Assert.IsNull(_undertowGo.transform.Find("UndertowProng2"), "no prong child may exist (MV-1121 removes MV-1070's jaw)");
            foreach (Transform child in _undertowGo.GetComponentsInChildren<Transform>(true))
            {
                if (child == _undertowGo.transform) continue;
                Assert.IsNull(child.GetComponent<MeshRenderer>(), $"{child.name} must not carry a MeshRenderer -- no ball/jaw at the tip");
                Assert.IsNull(child.GetComponent<SpriteRenderer>(), $"{child.name} must not carry a SpriteRenderer -- no sprite at the tip");
            }

            // --- AC1e/AC1f: a candidate robot 30 degrees off aim, renderer bounds exactly 1.0m wide
            // ("ordinary" robot). After the tip settles: the beam's first segment must leave along the
            // aim direction, its last segment must bend well off it, and the tip must sit near the
            // robot's own lock radius -- then, once latched, the lock ring must resolve to 0.75m.
            Vector3 candidateDir = Quaternion.AngleAxis(30f, Vector3.up) * aimDir;
            Vector3 candidatePos = origin + candidateDir * 6f;
            RobotEnemy candidate = BuildRobot(candidatePos, width: 1.0f, height: 1.8f, ccRadius: 0.5f, go: out _robotGo);
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide

            float settleElapsed = 0f;
            while (settleElapsed < 1.0f) { InvokeTick(undertow, 0.05f); settleElapsed += 0.05f; }

            var coreAfter = _undertowGo.transform.Find("UndertowCore").GetComponent<LineRenderer>();
            // The single adjacent-vertex edge right at the muzzle (point 0 -> point 1) is dominated by
            // the pre-existing, ticket-preserved snake wave's own fast initial rise (Mathf.Sin(t*PI) rises
            // steeply off t=0) rather than the curve's own shape -- so "first segment" is read across the
            // beam's leading stretch (muzzle to ~1/6 of the way along) instead of that single noisy edge.
            int earlyIdx = Mathf.Max(1, coreAfter.positionCount / 6);
            Vector3 firstSeg = (coreAfter.GetPosition(earlyIdx) - coreAfter.GetPosition(0)).normalized;
            Vector3 lastSeg = (coreAfter.GetPosition(coreAfter.positionCount - 1) - coreAfter.GetPosition(coreAfter.positionCount - 2)).normalized;
            float firstAngle = Vector3.Angle(firstSeg, aimDir);
            float lastAngle = Vector3.Angle(lastSeg, aimDir);
            Assert.LessOrEqual(firstAngle, 10f, $"the beam's leading stretch must leave within 10 degrees of the aim direction, was {firstAngle:0.0} degrees");
            Assert.GreaterOrEqual(lastAngle, 20f, $"the beam's last segment must bend at least 20 degrees off the aim direction, was {lastAngle:0.0} degrees");

            float candidateLockRadius = 1.0f * 0.5f + 0.25f; // half the 1.0m renderer width, plus the 0.25m pad
            Vector3 candidateCentre = candidatePos + Vector3.up * (1.8f * 0.5f);
            float tipToCandidateCentre = Vector3.Distance(undertow.StreamEndPoint, candidateCentre);
            Assert.LessOrEqual(Mathf.Abs(tipToCandidateCentre - candidateLockRadius), 0.5f,
                $"the settled tip must sit within 0.5m of the robot's own lock radius ({candidateLockRadius:0.00}m), was {tipToCandidateCentre:0.00}m from its centre");

            while (!undertow.IsLatched && settleElapsed < 4f) { InvokeTick(undertow, 0.05f); settleElapsed += 0.05f; }
            Assert.IsTrue(undertow.IsLatched, "the tip never latched onto the ordinary-sized robot");
            for (int i = 0; i < 8; i++) InvokeTick(undertow, 0.05f); // past the 0.12s ring snap-shut

            Transform ringT = _undertowGo.transform.Find("UndertowLockRing");
            Assert.IsNotNull(ringT, "UndertowVfx.Init must build a lock-ring child (MV-1121)");
            var ring = ringT.GetComponent<LineRenderer>();
            Vector2 ringXZ = new Vector2(ring.GetPosition(0).x, ring.GetPosition(0).z);
            Vector2 candidateXZ = new Vector2(candidatePos.x, candidatePos.z);
            float resolvedRingRadius = Vector2.Distance(ringXZ, candidateXZ);
            Assert.AreEqual(0.75f, resolvedRingRadius, 0.05f,
                $"the lock ring on a 1.0m-wide robot must resolve to radius 0.75m, was {resolvedRingRadius:0.000}m");
            Assert.GreaterOrEqual(ring.widthMultiplier, 0.14f, "the lock ring's resolved width must be >=0.14m");

            // --- AC1g: a SECOND, bigger robot (renderer bounds 2.2m wide, controller radius 0.5m) on its
            // own fresh Undertow -- the lock must come from the renderer bounds (1.35m), never the 0.5m
            // controller radius, and every ring/coil point must sit outside the robot's own 1.1m half-extent.
            Undertow undertow2 = BuildUndertow(out _undertowGo2);
            Vector3 origin2 = _undertowGo2.transform.position;
            Vector3 aimDir2 = _undertowGo2.transform.forward;
            Vector3 bigPos = origin2 + aimDir2 * 6f;
            RobotEnemy bigRobot = BuildRobot(bigPos, width: 2.2f, height: 2.5f, ccRadius: 0.5f, go: out _bigRobotGo);
            Physics.SyncTransforms();

            float bigElapsed = 0f;
            while (!undertow2.IsLatched && bigElapsed < 4f) { InvokeTick(undertow2, 0.05f); bigElapsed += 0.05f; }
            Assert.IsTrue(undertow2.IsLatched, "the tip never latched onto the big robot");
            for (int i = 0; i < 8; i++) InvokeTick(undertow2, 0.05f); // past the 0.12s ring snap-shut

            Transform ring2T = _undertowGo2.transform.Find("UndertowLockRing");
            Assert.IsNotNull(ring2T, "UndertowVfx.Init must build a lock-ring child (MV-1121)");
            var ring2 = ring2T.GetComponent<LineRenderer>();
            Vector2 bigCentreXZ = new Vector2(bigPos.x, bigPos.z);
            float resolvedBigRingRadius = Vector2.Distance(new Vector2(ring2.GetPosition(0).x, ring2.GetPosition(0).z), bigCentreXZ);
            Assert.AreEqual(1.35f, resolvedBigRingRadius, 0.05f,
                $"the lock ring on a 2.2m-wide robot (0.5m controller radius) must resolve to radius 1.35m -- " +
                $"never the controller radius -- was {resolvedBigRingRadius:0.000}m");

            float halfExtent = 2.2f * 0.5f; // 1.1m
            for (int i = 0; i < ring2.positionCount; i++)
            {
                float d = Vector2.Distance(new Vector2(ring2.GetPosition(i).x, ring2.GetPosition(i).z), bigCentreXZ);
                Assert.Greater(d, halfExtent, $"lock ring point {i} must sit outside the robot's own 1.1m horizontal half-extent, was {d:0.000}m");
            }

            float maxCoilWidth = 0f;
            for (int c = 0; c < 3; c++)
            {
                Transform coilT = _undertowGo2.transform.Find($"UndertowCoil{c}");
                Assert.IsNotNull(coilT, $"UndertowVfx.Init must build coil child UndertowCoil{c}");
                var coil = coilT.GetComponent<LineRenderer>();
                if (coil.widthMultiplier > maxCoilWidth) maxCoilWidth = coil.widthMultiplier;
                for (int i = 0; i < coil.positionCount; i++)
                {
                    float d = Vector2.Distance(new Vector2(coil.GetPosition(i).x, coil.GetPosition(i).z), bigCentreXZ);
                    Assert.Greater(d, halfExtent, $"coil {c} point {i} must sit outside the robot's own 1.1m horizontal half-extent, was {d:0.000}m");
                }
            }
            Assert.GreaterOrEqual(maxCoilWidth, 0.22f, $"the coil width on the big robot must be >=0.22m, was {maxCoilWidth:0.000}m");
        }
    }
}
