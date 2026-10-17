using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1220 — <see cref="WorldConfig.SolveComposition"/> used to treat ANY authored
    /// <see cref="WorldComposition"/> as a full replacement for the dial-derived solve (MV-365),
    /// with no way to add a NEW authored-composition-only kind (bolter/lurker/turret/sludger/
    /// charger/dreadnought — kinds the dial solve itself never grants) to an otherwise
    /// dial-derived area without also restating every dial-driven kind's current solved count by
    /// hand. <see cref="WorldComposition.blendWithDial"/> is the fix: an explicit opt-in (never
    /// inferred from zero-ness — see that field's own comment for why) that blends this
    /// composition's authored-only counts onto the dial solve instead of replacing it.
    ///
    /// One EditMode test (MV-465 Rule 1), asserting the RESOLVED composition
    /// <see cref="WorldConfig.SolveComposition"/> actually returns (Rule 2, Tier 2) — never an
    /// authored constant, never a rendered pixel.
    /// </summary>
    public sealed class MV1220CompositionBlendTests
    {
        [Test]
        public void SolveComposition_BlendsAnAuthoredOnlyKindOntoTheDialSolve_WhenBlendWithDialIsSet()
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
                        id = "a1", index = 1, role = "normal",
                        // The exact shape MV-1218's Reef diff needs: one new authored-only kind,
                        // opted into blending so the area's existing dial-derived mix survives.
                        composition = new WorldComposition { dreadnought = 1, blendWithDial = true },
                    },
                },
            };

            DifficultyEngine.Composition solved = cfg.SolveComposition(1);

            Assert.AreEqual(1, solved.Dreadnought, "the authored dreadnought count must come through");
            Assert.Greater(
                solved.Rusher + solved.Bruiser + solved.Heavy + solved.Brute +
                solved.Gunner + solved.Launcher + solved.Blinker,
                0,
                "blendWithDial must blend the dreadnought count onto the dial-derived solve for " +
                "every other kind in the area (MV-1220), not replace it");
        }
    }
}
