using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Unit tests for the map's geometry maths (MV-264, spatial rework MV-341): how many areas a map
    /// defines, their world-space bounds, and the plain and rotated projections
    /// <see cref="MaxWorlds.UI.MapScreen"/> draws them through (MV-563).
    /// </summary>
    public sealed class MinimapModelTests
    {
        [Test]
        public void CountAreas_CountsOnlyAreaPrefixedZones_IgnoringTheBossClearing()
        {
            var map = new MapData
            {
                zones = new[]
                {
                    new MapZone { id = "area1", type = "entry" },
                    new MapZone { id = "area2", type = "open" },
                    new MapZone { id = "area3", type = "open" },
                    new MapZone { id = "compost", type = "boss" },
                },
            };

            Assert.AreEqual(3, MinimapModel.CountAreas(map));
        }

        [Test]
        public void CountAreas_OnANullOrEmptyMap_IsZero()
        {
            Assert.AreEqual(0, MinimapModel.CountAreas(null));
            Assert.AreEqual(0, MinimapModel.CountAreas(new MapData()));
        }

        /// <summary>Pins the minimap to the real shipped level: if the map ever grows or shrinks its
        /// area count, this fails loudly instead of the HUD silently drawing the wrong number of pips.</summary>
        [Test]
        public void CountAreas_OnTheShippedBackyardSlice_IsTen()
        {
            MapData map = MapLibrary.Load(MapLibrary.BackyardSlice);
            Assert.IsNotNull(map);

            Assert.AreEqual(10, MinimapModel.CountAreas(map));
        }

        /// <summary>MV-341: the spatial minimap's world->map projection. Two area zones and one
        /// boss-clearing zone well outside them — the bounds must hug only the area zones, exactly as
        /// <see cref="CountAreas"/> already does for the count.</summary>
        private static MapData TwoAreaMap() => new MapData
        {
            zones = new[]
            {
                new MapZone { id = "area1", type = "entry", x = 5f, z = 2.5f, width = 10f, depth = 5f },
                new MapZone { id = "area2", type = "open", x = 25f, z = 7.5f, width = 10f, depth = 5f },
                new MapZone { id = "compost", type = "boss", x = 500f, z = 500f, width = 20f, depth = 20f },
            },
        };

        [Test]
        public void AreaBounds_CountsOnlyAreaPrefixedZones_IgnoringTheBossClearing()
        {
            Rect bounds = MinimapModel.AreaBounds(TwoAreaMap());

            Assert.AreEqual(0f, bounds.xMin, 0.001f);
            Assert.AreEqual(0f, bounds.yMin, 0.001f);
            Assert.AreEqual(30f, bounds.width, 0.001f);
            Assert.AreEqual(10f, bounds.height, 0.001f);
        }

        [Test]
        public void AreaBounds_OnNullOrEmptyMap_IsZero()
        {
            Assert.AreEqual(Rect.zero, MinimapModel.AreaBounds(null));
            Assert.AreEqual(Rect.zero, MinimapModel.AreaBounds(new MapData()));
        }

        [Test]
        public void NormalizedZoneRect_MapsAZonesFootprint_ToAFractionOfTheBounds()
        {
            var bounds = new Rect(0f, 0f, 20f, 10f);
            var zone = new MapZone { x = 5f, z = 2.5f, width = 10f, depth = 5f }; // XMin 0, ZMin 0

            Rect norm = MinimapModel.NormalizedZoneRect(bounds, zone);

            Assert.AreEqual(0f, norm.x, 0.001f);
            Assert.AreEqual(0f, norm.y, 0.001f);
            Assert.AreEqual(0.5f, norm.width, 0.001f);
            Assert.AreEqual(0.5f, norm.height, 0.001f);
        }

        [Test]
        public void NormalizedZoneRect_OnADegenerateBounds_FillsTheWholeSquare()
        {
            Rect norm = MinimapModel.NormalizedZoneRect(Rect.zero, new MapZone());
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), norm);
        }

        [Test]
        public void NormalizedPosition_MidBounds_IsHalfway()
        {
            var bounds = new Rect(0f, 0f, 20f, 10f);
            Vector2 norm = MinimapModel.NormalizedPosition(bounds, worldX: 10f, worldZ: 5f);

            Assert.AreEqual(0.5f, norm.x, 0.001f);
            Assert.AreEqual(0.5f, norm.y, 0.001f);
        }

        [Test]
        public void NormalizedPosition_OutsideTheBounds_ClampsToTheNearestEdge()
        {
            var bounds = new Rect(0f, 0f, 20f, 10f);

            Vector2 belowMin = MinimapModel.NormalizedPosition(bounds, worldX: -5f, worldZ: -5f);
            Assert.AreEqual(Vector2.zero, belowMin);

            Vector2 aboveMax = MinimapModel.NormalizedPosition(bounds, worldX: 25f, worldZ: 12f);
            Assert.AreEqual(Vector2.one, aboveMax);
        }

        [Test]
        public void NormalizedPosition_OnADegenerateBounds_CentresTheMarker()
        {
            Vector2 norm = MinimapModel.NormalizedPosition(Rect.zero, worldX: 3f, worldZ: 3f);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), norm);
        }

        [Test]
        public void NormalizedProjection_LeavesTheWorldUnrotated_SoTheRunReadsLeftToRight()
        {
            // World 1 is authored running +X (341 m along the run, 174 m across), so the map is a
            // plain north-up projection: +X is screen right, +Z is screen up. Nothing is swapped.
            var bounds = new Rect(0f, 0f, 20f, 10f);
            var zone = new MapZone { x = 2.5f, z = 5f, width = 5f, depth = 10f }; // XMin 0, ZMin 0, XMax 5, ZMax 10

            Rect rect = MinimapModel.NormalizedZoneRect(bounds, zone);

            Assert.That(rect.x,      Is.EqualTo(0f).Within(1e-4f));
            Assert.That(rect.width,  Is.EqualTo(0.25f).Within(1e-4f), "5 m of the world's 20 m X-extent");
            Assert.That(rect.height, Is.EqualTo(1f).Within(1e-4f),    "10 m of the world's 10 m Z-extent");

            Vector2 pos = MinimapModel.NormalizedPosition(bounds, worldX: 10f, worldZ: 5f);
            Assert.That(pos.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(pos.y, Is.EqualTo(0.5f).Within(1e-4f));
        }

        /// <summary>MV-566 AC 5: a gate into (or out of) a boss arena must read as a boss gate, and an
        /// ordinary gate between two non-boss rooms must not — the exact distinction the map screen
        /// draws with a different colour, read straight off the zone graph so it needs no access to the
        /// raw WorldConfig that authored the gate. Also covers the null-map/null-link guard so a caller
        /// iterating <c>map.links</c> never needs one of its own.</summary>
        [Test]
        public void IsBossGate_TrueOnlyWhenEitherJoinedZoneIsABossArena()
        {
            var map = new MapData
            {
                zones = new[]
                {
                    new MapZone { id = "area1", type = "open" },
                    new MapZone { id = "area2", type = "open" },
                    new MapZone { id = "compost", type = "boss" },
                },
                links = new[]
                {
                    new MapLink { from = "area1", to = "area2", gate = "g1" },
                    new MapLink { from = "area2", to = "compost", gate = "g2" },
                },
            };

            Assert.IsFalse(MinimapModel.IsBossGate(map, map.links[0]), "area1<->area2 joins no boss zone");
            Assert.IsTrue(MinimapModel.IsBossGate(map, map.links[1]), "area2<->compost joins a boss zone");
            Assert.IsFalse(MinimapModel.IsBossGate(null, map.links[0]), "a null map guards rather than throws");
            Assert.IsFalse(MinimapModel.IsBossGate(map, null), "a null link guards rather than throws");
        }
    }
}
