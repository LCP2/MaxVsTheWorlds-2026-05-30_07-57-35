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
    /// MV-1239 (one new test, per CC_AUTONOMY's testing policy): the Mothership's a30 ("The Bridge")
    /// boss builds as exactly one Helmsman, the shared boss wake radius is the ticket's own 16 m, the
    /// lens-sweep's tell (0.8 s) is shorter than its active sweep (1.2 s) so the warning never outlasts
    /// the thing it warns about, and damaging it below half health through the real
    /// <see cref="IDamageable.TakeDamage"/> path switches it straight from its seated Phase 1 into
    /// Phase 2 and actually moves it.
    ///
    /// Loads the Mothership through the real world-load path (<c>WorldLibrary.Load</c> -&gt;
    /// <c>WorldMapLoader.TryLoad</c> -&gt; <c>MapRuntime.Build</c>), the same idiom
    /// <see cref="MV1018AnchorheadBossTests"/> already uses for World 3's own finale boss.
    ///
    /// Fails on base commit 2cdd649: there is no <c>HelmsmanBoss</c>/<c>HelmsmanTuning</c>/
    /// <c>LensSweepTelegraph</c> in Runtime/, <c>BossKinds</c> carries no "helmsman" row, and the
    /// Mothership config's a30 authors no boss at all (role "exit", no "boss" key) — <c>MapRuntime.BuildBoss</c>
    /// never runs for it, so <c>built.Actors["helmsman"]</c> does not exist.
    /// </summary>
    public sealed class MV1239HelmsmanBossTests
    {
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static DestructibleHealth GetHealth(HelmsmanBoss boss) =>
            (DestructibleHealth)typeof(HelmsmanBoss)
                .GetField("_health", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(boss);

        [Test]
        public void Helmsman_ResolvesOnceInArea30_WakesAt16m_SweepTiming_AndMovesInPhaseTwo()
        {
            GameObject root = null, playerGo = null;
            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
                Assert.IsNotNull(cfg, "Mothership failed to load — see the error log above.");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                root = new GameObject("MV1239 Root");
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.IsTrue(built.Actors.TryGetValue("helmsman", out GameObject bossGo) && bossGo != null,
                    "Mothership's a30 boss ('helmsman') was not built");

                var all = Object.FindObjectsByType<HelmsmanBoss>(FindObjectsSortMode.None);
                Assert.AreEqual(1, all.Length, "area 30 must resolve exactly one Helmsman");

                var boss = bossGo.GetComponent<HelmsmanBoss>();
                Assert.IsNotNull(boss, "a30's boss must carry a HelmsmanBoss behaviour");

                // --- wake distance: the shared boss wake radius every boss reads (BossTuning.WakeRadius)
                // is the ticket's own authored 16 m, range + line of sight. ---
                Assert.AreEqual(16f, BossTuning.WakeRadius, 0.001f,
                    "the Helmsman wakes at the shared boss wake radius, authored at 16 m");

                // --- lens-sweep timing: the tell must be shorter than the active sweep it warns about,
                // or the warning would outlast the thing it is warning about. ---
                Assert.Less(LensSweepTelegraph.TellSeconds, LensSweepTelegraph.ActiveSeconds,
                    "the lens-sweep's tell duration must be shorter than its active duration");

                // --- AddComponent never fires Awake in this EditMode harness -- invoke it explicitly so
                // _health/_target are set up exactly as real gameplay's AddComponent ordering already
                // guarantees them to be before either component ticks (same idiom MV-1018 uses). ---
                playerGo = new GameObject("MV1239 Max") { tag = "Player" };
                playerGo.transform.position = bossGo.transform.position + new Vector3(4f, 0f, 0f);

                InvokeAwake(boss);
                SetPrivateField(boss, "_phase", System.Enum.Parse(
                    typeof(HelmsmanBoss).GetNestedType("Phase", BindingFlags.NonPublic), "Seated"));

                Assert.IsFalse(boss.IsWalking, "Helmsman must start seated, above the 50% HP threshold");

                Vector3 startPos = bossGo.transform.position;

                // --- damage to 49% HP through the real TakeDamage path must switch straight to phase 2. ---
                DestructibleHealth health = GetHealth(boss);
                boss.TakeDamage(new DamageInfo(health.Max * 0.51f, startPos, Vector3.forward, Team.Player));
                Assert.AreEqual(0.49f, health.Normalized, 0.01f, "the damage above should leave the Helmsman at ~49% HP");
                Assert.IsTrue(boss.IsWalking, "damage down to 49% HP must switch the Helmsman to phase 2");

                // --- phase 2 must actually move it, not just flip a flag. ---
                const float dt = 0.02f;
                for (float t = 0f; t < 3f; t += dt) boss.Tick(dt);

                float moved = Vector3.Distance(startPos, bossGo.transform.position);
                Assert.Greater(moved, 0.05f, $"phase 2 must actually move the Helmsman toward Max (moved {moved:F3} m)");
            }
            finally
            {
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (root != null) Object.DestroyImmediate(root);
                BossCensus.Reset();
                Pickup.ResetRegistry();
            }
        }
    }
}
