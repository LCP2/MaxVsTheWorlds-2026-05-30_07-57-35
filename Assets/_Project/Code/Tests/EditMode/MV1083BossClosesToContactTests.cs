using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1083 (Lee, device, 2026-10-06, TestFlight v0.11.7): "Big Bermuda does no damage. Just goes
    /// around Max in a circle. Same for sentinels?" <see cref="BigBermudaBoss.TickApproach"/> stopped
    /// at the fixed <see cref="BossTuning.Standoff"/> (3 m) while <see cref="BigBermudaBoss.TickContactDamage"/>'s
    /// own reach — bossRadius + targetRadius + <see cref="BossTuning.ContactSkin"/> — is SMALLER than
    /// that for every boss in every world (World 1's 3.6 m boss: 1.8+0.5+0.3 = 2.6 m; a boss parked at
    /// a fixed 3 m orbit can never close the remaining 0.4 m). MV-1037 fixed the reach maths but never
    /// touched the stop distance that has to match it — this ticket does.
    ///
    /// Fails on base commit origin/main at pickup (<c>b28f4a2</c>) — confirmed via <c>git log</c> that
    /// no commit touched <c>BigBermudaBoss</c>/<c>BossTuning</c>/<c>SludgequeenBoss</c>/<c>SludgequeenTuning</c>/
    /// <c>AnchorheadBoss</c>/<c>SentinelTargeting</c> ahead of this ticket's own fix, so pre-fix HEAD is
    /// behaviourally identical for this code path: a boss parked at a fixed Standoff orbits outside its
    /// own measured reach, so the target's health never moves. Quoted failure output: see the fix
    /// comment on the ticket.
    ///
    /// ONE new EditMode test (MV-465 Rule 1), carrying AC1 as re-derived by triage 2026-10-07 — TWO
    /// sub-scenarios per boss class instead of one combined run, because a boss in contact with Max can
    /// never simultaneously be in contact with a Sentinel (stop distance from Max is always
    /// bossRadius+0.70, while the whole reach to a Sentinel is only bossRadius+0.55 — a size-independent
    /// 0.15 m gap from Max's CharacterController radius (0.5) vs a Sentinel's (0.25)), and a single
    /// 12 s window cannot cover both sequentially either (see the ticket's own derivation). Scenario 1a
    /// and 1b are each entirely self-contained (no competing target, no retarget mid-run), so NEITHER
    /// depends on which way the boss orbits — unlike the ticket's withdrawn original AC1, which placed a
    /// Sentinel 2 m to Max's side and let the verdict hinge on <c>_preferSign</c>
    /// (<c>ObstacleSteering.PreferSignFor(GetInstanceID())</c>), an instance-ID parity Unity does not
    /// guarantee stable across runs.
    ///
    /// <see cref="BigBermudaBoss"/> and <see cref="AnchorheadBoss"/> (a companion on the SAME
    /// <see cref="BigBermudaBoss"/> instance — MV-1018's own class doc: "the brood volley and contact
    /// damage are still entirely BigBermudaBoss's, unmodified") are built through the REAL production
    /// route (<see cref="MapRuntime.Build"/> off world1_config's "a12_boss1" and world3_config's
    /// "anchorhead", so each carries its own authored world-space size) with a real, stationary Max
    /// (<see cref="CharacterController"/> + <see cref="PlayerController"/> + <see cref="PlayerHealth"/>)
    /// and a real deployed <see cref="Sentinel"/> (<see cref="Sentinel.Init"/>, the real deploy path) —
    /// same build idiom <c>MV720BossContactDamageTests</c> already uses. <see cref="SludgequeenBoss"/>
    /// has no map-loader entry at all yet (its own class doc: "actually placing this boss in
    /// bosses[]... is left for a follow-up") — built the same direct way its own
    /// <c>MV696SludgequeenFloodTests</c>/<c>MV699SludgequeenRigTests</c> precedent already does, the
    /// closest real entry point this class has today.
    ///
    /// Each boss is driven through its own private per-tick seams directly (reflection) —
    /// <c>RetargetIfNeeded</c>, <c>Approach</c>, <c>TickContactDamage</c> — the same granular idiom
    /// <c>MV720BossContactDamageTests</c>/<c>MV590BossWallSteeringTests</c>/<c>MV667BossConcaveRoutingTests</c>
    /// already use, rather than needing <c>Wake()</c> or a live <c>Update()</c> loop.
    ///
    /// EditMode only, reflection-driven (repo convention — this worker never authors PlayMode tests).
    /// </summary>
    public sealed class MV1083BossClosesToContactTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private const float Dt = 1f / 60f;
        private const float SimulatedSeconds = 12f;
        private const float ApproachDistance = 8f;   // the ticket's own "a stationary damageable target 8m away"
        private const float BeyondDistance = 20f;    // 1b's "Max positioned beyond" the Sentinel
        private const float MinHealthLost = 45f;     // the ticket's own "lost at least 45 health"

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", NonPublicInstance).Invoke(component, null);

        private static GameObject NewMax(Vector3 position)
        {
            var go = new GameObject("MV1083 Max", typeof(CharacterController), typeof(PlayerController)) { tag = "Player" };
            go.transform.position = position;
            InvokeAwake(go.GetComponent<PlayerController>());
            var health = go.AddComponent<PlayerHealth>();
            health.Initialize(); // Awake is not a reliable side effect of AddComponent outside Play mode
            return go;
        }

        private static Sentinel NewSentinel(Vector3 position)
        {
            var go = new GameObject("MV1083 Sentinel");
            var sentinel = go.AddComponent<Sentinel>();
            sentinel.Init(position, maxHp: 100f, range: 0f, fireInterval: 999f,
                moveSpeed: 0f, standoffDistance: 0f, followTarget: null);
            return sentinel;
        }

        [Test]
        public void BossesCloseToContactAndHurtMaxAndASentinel_AcrossEveryBossClass()
        {
            RunMapLoaderScenarios(WorldLibrary.World1, "a12_boss1");
            RunMapLoaderScenarios(WorldLibrary.World3, "anchorhead");
            RunSludgequeenScenarios();
        }

        // ---------------------------------------------------------------- BigBermudaBoss / AnchorheadBoss

        private static readonly MethodInfo BbRetarget =
            typeof(BigBermudaBoss).GetMethod("RetargetIfNeeded", NonPublicInstance);
        private static readonly MethodInfo BbApproach =
            typeof(BigBermudaBoss).GetMethod("Approach", NonPublicInstance);
        private static readonly MethodInfo BbTickContactDamage =
            typeof(BigBermudaBoss).GetMethod("TickContactDamage", NonPublicInstance);
        private static readonly MethodInfo BbContactReachTo =
            typeof(BigBermudaBoss).GetMethod("ContactReachTo", NonPublicInstance);
        private static readonly FieldInfo BbTarget =
            typeof(BigBermudaBoss).GetField("_target", NonPublicInstance);

        private void RunMapLoaderScenarios(string worldKey, string bossId)
        {
            RunMapLoaderScenario1a(worldKey, bossId);
            RunMapLoaderScenario1b(worldKey, bossId);
        }

        /// <summary>Built through the real map loader — world1_config's "a12_boss1" is a plain
        /// <see cref="BigBermudaBoss"/>; world3_config's "anchorhead" is the SAME component with an
        /// <see cref="AnchorheadBoss"/> companion riding alongside it (<see cref="MapRuntime.BuildBoss"/>),
        /// so driving the <see cref="BigBermudaBoss"/> this way exercises both.
        ///
        /// 1a — Max in contact, no Sentinel deployed anywhere: a stationary Max 8 m away with nothing
        /// between. After 12 simulated seconds Max must have lost at least 45 health, and the final
        /// centre distance must be less than the measured contact reach to Max.</summary>
        private void RunMapLoaderScenario1a(string worldKey, string bossId)
        {
            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"{worldKey} failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject($"MV1083 Root 1a {worldKey}");
            GameObject maxGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.IsTrue(built.Actors.TryGetValue(bossId, out GameObject bossGo) && bossGo != null,
                    $"{worldKey}'s boss ('{bossId}') was not built");
                var boss = bossGo.GetComponent<BigBermudaBoss>();
                Assert.IsNotNull(boss, $"{bossId} did not build as a BigBermudaBoss");
                Vector3 bossPos = bossGo.transform.position;

                // A stationary target 8m away with nothing between (verified against world1_config/
                // world3_config's own authored cover: a12/a30 both have a clear +X corridor from the
                // boss's authored position out to +8m).
                maxGo = NewMax(bossPos + new Vector3(ApproachDistance, 0f, 0f));
                var maxHealth = maxGo.GetComponent<PlayerHealth>();

                // Boss Awake AFTER Max exists and is tagged Player — AcquireTarget needs to find him.
                InvokeAwake(boss);
                Physics.SyncTransforms();

                var target = (Transform)BbTarget.GetValue(boss);
                float reach = (float)BbContactReachTo.Invoke(boss, new object[] { target });

                float maxHealthBefore = maxHealth.Current;

                int ticks = Mathf.CeilToInt(SimulatedSeconds / Dt);
                for (int i = 0; i < ticks; i++)
                {
                    BbRetarget.Invoke(boss, null);
                    BbApproach.Invoke(boss, new object[] { Dt, 1f });
                    BbTickContactDamage.Invoke(boss, new object[] { Dt });
                    Physics.SyncTransforms();
                }

                float healthLost = maxHealthBefore - maxHealth.Current;
                float finalDistance = Vector3.Distance(bossGo.transform.position, maxGo.transform.position);
                Debug.Log($"MV-1083 {worldKey}/{bossId} 1a: measured contact reach to Max={reach:F2}m, " +
                    $"measured stop distance={finalDistance:F2}m");

                Assert.GreaterOrEqual(healthLost, MinHealthLost,
                    $"{worldKey} 1a: Max must lose at least {MinHealthLost} health to {bossId}'s contact over " +
                    $"{SimulatedSeconds}s — lost {healthLost:F1}");
                Assert.Less(finalDistance, reach + 0.01f,
                    $"{worldKey} 1a: {bossId} must end within its own measured contact reach ({reach:F2}m) of " +
                    $"Max — ended {finalDistance:F2}m away");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                Object.DestroyImmediate(root);
                BossCensus.Reset();
                Sentinel.ResetRegistry();
                EnemyNavigation.Reset();
            }
        }

        /// <summary>1b — Sentinel in contact, in a fresh world. A Sentinel built through the real deploy
        /// path 8 m from the boss with nothing between; Max positioned well beyond it (20 m, same
        /// bearing) so the Sentinel is strictly nearer than Max and within
        /// <see cref="SentinelTargeting.AggroRadius"/> (10 m) from the very first tick —
        /// <see cref="SentinelTargeting.ShouldEngageSentinel"/> selects it immediately, deterministically,
        /// with no dependence on the boss's drift direction. After 12 simulated seconds the Sentinel must
        /// have lost at least 45 health, and the final centre distance to the Sentinel must be less than
        /// the measured contact reach to a Sentinel.</summary>
        private void RunMapLoaderScenario1b(string worldKey, string bossId)
        {
            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"{worldKey} failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject($"MV1083 Root 1b {worldKey}");
            GameObject maxGo = null, sentinelGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms();

                Assert.IsTrue(built.Actors.TryGetValue(bossId, out GameObject bossGo) && bossGo != null,
                    $"{worldKey}'s boss ('{bossId}') was not built");
                var boss = bossGo.GetComponent<BigBermudaBoss>();
                Assert.IsNotNull(boss, $"{bossId} did not build as a BigBermudaBoss");
                Vector3 bossPos = bossGo.transform.position;

                // Max first (tagged Player, far out) so AcquireTarget finds him, then a Sentinel 8m out
                // on the same bearing -- strictly nearer than Max from the first tick.
                maxGo = NewMax(bossPos + new Vector3(BeyondDistance, 0f, 0f));
                sentinelGo = NewSentinel(bossPos + new Vector3(ApproachDistance, 0f, 0f)).gameObject;
                var sentinel = sentinelGo.GetComponent<Sentinel>();

                InvokeAwake(boss);
                Physics.SyncTransforms();

                float reach = (float)BbContactReachTo.Invoke(boss, new object[] { sentinel.transform });

                float sentinelHealthBefore = sentinel.HealthCurrent;

                int ticks = Mathf.CeilToInt(SimulatedSeconds / Dt);
                for (int i = 0; i < ticks; i++)
                {
                    BbRetarget.Invoke(boss, null);

                    // Sanity on the test's own setup, not the fix: the Sentinel must actually have been
                    // selected as _target by the first tick, as AC1 requires.
                    if (i == 0)
                    {
                        var target = (Transform)BbTarget.GetValue(boss);
                        Assert.AreEqual(sentinel.transform, target,
                            $"{worldKey} 1b: the boss must engage the Sentinel from the first tick, not Max");
                    }

                    BbApproach.Invoke(boss, new object[] { Dt, 1f });
                    BbTickContactDamage.Invoke(boss, new object[] { Dt });
                    Physics.SyncTransforms();

                    // The Sentinel's only 100 HP against 15 dmg/tick -- sustained contact for the rest
                    // of the window can and does kill it outright (its DestructibleHealth.Destroyed
                    // handler DestroyImmediate's the GameObject in edit mode). That is conclusive proof
                    // of contact, not a test-setup failure -- stop driving it the instant it happens
                    // rather than touching its now-destroyed Transform below.
                    if (sentinel == null) break;
                }

                bool sentinelDied = sentinel == null;
                float healthLost = sentinelDied ? sentinelHealthBefore : sentinelHealthBefore - sentinel.HealthCurrent;
                float finalDistance = sentinelDied ? 0f : Vector3.Distance(bossGo.transform.position, sentinelGo.transform.position);
                Debug.Log($"MV-1083 {worldKey}/{bossId} 1b: measured contact reach to a Sentinel={reach:F2}m, " +
                    $"measured stop distance={(sentinelDied ? "n/a (Sentinel killed by contact)" : $"{finalDistance:F2}m")}");

                Assert.GreaterOrEqual(healthLost, MinHealthLost,
                    $"{worldKey} 1b: a Sentinel must lose at least {MinHealthLost} health to {bossId}'s contact " +
                    $"over {SimulatedSeconds}s — lost {healthLost:F1}");
                Assert.Less(finalDistance, reach + 0.01f,
                    $"{worldKey} 1b: {bossId} must end within its own measured contact reach ({reach:F2}m) of " +
                    $"the Sentinel — ended {finalDistance:F2}m away");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (sentinelGo != null) Object.DestroyImmediate(sentinelGo);
                Object.DestroyImmediate(root);
                BossCensus.Reset();
                Sentinel.ResetRegistry();
                EnemyNavigation.Reset();
            }
        }

        // ---------------------------------------------------------------- SludgequeenBoss

        private static readonly MethodInfo SqRetarget =
            typeof(SludgequeenBoss).GetMethod("RetargetIfNeeded", NonPublicInstance);
        private static readonly MethodInfo SqApproach =
            typeof(SludgequeenBoss).GetMethod("Approach", NonPublicInstance);
        private static readonly MethodInfo SqTickContactDamage =
            typeof(SludgequeenBoss).GetMethod("TickContactDamage", NonPublicInstance);
        private static readonly MethodInfo SqContactReachTo =
            typeof(SludgequeenBoss).GetMethod("ContactReachTo", NonPublicInstance);
        private static readonly FieldInfo SqTarget =
            typeof(SludgequeenBoss).GetField("_target", NonPublicInstance);

        // EditMode tests share one physics scene for the whole cc-verify run with no per-test reset —
        // distinctive, far-off origins sidestep colliding with another fixture's leftover geometry (same
        // reasoning as MV590BossWallSteeringTests.RigOrigin), and 1a/1b use different origins from each
        // other so "a fresh world" (AC1's own words for 1b) never shares physics state with 1a.
        private static readonly Vector3 SludgequeenOrigin1a = new Vector3(-61042f, 0f, 48031f);
        private static readonly Vector3 SludgequeenOrigin1b = new Vector3(-61042f, 0f, 58031f);

        private void RunSludgequeenScenarios()
        {
            RunSludgequeenScenario1a();
            RunSludgequeenScenario1b();
        }

        /// <summary>Built directly, the same way <c>MV696SludgequeenFloodTests</c>/
        /// <c>MV699SludgequeenRigTests</c> already do — <see cref="MapRuntime.BuildBoss"/> always builds
        /// a <see cref="BigBermudaBoss"/> body regardless of the authored boss id, so there is no real
        /// map-loader entry point for a true <see cref="SludgequeenBoss"/> instance today. Scaled to a
        /// plausible real boss size (3.6-4.5 m, World 1/3's own authored range) rather than left at the
        /// primitive's default scale 1, so the world-space radius scaling this ticket's fix depends on
        /// is actually exercised, not trivially 1:1.</summary>
        private void RunSludgequeenScenario1a()
        {
            GameObject bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = bossGo.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            bossGo.transform.position = SludgequeenOrigin1a;
            bossGo.transform.localScale = Vector3.one * 4f;

            GameObject maxGo = null;
            try
            {
                Vector3 bossPos = bossGo.transform.position;
                maxGo = NewMax(bossPos + new Vector3(ApproachDistance, 0f, 0f));
                var maxHealth = maxGo.GetComponent<PlayerHealth>();

                var boss = bossGo.AddComponent<SludgequeenBoss>();
                InvokeAwake(boss);
                Physics.SyncTransforms();

                var target = (Transform)SqTarget.GetValue(boss);
                float reach = (float)SqContactReachTo.Invoke(boss, new object[] { target });

                float maxHealthBefore = maxHealth.Current;

                int ticks = Mathf.CeilToInt(SimulatedSeconds / Dt);
                for (int i = 0; i < ticks; i++)
                {
                    SqRetarget.Invoke(boss, null);
                    SqApproach.Invoke(boss, new object[] { Dt });
                    SqTickContactDamage.Invoke(boss, new object[] { Dt });
                    Physics.SyncTransforms();
                }

                float healthLost = maxHealthBefore - maxHealth.Current;
                float finalDistance = Vector3.Distance(bossGo.transform.position, maxGo.transform.position);
                Debug.Log($"MV-1083 Sludgequeen 1a: measured contact reach to Max={reach:F2}m, " +
                    $"measured stop distance={finalDistance:F2}m");

                Assert.GreaterOrEqual(healthLost, MinHealthLost,
                    $"Sludgequeen 1a: Max must lose at least {MinHealthLost} health over {SimulatedSeconds}s — " +
                    $"lost {healthLost:F1}");
                Assert.Less(finalDistance, reach + 0.01f,
                    $"Sludgequeen 1a must end within its own measured contact reach ({reach:F2}m) of Max — " +
                    $"ended {finalDistance:F2}m away");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                Object.DestroyImmediate(bossGo);
                BossCensus.Reset();
                Sentinel.ResetRegistry();
                SludgequeenBoss.ResetRegistry();
                EnemyNavigation.Reset();
            }
        }

        /// <summary>1b — same shape as <see cref="RunMapLoaderScenario1b"/>: a Sentinel 8m out, Max 20m
        /// out on the same bearing, so the Sentinel is strictly nearer and selected from the first tick.</summary>
        private void RunSludgequeenScenario1b()
        {
            GameObject bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = bossGo.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            bossGo.transform.position = SludgequeenOrigin1b;
            bossGo.transform.localScale = Vector3.one * 4f;

            GameObject maxGo = null, sentinelGo = null;
            try
            {
                Vector3 bossPos = bossGo.transform.position;
                maxGo = NewMax(bossPos + new Vector3(BeyondDistance, 0f, 0f));
                sentinelGo = NewSentinel(bossPos + new Vector3(ApproachDistance, 0f, 0f)).gameObject;
                var sentinel = sentinelGo.GetComponent<Sentinel>();

                var boss = bossGo.AddComponent<SludgequeenBoss>();
                InvokeAwake(boss);
                Physics.SyncTransforms();

                float reach = (float)SqContactReachTo.Invoke(boss, new object[] { sentinel.transform });

                float sentinelHealthBefore = sentinel.HealthCurrent;

                int ticks = Mathf.CeilToInt(SimulatedSeconds / Dt);
                for (int i = 0; i < ticks; i++)
                {
                    SqRetarget.Invoke(boss, null);

                    if (i == 0)
                    {
                        var target = (Transform)SqTarget.GetValue(boss);
                        Assert.AreEqual(sentinel.transform, target,
                            "Sludgequeen 1b: the boss must engage the Sentinel from the first tick, not Max");
                    }

                    SqApproach.Invoke(boss, new object[] { Dt });
                    SqTickContactDamage.Invoke(boss, new object[] { Dt });
                    Physics.SyncTransforms();

                    // Same reasoning as RunMapLoaderScenario1b: sustained contact can and does kill a
                    // 100 HP Sentinel outright before the window ends -- conclusive proof of contact,
                    // not a test-setup failure.
                    if (sentinel == null) break;
                }

                bool sentinelDied = sentinel == null;
                float healthLost = sentinelDied ? sentinelHealthBefore : sentinelHealthBefore - sentinel.HealthCurrent;
                float finalDistance = sentinelDied ? 0f : Vector3.Distance(bossGo.transform.position, sentinelGo.transform.position);
                Debug.Log($"MV-1083 Sludgequeen 1b: measured contact reach to a Sentinel={reach:F2}m, " +
                    $"measured stop distance={(sentinelDied ? "n/a (Sentinel killed by contact)" : $"{finalDistance:F2}m")}");

                Assert.GreaterOrEqual(healthLost, MinHealthLost,
                    $"Sludgequeen 1b: a Sentinel must lose at least {MinHealthLost} health over {SimulatedSeconds}s " +
                    $"— lost {healthLost:F1}");
                Assert.Less(finalDistance, reach + 0.01f,
                    $"Sludgequeen 1b must end within its own measured contact reach ({reach:F2}m) of the " +
                    $"Sentinel — ended {finalDistance:F2}m away");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (sentinelGo != null) Object.DestroyImmediate(sentinelGo);
                Object.DestroyImmediate(bossGo);
                BossCensus.Reset();
                Sentinel.ResetRegistry();
                SludgequeenBoss.ResetRegistry();
                EnemyNavigation.Reset();
            }
        }
    }
}
