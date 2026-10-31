using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1332. MV-1273 turned HDR on for the Mobile quality tier, which let ShipHangar's own
    /// BloomIntensity/Scatter override (3.3x/1.3x <see cref="BackyardLook.Default"/>'s own ceiling-
    /// checked ShipBase values) actually fire for the first time -- Mobile had stayed LDR (no pixel
    /// could exceed 1.0) until then, so the oversized override never visibly bloomed. The fix caps
    /// every Ship deck's resolved Bloom volume, not just Hangar's: asserts the ENGINE-RESOLVED
    /// <see cref="Bloom"/> override on the real <see cref="VolumeProfile"/> <see cref="BackyardLighting.Apply"/>
    /// builds (CLAUDE.md Testing policy v2, Tier 2), not the authored <see cref="BackyardLook"/> struct
    /// fields directly -- a struct field that silently stopped being written through to the volume
    /// would pass a Tier-1 check and still bloom on-screen.
    /// </summary>
    public sealed class MV1332ShipBloomCapTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private Bloom ResolvedBloom(BackyardLook look)
        {
            var go = new GameObject("mv1332-bloom-cap-test");
            _spawned.Add(go);
            var lighting = go.AddComponent<BackyardLighting>();
            lighting.Apply(look);

            Assert.IsTrue(lighting.Profile.TryGet<Bloom>(out var bloom), "no Bloom override on the resolved profile");
            return bloom;
        }

        private static void AssertCapped(string deckName, Bloom bloom)
        {
            Assert.IsTrue(bloom.threshold.overrideState, $"{deckName}: BloomThreshold is not overridden");
            Assert.IsTrue(bloom.intensity.overrideState, $"{deckName}: BloomIntensity is not overridden");
            Assert.IsTrue(bloom.scatter.overrideState, $"{deckName}: BloomScatter is not overridden");

            Assert.That(bloom.threshold.value, Is.GreaterThanOrEqualTo(1.2f),
                $"{deckName}: resolved BloomThreshold ({bloom.threshold.value:0.00}) is below MV-1332's " +
                "1.2 cap -- an HDR pixel this side of 1.0 can bloom again.");
            Assert.That(bloom.scatter.value, Is.LessThanOrEqualTo(0.4f),
                $"{deckName}: resolved BloomScatter ({bloom.scatter.value:0.00}) is above MV-1332's 0.4 " +
                "cap -- the bloom kernel spreads wide enough to swallow Max again.");
            Assert.That(bloom.intensity.value, Is.LessThanOrEqualTo(1.0f),
                $"{deckName}: resolved BloomIntensity ({bloom.intensity.value:0.00}) is above MV-1332's " +
                "1.0 cap.");
        }

        /// <summary>MV-465 Rule 1 -- one new test for this ticket. Loops every Ship deck (AC3's own
        /// "for every Ship look") inside the single test rather than one [Test] method per deck.</summary>
        [Test]
        public void EveryShipDeck_ResolvedBloomStaysCapped()
        {
            AssertCapped("ShipHangar", ResolvedBloom(BackyardLook.ShipHangar));
            AssertCapped("ShipAssembly", ResolvedBloom(BackyardLook.ShipAssembly));
            AssertCapped("ShipHolds", ResolvedBloom(BackyardLook.ShipHolds));
            AssertCapped("ShipEngineering", ResolvedBloom(BackyardLook.ShipEngineering));
            AssertCapped("ShipCommand", ResolvedBloom(BackyardLook.ShipCommand));
        }
    }
}
