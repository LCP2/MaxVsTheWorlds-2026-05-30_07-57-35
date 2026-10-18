using System.Reflection;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1234 — <see cref="WorldRunner.ResolveShedComposition"/> (private, reflected into below) is a
    /// SECOND, independent "authored replaces dial" branch that <see cref="WorldConfig.SolveComposition"/>
    /// also has — MV-1220 only fixed the one in <c>SolveComposition</c> (the <see cref="WorldComposition.blendWithDial"/>
    /// opt-in). <c>ResolveShedComposition</c> still checked only <see cref="WorldComposition.IsAuthored"/>
    /// and returned the raw authored composition verbatim, with no <c>blendWithDial</c> check at all — so
    /// MV-1218's Reef a18 (<c>{dreadnought:1, blendWithDial:true}</c>) resolved every dial-driven kind
    /// (rusher/bruiser/.../launcher) to the authored zero instead of blending them from the dial solve,
    /// starving a18's shed of everything it was never meant to restate by hand.
    ///
    /// One EditMode test (MV-465 Rule 1), asserting the RESOLVED <see cref="WorldComposition"/>
    /// <c>ResolveShedComposition</c> actually returns (Rule 2, Tier 2) — never an authored constant,
    /// never a rendered pixel.
    /// </summary>
    public sealed class MV1234ShedBlendWithDialCompositionTests
    {
        private static readonly MethodInfo ResolveShedCompositionMethod =
            typeof(WorldRunner).GetMethod("ResolveShedComposition", BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void ResolveShedComposition_BlendsAnAuthoredOnlyKindOntoTheDialSolve_WhenBlendWithDialIsSet()
        {
            var cfg = new WorldConfig
            {
                dials = new WorldDials
                {
                    areaCount = 1, baseThreat = 14f, threatGrowth = 0.1f, pacingRhythm = new[] { 1f },
                    toughnessCurve = new WorldToughnessCurve(),
                },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "a18", index = 1, role = "normal",
                        // The exact shape MV-1218's Reef diff needs: one new authored-only kind,
                        // opted into blending so the area's existing dial-derived mix survives.
                        composition = new WorldComposition { dreadnought = 1, blendWithDial = true },
                    },
                },
            };
            WorldArea area = cfg.areas[0];

            Assert.IsNotNull(ResolveShedCompositionMethod,
                "setup failure: WorldRunner.ResolveShedComposition not found by reflection");

            var resolved = (WorldComposition)ResolveShedCompositionMethod.Invoke(null, new object[] { cfg, area });

            Assert.AreEqual(1, resolved.dreadnought, "the authored dreadnought count must come through");
            Assert.Greater(
                resolved.rusher + resolved.bruiser + resolved.heavy + resolved.brute +
                resolved.gunner + resolved.launcher + resolved.blinker,
                0,
                "blendWithDial must blend the dial-derived kinds onto the shed's resolved composition " +
                "(MV-1234), not zero them the way returning the raw authored composition verbatim did");
        }
    }
}
