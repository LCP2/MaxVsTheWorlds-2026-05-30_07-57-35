using NUnit.Framework;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The stacking upgrade state (YT-133): each installed part contributes its modifier, they
    /// accumulate, and the drop table hands out each of the five exactly once.
    /// </summary>
    public sealed class UpgradeStateTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => UpgradeState.Reset();

        [Test]
        public void FreshStateIsAllBaseline()
        {
            Assert.That(UpgradeState.ConeMultiplier, Is.EqualTo(1f), "no nozzle, no narrowing");
            Assert.That(UpgradeState.RangeBonus, Is.EqualTo(0f));
            Assert.That(UpgradeState.CapacityBonus, Is.EqualTo(0f));
            Assert.That(UpgradeState.MoveSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(UpgradeState.Untethered, Is.False);
        }

        [Test]
        public void BeamNozzleNarrowsTheCone()
        {
            UpgradeState.Install(PartKind.BeamNozzle);
            Assert.That(UpgradeState.ConeMultiplier, Is.EqualTo(UpgradeCatalog.NozzleConeMultiplier),
                "a beam nozzle should narrow the cone by one nozzle factor");
        }

        [Test]
        public void TwoNozzlesCompound_AndPowerAlsoLengthens()
        {
            UpgradeState.Install(PartKind.BeamNozzle);
            UpgradeState.Install(PartKind.PowerNozzle);

            float f = UpgradeCatalog.NozzleConeMultiplier;
            Assert.That(UpgradeState.ConeMultiplier, Is.EqualTo(f * f).Within(1e-5f),
                "two nozzles installed must compound — upgrades stack");
            Assert.That(UpgradeState.RangeBonus, Is.EqualTo(UpgradeCatalog.PowerRangeBonus),
                "the power nozzle also lengthens the beam");
        }

        // ---------------------------------------------------------------- YT-164: Range Extender / Wide-Bore

        [Test]
        public void RangeExtenderLengthensFurther_OnTopOfPower()
        {
            UpgradeState.Install(PartKind.PowerNozzle);
            UpgradeState.Install(PartKind.RangeExtender);

            Assert.That(UpgradeState.RangeBonus,
                Is.EqualTo(UpgradeCatalog.PowerRangeBonus + UpgradeCatalog.RangeExtenderBonus).Within(1e-5f),
                "power (4 m) + extender (2 m) should reach 6 m total");
        }

        [Test]
        public void WideBoreWidensTheConeBackOut_KeepingTheReach()
        {
            UpgradeState.Install(PartKind.BeamNozzle);
            UpgradeState.Install(PartKind.PowerNozzle);
            UpgradeState.Install(PartKind.RangeExtender);
            float narrowCone = UpgradeState.ConeMultiplier;
            float reachBeforeWideBore = UpgradeState.RangeBonus;

            UpgradeState.Install(PartKind.WideBore);

            Assert.That(UpgradeState.ConeMultiplier, Is.GreaterThan(narrowCone),
                "the wide-bore must widen the cone back out relative to the fully-narrowed beam");
            Assert.That(UpgradeState.RangeBonus, Is.EqualTo(reachBeforeWideBore).Within(1e-5f),
                "the wide-bore must not change the reach, only the cone");
        }

        [Test]
        public void HarnessAddsCapacity_AccelSpeeds_HydroUntethers()
        {
            UpgradeState.Install(PartKind.AugmentationHarness);
            UpgradeState.Install(PartKind.AccelerationEngine);
            UpgradeState.Install(PartKind.Hydro);

            Assert.That(UpgradeState.CapacityBonus, Is.EqualTo(UpgradeCatalog.HarnessCapacityBonus));
            Assert.That(UpgradeState.MoveSpeedMultiplier, Is.EqualTo(UpgradeCatalog.AccelSpeedMultiplier));
            Assert.That(UpgradeState.Untethered, Is.True, "the Hydro device must untether Max");
        }

        [Test]
        public void EverythingStacks_AndInstallIsIdempotent()
        {
            foreach (var k in UpgradeCatalog.AllKinds) UpgradeState.Install(k);
            UpgradeState.Install(PartKind.BeamNozzle);   // again — no-op

            Assert.That(UpgradeState.InstalledCount, Is.EqualTo(7), "installing a part twice must not double-count");
            Assert.That(UpgradeState.MoveSpeedMultiplier, Is.EqualTo(UpgradeCatalog.AccelSpeedMultiplier));
            Assert.That(UpgradeState.Untethered, Is.True);
        }

        [Test]
        public void InstalledExposesEveryPartCurrentlyOn()
        {
            UpgradeState.Install(PartKind.BeamNozzle);
            UpgradeState.Install(PartKind.Hydro);
            Assert.That(UpgradeState.Installed, Is.EquivalentTo(new[] { PartKind.BeamNozzle, PartKind.Hydro }),
                "a save slot (YT-151) reads the whole installed set off this");
        }

        [Test]
        public void ResetClearsEverything()
        {
            UpgradeState.Install(PartKind.Hydro);
            UpgradeState.Reset();
            Assert.That(UpgradeState.Untethered, Is.False);
            Assert.That(UpgradeState.InstalledCount, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- unique drop table

        [Test]
        public void TheDropTableDispensesEveryPartExactlyOnce()
        {
            var table = new PartDropTable();
            var seen = new System.Collections.Generic.HashSet<PartKind>();
            int total = UpgradeCatalog.AllKinds.Length;

            for (int i = 0; i < total; i++)
            {
                Assert.That(table.TryNext(out PartKind k), Is.True, $"part {i} should be available");
                Assert.That(seen.Add(k), Is.True, $"{k} dropped twice — the table must be unique");
            }

            Assert.That(seen.Count, Is.EqualTo(total), "every part must be in the table");
            Assert.That(table.TryNext(out _), Is.False, "after the last part, no more parts drop");
            Assert.That(table.HasNext, Is.False);
        }

        [Test]
        public void TheCatalogCoversEveryKind()
        {
            foreach (var k in UpgradeCatalog.AllKinds)
                Assert.That(UpgradeCatalog.For(k).Kind, Is.EqualTo(k), $"catalog entry for {k} is mislabelled");
            Assert.That(UpgradeCatalog.AllKinds.Length, Is.EqualTo(7));
        }
    }
}
