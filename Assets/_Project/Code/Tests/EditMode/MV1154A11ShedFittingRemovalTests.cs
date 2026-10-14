using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1154 (Lee, 2026-10-10, playing World 1): remove the two spiker turrets from the Compost
    /// Corner (a11) shed roof, and only that shed. Loads the shipped world1_config.json through the
    /// real loader and builds the map (same idiom as <c>World1RuntimeTests</c>), so this proves the
    /// authored JSON change actually reaches the built shed rather than just asserting the JSON field.
    /// Also checks a15 (still "spiker"/2) in the same test to prove the change is scoped to a11, not a
    /// global switch-off (CC_AUTONOMY's one-new-test rule).
    /// </summary>
    public sealed class MV1154A11ShedFittingRemovalTests
    {
        [Test]
        public void World1_A11ShedHasNoFittings_A15ShedKeepsItsTwoSpikers()
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(WorldLibrary.Load(WorldLibrary.World1), out MapData map, out string reason), reason);

            var root = new GameObject("MV-1154 Shed Fitting Probe Root");
            try
            {
                LogAssert.ignoreFailingMessages = true;

                MapBuild built = MapRuntime.Build(map, root.transform);

                Assert.IsTrue(built.Actors.TryGetValue("a11_shed", out GameObject a11Shed), "a11's shed actor did not build");
                Assert.AreEqual(0, a11Shed.GetComponentsInChildren<ShedFitting>().Length,
                    "a11's (Compost Corner) shed must have no fittings left on its roof");

                Assert.IsTrue(built.Actors.TryGetValue("a15_shed", out GameObject a15Shed), "a15's shed actor did not build");
                ShedFitting[] a15Fittings = a15Shed.GetComponentsInChildren<ShedFitting>();
                Assert.AreEqual(2, a15Fittings.Length, "a15's shed must keep its 2 authored fittings");
                foreach (ShedFitting f in a15Fittings)
                    Assert.AreEqual(ShedFittingKind.Spiker, f.Kind, "a15's fittings must stay Spiker");
            }
            finally
            {
                Object.DestroyImmediate(root);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
