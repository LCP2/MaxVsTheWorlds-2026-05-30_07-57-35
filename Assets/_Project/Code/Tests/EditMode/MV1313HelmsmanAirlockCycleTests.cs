using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1313 (one new test, per CC_AUTONOMY's testing policy): once the Helmsman is driven into Phase
    /// 2, the two real a30 airlocks (MV-1310: "a30_airlock1"/"a30_airlock2") blow in turn, alternating
    /// which one, every 12 s, with the first blow landing within 13 s — and before Phase 2, neither
    /// blows on its own.
    ///
    /// Fails on base commit (before this ticket's change): <c>HelmsmanBoss</c> carries no airlock cycle
    /// at all, so both panels stay READY for the whole run and the "an airlock blows within 13 s" wait
    /// loop below runs out without either one ever reaching <see cref="AirlockPanel.PanelState.Blowing"/>.
    ///
    /// Tier 2 (resolved values): every assertion reads <see cref="AirlockPanel.State"/>, the engine's own
    /// resolved panel state, never an authored constant.
    ///
    /// Loads the Mothership through the real world-load path (<c>WorldLibrary.Load</c> -&gt;
    /// <c>WorldMapLoader.TryLoad</c> -&gt; <c>MapRuntime.Build</c>), the same idiom
    /// <see cref="MV1239HelmsmanBossTests"/> already uses, then stamps the two real a30 airlocks'
    /// <see cref="AirlockPanel.AreaIndex"/> directly (same value <c>WorldRunner.Build</c>'s own pass
    /// would stamp them with in real play, MV-1312) since this EditMode harness builds via
    /// <c>MapRuntime.Build</c> alone, not the full <c>WorldRunner</c>.
    /// </summary>
    public sealed class MV1313HelmsmanAirlockCycleTests
    {
        private const int BridgeAreaIndex = 30;
        private const float Dt = 0.02f;

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static DestructibleHealth GetHealth(HelmsmanBoss boss) =>
            (DestructibleHealth)typeof(HelmsmanBoss)
                .GetField("_health", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(boss);

        [Test]
        public void Helmsman_BlowsBothBridgeAirlocksInAlternatingTurn_OnceInPhaseTwo_ButNeverBeforeIt()
        {
            GameObject root = null, playerGo = null;
            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
                Assert.IsNotNull(cfg, "Mothership failed to load — see the error log above.");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                root = new GameObject("MV1313 Root");
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.IsTrue(built.Actors.TryGetValue("helmsman", out GameObject bossGo) && bossGo != null,
                    "Mothership's a30 boss ('helmsman') was not built");
                var boss = bossGo.GetComponent<HelmsmanBoss>();
                Assert.IsNotNull(boss, "a30's boss must carry a HelmsmanBoss behaviour");

                Assert.IsTrue(built.Actors.TryGetValue("a30_airlock1", out GameObject airlock1Go) && airlock1Go != null,
                    "a30's first airlock ('a30_airlock1', MV-1310) was not built");
                Assert.IsTrue(built.Actors.TryGetValue("a30_airlock2", out GameObject airlock2Go) && airlock2Go != null,
                    "a30's second airlock ('a30_airlock2', MV-1310) was not built");
                AirlockPanel airlock1 = airlock1Go.GetComponent<AirlockPanel>();
                AirlockPanel airlock2 = airlock2Go.GetComponent<AirlockPanel>();
                Assert.IsNotNull(airlock1);
                Assert.IsNotNull(airlock2);

                // AddComponent never fires Awake in this EditMode harness (same footing as
                // MV1239HelmsmanBossTests' own InvokeAwake) -- without this, AirlockPanel.Build() never
                // runs, so the panel is never added to the static registry HelmsmanBoss.PanelsInArea
                // reads, and WorldRunner.Build (not exercised here either) is what stamps a real panel's
                // AreaIndex in play — reproduce that same stamp directly (MV-1312's own pattern).
                airlock1.Build();
                airlock2.Build();
                airlock1.SetAreaIndex(BridgeAreaIndex);
                airlock2.SetAreaIndex(BridgeAreaIndex);

                playerGo = new GameObject("MV1313 Max") { tag = "Player" };
                playerGo.transform.position = bossGo.transform.position + new Vector3(4f, 0f, 0f);

                InvokeAwake(boss);
                SetPrivateField(boss, "_phase", System.Enum.Parse(
                    typeof(HelmsmanBoss).GetNestedType("Phase", BindingFlags.NonPublic), "Seated"));
                SetPrivateField(boss, "_areaIndex", BridgeAreaIndex);

                Assert.IsFalse(boss.IsWalking, "Helmsman must start seated, above the 50% HP threshold");

                // --- before Phase 2: run 5 s seated and confirm neither airlock ever blows on its own. ---
                for (float t = 0f; t < 5f; t += Dt)
                {
                    boss.Tick(Dt);
                    Assert.AreNotEqual(AirlockPanel.PanelState.Blowing, airlock1.State,
                        "a30_airlock1 must never blow before the Helmsman reaches Phase 2");
                    Assert.AreNotEqual(AirlockPanel.PanelState.Blowing, airlock2.State,
                        "a30_airlock2 must never blow before the Helmsman reaches Phase 2");
                }

                // --- cross into Phase 2 through the real TakeDamage path, same as MV-1239. ---
                DestructibleHealth health = GetHealth(boss);
                boss.TakeDamage(new DamageInfo(health.Max * 0.51f, bossGo.transform.position, Vector3.forward, Team.Player));
                Assert.IsTrue(boss.IsWalking, "damage down to 49% HP must switch the Helmsman to Phase 2");

                // --- Phase 2: one airlock must blow within 13 s, the other within the next 12 s, and the
                // two blows must land on DIFFERENT airlocks ("alternating sides"). ---
                float? firstBlowAt = null, secondBlowAt = null;
                AirlockPanel firstPanel = null, secondPanel = null;

                for (float t = 0f; t < 26f && secondBlowAt == null; t += Dt)
                {
                    boss.Tick(Dt);

                    if (firstBlowAt == null)
                    {
                        if (airlock1.State == AirlockPanel.PanelState.Blowing) { firstBlowAt = t; firstPanel = airlock1; }
                        else if (airlock2.State == AirlockPanel.PanelState.Blowing) { firstBlowAt = t; firstPanel = airlock2; }
                    }
                    else if (secondBlowAt == null)
                    {
                        AirlockPanel other = firstPanel == airlock1 ? airlock2 : airlock1;
                        if (other.State == AirlockPanel.PanelState.Blowing) { secondBlowAt = t; secondPanel = other; }
                    }
                }

                Assert.IsTrue(firstBlowAt.HasValue, "an airlock must blow within 13 s of entering Phase 2");
                Assert.LessOrEqual(firstBlowAt.Value, 13f, $"first airlock blow landed at {firstBlowAt}s, later than the ticket's own 13 s");

                Assert.IsTrue(secondBlowAt.HasValue, "the other airlock must blow within the next 12 s");
                float gap = secondBlowAt.Value - firstBlowAt.Value;
                Assert.LessOrEqual(gap, 12.5f, $"second airlock blew {gap}s after the first, later than the ticket's own 12 s cadence");

                Assert.AreNotSame(firstPanel, secondPanel, "the two blows must alternate sides, not repeat the same airlock");
            }
            finally
            {
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (root != null) Object.DestroyImmediate(root);
                BossCensus.Reset();
                Pickup.ResetRegistry();
                AirlockPanel.ResetRegistry();
            }
        }
    }
}
