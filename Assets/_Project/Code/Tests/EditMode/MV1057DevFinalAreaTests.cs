using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1057 (the one new test, per CC_AUTONOMY's testing policy): the Home screen's DEV "FINAL AREA"
    /// button needs a single engine entry point (<see cref="WorldRunner.JumpToFinaleArea"/>) that lands
    /// Max at a world's own finale (its final, boss-role area) with every prerequisite on the route
    /// already satisfied, so Lee can test a world-to-world transition without replaying the whole world
    /// first. Does not exist before this ticket — fails to COMPILE on the commit this ticket picked up
    /// on, same "new API is its own base-commit failure" bar MV-524/MV-959's own tests used.
    ///
    /// Drives the real entry point against each world's own shipped config, through the real build
    /// pipeline (<c>WorldMapLoader.TryLoad</c> -&gt; <c>MapRuntime.Build</c>), same idiom as
    /// <see cref="Mv909ResumeAreaGateLatchTests"/>/<see cref="MV959World2FinaleChainTests"/>.
    ///
    /// Tier 2 (resolved values): every assertion reads a live <see cref="Replicator.IsAlive"/>, a live
    /// <see cref="AreaGate.Locked"/>, or a resolved world-space position — never an authored constant.
    /// </summary>
    public sealed class MV1057DevFinalAreaTests
    {
        [SetUp]
        public void SetUp()
        {
            // Same BuildBody collider-strip [Error] noise every full-world EditMode build carries
            // (see Mv909ResumeAreaGateLatchTests' own note).
            LogAssert.ignoreFailingMessages = true;
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            FactoryCensus.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            RobotEnemy.ResetRegistry();
            FactoryCensus.Reset();
            BossCensus.Reset();
            Pickup.ResetRegistry();
            DevTuning.Reset();
            Time.timeScale = 1f;
            ModalFrameRateGate.ResetForTests();
        }

        private static AreaGate GateInto(WorldRunner runner, int areaIndex)
        {
            var field = typeof(WorldRunner).GetField("_gateIntoArea", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = (Dictionary<int, AreaGate>)field.GetValue(runner);
            return dict.TryGetValue(areaIndex, out AreaGate gate) ? gate : null;
        }

        [TestCase(0, 30, "a30", null)]
        [TestCase(1, 31, "a31", "g47")]
        [TestCase(2, 30, "a30", null)]
        public void JumpToFinaleArea_LandsAtTheEntry_ClearsEarlierReplicators_AndOpensTheFinaleGate(
            int worldIndex, int expectedFinaleIndex, string expectedFinaleId, string conditionGateId)
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.KeyForIndex(worldIndex));
            Assert.IsNotNull(cfg, $"World {worldIndex + 1}'s own shipped config must load");
            Assert.AreEqual(expectedFinaleIndex, cfg.dials.areaCount,
                $"World {worldIndex + 1}'s authored final area must still be a{expectedFinaleIndex}");

            WorldArea finale = cfg.AreaByIndex(expectedFinaleIndex);
            Assert.IsNotNull(finale, "setup failure: the finale area must resolve");
            Assert.AreEqual(expectedFinaleId, finale.id, "setup failure: wrong finale id for this world");
            Assert.IsTrue(finale.IsBossRole, "setup failure: the finale area must be boss-role");

            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV-1057 Probe Root");
            GameObject playerGo = null, areaGo = null, runnerGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);

                areaGo = new GameObject("Area Accumulation");
                var areaDirector = areaGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg, worldIndex);
                areaDirector.Configure(map, built.Cover);

                runnerGo = new GameObject("WorldRunner Test Root");
                var runner = runnerGo.AddComponent<WorldRunner>();
                runner.Configure(cfg, map, built, areaDirector);

                MapZone area1 = map.Zone("area1");
                Assert.IsNotNull(area1, "setup failure: every world must have an area1 zone to cold-boot the player in");
                playerGo = new GameObject("Player") { tag = "Player" };
                playerGo.transform.position = area1.Center;

                bool jumped = runner.JumpToFinaleArea();
                Assert.IsTrue(jumped, "MV-1057: JumpToFinaleArea must succeed for a real, authored world");

                // ---- every Replicator strictly before the finale must be destroyed ----
                foreach (Replicator r in FactoryCensus.RegisteredReplicators)
                {
                    if (r.AreaIndex >= expectedFinaleIndex) continue;
                    Assert.IsFalse(r.IsAlive,
                        $"MV-1057: Replicator in area {r.AreaIndex} (outside the finale, a{expectedFinaleIndex}) " +
                        "must be destroyed before landing");
                }

                // ---- the finale's own incoming gate must have resolved open ----
                AreaGate finaleGate = GateInto(runner, expectedFinaleIndex);
                Assert.IsNotNull(finaleGate, "setup failure: the finale area must carry an incoming gate");

                if (conditionGateId != null)
                {
                    Assert.IsFalse(finaleGate.Locked,
                        $"MV-1057: {conditionGateId} (the finale's own condition-gated entry) must resolve open " +
                        "once every earlier Replicator is destroyed");
                }

                // ---- Max must land at the finale's own entry (standing at its gate), not somewhere else ----
                Vector2 playerXZ = new Vector2(playerGo.transform.position.x, playerGo.transform.position.z);
                Vector2 gateXZ = new Vector2(finaleGate.transform.position.x, finaleGate.transform.position.z);
                Assert.That(Vector2.Distance(playerXZ, gateXZ), Is.LessThanOrEqualTo(4f),
                    $"MV-1057: Max must land at the finale's own entry (near its incoming gate), not {playerXZ} " +
                    $"(gate at {gateXZ})");
            }
            finally
            {
                if (runnerGo != null) Object.DestroyImmediate(runnerGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (areaGo != null) Object.DestroyImmediate(areaGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }
    }
}
