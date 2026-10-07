using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-572 originally proved a boss wakes on entering its own authored area, not on the old
    /// world-wide <c>FactoryCensus.Cleared</c> signal. MV-1110 replaced that area-entry trigger
    /// itself: a boss authored deep inside a large area (World 1's 44x56 m area 30) was already
    /// walking toward Max before he had ever seen it (Lee, device, 2026-10-06 — "Big Bermuda two has
    /// already made its way down before I've even seen it"), so entering the area is no longer enough
    /// — a boss now wakes only once Max is within <see cref="BossTuning.WakeRadius"/> of its own post
    /// with a clear line of sight. These two tests are rewritten against that rule; what they still
    /// prove is unchanged from MV-572: each boss wakes off its OWN criteria, independent of any other
    /// boss or any world-wide signal.
    ///
    /// Drives <see cref="BigBermudaBoss"/>'s private <c>Awake</c> and Dormant tick directly via
    /// reflection — a plain MonoBehaviour never gets its Unity lifecycle called in EditMode (no
    /// [ExecuteAlways]), the same reason <c>MV363DormantRobotTests</c> calls <c>RobotEnemy.ResetState()</c>
    /// explicitly instead of relying on Awake; BigBermudaBoss has no such public reset, so Awake is
    /// invoked directly instead.
    /// </summary>
    public sealed class MV572BossAreaWakeTests
    {
        private GameObject _playerGo;

        [SetUp]
        public void SetUp() => _playerGo = new GameObject("Player") { tag = "Player" };

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_playerGo);

        private static GameObject NewBoss(Vector3 post)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.position = post;
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            var boss = go.AddComponent<BigBermudaBoss>();
            // EditMode never calls Awake on a plain MonoBehaviour -- invoke it explicitly so _health,
            // _brain and the "Player"-tagged target are set up exactly as Play mode would set them.
            typeof(BigBermudaBoss).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);
            return go;
        }

        private static void InvokeTickDormant(BigBermudaBoss b) =>
            typeof(BigBermudaBoss).GetMethod("TickDormant", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(b, null);

        [Test]
        public void Boss_StaysDormantBeyondWakeRadius_ThenWakesOnceTheTargetIsCloseWithClearSight()
        {
            GameObject bossGo = NewBoss(new Vector3(0f, 0f, 0f));
            try
            {
                var boss = bossGo.GetComponent<BigBermudaBoss>();

                _playerGo.transform.position = new Vector3(0f, 0f, 20f); // beyond WakeRadius (16 m)
                InvokeTickDormant(boss);
                Assert.IsFalse(boss.Engaged, "must not wake while the target is beyond WakeRadius");

                _playerGo.transform.position = new Vector3(0f, 0f, 10f); // within WakeRadius, clear sight
                InvokeTickDormant(boss);
                Assert.IsTrue(boss.Engaged,
                    "must wake (Dormant -> Intro) once the target is within WakeRadius with clear sight");
            }
            finally
            {
                Object.DestroyImmediate(bossGo);
            }
        }

        [Test]
        public void SecondBoss_FarFromTheTarget_StaysDormantWhileTheNearerBossWakes()
        {
            GameObject boss1Go = NewBoss(new Vector3(0f, 0f, 0f));
            GameObject boss2Go = NewBoss(new Vector3(0f, 0f, 100f)); // far away -- outside boss1's own range
            try
            {
                var boss1 = boss1Go.GetComponent<BigBermudaBoss>();
                var boss2 = boss2Go.GetComponent<BigBermudaBoss>();

                _playerGo.transform.position = new Vector3(0f, 0f, 10f); // within range of boss1 only
                InvokeTickDormant(boss1);
                InvokeTickDormant(boss2);

                Assert.IsTrue(boss1.Engaged, "the boss near the target must wake");
                Assert.IsFalse(boss2.Engaged,
                    "a second boss far from the target must stay Dormant even though the first one woke");
            }
            finally
            {
                Object.DestroyImmediate(boss1Go);
                Object.DestroyImmediate(boss2Go);
            }
        }
    }
}
