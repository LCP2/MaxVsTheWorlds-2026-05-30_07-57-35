using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1065 — Lee (World 2): every death was respawning Max into the area AFTER the one he died in,
    /// not the one before. Root cause: <c>AreaAccumulationDirector.PredecessorOf</c> (MV-1002) answered
    /// "what sat immediately before the LAST physical-history entry of the death area", with no check
    /// that the step it returned was an authored gate crossing at all. The ordinary play pattern — open
    /// the gate to area C, step in, retreat to B, die in B — leaves history <c>[A, B, C, B]</c>, and the
    /// old code blindly returned C (what sat before the LAST "B"), even though no authored gate runs
    /// C-&gt;B; only the retreat walked it. Separately, <c>WorldRunner.ResumeCheckpoint</c> never asked
    /// <c>PredecessorOf</c> at all — it called the 2-arg <c>RespawnPlanner.Resolve</c> overload, whose
    /// own <c>areaIndex - 1</c> fallback is wrong on World 2's descending gantry-deck leg (a15 -&gt; a12
    /// -&gt; a11 -&gt; a10), where the PREVIOUS area on the route sits at a HIGHER index than the current
    /// one, not a lower one.
    ///
    /// The fix: a predecessor must be a real, authored <c>WorldConfig.gates</c> inbound edge, not merely
    /// "whatever sat before the area in raw history" — <c>PredecessorOf</c> now scans history backward
    /// (most recent first, same order as before) but skips any hit whose immediate predecessor isn't one
    /// of the area's own authored "from" areas, and <c>ResumeCheckpoint</c> now asks the same deck-aware
    /// resolver <c>OnPlayerDied</c> already uses instead of falling back to raw index arithmetic.
    ///
    /// Must fail on b9fce15 (current main at pickup): the first assertion below reads <c>CurrentArea</c>
    /// as 3 (Z), not 1 (X) after the step-ahead-and-retreat death; the second reads <c>CurrentArea</c> as
    /// 10 (the raw <c>areaIndex - 1</c>), not 12 (a12 — the real route predecessor of a11 on the deck leg).
    /// </summary>
    public sealed class MV1065RouteOrderRespawnPredecessorTests
    {
        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            DeathRunState.Reset();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            var overlay = Object.FindFirstObjectByType<DeathOverlay>();
            if (overlay != null) Object.DestroyImmediate(overlay.gameObject);

            RobotEnemy.ResetRegistry();
            DeathRunState.Reset();
            DevTuning.Reset();
        }

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static void InvokeOnPlayerDied(WorldRunner runner) =>
            typeof(WorldRunner).GetMethod("OnPlayerDied", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(runner, null);

        private static Vector3 ProbePositionFor(MapData map, MapZone zone)
        {
            Assert.IsNotNull(zone, "setup failure: ProbePositionFor given a null zone");
            if (zone.level == 0) return new Vector3(zone.x, 0.5f, zone.z);

            foreach (MapEntity e in map.entities)
            {
                if (e == null) continue;
                if (e.Kind != EntityKind.Deck && e.Kind != EntityKind.Hatch) continue;
                if (!zone.Contains(e.x, e.z)) continue;
                return new Vector3(e.x, map.deckHeight, e.z);
            }

            Assert.Fail($"setup failure: no Deck/Hatch entity found inside level>0 zone '{zone.id}'");
            return default;
        }

        [Test]
        public void RespawnPredecessor_IsTheRouteGraphAnswer_NotRawCrossingOrIndexArithmetic()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see MV890AreaGateDressingPairingTests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);
            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();

            // --- data row 1: step ahead and retreat (A -> B -> C -> B), die in B. The authored inbound
            // --- gate into area2 is g1 (from area1) ONLY — area3 -> area2 is a retreat, never an
            // --- authored gate — so the predecessor must resolve to area1, not area3.
            GameObject host1 = new GameObject("MV1065 Host 1");
            GameObject areaGo1 = null, playerGo1 = null, runnerGo1 = null;
            try
            {
                MapBuild built1 = MapRuntime.Build(map, host1.transform);

                areaGo1 = new GameObject("Area Accumulation 1");
                var areaDirector1 = areaGo1.AddComponent<AreaAccumulationDirector>();
                areaDirector1.ConfigureWorld(cfg);
                areaDirector1.Configure(map, built1.Cover);

                runnerGo1 = new GameObject("WorldRunner Test Root 1");
                var runner1 = runnerGo1.AddComponent<WorldRunner>();
                runner1.Configure(cfg, map, built1, areaDirector1);

                playerGo1 = new GameObject("Player 1") { tag = "Player" };

                foreach (int toIndex in new[] { 2, 3 })
                {
                    playerGo1.transform.position = ProbePositionFor(map, map.Zone($"area{toIndex}"));
                    InvokePrivate(areaDirector1, "Update");
                    areaDirector1.EnterArea(toIndex);
                }

                // Retreat from area3 back into area2 — a real, linked physical crossing (the doorway is
                // open both ways), but NOT an authored inbound gate for area2.
                playerGo1.transform.position = ProbePositionFor(map, map.Zone("area2"));
                InvokePrivate(areaDirector1, "Update");
                Assert.That(areaDirector1.PhysicalArea, Is.EqualTo(2), "precondition: Max is physically back in area2");

                InvokeOnPlayerDied(runner1);
                Assert.IsTrue(runner1.HasPendingRespawn, "precondition: a death must leave a respawn pending");
                runner1.Continue();

                Assert.That(areaDirector1.CurrentArea, Is.EqualTo(1),
                    "MV-1065: dying in area2 after stepping ahead into area3 and retreating must respawn at " +
                    "area1 (area2's own authored inbound gate, g1) — NOT area3 (what merely sat before the " +
                    "last physical-history entry of area2), which Max never actually came from this time");
            }
            finally
            {
                if (runnerGo1 != null) Object.DestroyImmediate(runnerGo1);
                if (playerGo1 != null) Object.DestroyImmediate(playerGo1);
                if (areaGo1 != null) Object.DestroyImmediate(areaGo1);
                Object.DestroyImmediate(host1);
            }

            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            RobotEnemy.ResetRegistry();
            DeathRunState.Reset();

            // --- data row 2: RESUME at area11, captured on the descending deck leg (a15 -> a12 -> a11),
            // --- after the ordinary forward floor climb already passed through area11 once (from area10).
            // --- area11 now carries TWO authored inbound gates (g28 from area10, g34 from area12 [DECK]);
            // --- the most recent real crossing is the deck one, from area12, so the resume must land
            // --- there — NOT area10, the raw areaIndex-1 the old 2-arg RespawnPlanner.Resolve fell back to.
            GameObject host2 = new GameObject("MV1065 Host 2");
            GameObject areaGo2 = null, playerGo2 = null, runnerGo2 = null;
            try
            {
                MapBuild built2 = MapRuntime.Build(map, host2.transform);

                areaGo2 = new GameObject("Area Accumulation 2");
                var areaDirector2 = areaGo2.AddComponent<AreaAccumulationDirector>();
                areaDirector2.ConfigureWorld(cfg);
                areaDirector2.Configure(map, built2.Cover);

                runnerGo2 = new GameObject("WorldRunner Test Root 2");
                var runner2 = runnerGo2.AddComponent<WorldRunner>();
                runner2.Configure(cfg, map, built2, areaDirector2);

                playerGo2 = new GameObject("Player 2") { tag = "Player" };

                // Forward floor climb 2..15 (Configure() already seeds area1), then the deck descent onto
                // area12 and area11 — the real shipped World 2 gate graph (g1..g32, then g33, g34).
                foreach (int toIndex in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 12, 11 })
                {
                    playerGo2.transform.position = ProbePositionFor(map, map.Zone($"area{toIndex}"));
                    InvokePrivate(areaDirector2, "Update");
                    areaDirector2.EnterArea(toIndex);
                }
                Assert.That(areaDirector2.PhysicalArea, Is.EqualTo(11),
                    "precondition: Max is physically standing in area11, reached via the deck descent");

                runner2.ResumeCheckpoint(11);

                Assert.That(areaDirector2.CurrentArea, Is.EqualTo(12),
                    "MV-1065: resuming a checkpoint at area11 captured on the deck leg must land Max at " +
                    "area12 (the deck gate he most recently actually crossed, g34) — NOT area10 (the raw " +
                    "areaIndex-1 the old 2-arg RespawnPlanner.Resolve fell back to, which is the NEXT area " +
                    "on this descending leg, not the previous one)");
            }
            finally
            {
                if (runnerGo2 != null) Object.DestroyImmediate(runnerGo2);
                if (playerGo2 != null) Object.DestroyImmediate(playerGo2);
                if (areaGo2 != null) Object.DestroyImmediate(areaGo2);
                Object.DestroyImmediate(host2);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }
    }
}
