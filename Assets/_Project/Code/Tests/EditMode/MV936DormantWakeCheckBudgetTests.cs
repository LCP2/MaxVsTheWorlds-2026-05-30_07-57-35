using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-936 — <c>RobotEnemy.TickDormant</c> ran its frustum test (<c>IsOnScreen</c>) for every
    /// dormant robot that is NOT well behind the player (the population <c>MV611DormantAreaGateTests</c>
    /// deliberately keeps unthrottled) on every single call, with no bound at all. Fine for a handful;
    /// not fine for the residue population W2's midgame accumulates — this ticket's own evidence is
    /// 103 dormant robots and a 138ms robot bucket with Max standing on a deck above them. The fix
    /// spreads each robot's own check across a bounded number of calls (round-robin, per instance) so
    /// the steady-state cost divides by that factor instead of scaling with the population directly.
    ///
    /// Must fail on the pre-fix commit: before MV-936, <c>TickDormant</c> ran <c>IsOnScreen</c> on
    /// EVERY call with nothing to skip, so <c>_frustumTestCount</c> after N calls equals N exactly —
    /// this test's "far fewer than N" assertion does not hold.
    ///
    /// Same reflection idiom as <c>MV611DormantAreaGateTests</c>/<c>MV363DormantRobotTests</c>: Unity
    /// does not run Awake/OnEnable for a plain MonoBehaviour outside Play mode.
    /// </summary>
    public sealed class MV936DormantWakeCheckBudgetTests
    {
        private Camera[] _suppressedAmbientCameras;

        [SetUp]
        public void SetUp() => _suppressedAmbientCameras = CameraTestUtil.SuppressAmbientMainCameras();

        [TearDown]
        public void TearDown() => CameraTestUtil.RestoreAmbientMainCameras(_suppressedAmbientCameras);

        private static RobotEnemy NewDormantRobotWithNoSight()
        {
            var go = new GameObject("MV936-dormant-robot");
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.ResetState(); // EditMode has no Awake/OnEnable lifecycle — init explicitly, seeds _playerTarget
            e.BeginDormant();
            e.Sight.Tick(false, Vector3.zero, 0.1f); // never has sight — AmbushWake can never fire,
                                                       // so the robot stays Dormant across every call
            return e;
        }

        private static void InvokeTickDormant(RobotEnemy e) =>
            typeof(RobotEnemy).GetMethod("TickDormant", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, null);

        private static int FrustumTestCount(RobotEnemy e)
        {
            FieldInfo field = typeof(RobotEnemy).GetField("_frustumTestCount",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "RobotEnemy._frustumTestCount went missing — the instrumentation this test guards (MV-611/MV-936)");
            return (int)field.GetValue(e);
        }

        [Test]
        public void TickDormant_NotWellBehind_BudgetsTheWakeCheckAcrossCalls_RatherThanRunningEveryOne()
        {
            RobotEnemy e = NewDormantRobotWithNoSight();
            const int calls = 30;

            try
            {
                for (int i = 0; i < calls; i++) InvokeTickDormant(e);

                int count = FrustumTestCount(e);
                Assert.Greater(count, 0,
                    "the very first check must still run immediately — a robot newly in range must " +
                    "never look like it stopped checking altogether");
                Assert.Less(count, calls,
                    "the wake check must be budgeted across calls, not run on every single one — " +
                    "with no budget at all, count would equal the call count exactly (MV-936)");
                Assert.LessOrEqual(count, calls / 3,
                    "the budget must actually bound the steady-state rate, not just skip occasionally");

                Assert.AreEqual(RobotEnemy.State.Dormant, e.Current,
                    "sanity: a robot with no sight-line must never wake regardless of how the frustum test is budgeted");
            }
            finally
            {
                Object.DestroyImmediate(e.gameObject);
            }
        }
    }
}
