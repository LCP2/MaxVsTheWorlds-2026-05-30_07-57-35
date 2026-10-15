using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1193 (the one new test, per CC_AUTONOMY's testing policy): World 1 a30, Lee's device, 15 Oct
    /// -- killing the first of its two authored Big Bermudas marked a30 "defeated" while the second
    /// still stood Dormant, never approached, never <see cref="BossCensus.Register"/>-ed. The second
    /// then read its own area as already-beaten the moment anything hit it (the MV-995 "already beaten
    /// on a prior run" path) and self-destructed -- its unparented <see cref="BigBermudaRig"/> body was
    /// left standing: inert, undamageable, never dying. No orb, exit stays LOCKED.
    ///
    /// Drives the real, authored <c>world1_config.json</c> through the actual map build (a30 authors
    /// exactly two bosses, a30_boss1/a30_boss2, same as production), so this is also proof the fix is
    /// wired into <see cref="MapRuntime.Build"/> for real, not just <see cref="BossCensus"/> in
    /// isolation.
    ///
    /// Fails on base commit 5e77659: <c>BossCensus.ReportDefeated</c> there marks an area defeated the
    /// instant no REGISTERED boss in it is alive, so the first assertion below -- a30 must NOT read
    /// defeated once only boss1 (of two) has died -- fails; a30 already reads defeated at that point.
    ///
    /// Tier 2 (resolved values): asserts BossCensus's own resolved IsAreaDefeated/AnyLivingIn answers
    /// and real GameObjects' Unity fake-null / activeInHierarchy state after Destroy -- never an
    /// authored constant asserted back at itself.
    /// </summary>
    public sealed class MV1193TwoBossAreaDefeatTests
    {
        private const int A30AreaIndex = 30;

        // BigBermudaBoss.Wake's own ResolveAreaIndex reads EnemyNavigation.Map, not whatever MapData
        // local a caller built with -- that static lazily resolves via FindFirstObjectByType<BackyardPath>
        // and caches the answer, so without a BackyardPath in the scene it reads null and every boss
        // resolves to area 0. Same reflection seam BackyardEntryDoorTests/MV1001PickupDeckHeightAndLevelTests/
        // MV1006SentinelFiresThroughBarriersTests already use to point it at a map built outside Awake.
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        // Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode (same note
        // MV1190BossDeathSequenceTests/MV613BossRigScaleTests/MV590BossWallSteeringTests already carry) --
        // boss2's own Wake() (driven for real below, via TakeDamage) reads _health/_brain/_volley, all set
        // in Awake, so this is not optional here the way it is for tests that only ever call
        // BossCensus.Register/OnDeath directly.
        private static void InvokeAwake(Object c) =>
            c.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, null);

        private static void InvokeOnEnable(Object c) =>
            c.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, null);

        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        private static void InvokeTickDeath(BigBermudaRig rig, float dt) =>
            typeof(BigBermudaRig).GetMethod("TickDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(rig, new object[] { dt });

        private static BigBermudaRig RigFor(BigBermudaBoss boss) =>
            Object.FindObjectsByType<BigBermudaRig>(FindObjectsSortMode.None).First(r => r.Boss == boss);

        // Drives a rig's own death choreography comfortably past its ~2.2s total -- same margin
        // MV1190BossDeathSequenceTests uses (160 ticks @ 1/60s =~ 2.67s) -- which is what actually flips
        // BossDeathSequence.Done and lets BigBermudaRig.TickDeath deactivate the rig's own gameObject.
        private static void RunDeathSequenceToCompletion(BigBermudaRig rig)
        {
            const float dt = 1f / 60f;
            for (int i = 0; i < 160; i++) InvokeTickDeath(rig, dt);
        }

        [Test]
        public void SecondDormantBossInATwoBossArea_StaysFightable_UntilBothAuthoredBossesAreDead()
        {
            BossCensus.Reset();
            EnemyNavigation.Reset();
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV-1193 Two-Boss Area Defeat Probe Root");
            try
            {
                var backyardPathGo = new GameObject("MV-1193 BackyardPath stand-in");
                backyardPathGo.transform.SetParent(root.transform, false);
                BackyardPathMapField.SetValue(backyardPathGo.AddComponent<BackyardPath>(), map);

                MapBuild built = MapRuntime.Build(map, root.transform);

                Assert.IsTrue(built.Actors.TryGetValue("a30_boss1", out GameObject boss1Go) && boss1Go != null,
                    "world1_config.json's a30_boss1 was not built");
                Assert.IsTrue(built.Actors.TryGetValue("a30_boss2", out GameObject boss2Go) && boss2Go != null,
                    "world1_config.json's a30_boss2 was not built");

                var boss1 = boss1Go.GetComponent<BigBermudaBoss>();
                var boss2 = boss2Go.GetComponent<BigBermudaBoss>();
                InvokeAwake(boss1);
                InvokeAwake(boss2);

                BigBermudaRig rig1 = RigFor(boss1);
                BigBermudaRig rig2 = RigFor(boss2);
                InvokeOnEnable(rig1);
                InvokeOnEnable(rig2);

                // Boss1 wakes and fights for real; boss2 stays Dormant, never approached, never
                // Register-ed -- exactly a30's own live sequence (Lee, device, 15 Oct).
                BossCensus.Register(boss1, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: A30AreaIndex);
                InvokeOnDeath(boss1);

                Assert.IsFalse(BossCensus.IsAreaDefeated(A30AreaIndex),
                    "boss2 (one of a30's TWO authored bosses) is still standing, un-killed -- a30 must " +
                    "not read defeated yet");

                // Boss2 wakes for real -- a hit landing is itself a wake trigger (MV-1110); it must
                // engage normally, not read its own area as already-beaten and self-destruct.
                boss2.TakeDamage(new DamageInfo(0f, boss2.transform.position, Vector3.forward, Team.Player));

                Assert.IsTrue(boss2 != null, "boss2 must not have been destroyed on waking");
                Assert.IsTrue(BossCensus.AnyLivingIn(A30AreaIndex),
                    "boss2 must be registered and alive in BossCensus after waking for real");

                InvokeOnDeath(boss2);

                Assert.IsTrue(BossCensus.IsAreaDefeated(A30AreaIndex),
                    "both of a30's authored bosses are now dead -- the area must read defeated");

                RunDeathSequenceToCompletion(rig1);
                RunDeathSequenceToCompletion(rig2);

                Assert.IsFalse(rig1.gameObject.activeInHierarchy,
                    "boss1's own rig body must not be left standing active once its death sequence finished");
                Assert.IsFalse(rig2.gameObject.activeInHierarchy,
                    "boss2's own rig body must not be left standing active once its death sequence finished");
            }
            finally
            {
                Object.DestroyImmediate(root);
                BossCensus.Reset();
                EnemyNavigation.Reset();
            }
        }
    }
}
