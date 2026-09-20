using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-833 — walking up a3's ramp used to count as entering a19: <see cref="MapData.ZoneAt(float, float, float)"/>
    /// returned the deck zone the instant a position cleared deck height ANYWHERE inside the shared
    /// a3/a19 footprint, including partway up the ramp that used to be <c>a3_ramp1</c>, well before Max
    /// ever reached the (then unlocked) hatch at the top. That opened the hatch instantly and jumped
    /// <see cref="AreaAccumulationDirector.CurrentArea"/> straight from 3 to 19, silently skipping areas
    /// 4-18's population.
    ///
    /// Retargeted by MV-852 (World 2 re-layout), which removed every ramp/hatch from every floor+deck
    /// overlay pair in World 2 (a3/a19, a6/a15 in that ticket's own numbering) — the only way up is now
    /// the new Replicator door into a12 (that numbering) and the deck-gate chain above it. The original
    /// AC1 ("a ramp-top probe still resolves to the floor area") has no scenario left to test against
    /// real content: no overlay-pair floor area authors a ramp of its own anywhere in the shipped game
    /// any more. AC2-4 still have real, unrelated content to prove against and are kept, ported from
    /// a3/a19/g19 onto a6/a15/g32 (again, MV-852's own numbering):
    ///
    /// Retargeted again by MV-865 (World 2 re-author, renumbered areas in play order): every "aN" area
    /// literal below is the NEW id for the same physical area — old a6 (this test's overlay target) is
    /// now a13, and old a12 (the deck's entry point) is now a14. a3/a15/g32 are unchanged.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) loading the real, shipped World 2 config
    /// through <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>/<see cref="WorldRunner"/>/
    /// <see cref="AreaAccumulationDirector"/> — no hand-set zone/gate state anywhere in this file —
    /// asserting three RESOLVED values (Rule 2, Tier 2): (1) standing on a15_deck1 while CurrentArea is
    /// 13 does not advance it, since a13 and a15 share a footprint but no MapLink; (2) from CurrentArea
    /// 14, stepping through gate g32 onto that same a15_deck1 position DOES advance CurrentArea to 15;
    /// (3) MapValidation rejects a 'never' condition on a wall gate but accepts the shipped config.
    /// </summary>
    public sealed class MV833RampZoneTests
    {
        [Test]
        public void UnlinkedOverlayNeverAdvancesArea_AndARealDeckGateDoes()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see other World2 EditMode tests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var root = new GameObject("MV833 Root");
            GameObject areaGo = null;
            GameObject playerGo = null;
            try
            {
                MapBuild build = MapRuntime.Build(map, root.transform);

                areaGo = new GameObject("Area Accumulation");
                var director = areaGo.AddComponent<AreaAccumulationDirector>();
                director.ConfigureWorld(cfg);
                director.Configure(map, build.Cover);

                var runner = root.AddComponent<WorldRunner>();
                runner.Configure(cfg, map, build, director);

                playerGo = new GameObject("Player") { tag = "Player" };

                // === AC1: standing on the middle of a15_deck1 while CurrentArea is 13 must not advance
                // it — a13 and a15 share a footprint but no MapLink ===
                director.EnterArea(13);
                WorldArea a15 = cfg.Area("a15");
                playerGo.transform.position = new Vector3(a15.XMin + 15f, 2.5f, a15.ZMin + 24.5f);
                InvokeDirectorUpdate(director);

                MapZone atDeck = map.ZoneAt(playerGo.transform.position.x, playerGo.transform.position.y, playerGo.transform.position.z);
                Assert.AreEqual("area15", atDeck?.id, "setup failure: this probe must actually sit on a15's own deck rect");
                Assert.AreEqual(13, director.CurrentArea, "MV-833 AC1: CurrentArea must not skip from 13 straight to 15 — no MapLink joins them");

                // === AC2: from CurrentArea 14, stepping through gate g32 onto a15_deck1 DOES advance
                // CurrentArea to 15 — a14 and a15 ARE joined by g32's own MapLink ===
                director.EnterArea(14);
                InvokeDirectorUpdate(director); // re-assert the same a15_deck1 position now that we're coming from 14

                Assert.AreEqual(15, director.CurrentArea, "MV-833 AC2: CurrentArea must advance through the real g32 link");
            }
            finally
            {
                // FillArea (via EnterArea above) places real robots into a lazily-created, top-level
                // "Area Robots" container (AreaAccumulationDirector's own convention — see e.g.
                // MV828ReplicatorAreaIndexTests' identical teardown) — not a child of root, so it needs
                // its own cleanup, same as playerGo/areaGo below. RobotEnemy.ResetRegistry() drops the
                // now-dangling entries from the static Active list a later test's own FindObjectsByType
                // scan or tag lookup would otherwise trip over.
                GameObject bodies = GameObject.Find("Area Robots");
                if (bodies != null) Object.DestroyImmediate(bodies);
                RobotEnemy.ResetRegistry();

                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (areaGo != null) Object.DestroyImmediate(areaGo);
                Object.DestroyImmediate(root);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }

            // === AC3: MapValidation rejects 'never' on a wall gate, and passes the shipped config ===
            Assert.IsTrue(MapValidation.ValidateWorldConfig(cfg, out string shippedReason),
                $"MV-833 AC3: the shipped World 2 config must pass validation: {shippedReason}");

            WorldConfig broken = WorldLibrary.Load(WorldLibrary.World2);
            WorldGate wallGate = broken.gates.First(g => g.id == "g1");
            wallGate.opensWith = "never";

            Assert.IsFalse(MapValidation.ValidateWorldConfig(broken, out string brokenReason),
                "MV-833 AC3: a wall gate authored 'never' must fail validation");
            StringAssert.Contains("g1", brokenReason);
        }

        private static void InvokeDirectorUpdate(AreaAccumulationDirector director) =>
            typeof(AreaAccumulationDirector).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(director, null);
    }
}
