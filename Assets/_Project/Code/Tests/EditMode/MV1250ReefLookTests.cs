using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1250: the Reef has shipped lit by <see cref="BackyardLook.Default"/> since MV-713
    /// (MV-745/MV-1141's own deferral) — a bright, warm-keyed look over the Reef's own navy hull
    /// palette that measured median luminance 74/255 on a live TestFlight screenshot, against the
    /// design frames' 27-36/255 dark navy. This proves the catalog row itself now resolves
    /// <see cref="BackyardLook.Reef"/>, not <see cref="BackyardLook.Default"/> — looked up by
    /// <see cref="WorldCatalog.IndexOf"/>, never a literal index (MV-1176 moved the Reef's own index
    /// once already when the City was inserted ahead of it).
    ///
    /// Asserts two RESOLVED fields off the row's own <see cref="WorldDefinition.Look"/> — ambient sky
    /// and fog colour, the two fields that most directly separate a bright warm room from a dark navy
    /// one — against <see cref="BackyardLook.Reef"/>'s own values, not a constant re-read from the
    /// source (Rule 2, Tier 2).
    ///
    /// Fails on base commit 1f08215: that commit's Reef row resolves <see cref="BackyardLook.Default"/>,
    /// whose AmbientSky is (0.47, 0.58, 0.80) and FogColor is (0.66, 0.67, 0.62) — both far from
    /// <see cref="BackyardLook.Reef"/>'s dark navy values (quoted failure output in the fix comment).
    /// </summary>
    public sealed class MV1250ReefLookTests
    {
        [Test]
        public void ReefRowResolvesItsOwnDarkLook()
        {
            WorldDefinition reef = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Reef));
            BackyardLook expected = BackyardLook.Reef;

            Assert.AreEqual(expected.AmbientSky, reef.Look.AmbientSky,
                $"Reef row AmbientSky ({reef.Look.AmbientSky}) must resolve BackyardLook.Reef's " +
                $"({expected.AmbientSky}), not BackyardLook.Default's bright warm ambient.");

            Assert.AreEqual(expected.FogColor, reef.Look.FogColor,
                $"Reef row FogColor ({reef.Look.FogColor}) must resolve BackyardLook.Reef's " +
                $"({expected.FogColor}), not BackyardLook.Default's light warm haze.");
        }
    }
}
