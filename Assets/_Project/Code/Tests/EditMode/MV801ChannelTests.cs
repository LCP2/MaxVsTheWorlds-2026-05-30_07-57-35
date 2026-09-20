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
    /// area) — <c>a3</c>'s 36x5 sludge rect qualifies. Fails on base commit f89c4e7:
    /// <c>StormdrainDressing.BayRects</c> took no obstacles parameter (so a bay could never be skipped)
    /// and <c>StormdrainKit.DressSludgeTile</c> took no <c>isChannel</c> parameter (so the ooze never
    /// moved) — see the fix comment for the captured failure output.
    ///
    /// MV-865's V3 re-author dropped the ineligible negative-control content this test used to compare
    /// against (old a18's whole-area 26x26 rect, old a14's exact-fit rect, old a8's 10x10-too-wide sump
    /// — every sludge rect authored anywhere in the current World 2 config is now channel-eligible, per
    /// an exhaustive sweep of the shipped file against <c>IsChannelEligible</c>'s own rule). With no
    /// surviving real content to prove the "ineligible" branch against, that half of the assertion is
    /// dropped rather than pinned to a stale id that no longer means what the test claims (Rule 3: assert
    /// a measured property against real content, not a guess) — the eligible-branch coverage below still
    /// guards the original MV-801 regression.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting two RESOLVED values (Rule 2,
    /// Tier 2): no cast-bay's <see cref="Renderer.bounds"/> overlaps a3's channel footprint (the floor
    /// is genuinely cut, not painted over — Rule 3: read from the built renderer, not the rect list);
    /// a3's lowest sludge piece drops by exactly the ooze surface's own 0.18 m relative to the same tile
    /// built without the channel treatment; and <see cref="MapSlowZones.Instance"/>'s
    /// <c>SpeedMultiplierAt</c> is unchanged before/after the dressing pass at three points inside a3's
    /// channel — the proof nothing about movement changed.
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
                Vector3 a3Flow = StormdrainDressing.SludgeFlowDirection(map, a3Sludge);

                SludgeFlowRig baselineA3 = StormdrainKit.DressSludgeTile(baselineHost, a3Center,
                    a3Sludge.width, a3Sludge.depth, a3Flow, seed: 1, isChannel: false);
                float baselineA3MinY = LowestPieceY(baselineA3.transform);

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
                Assert.IsNotNull(a3Tile, "a3's sludge tile was never built");

                float a3MinY = LowestPieceY(a3Tile);

                Assert.AreEqual(-StormdrainKit.ChannelOozeDrop, a3MinY - baselineA3MinY, 0.02f,
                    "a3 is channel-eligible -- its ooze surface must resolve exactly ChannelOozeDrop (0.18m) lower than the same tile built without the channel treatment");

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
