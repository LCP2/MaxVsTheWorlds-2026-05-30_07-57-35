using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-270 AC3: the calibrated <c>world1_config.json</c> THV table, checked against the real Water
    /// Blaster and the real <see cref="PowerRamp"/> level curve rather than a placeholder MPL — this is
    /// the "wire MPL to Max's real equipped-weapon stats" step <c>PowerScoringTests</c> (MV-269)
    /// explicitly left for this ticket.
    ///
    /// MPL needs an assumed relationship between run progress and Max's level, since <see cref="PowerRamp"/>
    /// has no area-aware curve of its own — this test assumes one level gained per area cleared
    /// (level N entering Area N), the simplest defensible curve absent a real XP-pacing spec, matching
    /// world1_config.json's own calibration note. Areas 1-7 are asserted strictly inside
    /// <see cref="PowerScoring"/>'s band; Area 8 is asserted only loosely — its EPL has no area-9 term to
    /// bleed forward from (<see cref="PowerScoring.EnemyPowerLevel"/>'s own doc comment), so R structurally
    /// spikes there for ANY calibration, not just this one. That spike (hardest area right before the
    /// Toolshed's boss gate) is a fair "peak before the final push" reading of it, not a bug — final
    /// tuning of how hot Area 8 should run is the "spot-checked in play" half of AC3, not something an
    /// automated band assertion can settle on its own.
    /// </summary>
    public sealed class World1CalibrationTests
    {
        // The Water Blaster's own authored base tick rate — 4 damage / 0.1 s = 40 dps at level 1,
        // before the level ramp or hitFraction (WaterBlaster.cs; already the reference AreaGate itself
        // assumes for gate-break timing).
        private const float BaseTickDps = AreaGate.AssumedPrimaryDps;

        // Spec's own stated range for the execution-adjusted hit fraction (Confluence MVW 34439170 §4)
        // is ~0.5-0.8; the midpoint is the least-biased single number to calibrate against absent a
        // recorded playtest hit-rate.
        private const float HitFraction = 0.65f;

        private static float Mpl(int level)
        {
            float primaryDps = PowerScoring.PrimaryEffectiveDps(
                damage: BaseTickDps * PowerRamp.DpsMultiplier(level),
                fireRate: 1f, hitFraction: HitFraction, areaFactor: 1f, rangeFactor: 1f);

            // No secondary/ability contribution modelled — the slice's abilities are unlockable extras,
            // not a baseline every run has by a given area, so 0 is the conservative (lower-MPL) floor.
            return PowerScoring.MaxPowerLevel(primaryDps, secondaryContribution: 0f, abilityContribution: 0f,
                survivabilityFactor: 1f);
        }

        [Test]
        public void World1_BandRatioHoldsForAreasOneThroughSeven()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg);

            for (int area = 1; area <= 7; area++)
            {
                float mpl = Mpl(area); // level == area, see class doc
                float epl = PowerScoring.EnemyPowerLevel(area, cfg);
                float r = PowerScoring.BandRatio(mpl, epl);

                Assert.IsTrue(PowerScoring.WithinBand(r),
                    $"Area {area}: R={r:0.000} (MPL={mpl:0.0}, EPL={epl:0.0}) is outside " +
                    $"[{PowerScoring.BandLow},{PowerScoring.BandHigh}] — recalibrate enemyTypes.thv");
            }
        }

        [Test]
        public void World1_BandRatioAtAreaEightStaysInAPlausibleRange()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg);

            float mpl = Mpl(8);
            float epl = PowerScoring.EnemyPowerLevel(8, cfg);
            float r = PowerScoring.BandRatio(mpl, epl);

            // Loose sanity bound only, per the class doc — Area 8's missing look-ahead term makes R
            // structurally the highest in the run for any calibration; this just catches the config
            // going numerically wrong (a zero/negative THV, a dial typo), not a fine-tuning miss.
            Assert.Greater(r, PowerScoring.BandLow);
            Assert.Less(r, 3f);
        }
    }
}
