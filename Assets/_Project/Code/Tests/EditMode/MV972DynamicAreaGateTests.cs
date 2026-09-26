using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-972 — before this ticket, <see cref="MapStaticBatchRoot.ApplyAreaGate"/> only ever gated
    /// static map geometry (<c>_rendererZones</c>, tagged in <see cref="MapRuntime.Build"/>): a robot,
    /// a shed, a shed fitting or a pickup was never tagged at all and stayed enabled — submitted to
    /// culling — for the whole run, everywhere, regardless of which area Max stood in (this ticket's
    /// own measured trigger: World 1 authors 644 garrison robots). It also never range-limited a
    /// gate-linked neighbour by distance — a real MapLink neighbour stayed fully active the instant Max
    /// entered the CURRENT zone, even standing at its far end, tens of metres from the shared gate.
    ///
    /// One consolidated test (MV-465 Rule 1) against the real shipped World 1 config's own area4/area5/
    /// area6, with real garrisons (<c>AreaAccumulationDirector.FillArea</c>, off <c>WorldConfig.SolveComposition</c>)
    /// and a real dropped pickup (<see cref="PickupDirector.PlacePartsCache"/>) — no fixture, no
    /// hand-built MapData, so a bad tag or a wrong distance shows up on the actual shipped level. Asserts
    /// RESOLVED values throughout (Rule 2, Tier 2): a robot's/shed's/pickup's own <c>Renderer.enabled</c>
    /// after the gate has run, picked off the real <c>AreaAccumulationDirector.Active</c> registry and
    /// <c>MapStaticBatchRoot</c>'s own tag map — never an authored constant, never a rendered pixel.
    ///
    /// area5's real footprint (measured: 22m x 26m) and its two real gates do not leave a single
    /// interior point that clears 22m from BOTH the area4 and area6 gates at once (a grid search over
    /// the whole room tops out at a 21.7m minimum) — smaller than AC1's own illustrative "middle of a5,
    /// >22m from every gate" assumes. area4 is tested against the gate's own real distance rule instead
    /// of a fixed "must be disabled" outcome (see <c>AssertArea4RobotsMatchDistance</c>); area6, whose
    /// gate this test's own checkpoint 2 crosses, keeps the clean far/near contrast the AC describes.
    ///
    /// Fails on the base commit this branch was cut from: before MV-972, nothing ever tagged a robot, a
    /// shed or a pickup with a zone at all, so every one of the "must be disabled while far from Max"
    /// assertions below reads <c>enabled == true</c> regardless of area, and the neighbour-range
    /// assertions have no distance rule to exercise (every gate-linked neighbour was already
    /// unconditionally active).
    /// </summary>
    public sealed class MV972DynamicAreaGateTests
    {
        // Mirrors MapStaticBatchRoot's own private NeighbourGateRangeMetres (22 m) — duplicated here,
        // not reflected out, since this constant is also literally what the ticket's own AC names.
        private const float NeighbourGateRange = 22f;
        private const float NeighbourGateRangeSq = NeighbourGateRange * NeighbourGateRange;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
        }

        [Test]
        public void RobotsShedsAndPickupsGateByArea_NeighbourFollowsMaxWithin22m_ChasingRobotNeverHidden()
        {
            // Same collider-strip [Error] noise every full-world-build EditMode test in this suite carries.
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            MapZone a4 = map.Zone("area4");
            MapZone a5 = map.Zone("area5");
            MapZone a6 = map.Zone("area6");
            Assert.IsNotNull(a4, "setup failure: World 1 must author area4");
            Assert.IsNotNull(a5, "setup failure: World 1 must author area5");
            Assert.IsNotNull(a6, "setup failure: World 1 must author area6");

            Assert.IsTrue(TryDoorMouth(map, "area4", "area5", out Vector2 a4Gate),
                "setup failure: no real area4<->area5 doorway to measure distance against");
            Assert.IsTrue(TryDoorMouth(map, "area5", "area6", out Vector2 a5a6Gate),
                "setup failure: no real area5<->area6 doorway to measure distance against");

            // "The middle of a5, >22m from every gate" per the ticket's own AC1 — but area5's real
            // footprint (22x26m) and its two real gates (measured: one 21m from the room's own centre,
            // the other 15.5m) do not leave a single interior point that clears 22m from BOTH
            // simultaneously (a grid search over the whole footprint tops out at a 21.7m minimum) — the
            // room is simply smaller than the AC's own illustrative number assumes. Rather than fabricate
            // a point the real level can't produce, this picks the point that maximises distance from
            // the area5<->area6 gate specifically (the one this test's own checkpoint 2 crosses), and
            // reads area4's own state off the SAME distance rule the gate itself uses instead of
            // asserting a fixed outcome for it — see AssertArea4RobotsMatchDistance below.
            Assert.IsTrue(TryFarthestFrom(a5, a5a6Gate, out Vector2 farStart, out float farStartDistance),
                "setup failure: could not compute a farthest-from-gate point inside area5");
            Assert.Greater(farStartDistance, NeighbourGateRange,
                $"setup failure: even area5's own farthest point from its area6 gate is only {farStartDistance:0.#}m — area5 is too small for this test's own premise");

            var startMax = new Vector3(farStart.x, 0.5f, farStart.y);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV972 Host");
            GameObject playerGo = null, areaGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);
                Transform mapRoot = host.transform.Find($"Map: {map.name}");
                Assert.IsNotNull(mapRoot, "MapRuntime never built its own map root");
                var batchRoot = mapRoot.GetComponent<MapStaticBatchRoot>();
                Assert.IsNotNull(batchRoot, "MapRuntime never attached its own MapStaticBatchRoot");

                playerGo = new GameObject("Player") { tag = "Player" };
                playerGo.transform.position = startMax;

                areaGo = new GameObject("Area Accumulation");
                var areaDirector = areaGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg);
                areaDirector.Configure(map, built.Cover); // FillArea(1)

                // MV-966 Change 2 parks (deactivates) a just-placed garrison member immediately when
                // ReachableAreas(physicalArea) doesn't yet contain its own area — Configure() leaves
                // physicalArea at 1, under which area4/5/6 are all several rooms out of reach, so without
                // this every one of their robots would come back inactive from FillArea below and vanish
                // from RobotsInArea's own FindObjectsByType (active-only) query before this test's own
                // MV-972 gate ever got a chance to run on them. Raising physicalArea to 5 here — Max's
                // own real starting area per this test's premise — makes ReachableAreas(5) cover area4
                // and area6 too (both linked directly to area5), so their garrisons stay active exactly
                // as a real run standing in area5 would leave them, and this test can exercise MV-972's
                // OWN finer-grained distance rule on top rather than being pre-empted by MV-966's coarser
                // graph-reachability one.
                areaDirector.SetCurrentArea(5);

                // MapStaticBatchRoot.Start() — never invoked automatically outside Play mode. Sets
                // MapStaticBatchRoot.Active, which every robot placed below registers itself with.
                InvokePrivate(batchRoot, "Start");

                // MV-972: FillArea's own garrison seeding draws from a shared AreaSpawnQueue whose
                // global/active caps are tuned for a real run's own paced-over-time population, not for
                // this test's own "fill five areas back to back, synchronously" setup — left at their
                // authored defaults, area4's own garrison queue starves out entirely (measured: zero
                // robots placed) well before area4 is even reached. Raised generously here, reset by
                // TearDown's own DevTuning.Reset() like every other override this suite makes.
                DevTuning.GlobalRobotBudget = 999f;
                DevTuning.MaxActiveRobots = 999f;

                // Real garrisons, off the real authored composition — the same private entry point
                // AreaAccumulationDirector.Update itself calls on a live area crossing. Filled in
                // authored order (2, 3, ... up to 6) rather than jumping straight to 4/5/6, matching how
                // a real run would actually reach them.
                for (int area = 2; area <= 6; area++)
                    InvokePrivate(areaDirector, "FillArea", area);

                List<RobotEnemy> a4Robots = RobotsInArea(4);
                List<RobotEnemy> a5Robots = RobotsInArea(5);
                List<RobotEnemy> a6Robots = RobotsInArea(6);
                Assert.IsTrue(a4Robots.Count > 0, "setup failure: area4's real garrison produced no robots");
                Assert.IsTrue(a5Robots.Count > 0, "setup failure: area5's real garrison produced no robots");
                Assert.IsTrue(a6Robots.Count > 1, "setup failure: area6's real garrison needs at least 2 robots (one stays put, one 'chases')");

                // MV-972 AC1's "a robot chasing Max from a6 into a5": one of area6's own real garrison
                // robots, physically moved to stand right next to Max's own starting position — its own
                // zone tag stays "area6" (stamped once at FillArea time, never re-registered as it
                // "chased"), so staying enabled here can ONLY be the 22m chase override, never the
                // ordinary zone-membership path.
                RobotEnemy chaser = a6Robots[0];
                chaser.transform.position = startMax + new Vector3(1.5f, 0f, 0f);
                RobotEnemy stationaryA6 = a6Robots[1];

                // A shed exists somewhere in this world (World 1 authors 17) — proof MV-972's own
                // MapRuntime.BuildFactory tagging applies to every shed, not just one hand-picked area.
                Assert.IsTrue(built.Factories.Count > 0, "setup failure: World 1 built no sheds at all");
                MowerHutch farShed = built.Factories[0];
                Renderer farShedRenderer = farShed.GetComponent<Renderer>();
                Assert.IsNotNull(farShedRenderer, "setup failure: the built shed carries no Renderer");

                // A real dropped pickup, in area5 — the same PickupDirector.PlacePartsCache call
                // MapRuntime.BuildProps makes for a map-authored EntityKind.Pickup, driven directly here
                // (World 1's own guaranteed-cache area is PowerupCadence's own runtime choice, not fixed
                // per-area in the JSON) so this test controls exactly where it lands.
                List<Pickup> a5Pickups = PickupDirector.EnsureInstalled().PlacePartsCache(startMax);
                Assert.IsTrue(a5Pickups.Count > 0, "setup failure: PlacePartsCache produced no pickups");
                Renderer pickupRenderer = a5Pickups[0].GetComponentInChildren<Renderer>();
                Assert.IsNotNull(pickupRenderer, "setup failure: the dropped pickup carries no Renderer");

                Dictionary<Renderer, List<string>> rendererZones = RendererZones(batchRoot);
                Renderer a5Scenery = FindGatedRenderer(rendererZones, "area5");
                Renderer a6Scenery = FindGatedRenderer(rendererZones, "area6");
                Assert.IsNotNull(a5Scenery, "setup failure: area5 has no tagged static renderer (cover/wall) to assert against");
                Assert.IsNotNull(a6Scenery, "setup failure: area6 has no tagged static renderer (cover/wall) to assert against");

                // --- Checkpoint 1: Max at area5's own farthest point from the area5<->area6 gate. ---
                batchRoot.ApplyAreaGate("area5", startMax);

                Assert.IsTrue(a5Scenery.enabled, "MV-972: area5's own scenery must stay enabled — it's Max's current zone");
                Assert.IsFalse(a6Scenery.enabled, "MV-972: area6's scenery must be disabled — Max is >22m from the area5<->area6 gate");
                Assert.IsFalse(farShedRenderer.enabled, "MV-972: a shed outside Max's active set must be disabled — it never used to be tagged at all");
                Assert.IsTrue(pickupRenderer.enabled, "MV-972: a pickup dropped in Max's own current zone must be enabled");

                foreach (RobotEnemy r in a5Robots) AssertRobotEnabled(r, true, "area5 (Max's own current zone)");
                AssertArea4RobotsMatchDistance(a4Robots, startMax, a4Gate, "checkpoint 1");
                // No "starts enabled" sanity check here: MapStaticBatchRoot.Start() ran (registering
                // MapStaticBatchRoot.Active) BEFORE this loop's own FillArea calls, exactly matching a
                // real map's own boot order — so RegisterGatedActor's "apply the gate's current verdict
                // immediately" behaviour (its own doc) has already hidden stationaryA6 by the time this
                // line runs, same as it would in a live game. Checkpoint 2 below (line ~227) already
                // proves this robot's renderers CAN turn on, ruling out the false-positive a "starts
                // enabled" check would otherwise guard against — just confirmed after the fact instead
                // of before.
                AssertRobotEnabled(stationaryA6, false, "area6, standing still, far from Max (>22m from the area5<->area6 gate)");
                AssertRobotEnabled(chaser, true, "area6, but standing 1.5m from Max — the MV-972 chase override");

                // --- Checkpoint 2: Max moves to within 10m of the area5<->area6 gate. ---
                Vector2 towardA5 = (a5.CenterXz - a5a6Gate).normalized;
                Vector2 nearGateXz = a5a6Gate + towardA5 * 10f;
                Assert.Less((nearGateXz - a5a6Gate).sqrMagnitude, NeighbourGateRangeSq,
                    "setup failure: the computed checkpoint-2 position isn't actually within 22m of the area5<->area6 gate");
                var nearGateMax = new Vector3(nearGateXz.x, 0.5f, nearGateXz.y);
                playerGo.transform.position = nearGateMax;

                // One throttled evaluation — TickDynamicGate's own production path calls this exact
                // method, at most 4x/second; this call stands in for "one tick has elapsed", the AC's
                // own "enable within 0.25s" (MapStaticBatchRoot.DynamicGateIntervalSeconds).
                batchRoot.ApplyAreaGate("area5", nearGateMax);

                Assert.IsTrue(a6Scenery.enabled, "MV-972: area6's scenery must enable once Max is within 22m of the shared gate");
                AssertRobotEnabled(stationaryA6, true, "area6, now within Max's 22m gate range");
                AssertRobotEnabled(chaser, true, "area6, still within 22m of Max throughout the whole approach");
                AssertArea4RobotsMatchDistance(a4Robots, nearGateMax, a4Gate, "checkpoint 2");
            }
            finally
            {
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (areaGo != null) Object.DestroyImmediate(areaGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        private static List<RobotEnemy> RobotsInArea(int areaIndex)
        {
            // Object.FindObjectsByType, not RobotEnemy.Active — that registry is populated by
            // OnEnable/OnDisable, which (per AreaAccumulationDirectorGarrisonAndPlacementTests' own
            // established idiom) doesn't reliably fire for a robot taken from the pool and placed
            // programmatically outside Play mode the way this test's own FillArea calls do.
            var result = new List<RobotEnemy>();
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                if (r != null && r.AreaIndex == areaIndex) result.Add(r);
            return result;
        }

        private static bool AnyRendererEnabled(RobotEnemy r)
        {
            foreach (Renderer rend in r.GetComponentsInChildren<Renderer>(true))
                if (rend != null && rend.enabled) return true;
            return false;
        }

        private static void AssertRobotEnabled(RobotEnemy r, bool expected, string why)
        {
            bool anyEnabled = AnyRendererEnabled(r);
            if (expected)
                Assert.IsTrue(anyEnabled, $"MV-972: robot '{r.name}' must be enabled — {why}");
            else
                Assert.IsFalse(anyEnabled, $"MV-972: robot '{r.name}' must be disabled — {why}");
        }

        /// <summary>area4's own real gate sits close enough to area5's own interior (measured: as near
        /// as 15-21m from every point this test's two checkpoints use) that it cannot be relied on to
        /// stay outside the 22m neighbour range the way the ticket's own AC1 narrative assumes — see the
        /// setup comment above <c>TryFarthestFrom</c>. Rather than assert a fixed outcome that would be
        /// wrong for this real room, this reads area4's OWN expected state off the exact same distance
        /// rule <see cref="MapStaticBatchRoot.ApplyAreaGate"/> itself applies (squared XZ distance from
        /// <paramref name="maxPosition"/> to <paramref name="a4Gate"/> against the 22m range) and asserts
        /// every one of area4's real garrison robots matches it — still a resolved-value check on the
        /// real mechanism, just not gambling on which way this particular room's geometry resolves it.</summary>
        private static void AssertArea4RobotsMatchDistance(List<RobotEnemy> a4Robots, Vector3 maxPosition, Vector2 a4Gate, string checkpointLabel)
        {
            float dx = maxPosition.x - a4Gate.x, dz = maxPosition.z - a4Gate.y;
            bool expectedActive = dx * dx + dz * dz <= NeighbourGateRangeSq;
            foreach (RobotEnemy r in a4Robots)
                AssertRobotEnabled(r, expectedActive,
                    $"area4, at {checkpointLabel} ({(expectedActive ? "within" : "beyond")} 22m of its own real gate to area5)");
        }

        /// <summary>The door mouth (MV-972: the same point <see cref="MapStaticBatchRoot.ApplyAreaGate"/>
        /// itself measures the 22m neighbour range from) of the real <see cref="MapLink"/> joining
        /// <paramref name="zoneA"/> and <paramref name="zoneB"/>, in either direction.</summary>
        private static bool TryDoorMouth(MapData map, string zoneA, string zoneB, out Vector2 mouth)
        {
            mouth = default;
            if (map.links == null) return false;

            foreach (MapLink link in map.links)
            {
                if (link == null) continue;
                bool matches = (link.from == zoneA && link.to == zoneB) || (link.from == zoneB && link.to == zoneA);
                if (!matches) continue;
                if (!MapGeometry.Doorway(map, link, out bool runsAlongX, out float coord, out Span hole)) continue;
                mouth = runsAlongX ? new Vector2(hole.Mid, coord) : new Vector2(coord, hole.Mid);
                return true;
            }
            return false;
        }

        /// <summary>The point inside <paramref name="zone"/>'s own footprint (inset 0.5m from every
        /// wall) that maximises distance from <paramref name="gate"/> — found by a 0.25m grid search
        /// over the whole inset footprint (the farthest point from a single gate over a rectangle isn't
        /// always a hand-obvious corner once you account for the inset), rather than assumed.</summary>
        private static bool TryFarthestFrom(MapZone zone, Vector2 gate, out Vector2 point, out float bestDistance)
        {
            const float inset = 0.5f;
            const float step = 0.25f;

            point = zone.CenterXz;
            bestDistance = float.NegativeInfinity;

            for (float x = zone.XMin + inset; x <= zone.XMax - inset; x += step)
            {
                for (float z = zone.ZMin + inset; z <= zone.ZMax - inset; z += step)
                {
                    var c = new Vector2(x, z);
                    float distance = (c - gate).magnitude;
                    if (distance > bestDistance)
                    {
                        bestDistance = distance;
                        point = c;
                    }
                }
            }

            return bestDistance > float.NegativeInfinity;
        }

        /// <summary>The first tagged renderer belonging to <paramref name="zoneId"/> that isn't a
        /// gameplay actor's own — this ticket now tags actors too, so the exclusions MV887AreaRendererGateTests
        /// already carries (wall/replicator/hutch/boss/gate) apply unchanged; a robot is excluded by
        /// type here as well since this test asserts robots separately, off the live registry, not off
        /// this dictionary.</summary>
        private static Renderer FindGatedRenderer(Dictionary<Renderer, List<string>> rendererZones, string zoneId)
        {
            foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
            {
                Renderer r = pair.Key;
                if (r == null || !pair.Value.Contains(zoneId)) continue;
                if (r.GetComponent<StructuralWall>() != null) continue;
                if (r.GetComponentInParent<Replicator>() != null) continue;
                if (r.GetComponentInParent<MowerHutch>() != null) continue;
                if (r.GetComponentInParent<MaxWorlds.Bosses.BigBermudaBoss>() != null) continue;
                if (r.GetComponentInParent<AreaGate>() != null) continue;
                if (r.GetComponentInParent<Pickup>() != null) continue;
                return r;
            }
            return null;
        }

        private static void InvokePrivate(object target, string methodName, params object[] args) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);

        private static Dictionary<Renderer, List<string>> RendererZones(MapStaticBatchRoot batchRoot) =>
            (Dictionary<Renderer, List<string>>)typeof(MapStaticBatchRoot)
                .GetField("_rendererZones", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(batchRoot);
    }
}
