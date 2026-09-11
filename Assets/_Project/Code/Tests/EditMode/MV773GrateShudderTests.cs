using UnityEngine;
using NUnit.Framework;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-773 change 3: "the bars get a short vertical shudder ... as the Lurker rises". World 2's 25
    /// authored grates and their geometry (exactly-25 count, no Collider, grille-bar children) were
    /// already built and already covered by <see cref="MV781FloorCompositionTests"/>, which landed first
    /// — this ticket's only remaining gap is that nothing ever moved a grate's own bars when its Lurker
    /// rattles. Fails on base commit b84f30f: <c>MaxWorlds.Rendering.GrateShudder</c> does not exist
    /// there, so this fails to COMPILE.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2): a bar's own local Y actually moves away from its built resting height at mid-progress and
    /// returns to exactly that resting height at both ends; and <see cref="GrateShudder.TriggerNear"/>
    /// actually triggers only the one registered grate whose authored tile contains the given world
    /// position, leaving every other registered grate untouched.
    /// </summary>
    public sealed class MV773GrateShudderTests
    {
        [Test]
        public void GrateShudder_AppliesBarOffsetAndTriggerNearHitsOnlyTheContainingTile()
        {
            var insideHost = new GameObject("Inside Grate");
            var outsideHost = new GameObject("Outside Grate");
            try
            {
                GrateShudder.ClearRegistry();

                Transform insideBar = NewBar(insideHost.transform);
                float restY = insideBar.localPosition.y;
                var insideShudder = insideHost.AddComponent<GrateShudder>();
                insideShudder.Configure(new[] { insideBar });
                GrateShudder.Register(new Vector2(6f, 6f), insideShudder);

                Transform outsideBar = NewBar(outsideHost.transform);
                var outsideShudder = outsideHost.AddComponent<GrateShudder>();
                outsideShudder.Configure(new[] { outsideBar });
                GrateShudder.Register(new Vector2(20f, 20f), outsideShudder);

                AssertBarOffsetRisesThenReturnsToRest(insideShudder, insideBar, restY);

                Assert.IsFalse(insideShudder.IsShuddering, "resetting Apply() manually must not leave the timer itself running");
                Assert.IsFalse(outsideShudder.IsShuddering, "an unrelated grate must start un-triggered");

                // (6.5, 6.5) is the tile centre of the (6,6) grate (MV-724's own authoring convention for
                // where a garrisoned Lurker actually stands) — it must trigger ONLY that grate.
                GrateShudder.TriggerNear(new Vector3(6.5f, 0f, 6.5f));
                Assert.IsTrue(insideShudder.IsShuddering, "a world position on the (6,6) grate's own tile must trigger it");
                Assert.IsFalse(outsideShudder.IsShuddering, "triggering the (6,6) grate must not also trigger the (20,20) grate");

                // Two tiles away from either registered grate: must trigger neither.
                GrateShudder.TriggerNear(new Vector3(50f, 0f, 50f));
                Assert.IsFalse(outsideShudder.IsShuddering, "a world position on no registered grate's tile must trigger nothing");
            }
            finally
            {
                GrateShudder.ClearRegistry();
                Object.DestroyImmediate(insideHost);
                Object.DestroyImmediate(outsideHost);
            }
        }

        private static Transform NewBar(Transform parent)
        {
            var bar = new GameObject("Grille Bar0").transform;
            bar.SetParent(parent, false);
            bar.localPosition = new Vector3(0.2f, -0.03f, 0f);
            return bar;
        }

        private static void AssertBarOffsetRisesThenReturnsToRest(GrateShudder shudder, Transform bar, float restY)
        {
            shudder.Apply(0f);
            Assert.AreEqual(restY, bar.localPosition.y, 0.0001f,
                "at progress 0 the bar must sit exactly at its built resting height");

            shudder.Apply(0.5f);
            float midY = bar.localPosition.y;
            Assert.Greater(midY, restY,
                $"at progress 0.5 the bar ({midY:F4}) must have risen above its resting height ({restY:F4})");

            shudder.Apply(1f);
            Assert.AreEqual(restY, bar.localPosition.y, 0.0001f,
                "at progress 1 the bar must have settled back to exactly its resting height");
        }
    }
}
