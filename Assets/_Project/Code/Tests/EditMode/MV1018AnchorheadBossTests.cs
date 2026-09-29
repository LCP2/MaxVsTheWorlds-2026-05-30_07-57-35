using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1018 (the one new test, per CC_AUTONOMY's testing policy): World 3's a30 boss builds as
    /// Anchorhead — <see cref="BigBermudaBoss"/> still underneath (HP/HUD/finale wiring, unchanged) plus
    /// the new <see cref="AnchorheadBoss"/> companion behaviour and its own <see cref="AnchorheadRig"/>
    /// body, not the reused mower/Brood-Hulk <see cref="BigBermudaRig"/>. Damaging it to 49% HP through
    /// the real <see cref="IDamageable.TakeDamage"/> path must read its phase as Awake, and one drag
    /// cycle (telegraph, then the anchor hauled back) must pull a nearby Max meaningfully closer.
    ///
    /// Loads World 3 through the real world-load path (<c>WorldLibrary.Load</c> -&gt;
    /// <c>WorldMapLoader.TryLoad</c> -&gt; <c>MapRuntime.Build</c>), the same idiom
    /// <see cref="MV1013LastWorldFinaleTests"/> already uses for this exact boss.
    ///
    /// Fails on base commit fe19905: <c>MapRuntime.BuildBoss</c> builds every boss entity as a plain
    /// <see cref="BigBermudaBoss"/> with <see cref="BigBermudaRig"/> — there is no
    /// <see cref="AnchorheadBoss"/> component, "Anchorhead" appears nowhere in <c>Runtime/</c>, and the
    /// a30 boss GameObject carries the mower/Brood-Hulk rig instead.
    /// </summary>
    public sealed class MV1018AnchorheadBossTests
    {
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private static void SetPrivateField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static DestructibleHealth GetHealth(BigBermudaBoss boss) =>
            (DestructibleHealth)typeof(BigBermudaBoss)
                .GetField("_health", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(boss);

        [Test]
        public void Anchorhead_BuildsItsOwnBodyAndPhase_AndDragsMaxTowardIt()
        {
            GameObject root = null, playerGo = null;
            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
                Assert.IsNotNull(cfg, "World 3 failed to load — see the error log above.");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                root = new GameObject("MV1018 Root");
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.IsTrue(built.Actors.TryGetValue("anchorhead", out GameObject bossGo) && bossGo != null,
                    "World 3's a30 boss ('anchorhead') was not built");

                var boss = bossGo.GetComponent<BigBermudaBoss>();
                Assert.IsNotNull(boss, "Anchorhead must still build as a BigBermudaBoss (HP bar/BossCensus/finale wiring)");

                var anchor = bossGo.GetComponent<AnchorheadBoss>();
                Assert.IsNotNull(anchor, "a30's boss must also carry its own AnchorheadBoss behaviour");

                AnchorheadRig anchorRig = Object.FindObjectsByType<AnchorheadRig>(FindObjectsSortMode.None)
                    .FirstOrDefault(r => r.Boss == boss);
                Assert.IsNotNull(anchorRig, "a30's boss must build its own AnchorheadRig body");

                bool woreTheMowerRig = Object.FindObjectsByType<BigBermudaRig>(FindObjectsSortMode.None)
                    .Any(r => r.Boss == boss);
                Assert.IsFalse(woreTheMowerRig, "a30's boss must not also carry the old mower/Brood-Hulk rig");

                // --- Max, 8 m from the boss, tagged but with no CharacterController -- the drag-pull's
                // own raw-transform fallback, so this check is a deterministic pull distance rather than
                // depending on a30's real cover layout not blocking an 8 m sweep. ---
                playerGo = new GameObject("MV1018 Max") { tag = "Player" };
                Vector3 start = bossGo.transform.position + new Vector3(8f, 0f, 0f);
                playerGo.transform.position = start;

                // AddComponent never fires Awake in this EditMode harness (MV-1013's own note) -- invoke
                // it explicitly so _health/_target are set up exactly as real gameplay's AddComponent
                // ordering already guarantees them to be before either component ticks.
                InvokeAwake(boss);
                SetPrivateField(boss, "_phase", System.Enum.Parse(
                    typeof(BigBermudaBoss).GetNestedType("Phase", BindingFlags.NonPublic), "Fight"));
                InvokeAwake(anchor);

                Assert.AreEqual(AnchorheadBoss.AnchorPhase.Dragging, anchor.Phase,
                    "Anchorhead must start in its dragging phase, above the 50% HP threshold");

                // --- AC1: damage to 49% HP through the normal TakeDamage path must read Awake. ---
                DestructibleHealth health = GetHealth(boss);
                boss.TakeDamage(new DamageInfo(health.Max * 0.51f, boss.transform.position, Vector3.forward, Team.Player));
                Assert.AreEqual(0.49f, health.Normalized, 0.01f, "the damage above should leave Anchorhead at ~49% HP");
                Assert.AreEqual(AnchorheadBoss.AnchorPhase.Awake, anchor.Phase,
                    "damage down to 49% HP must read Anchorhead's phase as Awake");

                // --- AC2: one drag cycle must pull Max noticeably closer. ---
                float startDist = Vector3.Distance(playerGo.transform.position, bossGo.transform.position);
                Assert.AreEqual(8f, startDist, 0.01f);

                // The Idle countdown already running when damage flips the phase was seeded with the
                // DRAGGING cadence (6s) back in Awake() -- a phase flip mid-cycle does not rewind an
                // already-running countdown, only the NEXT one -- so this first cycle still needs the
                // full 6s + the 1s telegraph + the 0.8s pull, not the Awake cadence's shorter 3.5s.
                const float dt = 0.02f;
                const float simulatedSeconds = 9f;
                for (float t = 0f; t < simulatedSeconds; t += dt) anchor.Tick(dt);

                float endDist = Vector3.Distance(playerGo.transform.position, bossGo.transform.position);
                Assert.LessOrEqual(endDist, startDist - 2.5f,
                    $"one drag cycle must pull Max at least 2.5 m closer (was {startDist:F2} m, now {endDist:F2} m)");
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
