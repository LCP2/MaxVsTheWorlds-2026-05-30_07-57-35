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
    /// MV-1305 AC1, every sub-case in one test (MV-465 Rule 1 — one scenario, repeated at several
    /// points): a READY airlock hit once blows; a Rusher 8 m in front is removed within 4 s; a Rusher
    /// 13 m away is untouched; a second hit 5 s later does nothing; it is READY again at 20 s.
    ///
    /// Fails to compile on the commit before this ticket: neither <c>AirlockPanel</c> nor
    /// <c>RobotEnemy.ApplyAirlockPull</c> exist there.
    ///
    /// The Rusher goes through the real per-tick helper (<c>RobotEnemy.ApplyAirlockPull</c>),
    /// reflection-invoked directly in a fixed-dt loop — the same idiom <c>MV1300CurrentLaneTests</c> and
    /// <c>MV1303AnchorheadWhirlpoolTests</c> already established, rather than the whole AI state machine.
    /// Every assertion reads a RESOLVED value (MV-465 Tier 2: <c>AirlockPanel.State</c>, a Rusher's own
    /// <c>IsAlive</c>/position) — never an authored constant.
    /// </summary>
    public sealed class MV1305AirlockPanelTests
    {
        private const float Dt = 0.02f;
        private const int OneSecondTicks = 50; // 50 * 0.02s = 1s

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo RobotCcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo ApplyAirlockPullMethod =
            typeof(RobotEnemy).GetMethod("ApplyAirlockPull", NonPublicInstance);

        [SetUp]
        public void SetUp()
        {
            // First MaterialLibrary shader lookup in this test domain logs a plain Debug.Log once, not
            // a failure — same idiom MV1296ExplosiveCanisterBlastTests/MV1240PlasmaVentDamageTests use
            // for the same reason (AirlockPanel.Build() tints its body the same way Canister.Build() does).
            LogAssert.ignoreFailingMessages = true;
            RobotEnemy.ResetRegistry();
            AirlockPanel.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            AirlockPanel.ResetRegistry();
        }

        private static RobotEnemy NewRusher(Vector3 position)
        {
            var go = new GameObject("Rusher");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // EditMode never runs Awake, so _cc (normally seeded there) has to be stamped by hand --
            // same idiom MV1300CurrentLaneTests.NewRusher already established. Kind defaults to
            // EnemyKind.Rusher (its own field initializer), so no archetype needs applying. _health
            // itself has no field initializer (only Apply(), never called outside the spawner, sets
            // it) -- it defaults to 0, which reads as already-dead for IsAlive, so this test (unlike
            // MV1300CurrentLaneTests, which never asserts IsAlive) must seed it explicitly.
            RobotCcField.SetValue(e, cc);
            e.SetHealthFraction(1f);
            Physics.SyncTransforms();
            return e;
        }

        private static void TickRobot(RobotEnemy robot, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                ApplyAirlockPullMethod.Invoke(robot, new object[] { Dt });
                Physics.SyncTransforms();
            }
        }

        private static void TickPanel(AirlockPanel panel, int ticks)
        {
            for (int i = 0; i < ticks; i++) panel.Tick(Dt);
        }

        [Test]
        public void ReadyAirlock_BlowsOnOneHit_RemovesANearRusherButNotAFarOne_IgnoresASecondHitWhileRecharging_ReadyAgainAt20s()
        {
            GameObject panelGo = null, nearGo = null, farGo = null;
            try
            {
                panelGo = new GameObject("MV1305 Airlock");
                var panel = panelGo.AddComponent<AirlockPanel>();
                panel.Build(); // AddComponent never fires Awake in this EditMode harness

                Assert.AreEqual(AirlockPanel.PanelState.Ready, panel.State,
                    "a fresh airlock panel must start READY");

                // --- a hit while READY blows it ---
                panel.Hit();
                Assert.AreEqual(AirlockPanel.PanelState.Blowing, panel.State,
                    "a READY panel hit once must be BLOWING immediately after");

                // --- a Rusher 8 m in front is removed within 4 s ---
                RobotEnemy near = NewRusher(panelGo.transform.position + new Vector3(8f, 0f, 0f));
                nearGo = near.gameObject;

                // --- a Rusher 13 m away is untouched ---
                RobotEnemy far = NewRusher(panelGo.transform.position + new Vector3(13f, 0f, 0f));
                farGo = far.gameObject;
                Vector3 farStart = farGo.transform.position;

                // A fixed-length loop (never an early break on death) so the panel's own elapsed time
                // stays exactly predictable for the RECHARGING/READY checks below.
                for (int i = 0; i < 4 * OneSecondTicks; i++)
                {
                    TickPanel(panel, 1);
                    if (near.IsAlive) TickRobot(near, 1);
                    if (far.IsAlive) TickRobot(far, 1);
                }

                Assert.IsFalse(near.IsAlive,
                    "an 8 m Rusher must be pulled in and killed within the 4 s blow window");
                Assert.IsTrue(far.IsAlive, "a 13 m Rusher (outside the 12 m pull radius) must be untouched");
                float farMoved = Vector3.Distance(farStart, farGo.transform.position);
                Assert.Less(farMoved, 0.01f,
                    $"a 13 m Rusher must not be pulled at all, but moved {farMoved:0.000} m");

                // --- a second hit 5 s after the first (t=4..5s is RECHARGING, not READY) does nothing ---
                TickPanel(panel, OneSecondTicks); // +1s -> t=5s since the hit
                Assert.AreEqual(AirlockPanel.PanelState.Recharging, panel.State,
                    "5 s after the first hit the panel must be RECHARGING, not READY");
                panel.Hit();
                Assert.AreEqual(AirlockPanel.PanelState.Recharging, panel.State,
                    "a hit while RECHARGING must do nothing");

                // --- READY again at 20 s since the original hit (currently at t=5s; one tick past the
                // 20s boundary to clear float-accumulation error rather than land exactly on it) ---
                TickPanel(panel, 15 * OneSecondTicks + 1); // +15.02s -> t ~ 20.02s since the hit
                Assert.AreEqual(AirlockPanel.PanelState.Ready, panel.State,
                    "the panel must be READY again 20 s after it blew");
            }
            finally
            {
                if (farGo != null) Object.DestroyImmediate(farGo);
                if (nearGo != null) Object.DestroyImmediate(nearGo);
                if (panelGo != null) Object.DestroyImmediate(panelGo);
                RobotEnemy.ResetRegistry();
                AirlockPanel.ResetRegistry();
            }
        }
    }
}
