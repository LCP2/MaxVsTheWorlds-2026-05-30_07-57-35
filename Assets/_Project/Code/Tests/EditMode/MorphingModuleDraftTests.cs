using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// THE RIG 3/5 (MV-424) — the Morphing Module draft happens on the board itself, replacing the old
    /// card modal (<c>UpgradeScreen.OpenAbilityChoice</c>, deleted by this ticket). Built and driven the
    /// same way THE RIG's other EditMode suites do: <see cref="WeaponsScreen"/> constructs
    /// its canvas synchronously, so this needs no Play mode / coroutine.
    ///
    /// Per <c>CC_AUTONOMY.md</c>, this worker never authors PlayMode tests (Unity PlayMode in batch mode
    /// hangs indefinitely) — every scenario the ticket describes as a PlayMode test is proven here as an
    /// equivalent EditMode one instead, driving <see cref="WeaponsScreen"/> and <see cref="RigState"/>
    /// directly rather than through a live pickup/movement simulation.
    /// </summary>
    public sealed class MorphingModuleDraftTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        /// <summary>This suite is about the board's own draft MECHANICS (numbering, tap-to-grant, HUD
        /// signalling), not MV-457's shed/category-lock gate (<c>RigStateTests</c> owns that) — force
        /// every category open so every ability id this file exercises stays reached, exactly as it
        /// always was before MV-457. Called after every <c>RigState.Reset()</c> in this file, including
        /// the one inside a test body (MV-435's HUD-signal test re-resets per iteration).</summary>
        private static void UnlockAllCategories()
        {
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
        }

        [SetUp]
        public void SetUp()
        {
            RigState.Reset();
            PickupWallet.Reset();   // MV-457: also calls RigState.Reset() — the category unlock below must come AFTER this
            UnlockAllCategories();
            PendingMorphingModule.Reset();
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            RigState.Reset();
            PickupWallet.Reset();
            PendingMorphingModule.Reset();
            Time.timeScale = 1f;
        }

        // ---------------------------------------------------------------- 2-3 candidates: the board opens

        [Test]
        public void TwoOrMoreCandidatesOpensTheBoardPaused()
        {
            var candidates = new[] { "s_bal", "e_ff", "m_spd" };
            _screen.OpenMorphingModuleDraft(candidates);

            Assert.That(_screen.IsOpen, Is.True, "2-3 candidates must open THE RIG");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the fight must pause while a draft is pending");
        }

        [Test]
        public void CandidatesAreNumberedOneThroughNInDrawOrder()
        {
            var candidates = new[] { "m_tp", "u_sen", "e_cel" };
            _screen.OpenMorphingModuleDraft(candidates);

            for (int i = 0; i < candidates.Length; i++)
            {
                var node = _screen.BoardNode(candidates[i]);
                var badgeText = node.Find("Draft Badge").GetComponentInChildren<Text>();
                Assert.That(badgeText.text, Is.EqualTo((i + 1).ToString()));
            }
        }

        // ---------------------------------------------------------------- taking a candidate

        [Test]
        public void TappingACandidateGrantsItAtLevelOneAndLeavesTheOthersInThePool()
        {
            var candidates = new[] { "s_bal", "e_ff", "m_spd" };
            _screen.OpenMorphingModuleDraft(candidates);

            var takenNode = _screen.BoardNode("s_bal");
            takenNode.GetComponentInChildren<Button>().onClick.Invoke();

            Assert.That(RigState.Level("s_bal"), Is.EqualTo(1), "the taken candidate must be granted at level 1");
            Assert.That(RigState.IsOwned("e_ff"), Is.False, "an untaken candidate must remain unowned");
            Assert.That(RigState.IsOwned("m_spd"), Is.False, "an untaken candidate must remain unowned");

            var pool = new System.Collections.Generic.HashSet<string>(RigState.EligibleCapIds());
            Assert.That(pool, Does.Contain("e_ff"), "an untaken candidate must go back in the draft pool");
            Assert.That(pool, Does.Contain("m_spd"), "an untaken candidate must go back in the draft pool");

            // MV-521: taking a pick used to close the screen — now it stays open so the player actually
            // sees what just unlocked; only CLOSE/QUIT TO MENU dismiss it from here on.
            Assert.That(_screen.IsOpen, Is.True, "taking a candidate must NOT close the draft (MV-521)");
            Assert.That(_screen.IsDraftActive, Is.False, "the resolved draft itself must no longer be active");
        }

        /// <summary>MV-521, one test walking every AC in order (CC_AUTONOMY's own "at most one new test
        /// per ticket" — this is that one). Proven to fail on cdb-era main (before this ticket): AC1
        /// failed because <c>OnRigNodeTapped</c> called <c>Close()</c> on a pick, so <c>IsOpen</c> came
        /// back false instead of true.</summary>
        [Test]
        public void TakingADraftPickStaysOpenAndRevealsTheFamily_MV521()
        {
            // A fresh run (only the PRIMARY starting category unlocked) so there's a real locked family
            // for AC2 to reveal — this file's own SetUp unlocks every category, which exists for the
            // OTHER tests here that are about draft mechanics, not MV-457's category gate.
            RigState.Reset();
            PickupWallet.Reset();
            PendingMorphingModule.Reset();
            Time.timeScale = 0.4f;   // a distinct paused-FROM speed, so a stray Close() would be visible

            var locked = new System.Collections.Generic.List<string>(RigState.LockedCategoryIds());
            Assert.That(locked.Count, Is.GreaterThanOrEqualTo(2), "fixture: needs 2+ locked categories to draft between");
            string revealedCategory = locked[0];
            string leftBehindCategory = locked[1];

            // ---------------------------------------------------------------- AC1: the pick stays open
            _screen.OpenMorphingModuleDraft(new[] { revealedCategory, leftBehindCategory });
            _screen.BoardNode(revealedCategory).GetComponentInChildren<Button>().onClick.Invoke();

            Assert.That(_screen.IsOpen, Is.True, "AC1: taking a pick must not close THE RIG");
            Assert.That(_screen.ScreenRoot.activeSelf, Is.True, "AC1: the screen root itself must stay active");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "AC1: the game must stay at the paused value");
            Assert.That(_screen.IsDraftActive, Is.False, "AC1: the resolved draft must no longer be active");
            Assert.That(_screen.DraftCandidateIds, Is.Empty, "AC1: no leftover candidates once a draft resolves");
            Assert.That(RigState.IsCategoryUnlocked(revealedCategory), Is.True, "fixture: the taken category must be granted");
            Assert.That(RigState.IsCategoryUnlocked(leftBehindCategory), Is.False, "fixture: the untaken category must stay locked");
            Assert.That(_screen.IsCeremonyActive, Is.True, "fixture (feeds AC5): a reveal ceremony must start when a pick resolves");

            // ---------------------------------------------------------------- AC2: no longer family-locked
            // MV-538: the category's own panel border alpha now carries ONLY "lit" (RigState.IsCategoryUnlocked,
            // what this AC is about) — the separate "familyLit"/CategoryHasOwnedAbility dim MV-462 also
            // applied here was the bug MV-538 fixed (a freshly-unlocked, still-empty family must not
            // dim), so the border now renders at the plain RegionBorderAlphaLit constant, undimmed.
            Assert.That(_screen.CategoryPanelBorder(revealedCategory).color.a, Is.EqualTo(RigBoardLayout.RegionBorderAlphaLit).Within(0.0001f),
                "AC2: the unlocked category's own panel must render at full (lit, undimmed) strength, not the dark/family-locked reading");
            RigAbilityLayout rootAbility = null;
            foreach (var ab in RigBoardLayout.Abilities)
                if (ab.Category == revealedCategory && string.IsNullOrEmpty(ab.Parent)) { rootAbility = ab; break; }
            Assert.That(rootAbility, Is.Not.Null, "fixture: the revealed category must have a root ability");
            Assert.That(_screen.NodeLabel(rootAbility.Id).text, Is.Not.EqualTo("? ? ?"),
                "AC2: the family's own root ability must show its real name, not the family-locked placeholder");

            // ---------------------------------------------------------------- AC3: a second module chains in place
            UnlockAllCategories();   // this section is about the CHAIN mechanism, not the category-lock visuals above
            PendingMorphingModule.Set(new[] { "e_ff", "m_spd" });

            _screen.OpenMorphingModuleDraft(new[] { "s_bal", "u_sen" });
            _screen.BoardNode("s_bal").GetComponentInChildren<Button>().onClick.Invoke();

            Assert.That(_screen.IsOpen, Is.True, "AC3: chaining into the next module must not close the screen");
            Assert.That(_screen.IsDraftActive, Is.True, "AC3: the second module must arm a new draft in place");
            CollectionAssert.AreEquivalent(new[] { "e_ff", "m_spd" }, _screen.DraftCandidateIds,
                "AC3: the second module's own candidates must be the ones now loaded");

            _screen.BoardNode("e_ff").GetComponentInChildren<Button>().onClick.Invoke();
            Assert.That(_screen.IsOpen, Is.True, "AC3: taking the second pick must also leave the screen open");
            Assert.That(_screen.IsDraftActive, Is.False, "AC3: with nothing left pending, the draft itself must clear");

            // ---------------------------------------------------------------- AC4: no re-grant on a second tap
            int levelAfterGrant = RigState.Level("s_bal");
            int cellsAfterGrant = PickupWallet.PowerCells;
            _screen.BoardNode("s_bal").GetComponentInChildren<Button>().onClick.Invoke();   // 2nd tap, draft long over
            Assert.That(RigState.Level("s_bal"), Is.EqualTo(levelAfterGrant), "AC4: a second tap must not raise the level again");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(cellsAfterGrant), "AC4: a second tap must not touch the wallet either");

            // ---------------------------------------------------------------- AC5: the ceremony is gone once its own duration has passed
            // MV-605 supersedes MV-521's own ~0.6s reveal glow with a staged ~3s ceremony — this used to
            // assert nothing remained 1.5s later (trivially true for a 0.6s glow); now it asserts the
            // ceremony HAS ended once its own (longer) duration has elapsed, same idiom, new timescale.
            Assert.That(_screen.IsCeremonyActive, Is.True, "fixture: the AC3 picks above must have re-started a ceremony");
            _screen.ApplyCeremonyTiming(3.5f);
            Assert.That(_screen.IsCeremonyActive, Is.False, "AC5: no ceremony may remain active well after its own duration has elapsed");
        }

        // ---------------------------------------------------------------- 0 candidates: no screen; 1: grants AND reveals

        [Test]
        public void ZeroCandidatesNeverOpensTheScreen()
        {
            _screen.OpenMorphingModuleDraft(new string[0]);
            Assert.That(_screen.IsOpen, Is.False);
        }

        [Test]
        public void NullCandidatesNeverOpensTheScreen()
        {
            _screen.OpenMorphingModuleDraft(null);
            Assert.That(_screen.IsOpen, Is.False);
        }

        /// <summary>MV-595 AC4: a single candidate (now the ONLY shape a shed ever offers, since
        /// <c>CategoryDraftMaxCandidates</c> dropped to 1) used to grant silently with no screen at all
        /// — that stale pin lived here as <c>OneCandidateGrantsItDirectlyWithoutOpeningTheScreen</c> until
        /// this ticket. The reveal is the reward (MV-521's own phrase): a single candidate must still
        /// open THE RIG and play the same family-reveal a multi-candidate pick does once resolved.</summary>
        [Test]
        public void OneCandidateGrantsItAndOpensTheBoardWithTheReveal_MV595()
        {
            _screen.OpenMorphingModuleDraft(new[] { "e_cel" });

            Assert.That(RigState.Level("e_cel"), Is.EqualTo(1), "the sole candidate must be granted outright");
            Assert.That(_screen.IsOpen, Is.True, "MV-595: a single candidate must open THE RIG, never grant silently");
            Assert.That(_screen.IsCeremonyActive, Is.True, "MV-595: the newly granted family's reveal ceremony must play");
        }

        // ---------------------------------------------------------------- MV-605: the reveal ceremony

        /// <summary>MV-605, one test walking every AC in order (CC_AUTONOMY's own "at most one new test
        /// per ticket" — this is that one, same convention <c>TakingADraftPickStaysOpenAndRevealsTheFamily_MV521</c>
        /// already used for MV-521). Covers AC4-8 end to end: spend suppressed mid-ceremony but not
        /// before/after (AC4), a skipped ceremony and a naturally-timed-out one landing in the identical
        /// settled state, proven by direct comparison rather than just "it ended" (AC5), the pending
        /// queue clearing and never replaying (AC6), a MID-ceremony close neither replaying nor granting
        /// twice — pinned on a literal <c>RigState.Level(...) == 1</c>, the AC's own wording (AC7), and
        /// two banked modules both surviving and chaining ceremonies back to back in one visit (AC8).
        /// Proven to fail before this ticket: <c>PendingMorphingModule.PendingCount</c> and
        /// <c>WeaponsScreen.IsCeremonyActive</c>/<c>ApplyCeremonyTiming</c> did not exist on the pre-605
        /// tree (a compile failure, the most decisive proof available) — and even reasoning past that,
        /// the old <c>PendingMorphingModule.Set</c> OVERWROTE its single slot (AC7/8's own "must not
        /// silently drop one" defect) and the old grant path made a node interactable the instant
        /// <c>OpenMorphingModuleDraft</c>/<c>ResolveDraftPick</c> granted it, with no ceremony window at
        /// all to gate spend during (AC4's own defect).</summary>
        [Test]
        public void RevealCeremonyGatesSpendUntilSettleAndChainsTwoBankedModulesBackToBack_MV605()
        {
            RigState.Reset();
            PickupWallet.Reset();
            PendingMorphingModule.Reset();
            PickupWallet.SetPowerCells(50);   // comfortably affords every unlock this test touches

            var locked = new System.Collections.Generic.List<string>(RigState.LockedCategoryIds());
            Assert.That(locked.Count, Is.GreaterThanOrEqualTo(2), "fixture: needs 2+ locked categories to bank two modules against");
            string firstCategory = locked[0];
            string secondCategory = locked[1];

            RigAbilityLayout firstRoot = null, secondRoot = null;
            foreach (var ab in RigBoardLayout.Abilities)
            {
                if (ab.Category == firstCategory && string.IsNullOrEmpty(ab.Parent)) firstRoot = ab;
                if (ab.Category == secondCategory && string.IsNullOrEmpty(ab.Parent)) secondRoot = ab;
            }
            Assert.That(firstRoot, Is.Not.Null, "fixture: the first category needs a root ability");
            Assert.That(secondRoot, Is.Not.Null, "fixture: the second category needs a root ability");

            // ---------------------------------------------------------------- AC7/8: two banked modules must both survive
            PendingMorphingModule.Set(new[] { firstCategory });
            PendingMorphingModule.Set(new[] { secondCategory });
            Assert.That(PendingMorphingModule.PendingCount, Is.EqualTo(2), "AC7: a second banked module must not silently overwrite the first");

            _screen.Open();   // consumes the OLDEST banked draw and starts its ceremony

            Assert.That(_screen.IsOpen, Is.True);
            Assert.That(_screen.IsCeremonyActive, Is.True, "AC4: opening with a module pending must start a ceremony");
            Assert.That(RigState.IsCategoryUnlocked(firstCategory), Is.True, "the family is still granted immediately, same as MV-521");

            // ---------------------------------------------------------------- AC4: granted != spendable while it plays
            Assert.That(WeaponsScreen.IsAbilityNodeSpendable(firstRoot.Id, PickupWallet.PowerCells), Is.True,
                "fixture: the pure affordability predicate alone must already say yes — proves the gate below is the ceremony, not cost");
            var firstButton = _screen.BoardNode(firstRoot.Id).GetComponentInChildren<Button>();
            Assert.That(firstButton.interactable, Is.False, "AC4: the family must not be spendable until the ceremony settles");

            _screen.ApplyCeremonyTiming(0.5f);
            Assert.That(_screen.IsCeremonyActive, Is.True, "AC4: sampled mid-ceremony (0.5s), it must still be playing");
            Assert.That(firstButton.interactable, Is.False, "AC4: still not spendable mid-ceremony");

            // ---------------------------------------------------------------- AC5: skip lands in the SAME settled state a natural finish would
            Button skipCatcher = null;
            foreach (var b in _screen.ScreenRoot.GetComponentsInChildren<Button>(true))
                if (b.gameObject.name == "Ceremony Skip Catcher") { skipCatcher = b; break; }
            Assert.That(skipCatcher, Is.Not.Null, "fixture: the ceremony must build its own skip catcher");
            skipCatcher.onClick.Invoke();

            // AC5's own settled state, proven directly on the family the skip just resolved — NOT
            // IsCeremonyActive itself, which AC7/8 immediately flips true again by chaining straight into
            // the second banked module's own ceremony (see below); skip therefore lands in the same
            // settled state a natural finish would (spendable), it just doesn't imply the SCREEN goes idle
            // when something else is still queued.
            Assert.That(firstButton.interactable, Is.True, "AC5: skipping must reach the exact same settled (spendable) state a natural finish would");

            // ---------------------------------------------------------------- AC7/8: the second banked module chains in, back to back
            Assert.That(PendingMorphingModule.HasPending, Is.False, "AC6: consumed once taken — nothing left banked");
            Assert.That(_screen.IsCeremonyActive, Is.True, "AC7/8: the second module's own ceremony must start immediately, in the same rig visit");
            Assert.That(RigState.IsCategoryUnlocked(secondCategory), Is.True, "AC8: the second family must also end up unlocked");
            var secondButton = _screen.BoardNode(secondRoot.Id).GetComponentInChildren<Button>();
            Assert.That(secondButton.interactable, Is.False, "AC4 again: the SECOND family must not be spendable until ITS ceremony settles either");

            skipCatcher.onClick.Invoke();   // same catcher GameObject, reused for the second ceremony

            Assert.That(_screen.IsCeremonyActive, Is.False, "AC6: with nothing left pending, no further ceremony should start");
            Assert.That(secondButton.interactable, Is.True, "AC8: both families must end up spendable, not just unlocked");

            // ---------------------------------------------------------------- AC6: reopening plays no ceremony
            _screen.Close();
            _screen.Open();
            Assert.That(_screen.IsCeremonyActive, Is.False, "AC6: reopening THE RIG with nothing pending must not replay a ceremony");

            // ---------------------------------------------------------------- AC5: a natural finish and a skipped finish reach the IDENTICAL settled state
            // Run 1: the SAME category as above (locked again after RigState.Reset), let out to its own
            // full CeremonyDuration naturally instead of skipped.
            RigState.Reset();
            PendingMorphingModule.Reset();
            PendingMorphingModule.Set(new[] { firstCategory });
            _screen.Close();
            _screen.Open();
            _screen.ApplyCeremonyTiming(10f);   // well past CeremonyDuration — the timeout branch itself ends it
            bool naturalCeremonyActive = _screen.IsCeremonyActive;
            bool naturalSpendable = _screen.BoardNode(firstRoot.Id).GetComponentInChildren<Button>().interactable;
            _screen.Close();

            // Run 2: an identical starting state, resolved by a tap instead of waiting it out.
            RigState.Reset();
            PendingMorphingModule.Reset();
            PendingMorphingModule.Set(new[] { firstCategory });
            _screen.Open();
            skipCatcher.onClick.Invoke();
            bool skippedCeremonyActive = _screen.IsCeremonyActive;
            bool skippedSpendable = _screen.BoardNode(firstRoot.Id).GetComponentInChildren<Button>().interactable;

            Assert.That(skippedSpendable, Is.True, "fixture: the skip run must actually reach the settled (spendable) state, not just agree while broken");
            Assert.That(skippedCeremonyActive, Is.EqualTo(naturalCeremonyActive), "AC5: skip must leave IsCeremonyActive in EXACTLY the state a natural finish would");
            Assert.That(skippedSpendable, Is.EqualTo(naturalSpendable), "AC5: skip must leave the family exactly as spendable as a natural finish would — the two end states must be equal, not just both 'ended'");

            // ---------------------------------------------------------------- AC7: closing MID-ceremony must not replay it or grant the family twice
            // An ability-id candidate (not a category id) so there's a literal Level() to pin at exactly
            // 1, matching the AC's own wording — routes through ResolveDraftPick, the ceremony's other
            // call site (StartCeremony is shared by both, see WeaponsScreen.cs).
            RigState.Reset();
            PickupWallet.Reset();
            PendingMorphingModule.Reset();
            UnlockAllCategories();   // this scenario is about ceremony/close mechanics, not the category-lock gate — same convention this file's own SetUp uses
            _screen.Close();

            _screen.OpenMorphingModuleDraft(new[] { "u_sen", "m_spd" });
            _screen.BoardNode("u_sen").GetComponentInChildren<Button>().onClick.Invoke();

            Assert.That(_screen.IsCeremonyActive, Is.True, "fixture: needs a ceremony actually playing to close mid-flight");
            Assert.That(RigState.Level("u_sen"), Is.EqualTo(1), "fixture: the family is still granted immediately, same as before this ticket");

            _screen.Close();   // MID-ceremony close
            Assert.That(RigState.Level("u_sen"), Is.EqualTo(1), "AC7: closing mid-ceremony must not grant the family a second time");

            _screen.Open();   // nothing pending — an ordinary reopen
            Assert.That(_screen.IsCeremonyActive, Is.False, "AC7: closing mid-ceremony must not replay it on the next open");
            Assert.That(RigState.Level("u_sen"), Is.EqualTo(1), "AC7: reopening must not grant the family a second time either");
        }

        // ---------------------------------------------------------------- MV-425: 2-3 candidates wait, not force-open

        [Test]
        public void TwoOrMoreCandidatesNoLongerOpenTheScreenAtDrawTime_TheyBankInstead()
        {
            // PickupDirector (not exercised here — no live pickup) now calls PendingMorphingModule.Set
            // for 2-3 candidates instead of OpenMorphingModuleDraft directly. This pins the class the
            // pool actually lands in when drawn, independent of any live pickup/scene wiring.
            PendingMorphingModule.Set(new[] { "s_bal", "e_ff", "m_spd" });

            Assert.That(_screen.IsOpen, Is.False, "a banked draft must not force the board open");
            Assert.That(Time.timeScale, Is.EqualTo(1f), "a banked draft must not pause the fight");
            Assert.That(PendingMorphingModule.HasPending, Is.True);
        }

        [Test]
        public void OpeningWeaponsWithAPendingDraftShowsItInstead_AndClearsThePending()
        {
            var candidates = new[] { "s_bal", "e_ff", "m_spd" };
            PendingMorphingModule.Set(candidates);

            _screen.Open();

            Assert.That(_screen.IsOpen, Is.True, "opening WEAPONS with a draft waiting must show it");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the fight must pause once the draft is actually shown");
            Assert.That(PendingMorphingModule.HasPending, Is.False, "Open() must consume the pending draft");

            foreach (var id in candidates)
            {
                var badge = _screen.BoardNode(id).Find("Draft Badge");
                Assert.That(badge.gameObject.activeSelf, Is.True, $"'{id}' must show as a draft candidate");
            }
        }

        // ---------------------------------------------------------------- MV-435: the grant reaches the HUD

        /// <summary>The root cause: both draft grant paths used to call <c>RigState.AcquireCap</c>
        /// directly, which never raises <see cref="WeaponSystemState.Changed"/> — the only signal
        /// <see cref="HudController"/> listens for to reveal an ability's control. Fixed by routing
        /// both through <see cref="WeaponSystemState.AcquireById"/>.</summary>
        [Test]
        public void TappingACandidateOnTheBoardRaisesWeaponSystemStateChanged_ForEveryHudBearingAbility()
        {
            foreach (string id in new[] { "m_spd", "m_tp", "s_bal", "e_ff", "u_sen" })
            {
                RigState.Reset();
                UnlockAllCategories();
                _screen.OpenMorphingModuleDraft(new[] { id, "e_cel" }); // 2 candidates -> opens the board

                int fired = 0;
                System.Action handler = () => fired++;
                WeaponSystemState.Changed += handler;
                try
                {
                    var node = _screen.BoardNode(id);
                    node.GetComponentInChildren<Button>().onClick.Invoke();
                    Assert.That(fired, Is.EqualTo(1), $"taking '{id}' on the board must fire WeaponSystemState.Changed exactly once");
                }
                finally
                {
                    WeaponSystemState.Changed -= handler;
                }
            }
        }

        [Test]
        public void TheOneCandidateAutoGrantAlsoRaisesWeaponSystemStateChanged()
        {
            int fired = 0;
            System.Action handler = () => fired++;
            WeaponSystemState.Changed += handler;
            try
            {
                _screen.OpenMorphingModuleDraft(new[] { "e_ff" });
                Assert.That(fired, Is.EqualTo(1), "the 1-candidate auto-grant path must also fire Changed (MV-435)");
            }
            finally
            {
                WeaponSystemState.Changed -= handler;
            }
        }

        [Test]
        public void ADraftedAbilityLandsInAcquiredInDraftOrder()
        {
            _screen.OpenMorphingModuleDraft(new[] { "u_sen", "m_spd" });
            _screen.BoardNode("u_sen").GetComponentInChildren<Button>().onClick.Invoke();

            CollectionAssert.Contains(new System.Collections.Generic.List<AbilityKind>(WeaponSystemState.Acquired), AbilityKind.Sentinels);
        }

        // ---------------------------------------------------------------- the old card modal is gone

        [Test]
        public void UpgradeScreenNoLongerHasAnAbilityChoiceMethod()
        {
            var method = typeof(UpgradeScreen).GetMethod("OpenAbilityChoice",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.That(method, Is.Null, "UpgradeScreen.OpenAbilityChoice's 340x440 card layout must be deleted");
        }
    }
}
