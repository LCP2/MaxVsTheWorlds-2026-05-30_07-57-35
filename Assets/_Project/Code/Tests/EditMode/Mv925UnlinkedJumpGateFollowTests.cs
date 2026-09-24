using System.Collections.Generic;
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
    /// MV-925 — <see cref="MapStaticBatchRoot.ApplyAreaGate"/> only ever re-ran off
    /// <see cref="AreaAccumulationDirector.PlayerCrossedIntoArea"/>, which <see cref="AreaAccumulationDirector.Update"/>
    /// refuses to fire for a jump between two areas with no authored <see cref="MapLink"/> joining them —
    /// logged as "blocked an area-tracker jump" while the tracker holds at its old value. A Blink over a
    /// wall, or Max standing in an overlay zone with no MapLink of its own (World 2's a15/a17 per this
    /// ticket's own Jira comment), both produce exactly that: the tracker (correctly, per its own
    /// accumulation rules) refuses to move, but Max's real position has already jumped, and before this
    /// fix the render gate had no way of ever finding out — every renderer outside whatever area the
    /// tracker was last allowed to reach stayed dark ("no walls anywhere, no upper decks at all").
    ///
    /// One consolidated test (MV-465 Rule 1): builds the real shipped World 2 config, drives
    /// <see cref="AreaAccumulationDirector.Update"/> with the player physically placed in a zone that has
    /// no MapLink to area1 (an unlinked jump — the tracker must refuse it, same premise MV-833 already
    /// proved), then drives <see cref="MapStaticBatchRoot.Update"/> (MV-925's own self-heal) and asserts
    /// the gate follows Max there anyway: a wall renderer belonging to the new zone (Rule 2/3: a resolved,
    /// measured enabled state, not mere presence) is enabled, even though the tracker — asserted
    /// separately — never moved.
    ///
    /// Fails on 5873bda (current main at pickup): <see cref="MapStaticBatchRoot"/> carries no
    /// <c>Update()</c> at all before this ticket, so nothing ever re-applies the gate for an unlinked
    /// jump — the wall assertion below reads disabled.
    /// </summary>
    public sealed class Mv925UnlinkedJumpGateFollowTests
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
        public void UnlinkedJump_TrackerRefuses_ButGateFollowsMaxAndEnablesTheNewZonesWall()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries (see MV887AreaRendererGateTests' own note).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            var host = new GameObject("MV925 Host");
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

                // MapStaticBatchRoot.Start() — never invoked automatically outside Play mode. Same
                // reflection trick MV887AreaRendererGateTests/MV920AreaTrackingGantryTests already use.
                InvokePrivate(batchRoot, "Start");
                Assert.That(areaDirector.PhysicalArea, Is.EqualTo(1), "precondition: a fresh Configure() cold-boots at area1");

                Dictionary<Renderer, List<string>> rendererZones = RendererZones(batchRoot);

                // The far, unlinked zone: a zone with no MapLink to area1 (map.AreLinked false) that ALSO
                // carries a wall renderer currently disabled (area1 is current) — so the assertion below
                // is decisive rather than a renderer that happened to already be lit some other way.
                string farId = null;
                Renderer farWall = null;
                foreach (MapZone z in map.zones)
                {
                    if (z == null || z.id == "area1" || map.AreLinked("area1", z.id)) continue;
                    Renderer wall = FindWallRenderer(rendererZones, z.id);
                    if (wall == null || wall.enabled) continue;
                    farId = z.id;
                    farWall = wall;
                    break;
                }
                Assert.IsNotNull(farWall,
                    "setup failure: no zone with no MapLink to area1 has a currently-disabled wall renderer to assert against");

                playerGo = new GameObject("Player") { tag = "Player" };
                playerGo.transform.position = ProbePositionFor(map, map.Zone(farId));

                // The tracker's own refusal (MV-833's existing behaviour, unchanged by this ticket) —
                // driven with the exact same Update() reflection trick MV920AreaTrackingGantryTests uses.
                InvokePrivate(areaDirector, "Update");
                Assert.That(areaDirector.PhysicalArea, Is.EqualTo(1),
                    $"setup failure: area1 and {farId} must have no MapLink for this test to exercise the " +
                    $"refusal — the tracker moved to {areaDirector.PhysicalArea}");

                // MV-925's own fix: MapStaticBatchRoot.Update(), the gate's self-heal, must follow Max to
                // the zone he is physically standing in even though the tracker above refused to move.
                InvokePrivate(batchRoot, "Update");

                Assert.IsTrue(farWall.enabled,
                    $"MV-925: the render gate must follow Max into {farId} and enable its own wall even " +
                    "though the area tracker refused the (unlinked) jump");
            }
            finally
            {
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (areaGo != null) Object.DestroyImmediate(areaGo);
                Object.DestroyImmediate(host);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
            }
        }

        /// <summary>The first wall (<see cref="StructuralWall"/>-carrying) renderer <paramref name="rendererZones"/>
        /// tags as belonging to <paramref name="zoneId"/> — walls specifically, since this ticket's own
        /// acceptance criterion names walls, and <see cref="MapRuntime.TagWallZones"/>'s two-sided probe
        /// is exactly the tagging path a shared boundary exercises.</summary>
        private static Renderer FindWallRenderer(Dictionary<Renderer, List<string>> rendererZones, string zoneId)
        {
            foreach (KeyValuePair<Renderer, List<string>> pair in rendererZones)
            {
                if (pair.Key == null || pair.Key.GetComponent<StructuralWall>() == null) continue;
                if (pair.Value.Contains(zoneId)) return pair.Key;
            }
            return null;
        }

        /// <summary>A world position that resolves (via <see cref="MapData.ZoneAt(float,float,float)"/>)
        /// to <paramref name="zone"/> — its own centre at a low floor height for a level-0 room, or the
        /// centre of the Deck/Hatch entity built for it at deck height for a level&gt;0 overlay. Copied
        /// from MV920AreaTrackingGantryTests' own identical helper.</summary>
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

        private static Dictionary<Renderer, List<string>> RendererZones(MapStaticBatchRoot batchRoot) =>
            (Dictionary<Renderer, List<string>>)typeof(MapStaticBatchRoot)
                .GetField("_rendererZones", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(batchRoot);
    }
}
