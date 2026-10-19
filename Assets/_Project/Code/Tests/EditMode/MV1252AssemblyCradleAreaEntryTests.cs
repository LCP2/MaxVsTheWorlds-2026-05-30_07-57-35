using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1252 (one new EditMode test, MV-465 Rule 1) — no cradle ever built a robot in play: Tick()
    /// returns at once unless <c>_maxInArea</c> is true, and on base commit 2d4e353 nothing in shipped
    /// gameplay ever called <see cref="AssemblyCradle.SetMaxInArea"/> (only <c>Dev/CaptureDirector.cs</c>
    /// did). This test drives the real <see cref="AreaAccumulationDirector"/>/<see cref="WorldRunner"/>
    /// wiring path a live game uses — never <c>SetMaxInArea</c> — same idiom
    /// <c>MV1247CradleCensusGateTests</c> already established for a real Mothership build.
    ///
    /// Tier 2 (resolved values): asserts the spawner's own resolved live count, never an authored constant.
    /// </summary>
    public sealed class MV1252AssemblyCradleAreaEntryTests
    {
        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            FactoryCensus.Reset();
            AssemblyCradle.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            FactoryCensus.Reset();
            AssemblyCradle.ResetRegistry();
        }

        [Test]
        public void BuildsOnlyWhileMaxIsReportedInItsOwnArea_NeverCallingSetMaxInArea()
        {
            GameObject root = null, areaDirectorGo = null, runnerGo = null;
            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
                Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                root = new GameObject("MV-1252 Root");
                MapBuild built = MapRuntime.Build(map, root.transform);
                foreach (AssemblyCradle c in built.Cradles) c.Build(); // AddComponent's own Awake never runs outside Play mode

                areaDirectorGo = new GameObject("MV-1252 AreaDirector");
                var areaDirector = areaDirectorGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg, WorldCatalog.IndexOf(WorldIds.Mothership));

                runnerGo = new GameObject("MV-1252 WorldRunner");
                var runner = runnerGo.AddComponent<WorldRunner>();
                // The real wiring: stamps every cradle's AreaIndex from the real Mothership config, the
                // same path WorldRunner.Boot uses in shipped gameplay (never hand-set by this test).
                runner.Configure(cfg, map, built, areaDirector);

                AssemblyCradle cradle = built.Cradles.FirstOrDefault(c => c.AreaIndex == 1);
                Assert.IsNotNull(cradle, "area 1 ('a1') must build at least one cradle");
                EnemySpawner spawner = cradle.GetComponent<EnemySpawner>();

                // Max reported in area 2, never area 1: the cradle must never build. Driven through the
                // real OnAreaEntered signal a subscribed AreaAccumulationDirector fires — not SetMaxInArea.
                cradle.OnAreaEntered(2);
                cradle.Tick(4.05f);
                Assert.AreEqual(0, spawner.LiveCount,
                    "a cradle must never build while Max is reported in a different area");

                // Max reported in area 1 — the exact "already there when Start runs" case
                // PrimeFromCurrentPhysicalArea exists for (areaDirector.PhysicalArea defaults to 1, and
                // PlayerCrossedIntoArea never fires for an area Max already occupies).
                cradle.PrimeFromCurrentPhysicalArea(areaDirector);
                cradle.Tick(4.05f);
                Assert.AreEqual(1, spawner.LiveCount,
                    "a cradle must build exactly one robot after one 4.0s cycle while Max is reported in its own area");
            }
            finally
            {
                if (runnerGo != null) Object.DestroyImmediate(runnerGo);
                if (areaDirectorGo != null) Object.DestroyImmediate(areaDirectorGo);
                if (root != null) Object.DestroyImmediate(root);
            }
        }
    }
}
