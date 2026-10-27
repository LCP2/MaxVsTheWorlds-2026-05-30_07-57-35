using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1303 (the one new test, per CC_AUTONOMY's testing policy): while Anchorhead is awake and alive,
    /// its whirlpool pulls a still Max standing 8 m away 0.8-1.2 m closer over 1 s; once Anchorhead is
    /// dead, the same Max does not move at all.
    ///
    /// Goes through the real per-tick helper (<c>PlayerController.TickAnchorheadWhirlpool</c>),
    /// reflection-invoked directly in a fixed-dt loop — the same idiom <c>MV1300CurrentLaneTests</c> and
    /// <c>MV1238TractorPullTests</c> already established, rather than the whole AI state machine.
    ///
    /// Fails on the commit before this ticket: <c>AnchorheadBoss</c> carries no <c>WhirlpoolDriftAt</c> /
    /// <c>Active</c>, and <c>PlayerController.TickAnchorheadWhirlpool</c> does not exist to reflect onto.
    /// </summary>
    public sealed class MV1303AnchorheadWhirlpoolTests
    {
        private const float Dt = 0.02f;
        private const int OneSecondTicks = 50; // 50 * 0.02s = 1s

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo BossAwakeMethod =
            typeof(BigBermudaBoss).GetMethod("Awake", NonPublicInstance);
        private static readonly FieldInfo BossPhaseField =
            typeof(BigBermudaBoss).GetField("_phase", NonPublicInstance);
        private static readonly System.Type BossPhaseType =
            typeof(BigBermudaBoss).GetNestedType("Phase", NonPublicInstance);

        private static readonly MethodInfo AnchorAwakeMethod =
            typeof(AnchorheadBoss).GetMethod("Awake", NonPublicInstance);

        private static readonly MethodInfo PlayerAwakeMethod =
            typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo TickWhirlpoolMethod =
            typeof(PlayerController).GetMethod("TickAnchorheadWhirlpool", NonPublicInstance);

        private static void SetBossPhase(BigBermudaBoss boss, string phaseName) =>
            BossPhaseField.SetValue(boss, System.Enum.Parse(BossPhaseType, phaseName));

        [Test]
        public void Whirlpool_PullsStillMaxCloserWhileAwakeAndAlive_ButNotAtAllOnceDead()
        {
            GameObject bossGo = null, maxGo = null;
            try
            {
                bossGo = new GameObject("MV1303 Anchorhead");
                var boss = bossGo.AddComponent<BigBermudaBoss>();
                BossAwakeMethod.Invoke(boss, null); // AddComponent never fires Awake in this EditMode harness
                SetBossPhase(boss, "Fight"); // past Dormant/Intro -- "awake" -- and not yet Dead -- "alive"

                var anchor = bossGo.AddComponent<AnchorheadBoss>();
                AnchorAwakeMethod.Invoke(anchor, null); // sets AnchorheadBoss.Active = anchor

                maxGo = new GameObject("MV1303 Max", typeof(CharacterController), typeof(PlayerController))
                    { tag = "Player" };
                var player = maxGo.GetComponent<PlayerController>();
                PlayerAwakeMethod.Invoke(player, null);
                maxGo.transform.position = bossGo.transform.position + new Vector3(8f, 0f, 0f);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                // --- AC: 8 m away, awake and alive, pulled 0.8-1.2 m closer in 1 s ---
                float startDist = Vector3.Distance(maxGo.transform.position, bossGo.transform.position);
                Assert.AreEqual(8f, startDist, 0.01f);

                for (int i = 0; i < OneSecondTicks; i++)
                {
                    TickWhirlpoolMethod.Invoke(player, new object[] { Dt });
                    Physics.SyncTransforms();
                }

                float pulled = startDist - Vector3.Distance(maxGo.transform.position, bossGo.transform.position);
                Assert.GreaterOrEqual(pulled, 0.8f,
                    $"a still Max 8 m from an awake, alive Anchorhead must be pulled at least 0.8 m closer in 1 s, was {pulled:0.000} m");
                Assert.LessOrEqual(pulled, 1.2f,
                    $"a still Max 8 m from an awake, alive Anchorhead must be pulled at most 1.2 m closer in 1 s, was {pulled:0.000} m");

                // --- AC: dead Anchorhead pulls Max not at all ---
                SetBossPhase(boss, "Dead");
                maxGo.transform.position = bossGo.transform.position + new Vector3(8f, 0f, 0f);
                Physics.SyncTransforms();
                Vector3 deadStart = maxGo.transform.position;

                for (int i = 0; i < OneSecondTicks; i++)
                {
                    TickWhirlpoolMethod.Invoke(player, new object[] { Dt });
                    Physics.SyncTransforms();
                }

                float deadMoved = Vector3.Distance(deadStart, maxGo.transform.position);
                Assert.Less(deadMoved, 0.01f,
                    $"Max must not move at all once Anchorhead is dead, but moved {deadMoved:0.000} m");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (bossGo != null) Object.DestroyImmediate(bossGo); // destroys AnchorheadBoss too, clearing Active
            }
        }
    }
}
