using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-868 — Lee: ARC fires correctly but is too faint to read against the impact flash and spark
    /// burst that land on the same frame. This gives it the bolt's own orange glow sleeve (the
    /// additive layer <c>SeekerPulse</c> wears around its core, authored here as ARC's own copy on
    /// <see cref="CombatVfxTuning.LppeArcTuning"/> rather than reaching into SeekerPulse's private
    /// fields) around the unchanged blue-white core, so an arc reads as its own event.
    ///
    /// Fails on base commit 68d962b -- not as a runtime assertion (the sleeve overload this test calls
    /// doesn't exist there yet), but as the compile error that is the only possible failure mode for a
    /// ticket introducing new API surface: <c>ArcBoltVfx.Show</c> there is
    /// <c>Show(Vector3, Vector3, float, float, Color)</c>, a single line at <c>lineWidth 0.12</c> with
    /// no sleeve, so this test's <c>Show(from, to, tuning, coreColor)</c> call fails the whole EditMode
    /// build with (Logs/compile-mv868-base.log):
    /// <c>error CS7036: There is no argument given that corresponds to the required formal parameter
    /// 'color' of 'ArcBoltVfx.Show(Vector3, Vector3, float, float, Color)'</c>.
    /// </summary>
    public sealed class MV868ArcSleeveVfxTests
    {
        [Test]
        public void ShowBuildsAnOrangeSleeveAroundTheCore_BothTracingTheExactFromTo()
        {
            var from = new Vector3(1f, 0f, 2f);
            var to = new Vector3(4f, 0f, 6f);
            var coreColor = new Color(1.4f, 1.7f, 2.2f, 1f);
            CombatVfxTuning.LppeArcTuning tuning = CombatVfxTuning.LppeArc();

            GameObject go = ArcBoltVfx.Show(from, to, tuning, coreColor);
            try
            {
                LineRenderer[] lines = go.GetComponentsInChildren<LineRenderer>()
                    .OrderByDescending(l => l.widthMultiplier)
                    .ToArray();

                Assert.AreEqual(2, lines.Length,
                    "ArcBoltVfx.Show must build TWO LineRenderers on the spawned GameObject -- a wide " +
                    "sleeve behind a narrow core -- not the single line the old faint ARC drew");

                LineRenderer sleeve = lines[0];
                LineRenderer core = lines[1];

                Assert.AreEqual(0.34f, sleeve.widthMultiplier, 0.0001f,
                    "the sleeve's resolved widthMultiplier must be the bolt sheath's own 0.34m width");
                Assert.AreEqual(0.07f, core.widthMultiplier, 0.0001f,
                    "the core's resolved widthMultiplier must be 0.07m, inside the sleeve");

                // LineRenderer.startColor round-trips through a 2-key Gradient, whose colour keys are
                // stored as Color32 (8 bits/channel) -- so a resolved channel can be off by up to
                // 1/255 (~0.0039) from the authored float even though nothing is wrong.
                const float colorTolerance = 0.004f;
                Color expectedSleeveColor = new Color(1.00f, 0.45f, 0.10f, 0.55f);
                Assert.AreEqual(expectedSleeveColor.r, sleeve.startColor.r, colorTolerance, "sleeve tint red channel");
                Assert.AreEqual(expectedSleeveColor.g, sleeve.startColor.g, colorTolerance, "sleeve tint green channel");
                Assert.AreEqual(expectedSleeveColor.b, sleeve.startColor.b, colorTolerance, "sleeve tint blue channel");
                Assert.AreEqual(expectedSleeveColor.a, sleeve.startColor.a, colorTolerance,
                    "the sleeve's resolved startColor must be the orange bolt tint at 0.55 alpha, not the " +
                    "core's blue-white");

                foreach (LineRenderer line in lines)
                {
                    Assert.AreEqual(from, line.GetPosition(0),
                        $"'{line.name}' must start exactly at the hit point, jitter or not");
                    Assert.AreEqual(to, line.GetPosition(line.positionCount - 1),
                        $"'{line.name}' must end exactly at the arc target, jitter or not");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
