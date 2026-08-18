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
    /// same way <see cref="RigBoardLayoutTests"/> already does: <see cref="WeaponsScreen"/> constructs
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

        [SetUp]
        public void SetUp()
        {
            RigState.Reset();
            PickupWallet.Reset();
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            RigState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
        }

        // ---------------------------------------------------------------- 2-3 candidates: the board opens

        [Test]
        public void TwoOrMoreCandidatesOpensTheBoardWithExactlyThoseNodesLitAsCandidates()
        {
            var candidates = new[] { "s_bal", "e_ff", "m_spd" };
            _screen.OpenMorphingModuleDraft(candidates);

            Assert.That(_screen.IsOpen, Is.True, "2-3 candidates must open THE RIG");
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the fight must pause while a draft is pending");

            var scrim = _screen.BoardNode("Draft Scrim");
            Assert.That(scrim, Is.Not.Null);
            Assert.That(scrim.gameObject.activeSelf, Is.True, "the board must be dimmed behind a scrim");

            foreach (var id in candidates)
            {
                var node = _screen.BoardNode(id);
                var badge = node.Find("Draft Badge");
                Assert.That(badge.gameObject.activeSelf, Is.True, $"'{id}' must show its numbered draft badge");
                Assert.That(node.GetComponentInChildren<Button>().interactable, Is.True, $"'{id}' must be tappable");
            }

            // Every ability node NOT in the candidate set must show no draft badge — "everything else
            // dimmed" (the ticket's own MV-424.png vs MV-423.png contrast).
            foreach (var ab in RigBoardLayout.Abilities)
            {
                if (System.Array.IndexOf(candidates, ab.Id) >= 0) continue;
                var node = _screen.BoardNode(ab.Id);
                var badge = node.Find("Draft Badge");
                Assert.That(badge.gameObject.activeSelf, Is.False, $"'{ab.Id}' is not a candidate and must not show a draft badge");
            }
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

            Assert.That(_screen.IsOpen, Is.False, "taking a candidate must close the draft");
        }

        // ---------------------------------------------------------------- 0 and 1 candidates: no screen

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

        [Test]
        public void OneCandidateGrantsItDirectlyWithoutOpeningTheScreen()
        {
            _screen.OpenMorphingModuleDraft(new[] { "e_cel" });

            Assert.That(RigState.Level("e_cel"), Is.EqualTo(1), "the sole candidate must be granted outright");
            Assert.That(_screen.IsOpen, Is.False, "a single candidate must never open the board");
        }

        // ---------------------------------------------------------------- the bottom band

        [Test]
        public void MorphingModuleBandIsAnchoredToTheBottomAndNeverObscuresTheCategoryRow()
        {
            _screen.Open();   // the band exists once Build() runs, whether or not a draft is active
            var band = _screen.BoardNode("draft_band");
            Assert.That(band, Is.Not.Null);

            // Board Root is a fixed 1920x1080 frame regardless of Safe Area (RigBoardLayoutTests'
            // own trick), and the band is anchored to ITS bottom edge — so its top edge in the
            // canvas's own top-down y is simply boardHeight - band height.
            float topY = 1080f - band.sizeDelta.y;
            Assert.That(topY, Is.GreaterThanOrEqualTo(306f),
                "the band must not reach up far enough to cover the category row (y <= 306)");
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
