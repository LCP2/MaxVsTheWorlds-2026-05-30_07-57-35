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
        public void HarnessAddsCapacity_AccelSpeeds_HydroUntethersOnceAssembled()
        {
            UpgradeState.Install(PartKind.AugmentationHarness);
            UpgradeState.Install(PartKind.AccelerationEngine);
            UpgradeState.Install(PartKind.Hydro);

            Assert.That(UpgradeState.CapacityBonus, Is.EqualTo(UpgradeCatalog.HarnessCapacityBonus));
            Assert.That(UpgradeState.MoveSpeedMultiplier, Is.EqualTo(UpgradeCatalog.AccelSpeedMultiplier));
            Assert.That(UpgradeState.Untethered, Is.True, "both detach parts installed must untether Max");
        }

        // ---------------------------------------------------------------- YT-165: Hydro sub-assembly

        [Test]
        public void HydroAlone_DoesNotUntether()
        {
            UpgradeState.Install(PartKind.Hydro);
            Assert.That(UpgradeState.HydroAssembled, Is.False);
            Assert.That(UpgradeState.Untethered, Is.False, "the condenser has nothing to clip into yet");
        }

        [Test]
        public void HarnessAlone_DoesNotUntether()
        {
            UpgradeState.Install(PartKind.AugmentationHarness);
            Assert.That(UpgradeState.HydroAssembled, Is.False);
            Assert.That(UpgradeState.Untethered, Is.False, "the mount alone has no condenser seated");
        }

        [Test]
        public void BothDetachPartsAssemble_AndUntether()
        {
            UpgradeState.Install(PartKind.AugmentationHarness);
            Assert.That(UpgradeState.Untethered, Is.False, "precondition — one part in");

            UpgradeState.Install(PartKind.Hydro);
            Assert.That(UpgradeState.HydroAssembled, Is.True, "both parts collected must auto-assemble");
            Assert.That(UpgradeState.Untethered, Is.True);
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

        // ---------------------------------------------------------------- YT-207: draft-pick peek/commit

        [Test]
        public void PeekNextReturnsCandidatesWithoutRemovingThem()
        {
            var table = new PartDropTable();
            int total = UpgradeCatalog.AllKinds.Length;

            var peeked = table.PeekNext(3);
            Assert.That(peeked.Length, Is.EqualTo(3), "should hand back exactly the number asked for while enough remain");
            Assert.That(table.Remaining, Is.EqualTo(total), "peeking must not remove anything from the pool");

            var peekedAgain = table.PeekNext(3);
            Assert.That(peekedAgain, Is.EqualTo(peeked), "peeking twice in a row without a commit must return the same candidates");
        }

        [Test]
        public void PeekNextShrinksAsThePoolDrainsAndIsEmptyOnceItsGone()
        {
            var table = new PartDropTable();
            int total = UpgradeCatalog.AllKinds.Length;

            for (int i = 0; i < total; i++) table.TryNext(out _);

            Assert.That(table.PeekNext(3), Is.Empty, "nothing left to preview once the pool is drained");
        }

        [Test]
        public void CommitRemovesExactlyTheChosenCandidateAndLeavesTheRestForLater()
        {
            var table = new PartDropTable();
            var candidates = table.PeekNext(3);

            Assert.That(table.Commit(candidates[1]), Is.True, "committing a candidate that's in the pool must succeed");
            Assert.That(table.Remaining, Is.EqualTo(UpgradeCatalog.AllKinds.Length - 1),
                "only the chosen candidate should leave the pool");

            var stillThere = table.PeekNext(UpgradeCatalog.AllKinds.Length);
            Assert.That(stillThere, Has.No.Member(candidates[1]), "the committed part must not be offered again");
            Assert.That(stillThere, Has.Member(candidates[0]).And.Member(candidates[2]),
                "the two unpicked candidates must still be available on a later draw");
        }

        [Test]
        public void CommitTwiceOnTheSamePartIsANoOp()
        {
            var table = new PartDropTable();
            var candidates = table.PeekNext(3);

            Assert.That(table.Commit(candidates[0]), Is.True);
            Assert.That(table.Commit(candidates[0]), Is.False, "double-committing the same part must not succeed or remove anything further");
            Assert.That(table.Remaining, Is.EqualTo(UpgradeCatalog.AllKinds.Length - 1));
        }

        [Test]
        public void CommittingEveryCandidateOneByOneDrainsThePoolToEmpty()
        {
            var table = new PartDropTable();
            int total = UpgradeCatalog.AllKinds.Length;

            for (int i = 0; i < total; i++)
            {
                var candidates = table.PeekNext(3);
                Assert.That(candidates.Length, Is.EqualTo(System.Math.Min(3, total - i)));
                table.Commit(candidates[0]);
            }

            Assert.That(table.Remaining, Is.EqualTo(0));
            Assert.That(table.HasNext, Is.False);
            Assert.That(table.PeekNext(3), Is.Empty);
        }

        [Test]
        public void TheCatalogCoversEveryKind()
        {
            foreach (var k in UpgradeCatalog.AllKinds)
                Assert.That(UpgradeCatalog.For(k).Kind, Is.EqualTo(k), $"catalog entry for {k} is mislabelled");
            Assert.That(UpgradeCatalog.AllKinds.Length, Is.EqualTo(7));
        }

        // ---------------------------------------------------------------- YT-198: reordered progression

        [Test]
        public void DropOrderGrantsTwoNozzlesThenUntetherThenTheRest()
        {
            var table = new PartDropTable();

            table.TryNext(out PartKind first);
            table.TryNext(out PartKind second);
            Assert.That(new[] { first, second }, Is.EquivalentTo(new[] { PartKind.BeamNozzle, PartKind.PowerNozzle }),
                "the first two drops must be the two nozzle feel-changers");

            table.TryNext(out PartKind third);
            table.TryNext(out PartKind fourth);
            Assert.That(new[] { third, fourth }, Is.EquivalentTo(new[] { PartKind.AugmentationHarness, PartKind.Hydro }),
                "the third and fourth drops must be the untether sub-assembly's two parts (YT-165)");

            UpgradeState.Install(first);
            UpgradeState.Install(second);
            Assert.That(UpgradeState.Untethered, Is.False, "untether must not fire before its sub-assembly is granted");
            UpgradeState.Install(third);
            UpgradeState.Install(fourth);
            Assert.That(UpgradeState.Untethered, Is.True, "granting both sub-assembly parts must untether Max");

            var remaining = new System.Collections.Generic.List<PartKind>();
            while (table.TryNext(out PartKind k)) remaining.Add(k);
            Assert.That(remaining, Is.EquivalentTo(new[]
            {
                PartKind.RangeExtender, PartKind.WideBore, PartKind.AccelerationEngine,
            }), "the rest follow the untether");
        }
    }
}
