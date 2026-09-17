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
    /// a3/a19 footprint, including partway up <c>a3_ramp1</c>, well before Max ever reached the (then
    /// unlocked) hatch at the top. That opened <c>a3_hatch1</c> instantly and jumped
    /// <see cref="AreaAccumulationDirector.CurrentArea"/> straight from 3 to 19, silently skipping areas
    /// 4-18's population.
    ///
    /// Fails on base commit 52b3cf2 (before this ticket): a probe at the top of <c>a3_ramp1</c> (still
    /// on the ramp, not through the hatch) resolves to zone "area19" instead of "area3", and the same
    /// probe height over the bare a3/a19 footprint away from any deck rect still reads as "on the deck".
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) loading the real, shipped World 2 config
    /// through <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/>/<see cref="WorldRunner"/>/
    /// <see cref="AreaAccumulationDirector"/> — no hand-set zone/gate state anywhere in this file —
    /// asserting four RESOLVED values (Rule 2, Tier 2), matching the ticket's own AC1-4: (1) a ramp-top
    /// probe resolves to a3, leaves CurrentArea at 3, and a3_hatch1 stays Locked; (2) standing on
    /// a19_deck1 while CurrentArea is 3 does not advance it, since a3 and a19 share no MapLink; (3) from
    /// CurrentArea 17, stepping through gate g19 onto a19_deck1 DOES advance CurrentArea to 19; (4)
    /// MapValidation rejects a 'never' condition on a wall gate but accepts the shipped config.
    /// </summary>
    public sealed class MV833RampZoneTests
    {
        [Test]
        public void RampNeverReadsAsTheDeck_AndTheAreaTrackerNeverSkipsAnUnlinkedArea()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see MV829HatchLockTests' own note).
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

                // === AC1: the top of a3_ramp1 (local x 7.5, z 7.5, y 2.4) resolves to a3, not a19 ===
                director.EnterArea(3);
                WorldArea a3 = cfg.Area("a3");
                playerGo.transform.position = new Vector3(a3.XMin + 7.5f, 2.4f, a3.ZMin + 7.5f);
                InvokeDirectorUpdate(director);

                MapZone atRampTop = map.ZoneAt(playerGo.transform.position.x, playerGo.transform.position.y, playerGo.transform.position.z);
                Assert.AreEqual("area3", atRampTop?.id, "MV-833 AC1: a probe at the top of the ramp must still resolve to the floor area");
                Assert.AreEqual(3, director.CurrentArea, "MV-833 AC1: CurrentArea must not have jumped off a ramp-top probe");

                AreaGate a3Hatch = build.Actors["a3_hatch1"].GetComponent<AreaGate>();
                Assert.IsTrue(a3Hatch.Locked, "MV-833 AC1: a3_hatch1 must stay Locked — it opens on 'never'");

                // === AC2: standing on the middle of a19_deck1 while CurrentArea is 3 must not advance it —
                // a3 and a19 share a footprint but no MapLink ===
                WorldArea a19 = cfg.Area("a19");
                playerGo.transform.position = new Vector3(a19.XMin + 9.5f, 2.5f, a19.ZMin + 15f);
                InvokeDirectorUpdate(director);

                MapZone atDeck = map.ZoneAt(playerGo.transform.position.x, playerGo.transform.position.y, playerGo.transform.position.z);
                Assert.AreEqual("area19", atDeck?.id, "setup failure: this probe must actually sit on a19's own deck rect");
                Assert.AreEqual(3, director.CurrentArea, "MV-833 AC2: CurrentArea must not skip from 3 straight to 19 — no MapLink joins them");

                // === AC3: from CurrentArea 17, stepping through gate g19 onto a19_deck1 DOES advance
                // CurrentArea to 19 — a17 and a19 ARE joined by g19's own MapLink ===
                director.EnterArea(17);
                InvokeDirectorUpdate(director); // re-assert the same a19_deck1 position now that we're coming from 17

                Assert.AreEqual(19, director.CurrentArea, "MV-833 AC3: CurrentArea must advance through the real g19 link");
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

            // === AC4: MapValidation rejects 'never' on a wall gate, and passes the shipped config ===
            Assert.IsTrue(MapValidation.ValidateWorldConfig(cfg, out string shippedReason),
                $"MV-833 AC4: the shipped World 2 config must pass validation: {shippedReason}");

            WorldConfig broken = WorldLibrary.Load(WorldLibrary.World2);
            WorldGate wallGate = broken.gates.First(g => g.id == "g1");
            wallGate.opensWith = "never";

            Assert.IsFalse(MapValidation.ValidateWorldConfig(broken, out string brokenReason),
                "MV-833 AC4: a wall gate authored 'never' must fail validation");
            StringAssert.Contains("g1", brokenReason);
        }

        private static void InvokeDirectorUpdate(AreaAccumulationDirector director) =>
            typeof(AreaAccumulationDirector).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(director, null);
    }
}
