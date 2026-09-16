using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-801: the sludge channel now cuts the floor and sinks the ooze surface into a trough, but only
    /// for a "channel-eligible" rect (narrower dimension &lt;= 8 m, and it does not cover its whole
    /// area) — <c>a3</c>'s 36x5 sludge rect qualifies, <c>a18</c>'s 26x26 (the whole area) does not, per
    /// the ticket's own rule. Fails on base commit f89c4e7: <c>StormdrainDressing.BayRects</c> took no
    /// obstacles parameter (so a bay could never be skipped) and <c>StormdrainKit.DressSludgeTile</c>
    /// took no <c>isChannel</c> parameter (so the ooze never moved) — see the fix comment for the
    /// captured failure output.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting three RESOLVED values (Rule 2,
    /// Tier 2): no cast-bay's <see cref="Renderer.bounds"/> overlaps a3's channel footprint (the floor
    /// is genuinely cut, not painted over — Rule 3: read from the built renderer, not the rect list);
    /// a3's lowest sludge piece drops by exactly the ooze surface's own 0.18 m relative to the same tile
    /// built without the channel treatment, while a18's stays exactly where it was; and
    /// <see cref="MapSlowZones.Instance"/>'s <c>SpeedMultiplierAt</c> is unchanged before/after the
    /// dressing pass at three points inside a3's channel — the proof nothing about movement changed,
    /// matching the ticket's own arithmetic on why a14/a18/a23 can never get a blocking trough.
    /// </summary>
    public sealed class MV801ChannelTests
    {
        [Test]
        public void ChannelEligibleSludge_CutsFloorAndSinksOoze_ButNeverTouchesMovement()
        {
            EnemyNavigation.Reset();

            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            MapEntity a3Sludge = map.entities.First(e => e != null && e.id == "a3_sludge1");
            MapEntity a18Sludge = map.entities.First(e => e != null && e.id == "a18_sludge1");

            var a3ChannelRect = new Rect(a3Sludge.x - a3Sludge.width * 0.5f, a3Sludge.z - a3Sludge.depth * 0.5f,
                a3Sludge.width, a3Sludge.depth);

            // Seed EnemyNavigation.Map through a bare BackyardPath -- same idiom
            // MV795FloodRobotImmunityTests.InstallFloodedZone already uses -- so MapSlowZones.Instance
            // (which reads EnemyNavigation.Map) resolves against THIS map, both before and after Dress()
            // runs (Dress() never touches the map's own authored data, only builds GameObjects, so the
            // two lookups must agree).
            var pathGo = new GameObject("MV801-backyard-path");
            var host = new GameObject("MV801 host").transform;
            var baselineHost = new GameObject("MV801 baseline host").transform;
            try
            {
                var path = pathGo.AddComponent<BackyardPath>();
                FieldInfo mapField = typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(mapField, "BackyardPath._map went missing -- EnemyNavigation.Map can't be seeded");
                mapField.SetValue(path, map);

                Vector3[] samplePoints =
                {
                    new Vector3(a3ChannelRect.center.x, 0f, a3ChannelRect.center.y),
                    new Vector3(a3ChannelRect.xMin + 1f, 0f, a3ChannelRect.center.y),
                    new Vector3(a3ChannelRect.xMax - 1f, 0f, a3ChannelRect.center.y),
                };
                float[] speedBefore = samplePoints.Select(p => MapSlowZones.Instance.SpeedMultiplierAt(p)).ToArray();

                // Baseline (pre-ticket-shape) resolved geometry for both tiles, built through the same
                // DressSludgeTile with isChannel forced false -- a resolved value, not a re-derived
                // constant, so this proves "moved down by exactly 0.18 m" against the real mesh bounds.
                Vector3 a3Center = new Vector3(a3Sludge.x, 0f, a3Sludge.z);
                Vector3 a18Center = new Vector3(a18Sludge.x, 0f, a18Sludge.z);
                Vector3 a3Flow = StormdrainDressing.SludgeFlowDirection(map, a3Sludge);
                Vector3 a18Flow = StormdrainDressing.SludgeFlowDirection(map, a18Sludge);

                SludgeFlowRig baselineA3 = StormdrainKit.DressSludgeTile(baselineHost, a3Center,
                    a3Sludge.width, a3Sludge.depth, a3Flow, seed: 1, isChannel: false);
                SludgeFlowRig baselineA18 = StormdrainKit.DressSludgeTile(baselineHost, a18Center,
                    a18Sludge.width, a18Sludge.depth, a18Flow, seed: 2, isChannel: false);
                float baselineA3MinY = LowestPieceY(baselineA3.transform);
                float baselineA18MinY = LowestPieceY(baselineA18.transform);

                MapBuild build = MapRuntime.Build(map, host);
                StormdrainDressing.Dress(host, map, build.Cover);

                // ---- AC1: no bay renderer overlaps a3's channel footprint -- the floor is cut ----
                Transform floorHost = host.Find("Stormdrain Dressing/Floor Composition");
                Assert.IsNotNull(floorHost, "the floor composition host was never built");
                int bayCount = 0;
                foreach (Transform child in floorHost)
                {
                    if (child.name != "Bay") continue;
                    bayCount++;
                    Renderer rend = child.GetComponent<Renderer>();
                    Assert.IsNotNull(rend, $"{child.name} carries no renderer to read bounds from");
                    Bounds b = rend.bounds;
                    bool overlapsChannel = b.max.x > a3ChannelRect.xMin && b.min.x < a3ChannelRect.xMax
                                         && b.max.z > a3ChannelRect.yMin && b.min.z < a3ChannelRect.yMax;
                    Assert.IsFalse(overlapsChannel,
                        $"{child.name} at {child.position} overlaps a3's channel footprint {a3ChannelRect} -- the floor was not cut");
                }
                Assert.Greater(bayCount, 0, "the floor composition pass built no bays at all -- this test proves nothing");

                // ---- AC2: the ooze surface drops by exactly ChannelOozeDrop for a3; a18 is untouched ----
                Transform sludgeHost = host.Find("Stormdrain Dressing/Sludge");
                Assert.IsNotNull(sludgeHost, "the sludge dressing host was never built");
                Transform a3Tile = FindTileNear(sludgeHost, a3Center);
                Transform a18Tile = FindTileNear(sludgeHost, a18Center);
                Assert.IsNotNull(a3Tile, "a3's sludge tile was never built");
                Assert.IsNotNull(a18Tile, "a18's sludge tile was never built");

                float a3MinY = LowestPieceY(a3Tile);
                float a18MinY = LowestPieceY(a18Tile);

                Assert.AreEqual(-StormdrainKit.ChannelOozeDrop, a3MinY - baselineA3MinY, 0.02f,
                    "a3 is channel-eligible -- its ooze surface must resolve exactly ChannelOozeDrop (0.18m) lower than the same tile built without the channel treatment");
                Assert.AreEqual(baselineA18MinY, a18MinY, 0.02f,
                    "a18's sludge rect covers its whole area, so it is not channel-eligible -- its ooze must stay exactly where it was");

                // ---- AC3: nothing about movement changed inside a3's channel ----
                float[] speedAfter = samplePoints.Select(p => MapSlowZones.Instance.SpeedMultiplierAt(p)).ToArray();
                for (int i = 0; i < samplePoints.Length; i++)
                    Assert.AreEqual(speedBefore[i], speedAfter[i], 0.0001f,
                        $"SpeedMultiplierAt must be unchanged by the dressing pass at {samplePoints[i]}");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
                Object.DestroyImmediate(baselineHost.gameObject);
                Object.DestroyImmediate(pathGo);
                EnemyNavigation.Reset();
            }
        }

        private static Transform FindTileNear(Transform sludgeHost, Vector3 center)
        {
            foreach (Transform tile in sludgeHost)
            {
                var flatTile = new Vector3(tile.position.x, 0f, tile.position.z);
                var flatCenter = new Vector3(center.x, 0f, center.z);
                if (Vector3.Distance(flatTile, flatCenter) < 0.5f) return tile;
            }
            return null;
        }

        private static float LowestPieceY(Transform tile)
        {
            float min = float.MaxValue;
            foreach (Renderer r in tile.GetComponentsInChildren<Renderer>(true))
            {
                // The trough (MV-801, change 2) is a distinct structural piece from "the whole
                // existing DressSludgeTile output" the ticket's change 3 moves -- it stays at the
                // tile's pre-ticket floor datum by design, so it must not count as "the ooze" here.
                if (IsUnderChannelTrough(r.transform)) continue;
                min = Mathf.Min(min, r.bounds.min.y);
            }
            return min;
        }

        private static bool IsUnderChannelTrough(Transform t)
        {
            for (Transform cur = t; cur != null; cur = cur.parent)
                if (cur.name == "Channel Trough") return true;
            return false;
        }
    }
}
