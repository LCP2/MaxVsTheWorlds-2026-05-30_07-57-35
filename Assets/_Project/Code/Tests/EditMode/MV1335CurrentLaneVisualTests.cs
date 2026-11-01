using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1335 AC1 and AC2, both in one test (MV-465 Rule 1): "loading World 4 builds exactly one lane
    /// visual per current entry (51 today), each matching its rect within 0.1 m and its dir" (AC1), and
    /// "a reversing lane's visual direction always equals its current drift direction, checked at t=0,
    /// just before and just after the first flip" (AC2).
    ///
    /// Fails to compile on the commit before this ticket (5adde697): <c>MapCurrentLaneVisual</c> does
    /// not exist there — MV-1300 shipped the drift and MV-1301/MV-1307 placed the lanes, but nothing
    /// ever built a visual for a <c>"current"</c> map entity.
    ///
    /// Loads the REAL shipped Reef config through the production path (<see cref="WorldLibrary.Load"/>
    /// -&gt; <see cref="WorldMapLoader.TryLoad"/> -&gt; <see cref="MapRuntime.Build"/>) — the same 51
    /// authored lanes <c>MV1301ReefCurrentLanesTests</c> already counts per area — then drives the real
    /// shared reversal clock (<see cref="MapCurrentReversalRunner.TickCurrentReversal"/>, same "a caller
    /// decides the cadence" idiom <c>MV1307CurrentReversalTests</c> already established) to the three
    /// ages AC2 names, reading <see cref="MapCurrentLaneVisual.ResolvedFlowDirection"/> directly — a
    /// RESOLVED value (MV-465 Tier 2), never an authored constant and never a rendered pixel.
    /// </summary>
    public sealed class MV1335CurrentLaneVisualTests
    {
        [Test]
        public void World3Map_BuildsOneLaneVisualPerCurrentEntityMatchingItsRectAndDir_AndA16Cur1FlipsOnSchedule()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV1335 World3 Probe Root");
            GameObject runnerGo = null;
            try
            {
                MapRuntime.Build(map, host.transform);

                // --- AC1: exactly one lane visual per authored current entity, matching its rect/dir ---
                MapEntity[] currents = map.CurrentEntities();
                Assert.AreEqual(51, currents.Length,
                    "World 4's (world3_config.json) own authored current-lane count changed -- update " +
                    "this expectation deliberately if that was intended, not incidentally");

                MapCurrentLaneVisual[] visuals = host.GetComponentsInChildren<MapCurrentLaneVisual>(true);
                Assert.AreEqual(currents.Length, visuals.Length,
                    $"expected exactly one lane visual per authored current entity ({currents.Length}), " +
                    $"found {visuals.Length}");

                var byId = new Dictionary<string, MapCurrentLaneVisual>();
                foreach (MapCurrentLaneVisual v in visuals)
                    byId[v.EntityId] = v;

                MapCurrentLaneVisual a16Cur1 = null;
                foreach (MapEntity e in currents)
                {
                    Assert.IsTrue(byId.TryGetValue(e.id, out MapCurrentLaneVisual visual),
                        $"current entity '{e.id}' resolved no lane visual");

                    var expectedRect = new Rect(e.x - e.width * 0.5f, e.z - e.depth * 0.5f, e.width, e.depth);
                    Assert.Less(Mathf.Abs(visual.Footprint.x - expectedRect.x), 0.1f, $"{e.id}: rect.x");
                    Assert.Less(Mathf.Abs(visual.Footprint.y - expectedRect.y), 0.1f, $"{e.id}: rect.y (z)");
                    Assert.Less(Mathf.Abs(visual.Footprint.width - expectedRect.width), 0.1f, $"{e.id}: rect.width");
                    Assert.Less(Mathf.Abs(visual.Footprint.height - expectedRect.height), 0.1f, $"{e.id}: rect.height (depth)");

                    Vector3 expectedDir = MapGeometry.CurrentDirectionVector(e.facing);
                    Assert.Less(Vector3.Distance(visual.AuthoredDir, expectedDir), 0.01f,
                        $"{e.id}: lane visual's authored dir {visual.AuthoredDir} does not match its facing " +
                        $"'{e.facing}' (expected {expectedDir})");

                    if (e.id == "a16_cur1") a16Cur1 = visual;
                }

                // --- AC2: a16_cur1 (dir "S", reverseEvery 10) flips on schedule ---
                Assert.IsNotNull(a16Cur1, "world3_config.json's own a16_cur1 (dir S, reverseEvery 10) must resolve a lane visual");
                Assert.AreEqual(10f, a16Cur1.ReverseEverySec, 0.01f, "a16_cur1: expected reverseEvery 10s");

                runnerGo = new GameObject("MV1335-current-reversal-runner");
                var runner = runnerGo.AddComponent<MapCurrentReversalRunner>();
                // Claims the shared clock at age 0 without advancing it -- TickCurrentReversal stamps
                // s_instance = this unconditionally before its own dt<=0 early-return (see that method's
                // own doc), which is what guarantees MapCurrentReversalRunner.Age reads THIS runner's own
                // _age (0), not whatever a previous test's now-destroyed runner happened to leave behind.
                runner.TickCurrentReversal(0f);

                // t = 0: still in the first (as-authored) interval [0, 10).
                Assert.AreEqual(a16Cur1.AuthoredDir, a16Cur1.ResolvedFlowDirection,
                    "a16_cur1 at t=0 must flow in its authored direction");

                // t = 9.9: just before the first flip at t=10.
                runner.TickCurrentReversal(9.9f);
                Assert.AreEqual(a16Cur1.AuthoredDir, a16Cur1.ResolvedFlowDirection,
                    "a16_cur1 at t=9.9s (just before its first flip at t=10s) must still flow in its authored direction");

                // t = 10.1: just after the first flip.
                runner.TickCurrentReversal(0.2f);
                Assert.AreEqual(-a16Cur1.AuthoredDir, a16Cur1.ResolvedFlowDirection,
                    "a16_cur1 at t=10.1s (just after its first flip at t=10s) must flow OPPOSITE its authored direction");
            }
            finally
            {
                if (runnerGo != null) Object.DestroyImmediate(runnerGo);
                Object.DestroyImmediate(host);
            }
        }
    }
}
