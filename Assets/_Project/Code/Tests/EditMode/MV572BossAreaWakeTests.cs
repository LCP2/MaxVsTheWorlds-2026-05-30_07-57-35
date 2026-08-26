using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-572 — a boss wakes on entering its OWN authored area, not on the old world-wide
    /// <c>FactoryCensus.Cleared</c> signal (every factory in the run destroyed). World 1 v4 authors
    /// bosses mid-run, well before that global count reaches zero, so the pre-fix boss stood
    /// permanently Dormant the moment its own gate opened (observed live, Lee, 2026-08-26).
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

        private static GameObject NewBoss(Rect wakeArea)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            var boss = go.AddComponent<BigBermudaBoss>();
            // EditMode never calls Awake on a plain MonoBehaviour -- invoke it explicitly so _health,
            // _brain and the "Player"-tagged target are set up exactly as Play mode would set them.
            typeof(BigBermudaBoss).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);
            boss.SetWakeArea(wakeArea);
            return go;
        }

        private static void InvokeTickDormant(BigBermudaBoss b) =>
            typeof(BigBermudaBoss).GetMethod("TickDormant", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(b, null);

        [Test]
        public void Boss_StaysDormantOutsideItsArea_ThenWakesTheInstantTheTargetEntersIt()
        {
            var area = new Rect(-5f, 10f, 10f, 10f); // x:[-5,5], z:[10,20]
            GameObject bossGo = NewBoss(area);
            try
            {
                var boss = bossGo.GetComponent<BigBermudaBoss>();

                _playerGo.transform.position = new Vector3(0f, 0f, 0f); // well outside the area
                InvokeTickDormant(boss);
                Assert.IsFalse(boss.Engaged, "must not wake while the target is outside its own area");

                _playerGo.transform.position = new Vector3(0f, 0f, 15f); // inside the area
                InvokeTickDormant(boss);
                Assert.IsTrue(boss.Engaged, "must wake (Dormant -> Intro) the instant the target enters its area");
            }
            finally
            {
                Object.DestroyImmediate(bossGo);
            }
        }

        [Test]
        public void SecondBoss_WithADifferentArea_StaysDormantWhileTheTargetIsInTheFirstBossArea()
        {
            var area1 = new Rect(-5f, 10f, 10f, 10f);  // x:[-5,5], z:[10,20]
            var area2 = new Rect(-5f, 100f, 10f, 10f); // x:[-5,5], z:[100,110] -- far away
            GameObject boss1Go = NewBoss(area1);
            GameObject boss2Go = NewBoss(area2);
            try
            {
                var boss1 = boss1Go.GetComponent<BigBermudaBoss>();
                var boss2 = boss2Go.GetComponent<BigBermudaBoss>();

                _playerGo.transform.position = new Vector3(0f, 0f, 15f); // inside area1 only
                InvokeTickDormant(boss1);
                InvokeTickDormant(boss2);

                Assert.IsTrue(boss1.Engaged, "the boss whose area the target entered must wake");
                Assert.IsFalse(boss2.Engaged,
                    "a second boss with a different area must stay Dormant while the target is elsewhere");
            }
            finally
            {
                Object.DestroyImmediate(boss1Go);
                Object.DestroyImmediate(boss2Go);
            }
        }
    }
}
