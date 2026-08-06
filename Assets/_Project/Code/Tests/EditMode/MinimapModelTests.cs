using NUnit.Framework;
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
    }
}
