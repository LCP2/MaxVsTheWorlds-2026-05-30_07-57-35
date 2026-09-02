using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-657 — Lee, from play: a group of Brutes standing on screen, in a clear sight-line, right in
    /// front of him after a death/re-entry sometimes froze for the rest of that life. Proven cause:
    /// <c>RobotEnemy.Update</c> only ticked <c>Perception</c> when <c>target != null</c>, and Unity's
    /// fake-null makes a destroyed target (the player object across a respawn, or a dead Sentinel this
    /// robot had retargeted to per MV-362) read as null forever after — nothing ever re-ran
    /// <c>AcquireTarget</c> outside spawn/<c>RetargetTo</c>, so the sight tick stayed permanently
    /// skipped, <c>Perception.HasSight</c> froze at whatever it last was, and a Dormant robot gated on
    /// it (<c>TickDormant</c> -> <c>AmbushWake.ShouldWake</c>) could never wake again.
    ///
    /// Tier 2 (resolved values): asserts the RESOLVED <see cref="RobotEnemy.Sight"/>.HasSight and
    /// <see cref="RobotEnemy.Current"/> after one <c>Update()</c> tick with the target reference
    /// cleared — not the authored fields that drive them. EditMode only, reflection-driven (repo
    /// convention): <c>Update()</c> never runs outside Play mode, so it is invoked directly, the same
    /// idiom <c>MV428MeleeReadabilityTests</c> uses for the private Tick* methods, with <c>_cc</c>
    /// stamped by hand first since Awake() never runs either.
    /// </summary>
    public sealed class MV657TargetReacquireTests
    {
        private GameObject _playerGo;
        private Camera[] _suppressedAmbientCameras;

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TargetField =
            typeof(RobotEnemy).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo UpdateMethod =
            typeof(RobotEnemy).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            // MV-478 AC8: TickDormant reads Camera.main and fails OPEN (on-screen = true) when none
            // resolves — suppressing every ambient camera means the sight-line is the only thing this
            // test needs to prove, not frustum geometry (already covered by MV363DormantRobotTests).
            _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = new Vector3(0f, 0f, 5f);
        }

        [TearDown]
        public void TearDown()
        {
            CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);
            RobotEnemy.ResetRegistry();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        private static RobotEnemy NewEnemy()
        {
            var go = new GameObject("Enemy");
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.ResetState(); // finds the tagged Player via AcquireTarget, seeds sight memory (HasSight false)
            return e;
        }

        [Test]
        public void DormantRobot_ReacquiresTarget_SeesAndWakes_AfterItsTargetReferenceGoesNull()
        {
            RobotEnemy e = NewEnemy();
            try
            {
                e.BeginDormant();
                // Simulates a destroyed target reference (Unity fake-null) the way a respawned player
                // or a dead MV-362 Sentinel would leave it — nothing re-points `target` at a live actor
                // afterwards without the fix.
                TargetField.SetValue(e, null);

                UpdateMethod.Invoke(e, null); // one tick: must re-acquire, then see, then wake

                Assert.IsTrue(e.Sight.HasSight,
                    "MV-657: re-acquiring the target must let sight resolve for real on the next tick, " +
                    "not stay frozen false forever because the sight tick was never called again");
                Assert.AreEqual(RobotEnemy.State.Alert, e.Current,
                    "MV-657 AC2: a dormant robot with its target cleared must still wake within one " +
                    "tick on a clear, on-screen sight-line, on a post-death re-entry exactly as on a " +
                    "first visit");
            }
            finally { Object.DestroyImmediate(e.gameObject); }
        }
    }
}
