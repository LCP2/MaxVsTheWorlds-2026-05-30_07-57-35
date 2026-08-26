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

        /// <summary>MV-563: the full map screen rotates the plain world-space projection 90° clockwise
        /// so the world's long Z-axis (the run) reads left-to-right, matching the design's own
        /// <c>MVW_World1_Map.svg</c> reference — old "up" (+Z) becomes new "right", old "right" (+X)
        /// becomes new "down". Same fixture as <see cref="NormalizedZoneRect_MapsAZonesFootprint_ToAFractionOfTheBounds"/>
        /// (bounds 20x10, zone occupying the low-X/low-Z quarter — plain-projection rect (0,0,0.5,0.5))
        /// so the two are directly comparable: rotating swaps which axis is width vs height AND flips
        /// the surviving X-derived axis, landing at (0, 0.5, 0.5, 0.5) — not the unrotated rect, and not
        /// a naive axis swap without the flip either. The player position gets the same treatment.</summary>
        [Test]
        public void RotatedNormalizedZoneRectAndPosition_TurnTheWorldsLongZAxisIntoScreenLeftToRight()
        {
            var bounds = new Rect(0f, 0f, 20f, 10f);
            var zone = new MapZone { x = 5f, z = 2.5f, width = 10f, depth = 5f }; // XMin 0, ZMin 0, XMax 10, ZMax 5

            Rect rotatedRect = MinimapModel.RotatedNormalizedZoneRect(bounds, zone);
            Assert.AreEqual(0f, rotatedRect.x, 0.001f, "rotated X should track the zone's Z-fraction min");
            Assert.AreEqual(0.5f, rotatedRect.y, 0.001f, "rotated Y should be 1 - the zone's X-fraction max");
            Assert.AreEqual(0.5f, rotatedRect.width, 0.001f, "rotated width should track the zone's Z extent");
            Assert.AreEqual(0.5f, rotatedRect.height, 0.001f, "rotated height should track the zone's X extent");

            Vector2 rotatedPos = MinimapModel.RotatedNormalizedPosition(bounds, worldX: 10f, worldZ: 5f);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rotatedPos, "world (10,5) is bounds-centre either way, so rotation is a no-op here");

            Vector2 rotatedCorner = MinimapModel.RotatedNormalizedPosition(bounds, worldX: 20f, worldZ: 0f);
            Assert.AreEqual(new Vector2(0f, 0f), rotatedCorner, "old max-X/min-Z corner should rotate to the new (0,0) corner");
        }
    }
}
