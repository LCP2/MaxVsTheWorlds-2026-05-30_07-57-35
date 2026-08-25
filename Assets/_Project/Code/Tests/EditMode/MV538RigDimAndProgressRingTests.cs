using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-538 — two defects Lee reported: (1) a newly shed-unlocked family stayed dimmed at
    /// <c>RigBoardLayout.FamilyDimFactor</c> until something in it was OWNED, even though its root
    /// nodes were already unlockable and affordable ("I can't see that I can do that"); (2) the
    /// per-node progress ring had no visible empty track, so a partial arc read as a stray mark, not
    /// a meter. Root cause confirmed in code: every <c>WeaponsScreen.DimIfUnlit</c> call site was
    /// keyed off <c>CategoryHasOwnedAbility</c> (has >=1 owned ability) instead of
    /// <c>RigState.IsCategoryUnlocked</c> (has a shed unlocked it at all) — fixed by rewiring every
    /// call site (category node, ability node, connectors, the per-frame pulse) to the unlock flag.
    /// Testing policy (MV-465): one new test, proven to fail on base commit
    /// 4e7786c4222cab539ffa5b829c458428c579b05c (main HEAD before this ticket) — that commit has
    /// none of <see cref="WeaponsScreen.NodeHexFill"/>, <see cref="WeaponsScreen.NodeButton"/>,
    /// <see cref="WeaponsScreen.NodeProgressRing"/> or <see cref="WeaponsScreen.NodeProgressTrack"/>,
    /// so this file fails to even compile there.
    /// </summary>
    public sealed class MV538RigDimAndProgressRingTests
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
        public void FreshlyUnlockedEmptyFamilyReadsFullStrengthAndTheRingCarriesAVisibleTrack()
        {
            // ---------------------------------------------------------------- fixture
            // SECONDARY: shed-unlocked this run, nothing owned in it yet — the exact defect state.
            RigState.UnlockCategory("SECONDARY");
            // PRIMARY: p_dmg owned at run start (RigState.Reset's own baseline); raised to level 4 so
            // (a) p_rng (its child) clears the parent->=2 cell-unlock gate and becomes draftable, and
            // (b) p_dmg's own next upgrade costs UpgradeCostFor(4) = 20, used by the AC6 section below.
            RigState.RaiseLevel("p_dmg");
            RigState.RaiseLevel("p_dmg");
            RigState.RaiseLevel("p_dmg");
            PickupWallet.SetPowerCells(10); // == UnlockCostCells: affords s_bal's and p_rng's unlock

            Assert.That(RigState.IsCategoryUnlocked("SECONDARY"), Is.True, "fixture: SECONDARY is shed-unlocked");
            Assert.That(RigState.IsOwned("s_bal"), Is.False, "fixture: SECONDARY has nothing owned yet");
            Assert.That(RigState.IsCellUnlockable("s_bal"), Is.True, "fixture: s_bal (root) is draftable once its category is unlocked");
            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(4), "fixture: p_dmg raised to level 4");
            Assert.That(RigState.IsCellUnlockable("p_rng"), Is.True, "fixture: p_rng draftable once its parent p_dmg clears level 2");
            Assert.That(RigState.IsCategoryUnlocked("MOVE"), Is.False, "fixture: MOVE stays locked all run");

            _screen.Open();
            int cellsBanked = PickupWallet.PowerCells;

            // ---------------------------------------------------------------- AC1 + AC3: a freshly-unlocked,
            // still-empty family's draftable root (s_bal) must render exactly like an affordable
            // draftable node in an already-owned family (p_rng, PRIMARY) — same hex alpha, same
            // interactable state. This is the exact defect: before the fix, s_bal rendered at
            // FamilyDimFactor (~0.39x) because CategoryHasOwnedAbility("SECONDARY") was still false.
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("s_bal", cellsBanked), Is.True, "fixture: s_bal must be spendable (10 cells, cost 10)");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_rng", cellsBanked), Is.True, "fixture: p_rng must be spendable (10 cells, cost 10)");

            float freshFamilyAlpha = _screen.NodeHexFill("s_bal").color.a;
            float ownedFamilyAlpha = _screen.NodeHexFill("p_rng").color.a;
            Assert.That(freshFamilyAlpha, Is.EqualTo(ownedFamilyAlpha).Within(1e-4f),
                $"AC1: s_bal (fresh, empty SECONDARY, alpha={freshFamilyAlpha}) must render at the same hex alpha as p_rng (owned PRIMARY, alpha={ownedFamilyAlpha})");

            Assert.That(_screen.NodeButton("s_bal").interactable, Is.True, "AC3: s_bal must be tappable — it is affordable");
            Assert.That(_screen.NodeButton("s_bal").interactable, Is.EqualTo(_screen.NodeButton("p_rng").interactable),
                "AC3: an affordable node in a fresh-empty family must be exactly as interactable as one in an owned family");

            // ---------------------------------------------------------------- AC2: a genuinely LOCKED
            // family (MOVE, never shed-unlocked) must still read dimmer than both unlocked families
            // above — the fix must not brighten a family that is actually still locked.
            float lockedCategoryAlpha = _screen.CategoryPanel("MOVE").color.a;
            float freshCategoryAlpha = _screen.CategoryPanel("SECONDARY").color.a;
            float ownedCategoryAlpha = _screen.CategoryPanel("PRIMARY").color.a;
            Assert.That(lockedCategoryAlpha, Is.LessThan(freshCategoryAlpha),
                "AC2: MOVE (still locked) must read dimmer than SECONDARY (unlocked, empty)");
            Assert.That(freshCategoryAlpha, Is.EqualTo(ownedCategoryAlpha).Within(1e-4f),
                "AC1: SECONDARY (unlocked, empty) and PRIMARY (unlocked, owned) must read the category panel at the same alpha");

            // ---------------------------------------------------------------- AC4: the progress ring's
            // own empty track — active whenever the fill is, non-zero alpha, so a partial arc always
            // reads as "part of a whole."
            var ring = _screen.NodeProgressRing("s_bal");
            var track = _screen.NodeProgressTrack("s_bal");
            Assert.That(ring, Is.Not.Null, "s_bal must have built a progress ring");
            Assert.That(ring.gameObject.activeSelf, Is.True, "s_bal's progress ring must be active — it has a live cell action");
            Assert.That(track, Is.Not.Null, "s_bal must have built a progress track (AC4)");
            Assert.That(track.gameObject.activeSelf, Is.True, "AC4: the track must be active whenever the fill is active");
            Assert.That(track.color.a, Is.GreaterThan(0f), "AC4: the track must be visible (non-zero alpha), not a decorative no-op");

            // ---------------------------------------------------------------- AC5: the ring and the
            // number can never disagree — fillAmount is exactly CellCostProgress01, for a full ring
            // (s_bal, 10/10) and a partial one (p_dmg, an owned node mid-upgrade, 10/20).
            Assert.That(ring.fillAmount, Is.EqualTo(CellSpend.CellCostProgress01("s_bal", cellsBanked)).Within(1e-4f),
                "AC5: s_bal's ring fillAmount must equal CellCostProgress01 exactly");
            var p_dmgRing = _screen.NodeProgressRing("p_dmg");
            Assert.That(p_dmgRing, Is.Not.Null, "p_dmg must have an active progress ring — owned, below max level");
            Assert.That(p_dmgRing.fillAmount, Is.EqualTo(CellSpend.CellCostProgress01("p_dmg", cellsBanked)).Within(1e-4f),
                "AC5: p_dmg's ring fillAmount must equal CellCostProgress01 exactly, mid-progress");
            Assert.That(p_dmgRing.fillAmount, Is.EqualTo(0.5f).Within(1e-4f),
                "fixture cross-check: 10 cells banked / UpgradeCostFor(4)=20 must resolve to a genuinely partial 0.5 fill");

            // ---------------------------------------------------------------- AC6: "ready" is a
            // discontinuity, not a fuller reading — at fillAmount 1.0 the node is actionable
            // (spendable/interactable); one cell short (0.95, still < 1.0) it is not. p_dmg's own
            // upgrade cost at level 4 is UpgradeCostFor(4) = 20, so 19/20 = 0.95 exactly.
            Assert.That(CellSpend.CellCostProgress01("p_dmg", 19), Is.EqualTo(0.95f).Within(1e-4f), "fixture: 19/20 = 0.95");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_dmg", 19), Is.False, "AC6: at fillAmount 0.95 (one cell short) the node must NOT be actionable");
            Assert.That(CellSpend.CellCostProgress01("p_dmg", 20), Is.EqualTo(1.0f).Within(1e-4f), "fixture: 20/20 = 1.0");
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable("p_dmg", 20), Is.True, "AC6: at fillAmount 1.0 (cost exactly met) the node must be actionable");
        }
    }
}
