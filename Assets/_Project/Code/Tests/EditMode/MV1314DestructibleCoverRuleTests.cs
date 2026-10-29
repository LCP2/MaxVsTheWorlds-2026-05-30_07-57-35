using NUnit.Framework;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1314: MV-1302 AC1 ("every world has destructible cover in &gt;= 20 areas") found the
    /// Mothership structurally stuck at 10/30 areas under the original rule (footprint &lt;= 6 m²,
    /// every hull-named piece excluded regardless of size) — <c>ship_crate</c> is authored at exactly
    /// 9 m² everywhere and 15 areas author only hull-named <c>ship_bulkhead_stub</c> cover, so no
    /// selection percentage could reach 20. Lee's decision (29 Oct 2026): raise the footprint cap to
    /// 10 m² and carve <c>ship_bulkhead_stub</c> instances &lt;= 6 m² out of the hull exclusion.
    ///
    /// Fails on c450969 (the commit this ticket started from, before <see cref="MapValidation.IsEligibleForDestructibleCover"/>/
    /// <see cref="MapValidation.CountAreasWithEligibleDestructibleCover"/> existed): those methods and
    /// <see cref="MapValidation.DestructibleCoverFootprintCapSqm"/>/<see cref="MapValidation.DestructibleCoverHullCarveOutSqm"/>
    /// are not present on that commit, so this test does not compile there — see the fix comment for
    /// the captured failure output.
    ///
    /// One test (testing policy MV-465, Rule 1), asserting a RESOLVED value (Rule 2, Tier 2): loads the
    /// real shipped <c>mothership_config.json</c> and counts areas via the engine's own rule, never an
    /// authored constant.
    /// </summary>
    public sealed class MV1314DestructibleCoverRuleTests
    {
        private const int RequiredAreas = 20;

        [Test]
        public void MothershipConfig_MarksAtLeast20AreasEligibleUnderTheNewRule()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(cfg, "the shipped Mothership config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            int eligible = MapValidation.CountAreasWithEligibleDestructibleCover(cfg, map);

            Assert.GreaterOrEqual(eligible, RequiredAreas,
                $"Mothership marks only {eligible} areas eligible for destructible cover under MV-1314's rule, " +
                $"short of MV-1302 AC1's required {RequiredAreas}");
        }
    }
}
