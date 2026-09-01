using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-625: a20's and a30's bosses stood alive but invisible once a12's boss had died earlier in the
    /// same run. Neither of the ticket's two listed hypotheses reproduced: the whole map (all six
    /// authored bosses) is built exactly ONCE, at scene load (<see cref="MapRuntime.Build"/>), with no
    /// exception and no later rebuild for a death-continue to skip <c>CreateFor</c> on.
    ///
    /// The actual mechanism, found by reading <see cref="HudSignals.BossDefeated"/>'s subscribers: it is
    /// a scene-wide signal carrying no boss identity, and every <see cref="BigBermudaRig"/> the map has
    /// built so far is subscribed to it from <c>OnEnable</c> — including a20's and a30's, since MV-573
    /// already built every boss's rig at scene load, long before Max reaches those areas. a12's boss
    /// dying broadcasts <c>BossDefeated</c> once, scene-wide, and (pre-fix) <c>BigBermudaRig.OnDefeated</c>
    /// started EVERY subscribed rig's death sequence unconditionally — which ends in
    /// <c>gameObject.SetActive(false)</c>. The actual <see cref="BigBermudaBoss"/> a20/a30's rigs are
    /// bound to never died (a separate object the rig only follows), so it kept fighting — brood volleys
    /// flinging robots out of a body no longer there to see.
    /// </summary>
    public sealed class MV625CrossAreaBossDeathTests
    {
        [SetUp]
        [TearDown]
        public void ResetCensus() => BossCensus.Reset();

        // BigBermudaBoss.OnDeath is what a real boss's own health-depleted callback calls; driving it
        // directly is the same idiom BossTests.cs already uses for BossCensus rather than ticking the
        // boss's own Update-only phase machine, which EditMode cannot advance.
        private static void InvokeOnDeath(BigBermudaBoss boss) =>
            typeof(BigBermudaBoss).GetMethod("OnDeath", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

        private static void InvokeOnEnable(BigBermudaRig rig) =>
            typeof(BigBermudaRig).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(rig, null);

        [Test]
        public void OneBossDying_DoesNotHideAnyOtherStillLivingBossOnTheMap()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV-625 Cross-Area Boss Death Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);

                // OnEnable isn't reliably invoked for AddComponent outside Play mode (same note
                // MV613BossRigScaleTests/MV590BossWallSteeringTests carry for Awake) -- drive it
                // directly so every rig actually subscribes to HudSignals.BossEngaged/BossDefeated the
                // way it does for real once the game is actually running.
                foreach (var r in UnityEngine.Object.FindObjectsByType<BigBermudaRig>(FindObjectsSortMode.None))
                    InvokeOnEnable(r);

                string[] allBossIds =
                    { "a12_boss1", "a20_boss1", "a20_boss2", "a30_boss1", "a30_boss2", "a30_boss3" };
                string[] stillLivingBossIds =
                    { "a20_boss1", "a20_boss2", "a30_boss1", "a30_boss2", "a30_boss3" };

                // --- baseline: every authored boss has exactly one bound, visible, correctly-scaled rig ---
                foreach (string id in allBossIds)
                    AssertBossHasOneVisibleCorrectlyScaledRig(built, id);

                // --- simulate the run sequence Lee hit: a12's boss actually dies while a20's and a30's
                // bosses are still alive elsewhere on the SAME already-built map. Registering first is
                // what a real Wake() does -- it is also what lets BossCensus.ReportDefeated (inside
                // OnDeath) actually broadcast HudSignals.BossDefeated below (an unregistered boss's
                // "death" is a silent no-op), and it is what puts every rig on the map into Running,
                // via BossCensus's own scene-wide "first boss to wake" HudSignals.BossEngaged broadcast
                // -- exactly the state a20's/a30's still-dormant rigs are ALSO in for real, the moment
                // any earlier boss in the run has woken.
                GameObject a12Go = built.Actors["a12_boss1"];
                BigBermudaBoss a12Boss = a12Go.GetComponent<BigBermudaBoss>();
                BossCensus.Register(a12Boss, "BIG BERMUDA", 2, current: 100f, max: 100f, areaIndex: 1);

                foreach (string id in stillLivingBossIds)
                    Assert.IsTrue(RigFor(built, id).Running,
                        $"precondition: '{id}'s rig must be Running before a12's boss ever dies");

                InvokeOnDeath(a12Boss);
                Assert.IsTrue(a12Boss.IsDead, "precondition: a12's boss must have actually died");

                // --- every OTHER boss on the map is still alive and its rig must still be Running: a
                // boss dying anywhere must never start another boss's rig's death sequence
                // (BigBermudaRig.OnDefeated), which is a one-way trip to gameObject.SetActive(false) ---
                foreach (string id in stillLivingBossIds)
                {
                    var boss = built.Actors[id].GetComponent<BigBermudaBoss>();
                    Assert.IsFalse(boss.IsDead, $"'{id}' never died -- only a12's boss did");
                    Assert.IsTrue(RigFor(built, id).Running,
                        $"'{id}'s rig stopped Running after a12's boss died, though '{id}' itself is still alive");

                    AssertBossHasOneVisibleCorrectlyScaledRig(built, id);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static BigBermudaRig RigFor(MapBuild built, string bossId)
        {
            BigBermudaBoss boss = built.Actors[bossId].GetComponent<BigBermudaBoss>();
            BigBermudaRig[] allRigs = UnityEngine.Object.FindObjectsByType<BigBermudaRig>(FindObjectsSortMode.None);
            return Array.Find(allRigs, r => r.Boss == boss);
        }

        private static void AssertBossHasOneVisibleCorrectlyScaledRig(MapBuild built, string bossId)
        {
            Assert.IsTrue(built.Actors.TryGetValue(bossId, out GameObject bossGo) && bossGo != null,
                $"world1_config.json's '{bossId}' was not built");
            BigBermudaBoss boss = bossGo.GetComponent<BigBermudaBoss>();
            Assert.IsNotNull(boss, $"'{bossId}' carries no BigBermudaBoss");

            BigBermudaRig[] allRigs = UnityEngine.Object.FindObjectsByType<BigBermudaRig>(FindObjectsSortMode.None);
            BigBermudaRig[] bound = Array.FindAll(allRigs, r => r.Boss == boss);
            Assert.AreEqual(1, bound.Length, $"'{bossId}' must have exactly one bound rig, found {bound.Length}");

            BigBermudaRig rig = bound[0];
            Assert.IsTrue(rig.gameObject.activeInHierarchy, $"'{bossId}'s rig must be active, not deactivated");

            MeshRenderer[] renderers = rig.GetComponentsInChildren<MeshRenderer>();
            Assert.IsNotEmpty(renderers, $"'{bossId}'s rig built no visible body");
            foreach (MeshRenderer r in renderers)
                Assert.IsTrue(r.enabled, $"'{bossId}'s rig has a disabled renderer");

            // BigBermudaRig.LegacyAuthoredBodyWidth == 6f -- the resolved scale Bind() actually wrote,
            // not the JSON, so a config edit that never reached the rig would still fail this.
            float expectedScale = boss.transform.localScale.x / 6f;
            Assert.AreEqual(expectedScale, rig.transform.lossyScale.x, 0.01f,
                $"'{bossId}'s rig root scale must track its authored width");
        }
    }
}
