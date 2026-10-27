using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1299 (one new test, per CC_AUTONOMY's testing policy): a config area authoring
    /// <c>boss: { kind: "gridlock" }</c> builds a <see cref="GridlockBoss"/> — not a
    /// <see cref="BigBermudaBoss"/> — carrying 8,600 health and registering HUD name "GRIDLOCK", and
    /// stays Dormant until Max is placed within the shared 16 m wake radius with a clear line of
    /// sight. Same "kind beats id" override idiom <c>BossKindDispatchTests</c>' own 1c sub-check uses:
    /// load a real world, author an existing boss entity's <c>kind</c> field directly, rebuild.
    ///
    /// Fails on base commit 707c51e: <c>GridlockBoss</c>/<c>GridlockTuning</c>/<c>GridlockRig</c> do
    /// not exist and <c>BossKinds</c> carries no "gridlock" row — a compile failure, which is the
    /// expected fail-first proof here (same shape <c>BossKindDispatchTests</c>' own header documents).
    /// </summary>
    public sealed class MV1299GridlockBossTests
    {
        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static DestructibleHealth GetHealth(GridlockBoss boss) =>
            (DestructibleHealth)typeof(GridlockBoss)
                .GetField("_health", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(boss);

        private static WorldBoss FindBoss(WorldConfig cfg, string bossId)
        {
            foreach (WorldArea a in cfg.areas)
                foreach (WorldBoss b in a.Bosses())
                    if (b.id == bossId) return b;

            Assert.Fail($"boss '{bossId}' not found in config");
            return null;
        }

        [Test]
        public void BossKindGridlock_Builds8600HpGridlock_DormantUntil16mInView()
        {
            GameObject root = null, playerGo = null;
            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
                Assert.IsNotNull(cfg, $"'{WorldLibrary.World3}' failed to load — see the error log above.");

                // --- AC1: a config area authoring boss: { kind: "gridlock" } ---
                WorldBoss a15Boss = FindBoss(cfg, "a15_boss");
                a15Boss.kind = "gridlock";

                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
                root = new GameObject("MV1299 Root");
                MapBuild built = MapRuntime.Build(map, root.transform);

                Assert.IsTrue(built.Actors.TryGetValue("a15_boss", out GameObject bossGo) && bossGo != null,
                    "'a15_boss' was not built once it authored kind 'gridlock'");

                var boss = bossGo.GetComponent<GridlockBoss>();
                Assert.IsNotNull(boss, "authoring kind 'gridlock' must build a GridlockBoss");
                Assert.IsNull(bossGo.GetComponent<BigBermudaBoss>(), "a gridlock boss must NOT also carry BigBermudaBoss");

                // AddComponent never fires Awake in this EditMode harness (MV-1013's own note) --
                // invoke it explicitly so _health/_target are set up exactly as real gameplay's
                // AddComponent ordering already guarantees them to be before Tick ever runs.
                InvokePrivate(boss, "Awake");

                // --- AC1: 8,600 health ---
                DestructibleHealth health = GetHealth(boss);
                Assert.AreEqual(8600f, health.Max, 0.01f, "GRIDLOCK must author 8,600 health");

                // --- AC1: dormant until Max is within 16 m in view ---
                playerGo = new GameObject("MV1299 Max") { tag = "Player" };
                playerGo.transform.position = bossGo.transform.position + new Vector3(-30f, 0f, 0f);

                BossCensus.Reset();
                string capturedName = null;
                int capturedPhases = -1;
                void OnEngaged(string name, int phases) { capturedName = name; capturedPhases = phases; }
                HudSignals.BossEngaged += OnEngaged;

                try
                {
                    for (int i = 0; i < 60; i++) boss.Tick(0.016f); // 30 m away, well beyond 16 m
                    Assert.IsFalse(boss.Engaged, "must stay Dormant while Max is 30 m away");
                    Assert.IsNull(capturedName, "must not have registered with BossCensus while Dormant");

                    playerGo.transform.position = bossGo.transform.position + new Vector3(-15f, 0f, 0f);
                    boss.Tick(0.016f); // 15 m away, clear sight, within the shared 16 m wake radius

                    Assert.IsTrue(boss.Engaged, "must wake once Max is within 16 m with clear sight");
                    Assert.AreEqual("GRIDLOCK", capturedName, "GRIDLOCK must register that exact HUD name");
                }
                finally
                {
                    HudSignals.BossEngaged -= OnEngaged;
                }
            }
            finally
            {
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (root != null) Object.DestroyImmediate(root);
                BossCensus.Reset();
            }
        }
    }
}
