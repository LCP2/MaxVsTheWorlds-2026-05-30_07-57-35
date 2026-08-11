using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-311: <see cref="AreaAccumulationDirector.Configure"/> fills area 1 SYNCHRONOUSLY, so
    /// <see cref="AreaAccumulationDirector.ConfigureWorld"/> must already have run or area 1 permanently
    /// falls back to the legacy <see cref="AreaPopulation"/> formula instead of world1_config's
    /// budget-solved composition — exactly what shipped, undetected, because
    /// <c>World1RuntimeTests</c> only ever exercised <see cref="WorldConfig.SolveComposition"/> directly,
    /// never the actual Configure()/ConfigureWorld() call order <c>BackyardPath.Awake()</c> uses. This
    /// drives the director the same way BackyardPath now does (world config wired before the first
    /// fill) against the real shipped world1_config.json, so a regression of that call order fails here.
    ///
    /// EditMode, not PlayMode (contract: PlayMode tests are never authored in-session). That rules out
    /// asserting on <see cref="RobotEnemy.Active"/> — it is only populated by <c>OnEnable</c>, which
    /// Unity does not invoke for plain MonoBehaviours outside Play mode (see
    /// <c>EnemyFriendlyFireTests.NewEnemy</c>'s "EditMode has no Awake/OnEnable lifecycle" note), so a
    /// spawned robot never registers there in this environment even though it really was spawned. This
    /// instead asserts on <see cref="AreaAccumulationDirector.ActiveCount"/>/<c>QueuedCount</c>, which
    /// the queue updates itself in <c>TryRelease</c> — independent of any GameObject callback — and
    /// compares the totals against <see cref="WorldConfig.SolveComposition"/>'s own count: the legacy
    /// fallback's area-1 swarm (8 robots from <c>RobotCompositionTuning</c>'s defaults) is unmistakably
    /// different from the solved budget (3-4), so a regression back to call-order-broken composition
    /// fails this loudly rather than by a coincidental count match.
    /// </summary>
    public sealed class AreaAccumulationWorldConfigTests
    {
        private GameObject _directorGo;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);

            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        [Test]
        public void Area1AndArea2FillFromWorldConfig_NotTheLegacySwarmFallback()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            DifficultyEngine.Composition area1 = cfg.SolveComposition(1);
            Assert.AreEqual(1, area1.Bruiser, "area 1 must hold exactly one tank (large robot)");
            Assert.That(area1.Rusher, Is.InRange(2, 3), "area 1 must hold 2-3 Rusher");
            Assert.AreEqual(0, area1.Heavy, "Heavy is not unlocked this early");
            Assert.AreEqual(0, area1.Brute, "Brute is not unlocked this early");

            DifficultyEngine.Composition area2 = cfg.SolveComposition(2);
            Assert.Greater(area2.Gunner, 0, "Gunner must be present by area 2, per world1_config's gunnerFromArea");

            _directorGo = new GameObject("Area Accumulation");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();

            // Same order BackyardPath.Awake() now uses (MV-311 fix) — world config wired BEFORE
            // Configure(), which fills area 1 the instant it runs.
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>());

            int filledArea1 = director.ActiveCount + director.QueuedCount;
            Assert.AreEqual(area1.TotalCount, filledArea1,
                "area 1's queued+active population must match world1_config's solved budget — a " +
                "mismatch (e.g. the legacy fallback's larger swarm) means area 1 fell back to the " +
                "legacy AreaPopulation formula instead of the world config wired in before Configure()");

            director.EnterArea(2);

            int filledArea1And2 = director.ActiveCount + director.QueuedCount;
            Assert.AreEqual(area1.TotalCount + area2.TotalCount, filledArea1And2,
                "area 2's population must also match world1_config's solved budget (including its " +
                "Gunner share) once entered — proves the world-config path, not the legacy fallback " +
                "(which never produces Gunner), composed area 2 too");
        }
    }
}
