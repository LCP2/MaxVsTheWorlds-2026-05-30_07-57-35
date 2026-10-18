using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1212 (the one new test, Rule 1): with COUNT 3 (three ball headings, fanned 15 degrees apart,
    /// centred on +Z) and three alive robots placed one along each heading, the aim preview must report
    /// the SAME three target robots <see cref="LightMissileBall.SelectInitialTarget"/> itself returns for
    /// that volley -- computed here a second, independent way (driving the real selection function through
    /// the ball's own claimed-set-sharing loop directly, not trusting the preview's own internals) -- and
    /// 3 launch lines whose RESOLVED length (read back off the real <see cref="LineRenderer"/> positions
    /// <see cref="LightMissileAimPreviewVfx.Refresh"/> actually wrote, not the authored constant) is 1.5 m,
    /// within 0.05 m (MV-465 Tier 2).
    ///
    /// Fails on base commit 61cd454 (main HEAD before this ticket) -- it cannot compile:
    /// <see cref="LightMissileAimPreviewVfx"/> does not exist there at all.
    /// </summary>
    public sealed class MV1212LightMissileAimPreviewTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo CcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [Test]
        public void PreviewLocksTheSameThreeRobotsTheRealSelectionFunctionPicksAndDraws1Point5mLines()
        {
            const int count = 3;
            const float fanDegrees = 15f;
            Vector3 origin = Vector3.zero;
            Vector3 forward = Vector3.forward;

            // One robot placed well out along each of the three fan headings, so each heading's own
            // nearest-angle candidate is unambiguously its own robot.
            var robots = new RobotEnemy[count];
            for (int i = 0; i < count; i++)
            {
                float angleOffset = (i - (count - 1) / 2f) * fanDegrees;
                Vector3 dir = Quaternion.Euler(0f, angleOffset, 0f) * forward;
                robots[i] = NewRegisteredEnemy(origin + dir * 6f);
            }
            Physics.SyncTransforms();

            // --- ground truth: drive the REAL selection function directly, through the same
            // claimed-set-sharing loop LightMissile.FireVolley itself uses -- not a call into the preview.
            var expectedClaimed = new HashSet<IDamageable>();
            var expected = new RobotEnemy[count];
            for (int i = 0; i < count; i++)
            {
                float angleOffset = (i - (count - 1) / 2f) * fanDegrees;
                Vector3 dir = Quaternion.Euler(0f, angleOffset, 0f) * forward;
                Vector3 legEnd = origin + dir * LightMissileBall.LaunchLegMetres;
                IDamageable picked = LightMissileBall.SelectInitialTarget(legEnd, dir, 0,
                    LightMissileBall.TargetRangeMetres, expectedClaimed);
                Assert.IsNotNull(picked, $"test precondition: heading {i} must find a candidate");
                expectedClaimed.Add(picked);
                expected[i] = picked as RobotEnemy;
            }
            Assert.That(expected, Is.EquivalentTo(robots),
                "test precondition: the real selection function must pick all three placed robots, one per heading");

            // --- the preview under test.
            var ownerGo = new GameObject("MV1212 Owner");
            _spawned.Add(ownerGo);
            LightMissileAimPreviewVfx preview = LightMissileAimPreviewVfx.Attach(ownerGo.transform);
            preview.Refresh(true, origin, forward, count, fanDegrees, areaIndex: 0);

            Assert.AreEqual(count, preview.ActiveLineCount, "all three headings must draw a line");
            Assert.AreEqual(count, preview.LockedTargets.Count, "all three headings must report a locked robot");
            for (int i = 0; i < count; i++)
            {
                Assert.AreSame(expected[i], preview.LockedTargets[i],
                    $"heading {i}: preview's locked robot must match the real selection function's own pick");

                float resolvedLength = Vector3.Distance(preview.LinePoint(i, 0), preview.LinePoint(i, 1));
                Assert.That(resolvedLength, Is.EqualTo(LightMissileBall.LaunchLegMetres).Within(0.05f),
                    $"heading {i}: the line's own resolved endpoints must be {LightMissileBall.LaunchLegMetres}m apart");
            }
        }

        private RobotEnemy NewRegisteredEnemy(Vector3 position)
        {
            var go = new GameObject("MV1212 Robot");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            go.transform.position = position;
            e.Apply(EnemyArchetype.Rusher);
            RobotEnemyOnEnable.Invoke(e, null);
            return e;
        }
    }
}
