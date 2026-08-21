using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-520 — before this ticket, <c>WeaponsScreen.RefreshAbilityNode</c> only ever showed a node's
    /// cost tag when <c>hasCellCost</c> (draftable, or owned-and-below-max) was true — a node gated by
    /// family lock or the parent level &gt;= 2 rule fell to the "LOCK"/"? ? ?" branch and showed no
    /// price at all. In Lee's own fresh-run screenshot (only PRIMARY lit, DAMAGE/p_dmg at level 1) every
    /// other node hit exactly this branch, which read as "cost was never implemented." Sole guard on
    /// this defect; do not cull. Testing policy (MV-465): one new test, proven to fail on base commit
    /// 6846edb6c7864319fc752bf968fe956304d597b7 (main HEAD before this ticket) — that commit has
    /// neither <see cref="CellSpend.PotentialCellCost"/> nor <see cref="WeaponsScreen.NodeCostText"/>/
    /// <see cref="WeaponsScreen.NodeCostIcon"/>/<see cref="WeaponsScreen.NodeLabel"/>, so this file
    /// fails to even compile there.
    /// </summary>
    public sealed class MV520RigCostAlwaysVisibleTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
        }

        [Test]
        public void EveryNodeShowsItsPrice_WithDistinctGlyphs_UndimmedAndNeverBesideAHiddenName()
        {
            // ---------------------------------------------------------------- fixture: Lee's own screenshot state
            PickupWallet.SetPowerCells(4);
            Assert.That(RigState.IsOwned("p_dmg"), Is.True, "fixture: p_dmg (DAMAGE) is owned at run start");
            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(1), "fixture: DAMAGE starts at level 1");
            Assert.That(RigState.IsCategoryUnlocked("PRIMARY"), Is.True, "fixture: PRIMARY is the only unlocked category at run start");
            _screen.Open();

            // ---------------------------------------------------------------- AC1: every node has an active, non-empty cost text
            foreach (string id in RigBoard.AllIds)
            {
                var text = _screen.NodeCostText(id);
                Assert.That(text, Is.Not.Null, $"'{id}' built no cost-text component");
                Assert.That(text.gameObject.activeInHierarchy, Is.True,
                    $"'{id}' cost text is inactive in the fresh-run state — this is the exact defect Lee's screenshot showed");
                Assert.That(text.text, Is.Not.Empty, $"'{id}' cost text is active but empty");
            }

            // ---------------------------------------------------------------- AC2: unlock vs upgrade glyph + escalating cost
            // p_rng: unowned, PRIMARY already open but its parent p_dmg is only level 1 (not >= 2) —
            // parent-gated, so still unowned and must show the flat unlock price with the unlock glyph.
            Assert.That(RigState.IsOwned("p_rng"), Is.False, "fixture: p_rng is not yet owned");
            Assert.That(_screen.NodeCostText("p_rng").text, Is.EqualTo(CellSpend.UnlockCostCells.ToString()),
                "an unowned node's cost text must read the flat unlock price regardless of the parent-level gate");
            var unlockGlyph = _screen.NodeCostIcon("p_rng").sprite;

            // p_dmg at level 1 (owned): its own next upgrade costs UpgradeCostFor(1) = 5.
            Assert.That(_screen.NodeCostText("p_dmg").text, Is.EqualTo(CellSpend.UpgradeCostFor(1).ToString()),
                "an owned level-1 node's cost text must read its own upgrade price");
            var upgradeGlyph = _screen.NodeCostIcon("p_dmg").sprite;

            Assert.That(unlockGlyph, Is.Not.EqualTo(upgradeGlyph),
                "the unlock glyph and the upgrade glyph must be two distinct sprites, distinguishable without reading the number");

            RigState.RaiseLevel("p_dmg");
            RigState.RaiseLevel("p_dmg"); // p_dmg model-layer raised to level 3, no currency spent
            _screen.Close();
            _screen.Open(); // force a fresh Refresh() — RigState.Changed isn't reliably pumped outside Play mode
            Assert.That(_screen.NodeCostText("p_dmg").text, Is.EqualTo(CellSpend.UpgradeCostFor(3).ToString()),
                "an owned level-3 node's cost text must read UpgradeCostFor(3) = 15");
            Assert.That(_screen.NodeCostIcon("p_dmg").sprite, Is.EqualTo(upgradeGlyph),
                "an owned node keeps the same upgrade glyph at every level below max");

            // ---------------------------------------------------------------- AC3: the cost tag ignores the family dim
            // s_bal sits in SECONDARY, still locked at this point — its family is unlit.
            Assert.That(RigState.IsCategoryUnlocked("SECONDARY"), Is.False, "fixture: SECONDARY is still locked");
            float litAlpha = _screen.NodeCostIcon("p_dmg").color.a;
            float unlitAlpha = _screen.NodeCostIcon("s_bal").color.a;
            Assert.That(unlitAlpha, Is.GreaterThanOrEqualTo(litAlpha * 0.9f),
                $"an unlit family's cost-tag alpha ({unlitAlpha}) fell under 90% of a lit family's ({litAlpha}) — it is still being hit by the 0.39 family dim");

            // ---------------------------------------------------------------- AC4: "? ? ?" never sits beside a spendable cost
            int cellsBanked = PickupWallet.PowerCells;
            foreach (string id in RigBoard.AllIds)
            {
                var label = _screen.NodeLabel(id);
                if (label != null && label.text == "? ? ?")
                {
                    Assert.That(WeaponsScreen.IsAbilityNodeSpendable(id, cellsBanked), Is.False,
                        $"'{id}' shows the hidden-name placeholder while also being spendable — the player has nothing to act on");
                }
            }
        }
    }
}
