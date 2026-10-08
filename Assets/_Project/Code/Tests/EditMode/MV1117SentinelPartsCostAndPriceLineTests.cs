using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1117 (Lee, 6 Oct 2026 device playtest): "I can't add sentinels because I have magneto on and
    /// every time I get a power cell it immediately gets used." A Sentinel deploy spent the scarce
    /// Power Cells secondary bank (MV-673) — the same bank Magneto auto-drains into the Shoulder
    /// Rack/Balloon the instant a cell lands, leaving a deploy permanently unaffordable with Magneto
    /// on. Fix: the deploy currency moves onto Parts (<see cref="PickupWallet.PowerCells"/>) and its
    /// base price rises 5 -&gt; 20 (<see cref="AbilityTuning.DefaultSentinelCost"/>). Lee also asked for
    /// the SLOTS pill ("x/y sentinels") on THE RIG's <c>u_slt</c> node — "the add circle" — to be drawn
    /// much bigger, with a second line showing the parts price.
    ///
    /// One test (MV-465 Rule 1), carrying every sub-check as a real entry-point exercise:
    ///  - 20 parts, 0 orange cells, a free slot: the deploy succeeds and the parts wallet reads 0;
    ///  - 19 parts, 50 orange cells: the deploy is refused (parts insufficient), and the REAL
    ///    WeaponsScreen's u_slt price line resolves to the unaffordable/red colour;
    ///  - 20 parts affording the 20-part price resolves the price line to the parts colour;
    ///  - at Cost level 4 the price is 8;
    ///  - from resolved rects after a real WeaponsScreen.Open()/layout: the slots pill text's resolved
    ///    font size is at least 2.0x a sibling node's own pill font size and at least 28px; the price
    ///    line is present, at least 22px, and neither resized text's preferred size overflows its own
    ///    box.
    ///
    /// Proven to fail on base commit 7584a99 (main HEAD immediately before this ticket) — quoted in the
    /// MV-1117 fix comment.
    /// </summary>
    public sealed class MV1117SentinelPartsCostAndPriceLineTests
    {
        private static readonly Color PartsColor = new Color(0.35f, 0.85f, 0.95f);
        private static readonly Color UnaffordableColor = new Color(0.85f, 0.20f, 0.20f);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();   // also resets RigState
            WeaponSystemState.Reset();
            Sentinel.DestroyAllActive();
            Sentinel.ResetRegistry();
        }

        [Test]
        public void SentinelDeployCostsPartsAndUSlotsPillShowsTheEnlargedCountAndPrice()
        {
            WeaponSystemState.Acquire(AbilityKind.Sentinels);

            // ---------------------------------------------------------------- shared RIG setup: reach u_slt (for the
            // pill/price layout checks below) off a real cells-path, the same chain
            // MV623SentinelEconomyTests/MV654SlotsPillDeployableSentinelsTests already use.
            RigState.UnlockCategory(RigBoard.Category("u_sen"));
            PickupWallet.SetPowerCells(CellSpend.UnlockCostFor("u_sen"));
            Assert.That(CellSpend.TryUnlockNode("u_sen"), Is.True, "setup: u_sen must unlock via cells");
            PickupWallet.SetPowerCells(CellSpend.UnlockCostFor("u_hp"));
            Assert.That(CellSpend.TryUnlockNode("u_hp"), Is.True, "setup: u_hp must unlock via cells");
            PickupWallet.SetPowerCells(CellSpend.UpgradeCostFor("u_hp", RigState.Level("u_hp")));
            Assert.That(CellSpend.TryUpgradeNode("u_hp"), Is.True, "setup: u_hp must reach level 2 via cells");
            Assert.That(RigState.IsCellUnlockable("u_slt"), Is.True, "setup: u_slt must now be draftable");

            var maxGo = new GameObject("Max");
            var abilities = maxGo.AddComponent<PlayerAbilities>();
            GameObject screenGo = null;
            try
            {
                // ---------------------------------------------------------------- 20 parts, 0 orange cells, free slot: deploys
                PickupWallet.SetPowerCells(20);
                PickupWallet.SetPowerCellSecondary(0);
                Assert.That(abilities.TryDeploySentinel(new Vector3(5f, 0f, 0f)), Is.True,
                    "20 parts must afford the 20-part deploy with no orange cells involved");
                Assert.That(PickupWallet.PowerCells, Is.EqualTo(0), "the deploy must spend exactly 20 parts");

                // ---------------------------------------------------------------- 19 parts, 50 orange cells: refused
                Sentinel.DestroyAllActive();   // free the slot back up so only affordability is under test
                PickupWallet.SetPowerCells(19);
                PickupWallet.SetPowerCellSecondary(50);
                Assert.That(abilities.TryDeploySentinel(new Vector3(20f, 0f, 0f)), Is.False,
                    "19 parts must not afford the deploy even with 50 orange cells banked");
                Assert.That(PickupWallet.PowerCells, Is.EqualTo(19), "a refused deploy must not spend parts");
                Assert.That(Sentinel.Active.Count, Is.EqualTo(0));

                // ---------------------------------------------------------------- WeaponsScreen: layout + the price line read
                screenGo = new GameObject("WeaponsScreen MV1117");
                var screen = screenGo.AddComponent<WeaponsScreen>();
                screen.Open();

                Text slotsPill = screen.NodePillText("u_slt");
                Text pricePill = screen.NodeSentinelPriceText("u_slt");
                Image priceIcon = screen.NodeSentinelPriceIcon("u_slt");
                Assert.That(slotsPill, Is.Not.Null, "u_slt built no pill-text component");
                Assert.That(pricePill, Is.Not.Null, "u_slt built no price-text component");
                Assert.That(priceIcon, Is.Not.Null, "u_slt built no price-icon component");

                Assert.That(pricePill.gameObject.activeInHierarchy, Is.True, "the price line must be visible");
                Assert.That(pricePill.text, Is.EqualTo(PlayerAbilities.SentinelCost.ToString()),
                    "the price line must read the current SentinelCost");
                Assert.That(pricePill.fontSize, Is.GreaterThanOrEqualTo(22),
                    "the price line must never resolve smaller than 22px at the reference resolution");
                Assert.That(pricePill.color, Is.EqualTo(UnaffordableColor),
                    "19 parts must not afford 20 -- the price line must read the unaffordable/red colour");
                Assert.That(priceIcon.color, Is.EqualTo(UnaffordableColor));

                // ---------------------------------------------------------------- slots pill: >= 2x a sibling node's, >= 28px
                Text siblingPill = screen.NodePillText("p_dmg");
                Assert.That(siblingPill, Is.Not.Null, "fixture: p_dmg built no pill-text component");
                Assert.That(slotsPill.fontSize, Is.GreaterThanOrEqualTo(siblingPill.fontSize * 2),
                    "u_slt's own slots text must resolve at least 2.0x a sibling node's pill font size");
                Assert.That(slotsPill.fontSize, Is.GreaterThanOrEqualTo(28),
                    "u_slt's own slots text must never resolve smaller than 28px at the reference resolution");

                // ---------------------------------------------------------------- neither resized line overflows its own box
                AssertNoOverflow(slotsPill, "slots pill");
                AssertNoOverflow(pricePill, "price line");

                // ---------------------------------------------------------------- 20 parts affording 20 reads the parts colour
                PickupWallet.SetPowerCells(20);
                screen.Close();
                screen.Open();   // force a fresh Refresh() -- PickupWallet.Changed isn't reliably pumped outside Play mode
                Assert.That(screen.NodeSentinelPriceText("u_slt").color, Is.EqualTo(PartsColor),
                    "20 parts affording the 20-part price must read the parts colour, not red");

                screen.Close();
                Object.DestroyImmediate(screenGo);
                screenGo = null;

                // ---------------------------------------------------------------- Cost level 4: price is 8
                Assert.That(RigState.AcquireCap("u_rng"), Is.True, "setup: u_rng must become owned (u_cst's own parent)");
                Assert.That(RigState.AcquireCap("u_cst"), Is.True, "setup: u_cst must reach level 1");
                Assert.That(RigState.RaiseLevel("u_cst"), Is.True); // level 2
                Assert.That(RigState.RaiseLevel("u_cst"), Is.True); // level 3
                Assert.That(RigState.RaiseLevel("u_cst"), Is.True); // level 4
                Assert.That(RigState.Level("u_cst"), Is.EqualTo(4));
                Assert.That(PlayerAbilities.SentinelCost, Is.EqualTo(8), "MV-1117: Cost level 4 must price a deploy at 8 parts");
            }
            finally
            {
                if (screenGo != null) Object.DestroyImmediate(screenGo);
                Sentinel.DestroyAllActive();
                Object.DestroyImmediate(maxGo);
            }
        }

        private static void AssertNoOverflow(Text text, string label)
        {
            Assert.That(text.rectTransform.rect.width, Is.GreaterThanOrEqualTo(text.preferredWidth - 0.5f),
                $"{label}: box width ({text.rectTransform.rect.width:0.0}px) is narrower than its own text needs ({text.preferredWidth:0.0}px)");
            Assert.That(text.rectTransform.rect.height, Is.GreaterThanOrEqualTo(text.preferredHeight - 0.5f),
                $"{label}: box height ({text.rectTransform.rect.height:0.0}px) is shorter than its own text needs ({text.preferredHeight:0.0}px)");
        }
    }
}
