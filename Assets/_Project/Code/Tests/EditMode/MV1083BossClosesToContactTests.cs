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
    /// Fails on base commit d30d293 (confirmed via <c>git log d30d293..HEAD</c> that no commit since
    /// touched <c>BigBermudaBoss</c>/<c>BossTuning</c>/<c>SludgequeenBoss</c>/<c>SludgequeenTuning</c>/
    /// <c>AnchorheadBoss</c>/<c>SentinelTargeting</c> before this ticket's own fix, so current HEAD
    /// pre-fix is behaviourally identical to it for this code path): a boss parked at a fixed Standoff
    /// orbits outside its own measured reach, so neither Max's nor the Sentinel's health ever moves.
    /// Quoted failure output (pre-fix): see the fix comment on the ticket.
    ///
    /// ONE new EditMode test (MV-465 Rule 1), carrying the ticket's whole AC1 as per-boss-class
    /// sub-checks. <see cref="BigBermudaBoss"/> and <see cref="AnchorheadBoss"/> (a companion on the
    /// SAME <see cref="BigBermudaBoss"/> instance — MV-1018's own class doc: "the brood volley and
    /// contact damage are still entirely BigBermudaBoss's, unmodified") are built through the REAL
    /// production route (<see cref="MapRuntime.Build"/> off world1_config's "a12_boss1" and
    /// world3_config's "anchorhead", so each carries its own authored world-space size) with a real,
    /// stationary Max (<see cref="CharacterController"/> + <see cref="PlayerController"/> +
    /// <see cref="PlayerHealth"/>) and a real deployed <see cref="Sentinel"/> (<see cref="Sentinel.Init"/>,
    /// the real deploy path) — same build idiom <c>MV720BossContactDamageTests</c> already uses.
    /// <see cref="SludgequeenBoss"/> has no map-loader entry at all yet (its own class doc: "actually
    /// placing this boss in bosses[]... is left for a follow-up") — built the same direct way its own
    /// <c>MV696SludgequeenFloodTests</c>/<c>MV699SludgequeenRigTests</c> precedent already does, the
    /// closest real entry point this class has today.
    ///
    /// Each boss is driven through its own private per-tick seams directly (reflection) —
    /// <c>RetargetIfNeeded</c>, <c>Approach</c>, <c>TickContactDamage</c> — the same granular idiom
    /// <c>MV720BossContactDamageTests</c>/<c>MV590BossWallSteeringTests</c>/<c>MV667BossConcaveRoutingTests</c>
    /// already use, rather than needing <c>Wake()</c> or a live <c>Update()</c> loop. The Sentinel's
    /// lateral offset side is chosen from the boss's own resolved <c>_preferSign</c> (read back after
    /// <c>Awake</c>) so the standoff drift sweeps TOWARD it — the shorter of the two arcs around Max —
    /// deterministically, rather than leaving it to <c>GetInstanceID()</c>'s parity.
    ///
    /// EditMode only, reflection-driven (repo convention — this worker never authors PlayMode tests).
    ///
    /// <para><b>MEASURED, NOT GUESSED — AC1's own numbers conflict with requirement #4, reported rather
    /// than silently worked around (needs-triage, see the ticket comment).</b> Requirement #4 says a
    /// boss reuses <see cref="MaxWorlds.Arena.SentinelTargeting.ShouldEngageSentinel"/> UNMODIFIED, the
    /// same function <see cref="MaxWorlds.Enemies.RobotEnemy.RetargetIfNeeded"/> calls every Chase tick
    /// — including while already in standing contact (<c>RobotEnemy.TickContactTouch</c> runs inside
    /// Chase too, confirmed by reading it). For ANY boss, at the instant it first reaches standoff from
    /// Max (radius R = measured reach − <see cref="BossTuning.StandoffMargin"/>), a Sentinel sitting
    /// exactly <see cref="SideOffset"/> m to Max's side becomes the NEARER target after the boss has
    /// swept only <c>arcsin(SideOffset / (2R))</c> of orbit — algebra that cancels R almost entirely
    /// (World 1's 2.6 m-reach boss: 23.6°, 1.03 m of arc; World 3's 3.05 m-reach boss: 19.8°, 1.02 m of
    /// arc — both ≈1.03 m / <see cref="BossTuning.MoveSpeed"/> ≈ 1.14 s), so the boss can land AT MOST
    /// ~2 of the <see cref="MinHealthLost"/>-worth-of-5 <see cref="BossTuning.ContactCooldown"/>-paced
    /// ticks it needs on Max before switching away — measured 30 HP (2 ticks), never 45, on World 1's
    /// a12_boss1. Placing the Sentinel on the OTHER side (so the boss drifts away from it instead)
    /// flips the failure the other way: Max reaches 75 HP by t=11s (comfortably over 45) but the
    /// Sentinel sits at 100/100 the entire 12 s, never once inside reach. Both runs, and the exact
    /// numbers above, are reproducible by swapping the <c>sideSign</c> ternary below. There is no
    /// Sentinel placement 2 m from Max that lets a single 12 s run satisfy both AC1 health floors AT
    /// ONCE once requirement #4 is implemented as specified — this is a property of
    /// (<see cref="SideOffset"/>, <see cref="BossTuning.MoveSpeed"/>, <see cref="BossTuning.ContactCooldown"/>)
    /// independent of which boss class or world is under test.</para>
    /// </summary>
    public sealed class MV1083BossClosesToContactTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private const float Dt = 1f / 60f;
        private const float SimulatedSeconds = 12f;
        private const float ApproachDistance = 8f;   // the ticket's own "a stationary damageable target 8m away"
        private const float SideOffset = 2f;          // the ticket's own "a second target placed 2m to the side"
        private const float MinHealthLost = 45f;       // the ticket's own "lost at least 45 health"

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
            RunMapLoaderScenario(WorldLibrary.World1, "a12_boss1");
            RunMapLoaderScenario(WorldLibrary.World3, "anchorhead");
            RunSludgequeenScenario();
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
        private static readonly FieldInfo BbPreferSign =
            typeof(BigBermudaBoss).GetField("_preferSign", NonPublicInstance);
        private static readonly FieldInfo BbTarget =
            typeof(BigBermudaBoss).GetField("_target", NonPublicInstance);

        /// <summary>Built through the real map loader — world1_config's "a12_boss1" is a plain
        /// <see cref="BigBermudaBoss"/>; world3_config's "anchorhead" is the SAME component with an
        /// <see cref="AnchorheadBoss"/> companion riding alongside it (<see cref="MapRuntime.BuildBoss"/>),
        /// so driving the <see cref="BigBermudaBoss"/> this way exercises both.</summary>
        private void RunMapLoaderScenario(string worldKey, string bossId)
        {
            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"{worldKey} failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject($"MV1083 Root {worldKey}");
            GameObject maxGo = null, sentinelGo = null;
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

                float preferSign = (float)BbPreferSign.GetValue(boss);
                float sideSign = preferSign >= 0f ? 1f : -1f; // drift toward the Sentinel's side, not away
                sentinelGo = NewSentinel(maxGo.transform.position + new Vector3(0f, 0f, SideOffset * sideSign)).gameObject;
                var sentinel = sentinelGo.GetComponent<Sentinel>();
                Physics.SyncTransforms();

                var target = (Transform)BbTarget.GetValue(boss);
                float reach = (float)BbContactReachTo.Invoke(boss, new object[] { target });
                float stopDistance = reach - BossTuning.StandoffMargin;
                Debug.Log($"MV-1083 {worldKey}/{bossId}: measured contact reach={reach:F2}m, stop distance={stopDistance:F2}m");

                float maxHealthBefore = maxHealth.Current;
                float sentinelHealthBefore = sentinel.HealthCurrent;

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

                Assert.GreaterOrEqual(healthLost, MinHealthLost,
                    $"{worldKey}: Max must lose at least {MinHealthLost} health to {bossId}'s contact over " +
                    $"{SimulatedSeconds}s — lost {healthLost:F1}");
                Assert.Less(finalDistance, reach + 0.01f,
                    $"{worldKey}: {bossId} must end within its own measured contact reach ({reach:F2}m) of " +
                    $"Max — ended {finalDistance:F2}m away");
                Assert.Greater(sentinelHealthBefore - sentinel.HealthCurrent, 0f,
                    $"{worldKey}: a Sentinel {SideOffset}m to the side of Max must also take contact damage " +
                    $"from {bossId}");
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
        private static readonly FieldInfo SqPreferSign =
            typeof(SludgequeenBoss).GetField("_preferSign", NonPublicInstance);
        private static readonly FieldInfo SqTarget =
            typeof(SludgequeenBoss).GetField("_target", NonPublicInstance);

        // EditMode tests share one physics scene for the whole cc-verify run with no per-test reset —
        // a distinctive, far-off origin sidesteps colliding with another fixture's leftover geometry
        // (same reasoning as MV590BossWallSteeringTests.RigOrigin).
        private static readonly Vector3 SludgequeenOrigin = new Vector3(-61042f, 0f, 48031f);

        /// <summary>Built directly, the same way <c>MV696SludgequeenFloodTests</c>/
        /// <c>MV699SludgequeenRigTests</c> already do — <see cref="MapRuntime.BuildBoss"/> always builds
        /// a <see cref="BigBermudaBoss"/> body regardless of the authored boss id, so there is no real
        /// map-loader entry point for a true <see cref="SludgequeenBoss"/> instance today. Scaled to a
        /// plausible real boss size (3.6-4.5 m, World 1/3's own authored range) rather than left at the
        /// primitive's default scale 1, so the world-space radius scaling this ticket's fix depends on
        /// is actually exercised, not trivially 1:1.</summary>
        private void RunSludgequeenScenario()
        {
            GameObject bossGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = bossGo.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            bossGo.transform.position = SludgequeenOrigin;
            bossGo.transform.localScale = Vector3.one * 4f;

            GameObject maxGo = null, sentinelGo = null;
            try
            {
                Vector3 bossPos = bossGo.transform.position;
                maxGo = NewMax(bossPos + new Vector3(ApproachDistance, 0f, 0f));
                var maxHealth = maxGo.GetComponent<PlayerHealth>();

                var boss = bossGo.AddComponent<SludgequeenBoss>();
                InvokeAwake(boss);

                float preferSign = (float)SqPreferSign.GetValue(boss);
                float sideSign = preferSign >= 0f ? 1f : -1f;
                sentinelGo = NewSentinel(maxGo.transform.position + new Vector3(0f, 0f, SideOffset * sideSign)).gameObject;
                var sentinel = sentinelGo.GetComponent<Sentinel>();
                Physics.SyncTransforms();

                var target = (Transform)SqTarget.GetValue(boss);
                float reach = (float)SqContactReachTo.Invoke(boss, new object[] { target });
                float stopDistance = reach - SludgequeenTuning.StandoffMargin;
                Debug.Log($"MV-1083 Sludgequeen: measured contact reach={reach:F2}m, stop distance={stopDistance:F2}m");

                float maxHealthBefore = maxHealth.Current;
                float sentinelHealthBefore = sentinel.HealthCurrent;

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

                Assert.GreaterOrEqual(healthLost, MinHealthLost,
                    $"Sludgequeen: Max must lose at least {MinHealthLost} health over {SimulatedSeconds}s — " +
                    $"lost {healthLost:F1}");
                Assert.Less(finalDistance, reach + 0.01f,
                    $"Sludgequeen must end within its own measured contact reach ({reach:F2}m) of Max — " +
                    $"ended {finalDistance:F2}m away");
                Assert.Greater(sentinelHealthBefore - sentinel.HealthCurrent, 0f,
                    $"Sludgequeen: a Sentinel {SideOffset}m to the side of Max must also take contact damage");
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
