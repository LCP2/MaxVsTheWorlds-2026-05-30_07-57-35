using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1341 (Lee, iPhone, TestFlight v0.25.0, World 5 Hangar, after MV-1332/1333/1336): the launch
    /// ring and a hazard lane line still washed out a quarter of the screen because <c>ShipKit</c>'s old
    /// <c>Hdr(c, boost)</c> multiplied a colour by a large HDR boost with no ceiling on where it landed
    /// once <see cref="ShipKit.NeonStrength"/> was applied — a boost tuned only to clear the tonemapper's
    /// compression (MV-1274), never checked against how far past <see cref="BackyardLook.BloomThreshold"/>
    /// (1.2) it actually resolved. AC1: "every emissive colour built by ShipKit (sampled across all five
    /// deck looks) has max channel &lt;= 1.1 after NeonStrength." Fails on the base commit (c59be86) —
    /// the hazard line resolves to ~4.74/3.85/1.02 there, the launch ring's outer gold to ~2.14/1.85/1.08.
    /// Proves the RESOLVED material colour (testing policy Tier 2) on every ShipKit call site that ever
    /// routes a colour through its private HDR-boost helper: the Hangar launch-pad hazard centreline
    /// (<see cref="ShipKit.BuildLaunchPadHazardStripes"/>), the launch ring's three elements
    /// (<see cref="ShipKit.BuildLaunchRingMarking"/>), and the Hangar wall-top cap's neon edge
    /// (<see cref="ShipKit.BuildBulkheadPanel"/>'s <c>hangarStyle</c> path), the last sampled against all
    /// five Mothership deck looks' own <see cref="BackyardLook.RimColor"/> to prove the cap holds
    /// regardless of which deck's colour is fed through it, not just the one deck that happens to wire
    /// it up today.
    /// </summary>
    public sealed class MV1341ShipHdrCapTests
    {
        private const float MaxChannelAfterNeonStrength = 1.1f; // AC1's own ceiling

        [Test]
        public void EveryShipKitHdrBoostResolvesUnderOnePointOneAfterNeonStrength()
        {
            var root = new GameObject("MV-1341 Ship HDR Cap Probe Root");
            try
            {
                AssertHazardLaneLineUnderCap(root.transform);
                AssertLaunchRingUnderCap(root.transform);
                AssertHangarCapNeonUnderCapForEveryDeckLook(root.transform);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---- launch-pad hazard centreline --------------------------------------------------------------

        private static void AssertHazardLaneLineUnderCap(Transform parent)
        {
            var area = new Rect(0f, 0f, 16f, 11f);
            GameObject pad = ShipKit.BuildLaunchPadHazardStripes(parent, area);
            AssertResolvedMaxChannelUnderCap(pad.transform, "Hazard N Neon");
            AssertResolvedMaxChannelUnderCap(pad.transform, "Hazard S Neon");
        }

        // ---- launch ring: outer gold, inner cyan, centre roundel ---------------------------------------

        private static void AssertLaunchRingUnderCap(Transform parent)
        {
            GameObject ring = ShipKit.BuildLaunchRingMarking(parent, Vector3.zero, radius: 6f);
            AssertResolvedMaxChannelUnderCap(ring.transform, "Ring Outer Gold");
            AssertResolvedMaxChannelUnderCap(ring.transform, "Ring Inner Cyan");
            AssertResolvedMaxChannelUnderCap(ring.transform, "Mark Roundel");
        }

        // ---- Hangar wall-top cap neon edge, sampled across all five deck looks' own RimColor -----------

        private static void AssertHangarCapNeonUnderCapForEveryDeckLook(Transform parent)
        {
            var deckRimColors = new[]
            {
                BackyardLook.ShipHangar.RimColor,
                BackyardLook.ShipAssembly.RimColor,
                BackyardLook.ShipHolds.RimColor,
                BackyardLook.ShipEngineering.RimColor,
                BackyardLook.ShipCommand.RimColor,
            };

            foreach (Color deckGlow in deckRimColors)
            {
                GameObject wall = ShipKit.BuildBulkheadPanel(parent, new Vector2(0f, 0f), new Vector2(3f, 0f),
                    Vector3.forward, wallHeight: 2.5f, wallThickness: 0.2f, deckGlow, isOuterEdge: false,
                    wallSeed: 1, capVisible: true, hangarStyle: true);
                try
                {
                    AssertResolvedMaxChannelUnderCap(wall.transform, "Wall Top Cap Neon Edge");
                }
                finally
                {
                    Object.DestroyImmediate(wall);
                }
            }
        }

        // ---- shared plumbing ----------------------------------------------------------------------------

        private static void AssertResolvedMaxChannelUnderCap(Transform root, string childName)
        {
            Transform child = root.Find(childName);
            Assert.IsNotNull(child, $"expected a '{childName}' child under '{root.name}'");

            var renderer = child.GetComponent<Renderer>();
            Assert.IsNotNull(renderer, $"'{childName}' has no renderer");

            Material mat = renderer.sharedMaterial;
            Assert.IsNotNull(mat, $"'{childName}' has no material");

            Color actual = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                          : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear;

            float maxChannel = Mathf.Max(actual.r, Mathf.Max(actual.g, actual.b));
            Assert.LessOrEqual(maxChannel, MaxChannelAfterNeonStrength,
                $"'{childName}' max channel must be <= {MaxChannelAfterNeonStrength:F2} after NeonStrength, resolved {actual}");
        }
    }
}
