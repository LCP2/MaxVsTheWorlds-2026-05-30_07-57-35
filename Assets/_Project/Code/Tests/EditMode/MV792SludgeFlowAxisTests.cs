using System;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-792: every one of World 2's 20 sludge rects steered its flow direction by looking up an
    /// entity id, "outfall", that no map authors — <c>MapRuntime.cs</c> says so in its own words:
    /// "today's placeholder World 2 config, which authors no outfall gate yet". The lookup always
    /// returned null, so <see cref="StormdrainDressing"/> fell back to <c>Vector3.forward</c> for every
    /// rect regardless of its own shape. Every one of the 20 authored rects is wider along X than deep
    /// along Z (a1 is 22x4, a3 is 36x5, ...), so the fallback was wrong for all twenty. Fails on base
    /// commit edd9c3c: <c>StormdrainDressing.SludgeFlowDirection</c> does not exist there (this test
    /// fails to COMPILE), and the equivalent inline lookup in <c>DressSludge</c> resolves
    /// <c>Vector3.forward</c> (a Z-axis direction) for a 22x4 rect, which is wide along X.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2): the direction a synthetic rect on each axis actually resolves; the direction every one
    /// of World 2's 20 shipped sludge rects actually resolves; the longest bounding axis a built tile's
    /// two lip children actually carry; how far a built tile's rig actually ticks a band in 1.0s; and
    /// the direction resolved when a map DOES author an "outfall" entity.
    /// </summary>
    public sealed class MV792SludgeFlowAxisTests
    {
        [Test]
        public void SludgeFlowFollowsItsOwnRectShape_NotAMissingOutfallLookup()
        {
            // ---- AC1: the flow axis follows the rect's own shape, not a lookup ----
            var wideEntity = new MapEntity { id = "wide", x = 0f, z = 0f, width = 22f, depth = 4f };
            var tallEntity = new MapEntity { id = "tall", x = 0f, z = 0f, width = 4f, depth = 22f };
            var emptyMap = new MapData
            {
                zones = Array.Empty<MapZone>(),
                links = Array.Empty<MapLink>(),
                entities = Array.Empty<MapEntity>(),
            };

            Vector3 wideFlow = StormdrainDressing.SludgeFlowDirection(emptyMap, wideEntity);
            Vector3 tallFlow = StormdrainDressing.SludgeFlowDirection(emptyMap, tallEntity);

            Assert.That(Mathf.Abs(Vector3.Dot(wideFlow.normalized, Vector3.right)), Is.GreaterThan(0.99f),
                $"a 22x4 rect must resolve a flow direction parallel to X (resolved {wideFlow})");
            Assert.That(Mathf.Abs(Vector3.Dot(tallFlow.normalized, Vector3.forward)), Is.GreaterThan(0.99f),
                $"a 4x22 rect must resolve a flow direction parallel to Z (resolved {tallFlow})");

            // ---- AC2: every one of World 2's 20 shipped sludge rects resolves an axis parallel to its OWN long axis ----
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData w2map, out string reason), reason);

            int sludgeCount = 0;
            foreach (MapEntity e in w2map.entities)
            {
                if (e == null || e.Kind != EntityKind.Sludge) continue;
                sludgeCount++;

                Vector3 flow = StormdrainDressing.SludgeFlowDirection(w2map, e);
                bool expectAxisX = e.width >= e.depth;
                float alignment = expectAxisX
                    ? Mathf.Abs(Vector3.Dot(flow.normalized, Vector3.right))
                    : Mathf.Abs(Vector3.Dot(flow.normalized, Vector3.forward));

                Assert.That(alignment, Is.GreaterThan(0.99f),
                    $"sludge rect '{e.id}' ({e.width}x{e.depth}) must resolve a flow axis parallel to its own " +
                    $"long axis ({(expectAxisX ? "X" : "Z")}), resolved {flow}");
            }
            Assert.AreEqual(20, sludgeCount, "World 2 must still author all 20 sludge rects this ticket counted");

            // ---- AC3: a built tile's two lip children run along its own long side ----
            var lipHost = new GameObject("MV792 lip host").transform;
            try
            {
                SludgeFlowRig rig = StormdrainKit.DressSludgeTile(lipHost, Vector3.zero, 22f, 4f, Vector3.right, seed: 3);
                Transform lipA = rig.transform.Find("Lip Bank A");
                Transform lipB = rig.transform.Find("Lip Bank B");
                Assert.IsNotNull(lipA, "a built sludge tile must carry 'Lip Bank A'");
                Assert.IsNotNull(lipB, "a built sludge tile must carry 'Lip Bank B'");

                foreach (Transform lip in new[] { lipA, lipB })
                {
                    Vector3 size = lip.GetComponent<MeshFilter>().sharedMesh.bounds.size;
                    Assert.That(size.x, Is.GreaterThan(size.z),
                        $"{lip.name} on a 22x4 (X-run) tile must run longest along its own local X " +
                        $"(the tile's long/flow side), resolved size {size}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(lipHost.gameObject);
            }

            // ---- AC4: ticking a 22x4 tile's rig by 1s moves a band 0.35m along X, not Z ----
            var axisHost = new GameObject("MV792 axis host").transform;
            try
            {
                SludgeFlowRig rig = StormdrainKit.DressSludgeTile(axisHost, Vector3.zero, 22f, 4f, Vector3.right, seed: 5);
                Transform band0 = rig.transform.Find("Bands").GetChild(0);
                Vector3 before = band0.localPosition;
                rig.Tick(1.0f);
                Vector3 after = band0.localPosition;

                Assert.AreEqual(0.35f, Mathf.Abs(after.x - before.x), 0.01f,
                    $"band 0 on a 22x4 tile must move 0.35m along X in 1.0s (from {before} to {after})");
                Assert.AreEqual(0f, Mathf.Abs(after.z - before.z), 0.001f,
                    $"band 0 on a 22x4 tile must NOT move along Z (from {before} to {after})");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(axisHost.gameObject);
            }

            // ---- AC5: a map that DOES author an "outfall" entity still points downstream toward it ----
            var outfallEntity = new MapEntity { id = "outfall", kind = "areaGate", x = 100f, z = 0f };
            var sludgeNearOutfall = new MapEntity { id = "near_outfall", x = -10f, z = 0f, width = 22f, depth = 4f };
            var outfallMap = new MapData
            {
                zones = Array.Empty<MapZone>(),
                links = Array.Empty<MapLink>(),
                entities = new[] { outfallEntity, sludgeNearOutfall },
            };

            Vector3 towardOutfall = StormdrainDressing.SludgeFlowDirection(outfallMap, sludgeNearOutfall);
            Assert.That(Vector3.Dot(towardOutfall.normalized, Vector3.right), Is.GreaterThan(0.99f),
                $"a rect with an authored 'outfall' entity to its +X side must resolve flow toward it (resolved {towardOutfall})");
        }
    }
}
