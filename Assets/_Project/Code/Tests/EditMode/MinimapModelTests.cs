using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Unit tests for the minimap's fog-of-war maths (MV-264): which areas are Hidden, Visited or
    /// Current for a given <see cref="MaxWorlds.Enemies.AreaAccumulationDirector.CurrentArea"/>, and
    /// how many areas a map defines in the first place.
    /// </summary>
    public sealed class MinimapModelTests
    {
        [Test]
        public void BuildStates_OnAFreshRun_OnlyAreaOneIsCurrent_TheRestAreHidden()
        {
            AreaVisibility[] states = MinimapModel.BuildStates(totalAreas: 5, currentArea: 1);

            Assert.AreEqual(5, states.Length);
            Assert.AreEqual(AreaVisibility.Current, states[0]);
            for (int i = 1; i < states.Length; i++)
                Assert.AreEqual(AreaVisibility.Hidden, states[i], $"area {i + 1} should still be hidden");
        }

        [Test]
        public void BuildStates_MidRun_AreasBehindAreVisited_CurrentIsMarked_AheadStaysHidden()
        {
            AreaVisibility[] states = MinimapModel.BuildStates(totalAreas: 5, currentArea: 3);

            Assert.AreEqual(AreaVisibility.Visited, states[0], "area 1");
            Assert.AreEqual(AreaVisibility.Visited, states[1], "area 2");
            Assert.AreEqual(AreaVisibility.Current, states[2], "area 3");
            Assert.AreEqual(AreaVisibility.Hidden, states[3], "area 4");
            Assert.AreEqual(AreaVisibility.Hidden, states[4], "area 5");
        }

        [Test]
        public void BuildStates_AtTheLastArea_EveryAreaIsRevealed_NoneHidden()
        {
            AreaVisibility[] states = MinimapModel.BuildStates(totalAreas: 5, currentArea: 5);

            for (int i = 0; i < states.Length - 1; i++)
                Assert.AreEqual(AreaVisibility.Visited, states[i], $"area {i + 1}");
            Assert.AreEqual(AreaVisibility.Current, states[4], "area 5");
        }

        [Test]
        public void BuildStates_NegativeTotal_ClampsToAnEmptyStrip()
        {
            Assert.AreEqual(0, MinimapModel.BuildStates(totalAreas: -3, currentArea: 1).Length);
        }

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
    }
}
