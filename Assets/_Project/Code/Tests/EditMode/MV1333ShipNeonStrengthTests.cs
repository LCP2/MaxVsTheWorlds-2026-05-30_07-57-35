using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1333 (Lee: Mothership neon "slightly too strong on the iPhone ... must stay neon"): every
    /// Mothership neon element's final emissive colour must be 0.75x its previous value, read through
    /// one named constant, <see cref="ShipKit.NeonStrength"/>. Fails to compile on the base commit
    /// (a34528b) — that constant does not exist yet, so nothing dims. Proves the RESOLVED material
    /// colour (testing policy Tier 2) on three representative elements: a wall strip light
    /// ("Wall Lamp", <see cref="ShipKit.BuildBulkheadPanel"/>), a hazard-yellow lane/edge line
    /// ("Hazard N Neon", <see cref="ShipKit.BuildLaunchPadHazardStripes"/>), and a hero set-piece neon
    /// ("Ring Outer Gold", <see cref="ShipKit.BuildLaunchRingMarking"/>, the Hangar Bay's own launch
    /// ring — <see cref="MaxWorlds.Arena.ShipSetPieces"/>'s hero piece for area 1).
    /// </summary>
    public sealed class MV1333ShipNeonStrengthTests
    {
        [Test]
        public void WallStripLaneLineAndSetPieceNeonAllDimToNeonStrengthOfTheirPreviousEmissive()
        {
            var root = new GameObject("MV-1333 Ship Neon Strength Probe Root");
            try
            {
                AssertWallStripDimmed(root.transform);
                AssertHazardLaneLineDimmed(root.transform);
                AssertSetPieceNeonDimmed(root.transform);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---- wall strip light: "Wall Lamp", built straight off the caller's own deckGlow -------------

        private static void AssertWallStripDimmed(Transform parent)
        {
            var deckGlow = new Color(0.30f, 0.50f, 0.80f);
            GameObject wall = ShipKit.BuildBulkheadPanel(parent, new Vector2(0f, 0f), new Vector2(3f, 0f),
                Vector3.forward, wallHeight: 2.5f, wallThickness: 0.2f, deckGlow, isOuterEdge: false, wallSeed: 1);

            Color expected = Dim(deckGlow);
            AssertResolvedEmissiveColour(wall.transform, "Wall Lamp", expected);
        }

        // ---- hazard-yellow lane/edge line: "Hazard N Neon", HDR-boosted off ShipKit's own hazard tone --

        private static void AssertHazardLaneLineDimmed(Transform parent)
        {
            var area = new Rect(0f, 0f, 16f, 11f);
            GameObject pad = ShipKit.BuildLaunchPadHazardStripes(parent, area);

            // ShipKit's own HazardYellow (Rgb(224, 182, 48)) and its Hdr(..., 12.0f) boost on the
            // centreline are both private to the kit -- duplicated here only to compute the expected
            // RESOLVED colour this check compares against, not to re-derive the dimming logic itself.
            Color hazardYellow = new Color(224f / 255f, 182f / 255f, 48f / 255f);
            Color expected = Dim(Hdr(hazardYellow, 12.0f));
            AssertResolvedEmissiveColour(pad.transform, "Hazard N Neon", expected);
        }

        // ---- hero set-piece neon: "Ring Outer Gold" on the Hangar Bay's own launch ring marking -------

        private static void AssertSetPieceNeonDimmed(Transform parent)
        {
            GameObject ring = ShipKit.BuildLaunchRingMarking(parent, Vector3.zero, radius: 6f);

            Color expected = Dim(Hdr(ShipKit.GoldLamp, 5.0f));
            AssertResolvedEmissiveColour(ring.transform, "Ring Outer Gold", expected);
        }

        // ---- shared plumbing ----------------------------------------------------------------------------

        private static Color Dim(Color c) => new Color(c.r * ShipKit.NeonStrength, c.g * ShipKit.NeonStrength, c.b * ShipKit.NeonStrength, c.a);

        private static Color Hdr(Color c, float boost)
        {
            Color r = c * boost;
            r.a = 1f;
            return r;
        }

        private static void AssertResolvedEmissiveColour(Transform root, string childName, Color expected)
        {
            Transform child = root.Find(childName);
            Assert.IsNotNull(child, $"expected a '{childName}' child under '{root.name}'");

            var renderer = child.GetComponent<Renderer>();
            Assert.IsNotNull(renderer, $"'{childName}' has no renderer");

            Material mat = renderer.sharedMaterial;
            Assert.IsNotNull(mat, $"'{childName}' has no material");

            Color actual = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                          : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear;

            const float tol = 0.002f;
            Assert.LessOrEqual(Mathf.Abs(actual.r - expected.r), tol, $"'{childName}' R: expected {expected.r:F4}, resolved {actual.r:F4}");
            Assert.LessOrEqual(Mathf.Abs(actual.g - expected.g), tol, $"'{childName}' G: expected {expected.g:F4}, resolved {actual.g:F4}");
            Assert.LessOrEqual(Mathf.Abs(actual.b - expected.b), tol, $"'{childName}' B: expected {expected.b:F4}, resolved {actual.b:F4}");
        }
    }
}
