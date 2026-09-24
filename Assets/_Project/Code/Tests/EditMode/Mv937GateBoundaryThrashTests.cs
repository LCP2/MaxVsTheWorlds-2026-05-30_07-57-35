using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-937 — <see cref="AreaAccumulationDirector.Update"/>'s own physical-crossing check advanced
    /// <see cref="AreaAccumulationDirector.PhysicalArea"/> (and fired <see cref="AreaAccumulationDirector.PlayerCrossedIntoArea"/>,
    /// which <see cref="MapStaticBatchRoot"/> answers by re-running <see cref="MapStaticBatchRoot.ApplyAreaGate"/>)
    /// on ANY raw <see cref="MapData.ZoneAt(float,float,float)"/> change, with no tolerance for a
    /// position still hovering right on a shared boundary. MV-920 widened that guard from "only a higher
    /// area number" to "any change, raised or lowered" — so standing exactly on the shared boundary
    /// between two gate-linked, same-corridor zones (a gantry doorway — World 2's a12/a11 deck, this
    /// ticket's own measured case) flips the raw answer on every sub-decimetre position jitter astride
    /// it, and before this fix every single flip re-ran the full gate (~30k renderers) plus a census
    /// re-record.
    ///
    /// This is the ticket's own required "count ApplyAreaGate calls, log resolved zone vs the active
    /// set" measurement, taken directly against the real World 2 map before any fix was written: walking
    /// the length of a12's own deck (40 steps) triggered ZERO extra <c>ApplyAreaGate</c> calls — the
    /// self-heal already inside <c>MapStaticBatchRoot.Update()</c> guards that case correctly. Jittering
    /// +/-0.1m astride the real, measured a12/a11 shared edge instead triggered 29 calls across 30 steps
    /// — the collapse is specifically a linked-zone BOUNDARY crossing, not merely standing or walking
    /// inside one zone, which is why an in-zone-only reproduction would not have failed here.
    ///
    /// Fails on 84ab045 (current main at pickup): the assertion below reads a nonzero rise in
    /// <see cref="MapStaticBatchRoot.ApplyAreaGateCallCount"/> across the 10 post-establishment jitter
    /// frames (measured: +9) instead of the required zero.
    /// </summary>
    public sealed class Mv937GateBoundaryThrashTests
    {
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
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        [Test]
        public void JitterAcrossALinkedZoneBoundary_TriggersNoFurtherGateReapplies()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see Mv925UnlinkedJumpGateFollowTests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV937 Host");
            GameObject areaGo = null, playerGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);
                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                areaGo = new GameObject("Area Accumulation");
                var areaDirector = areaGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg);
                areaDirector.Configure(map, built.Cover);

                // MapStaticBatchRoot.Start() — never invoked automatically outside Play mode (same
                // reflection trick every other real-World2-map test in this suite uses).
                InvokePrivate(batchRoot, "Start");
                Assert.That(areaDirector.PhysicalArea, Is.EqualTo(1), "precondition: a fresh Configure() cold-boots at area1");

                playerGo = new GameObject("Player") { tag = "Player" };

                // Walk the real gate graph from the entry stub to area12 exactly as Mv920AreaTrackingGantryTests
                // already proves is linked (1-14 straight, then the reported gantry's own first two legs,
                // 14-15-12) — a legitimate arrival, not a synthetic teleport.
                int[] walk = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 12 };
                foreach (int a in walk)
                {
                    playerGo.transform.position = ProbePositionFor(map, map.Zone($"area{a}"));
                    InvokePrivate(areaDirector, "Update");
                    InvokePrivate(batchRoot, "Update");
                }
                Assert.That(areaDirector.PhysicalArea, Is.EqualTo(12), "setup failure: the real gate graph walk must land on area12");

                MapZone area12 = map.Zone("area12");
                MapZone area11 = map.Zone("area11");
                Assert.IsNotNull(area11, "setup failure: World 2 must have an area11 zone");
                Assert.IsTrue(map.AreLinked("area12", "area11"),
                    "setup failure: area12 and area11 must be gate-linked for this to be the reported gantry boundary");

                // The real shared edge between the two zones' authored rects, and a Z inside both zones'
                // own bounds — the exact "standing in the gantry doorway" position a walking player can
                // hover around by a few centimetres.
                float boundaryX = area12.XMin;
                Assert.That(boundaryX, Is.EqualTo(area11.XMax).Within(0.5f),
                    "setup failure: area12's west edge must be area11's east edge (the shared gantry boundary) for this reproduction to be real");
                float boundaryZ = Mathf.Clamp((Mathf.Max(area12.ZMin, area11.ZMin) + Mathf.Min(area12.ZMax, area11.ZMax)) * 0.5f,
                    Mathf.Max(area12.ZMin, area11.ZMin), Mathf.Min(area12.ZMax, area11.ZMax));

                // Prime BOTH sides of the boundary individually before measuring — the gate graph walk
                // above already covered area11 once (the straight monotonic climb, long before the
                // reported gantry loops back through it), but every zone change since has replaced the
                // active set wholesale, so area11's own coverage has long since been evicted from it by
                // the time the walk lands back on area12. A player who has genuinely already stood in
                // both rooms is the real precondition this ticket's fix targets — first-time coverage of
                // a room neither side has covered yet is a legitimate, once-only gate apply, not the bug.
                playerGo.transform.position = ProbePositionFor(map, area11);
                InvokePrivate(areaDirector, "Update");
                InvokePrivate(batchRoot, "Update");
                playerGo.transform.position = ProbePositionFor(map, area12);
                InvokePrivate(areaDirector, "Update");
                InvokePrivate(batchRoot, "Update");
                Assert.That(areaDirector.PhysicalArea, Is.EqualTo(12), "setup failure: priming must end back on area12");

                int before = batchRoot.ApplyAreaGateCallCount;

                const int frames = 10;
                var dbg = new System.Text.StringBuilder();
                for (int i = 0; i < frames; i++)
                {
                    float jitter = (i % 2 == 0) ? 0.1f : -0.1f;
                    float px = boundaryX + jitter;
                    playerGo.transform.position = new Vector3(px, map.deckHeight, boundaryZ);
                    int cb = batchRoot.ApplyAreaGateCallCount;
                    MapZone resolved = map.ZoneAt(px, map.deckHeight, boundaryZ);
                    InvokePrivate(areaDirector, "Update");
                    InvokePrivate(batchRoot, "Update");
                    int ca = batchRoot.ApplyAreaGateCallCount;
                    dbg.AppendLine($"i={i} px={px:F2} resolved={resolved?.id ?? "null"} +{ca - cb} physicalArea={areaDirector.PhysicalArea}");
                }

                int extraCalls = batchRoot.ApplyAreaGateCallCount - before;
                Assert.That(extraCalls, Is.EqualTo(0),
                    $"MV-937: {frames} frames of +/-0.1m position jitter astride the already-active a12/a11 " +
                    $"gate-linked boundary must trigger zero further ApplyAreaGate calls (both zones are " +
                    $"already covered by the same active set) — measured {extraCalls} extra call(s), the " +
                    $"walking-only full gate re-apply (and census re-record) this ticket exists to fix\n" +
                    $"boundaryX={boundaryX} area11.XMax={area11.XMax} area12.XMin={area12.XMin} boundaryZ={boundaryZ}\n{dbg}");
            }
            finally
            {
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (areaGo != null) Object.DestroyImmediate(areaGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        /// <summary>A world position that resolves (via <see cref="MapData.ZoneAt(float,float,float)"/>)
        /// to <paramref name="zone"/> — copied from Mv920AreaTrackingGantryTests' own identical helper.</summary>
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

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);
    }
}
