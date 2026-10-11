using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1145 (the one new test, per CC_AUTONOMY's testing policy): a boss is built from its KIND,
    /// resolved through one table (<see cref="BossKinds"/>), not from a string comparison against its
    /// authored id. Every sub-check loads a world through the real path (<c>WorldLibrary.Load</c> /
    /// <c>WorldMapLoader.TryLoad</c> -&gt; <c>MapRuntime.Build</c>), the same idiom
    /// <see cref="MV1018AnchorheadBossTests"/> already uses.
    ///
    /// Fails on base commit 7db54db: <c>WorldBoss</c> has no <c>kind</c> field, <c>BossKinds</c> does
    /// not exist, and <c>MapRuntime.BuildBoss</c> still dispatches on <c>e.id == "sludgequeen"</c> /
    /// <c>e.id == "anchorhead"</c> — a compile failure, which is the expected fail-first proof here.
    /// </summary>
    public sealed class BossKindDispatchTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>();

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        /// <summary>Wakes <paramref name="boss"/> through its real Awake/Wake path (AddComponent never
        /// fires Awake in this EditMode harness — MV-1013's own note) and captures the name it registers
        /// with <see cref="BossCensus"/>, via the same <see cref="HudSignals.BossEngaged"/> signal the
        /// HUD itself listens to. A fresh <see cref="BossCensus.Reset"/> first makes this boss the FIRST
        /// to register, which is the only registration that re-emits the signal.</summary>
        private static string CaptureRegisteredName(BigBermudaBoss boss)
        {
            BossCensus.Reset();
            string captured = null;
            void OnEngaged(string name, int phases) => captured = name;

            HudSignals.BossEngaged += OnEngaged;
            try
            {
                InvokePrivate(boss, "Awake");
                InvokePrivate(boss, "Wake");
            }
            finally
            {
                HudSignals.BossEngaged -= OnEngaged;
            }

            return captured;
        }

        private MapBuild BuildWorld(string worldKey)
        {
            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"world '{worldKey}' failed to load — see the error log above.");
            return BuildConfig(cfg);
        }

        private MapBuild BuildConfig(WorldConfig cfg)
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            var root = new GameObject($"MV1145 Root {_roots.Count}");
            _roots.Add(root);
            return MapRuntime.Build(map, root.transform);
        }

        private static WorldBoss FindBoss(WorldConfig cfg, string bossId)
        {
            foreach (WorldArea a in cfg.areas)
                foreach (WorldBoss b in a.Bosses())
                    if (b.id == bossId) return b;

            Assert.Fail($"boss '{bossId}' not found in config");
            return null;
        }

        [Test]
        public void BuildBoss_DispatchesByKind_NotById()
        {
            try
            {
                // --- 1a. SHIPPED WORLDS: every World 1 boss is kind 'bigbermuda' and registers
                // "BIG BERMUDA". ---
                MapBuild world1 = BuildWorld(WorldLibrary.World1);
                foreach (string bossId in new[] { "a12_boss1", "a20_boss1", "a30_boss1", "a30_boss2" })
                {
                    Assert.IsTrue(world1.Actors.TryGetValue(bossId, out GameObject go) && go != null,
                        $"World 1 boss '{bossId}' was not built");

                    var boss = go.GetComponent<BigBermudaBoss>();
                    Assert.IsNotNull(boss, $"World 1 boss '{bossId}' must build as kind 'bigbermuda'");
                    Assert.IsNull(go.GetComponent<AnchorheadBoss>(), $"World 1 boss '{bossId}' must not carry AnchorheadBoss");
                    Assert.IsNull(go.GetComponent<SludgequeenBoss>(), $"World 1 boss '{bossId}' must not carry SludgequeenBoss");

                    Assert.AreEqual("BIG BERMUDA", CaptureRegisteredName(boss),
                        $"World 1 boss '{bossId}' must register 'BIG BERMUDA'");
                }

                // --- 1a/1b. World 3's 'anchorhead' carries AnchorheadBoss and its 'a15_boss' does not;
                // the Anchorhead fight registers "ANCHORHEAD" (it registered "BIG BERMUDA" before this
                // ticket — see BigBermudaBoss.cs's own un-overridden BossName, the hypothesis this
                // ticket's description recorded). ---
                MapBuild world3 = BuildWorld(WorldLibrary.World3);

                Assert.IsTrue(world3.Actors.TryGetValue("anchorhead", out GameObject anchorGo) && anchorGo != null,
                    "World 3's 'anchorhead' boss was not built");
                var anchorBigBermuda = anchorGo.GetComponent<BigBermudaBoss>();
                Assert.IsNotNull(anchorGo.GetComponent<AnchorheadBoss>(), "World 3's 'anchorhead' boss must carry AnchorheadBoss");

                Assert.IsTrue(world3.Actors.TryGetValue("a15_boss", out GameObject a15Go) && a15Go != null,
                    "World 3's 'a15_boss' was not built");
                Assert.IsNull(a15Go.GetComponent<AnchorheadBoss>(), "World 3's 'a15_boss' must NOT carry AnchorheadBoss");

                Assert.AreEqual("ANCHORHEAD", CaptureRegisteredName(anchorBigBermuda),
                    "the Anchorhead fight must register 'ANCHORHEAD' with BossCensus");

                // --- 1a. World 2's 'sludgequeen' carries SludgequeenBoss (MV-1127 has merged). ---
                MapBuild world2 = BuildWorld(WorldLibrary.World2);
                Assert.IsTrue(world2.Actors.TryGetValue("sludgequeen", out GameObject sludgeGo) && sludgeGo != null,
                    "World 2's 'sludgequeen' boss was not built");
                Assert.IsNotNull(sludgeGo.GetComponent<SludgequeenBoss>(), "World 2's 'sludgequeen' boss must carry SludgequeenBoss");

                // --- 1c. KIND BEATS ID: a copy of World 3 with 'a15_boss' authoring kind "anchorhead"
                // must build THAT boss with AnchorheadBoss, even though its id is not "anchorhead". ---
                WorldConfig world3Copy = WorldLibrary.Load(WorldLibrary.World3);
                WorldBoss a15Boss = FindBoss(world3Copy, "a15_boss");
                a15Boss.kind = "anchorhead";

                MapBuild kindBeatsId = BuildConfig(world3Copy);
                Assert.IsTrue(kindBeatsId.Actors.TryGetValue("a15_boss", out GameObject a15KindGo) && a15KindGo != null,
                    "'a15_boss' was not built once it authored kind 'anchorhead'");
                Assert.IsNotNull(a15KindGo.GetComponent<AnchorheadBoss>(),
                    "authoring kind 'anchorhead' on 'a15_boss' must build it with AnchorheadBoss");

                // --- 1d. UNKNOWN KIND: the same boss authoring an unregistered kind must fail the load,
                // not silently fall back to Big Bermuda. ---
                a15Boss.kind = "nosuchboss";
                bool loaded = WorldMapLoader.TryLoad(world3Copy, out MapData _, out string reason);

                Assert.IsFalse(loaded, "an unknown boss kind must fail the load");
                Assert.IsNotNull(reason);
                StringAssert.Contains("nosuchboss", reason);
                StringAssert.Contains("a15", reason);
                StringAssert.Contains("anchorhead", reason);
            }
            finally
            {
                foreach (GameObject root in _roots)
                    if (root != null) Object.DestroyImmediate(root);
                BossCensus.Reset();
            }
        }
    }
}
