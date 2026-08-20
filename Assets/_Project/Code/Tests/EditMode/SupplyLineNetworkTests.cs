using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Origination — sheds as reinforcement engines (Confluence MVW 34439170 §6, MV-269), pinned
    /// against this ticket's AC3: a shed's stream routes along the gate graph to its own area and
    /// adjacent shed-free areas, and destroying a shed halts only its own line.
    ///
    /// Fixture graph — a2 and a5 are both sheds, hubbed off a2:
    /// <code>a1 -- a2(shed) -- a3
    ///              |     \
    ///              a4    a5(shed)
    ///              |
    ///              a6</code>
    /// </summary>
    public sealed class SupplyLineNetworkTests
    {
        private static WorldGate Gate(string id, string from, string to) => new WorldGate
        {
            id = id,
            from = new WorldGateEndpoint { area = from },
            to = new WorldGateEndpoint { area = to },
        };

        private static WorldConfig FixtureWorld() => new WorldConfig
        {
            world = "Test World",
            areas = new[]
            {
                new WorldArea { id = "a1", index = 1, role = "normal" },
                new WorldArea { id = "a2", index = 2, role = "shed", hasShed = true, shed = new WorldShed() },
                new WorldArea { id = "a3", index = 3, role = "normal" },
                new WorldArea { id = "a4", index = 4, role = "normal" },
                new WorldArea { id = "a5", index = 5, role = "shed", hasShed = true, shed = new WorldShed() },
                new WorldArea { id = "a6", index = 6, role = "normal" },
            },
            gates = new[]
            {
                Gate("g1", "a1", "a2"),
                Gate("g2", "a2", "a3"),
                Gate("g3", "a2", "a4"),
                Gate("g4", "a2", "a5"),
                Gate("g5", "a4", "a6"),
            },
        };

        [Test]
        public void Recipients_IncludesOwnAreaAndAdjacentShedFreeAreas()
        {
            var net = new SupplyLineNetwork(FixtureWorld());

            var recipients = net.Recipients("a2").ToList();

            CollectionAssert.AreEquivalent(new[] { "a2", "a1", "a3", "a4" }, recipients);
        }

        [Test]
        public void Recipients_ExcludesAnAdjacentAreaThatHasItsOwnShed()
        {
            var net = new SupplyLineNetwork(FixtureWorld());

            var recipients = net.Recipients("a2").ToList();

            CollectionAssert.DoesNotContain(recipients, "a5");
        }

        [Test]
        public void Recipients_ForANonShedArea_IsEmpty()
        {
            var net = new SupplyLineNetwork(FixtureWorld());

            Assert.IsEmpty(net.Recipients("a1"));
        }

        [Test]
        public void RouteToFront_ReturnsGateGraphPathFromShedToFront()
        {
            var net = new SupplyLineNetwork(FixtureWorld());

            List<string> route = net.RouteToFront("a2", "a6");

            CollectionAssert.AreEqual(new[] { "a2", "a4", "a6" }, route);
        }

        [Test]
        public void RouteToFront_DirectNeighbor_IsATwoAreaRoute()
        {
            var net = new SupplyLineNetwork(FixtureWorld());

            List<string> route = net.RouteToFront("a2", "a3");

            CollectionAssert.AreEqual(new[] { "a2", "a3" }, route);
        }

        [Test]
        public void DestroyShed_HaltsItsOwnStream_ButNotAnotherStandingShed()
        {
            var net = new SupplyLineNetwork(FixtureWorld());

            net.DestroyShed("a2_shed");

            Assert.IsFalse(net.IsSupplying("a2"));
            Assert.IsEmpty(net.Recipients("a2"));
            Assert.IsTrue(net.IsSupplying("a5"), "a5 never had its line halted");
        }

        [Test]
        public void DestroyShed_OnANonShedArea_IsANoOp()
        {
            var net = new SupplyLineNetwork(FixtureWorld());
            bool fired = false;
            net.SupplyLineHalted += _ => fired = true;

            net.DestroyShed("a1");

            Assert.IsFalse(fired);
        }

        [Test]
        public void DestroyShed_FiresSupplyLineHaltedOnceWithTheAreaId()
        {
            var net = new SupplyLineNetwork(FixtureWorld());
            var firedFor = new List<string>();
            net.SupplyLineHalted += area => firedFor.Add(area);

            net.DestroyShed("a2_shed");
            net.DestroyShed("a2_shed"); // idempotent — must not double-fire

            CollectionAssert.AreEqual(new[] { "a2" }, firedFor);
        }

        [Test]
        public void AllShedsDestroyed_FalseUntilEveryShedIsDown_TrueAfter()
        {
            var net = new SupplyLineNetwork(FixtureWorld());

            Assert.IsFalse(net.AllShedsDestroyed);

            net.DestroyShed("a2_shed");
            Assert.IsFalse(net.AllShedsDestroyed);

            net.DestroyShed("a5_shed");
            Assert.IsTrue(net.AllShedsDestroyed);
        }

        [Test]
        public void AllShedsDestroyed_FalseForAWorldWithNoSheds()
        {
            WorldConfig cfg = FixtureWorld();
            foreach (WorldArea a in cfg.areas) a.hasShed = false;
            var net = new SupplyLineNetwork(cfg);

            Assert.IsFalse(net.AllShedsDestroyed);
        }

        /// <summary>MV-475: an area can now carry several sheds (<see cref="WorldArea.sheds"/>), and
        /// its line must not halt until the LAST of them falls — the exact bug the old
        /// area-id-keyed <c>_destroyedSheds</c> could not even express, since one <c>DestroyShed</c>
        /// call used to take out the whole area in one shot.</summary>
        [Test]
        public void IsSupplying_StaysTrueUntilTheLastOfSeveralShedsInOneAreaFalls()
        {
            var cfg = new WorldConfig
            {
                world = "Test World",
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "a7", index = 7, role = "shed", hasShed = true,
                        sheds = new[] { new WorldShed(), new WorldShed() },
                    },
                },
            };
            var net = new SupplyLineNetwork(cfg);

            Assert.IsTrue(net.IsSupplying("a7"));

            net.DestroyShed("a7_shed1");
            Assert.IsTrue(net.IsSupplying("a7"), "one of two sheds down — the area's line must still be up");
            Assert.IsFalse(net.AllShedsDestroyed);

            net.DestroyShed("a7_shed2");
            Assert.IsFalse(net.IsSupplying("a7"), "both sheds down — the line must now be halted");
            Assert.IsTrue(net.AllShedsDestroyed);
        }
    }
}
