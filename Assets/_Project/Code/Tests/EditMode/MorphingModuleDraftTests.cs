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

        [Test]
        public void OpeningWeaponsWithNothingPendingOpensThePlainBoard()
        {
            Assert.That(PendingMorphingModule.HasPending, Is.False);

            _screen.Open();

            Assert.That(_screen.IsOpen, Is.True);
            var scrim = _screen.BoardNode("Draft Scrim");
            Assert.That(scrim == null || !scrim.gameObject.activeSelf, Is.True,
                "no draft is pending — the board must open plain, not dimmed for a draft");
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

        [Test]
        public void TappingForceFieldOnTheBoardRevealsTheHudButton_DrivingTheRealSignalChain()
        {
            HudController.SkipTouchControlsForTests = true;
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            // Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode (same
            // workaround WaterBlasterGateDamageTests/RuntimeSurfaceDirectorTests already rely on) —
            // drive them directly so HudController actually builds and subscribes to
            // WeaponSystemState.Changed.
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            try
            {
                var fieldButton = FindRect(hudGo, "Force Field Button");
                Assert.That(fieldButton, Is.Not.Null, "the HUD must build a Force Field button root");
                Assert.That(fieldButton.gameObject.activeSelf, Is.False, "Force Field isn't owned yet");

                _screen.OpenMorphingModuleDraft(new[] { "e_ff", "m_spd" });
                _screen.BoardNode("e_ff").GetComponentInChildren<Button>().onClick.Invoke();

                Assert.That(fieldButton.gameObject.activeSelf, Is.True,
                    "drafting Force Field on the board must reveal the HUD's FIELD button without closing/reopening anything");
            }
            finally
            {
                // OnEnable subscribed hud.OnAbilitiesChanged to WeaponSystemState.Changed — without
                // OnDisable that subscription outlives DestroyImmediate and a later test's Changed
                // fire would hit a destroyed MonoBehaviour.
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                HudController.SkipTouchControlsForTests = false;
            }
        }

        /// <summary>Same lookup <c>HudDropsPlayTests.FindRect</c> uses for the live HUD.</summary>
        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        private static void InvokeLifecycle(Object component, string methodName)
        {
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
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
