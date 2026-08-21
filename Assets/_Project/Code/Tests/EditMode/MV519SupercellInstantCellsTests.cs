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
    /// MV-519: a Supercell grants <see cref="PickupWallet.SupercellCellValue"/> cells the instant it's
    /// picked up — no bank, no cash-in step, no persistent counter — even past <see cref="PickupWallet.Capacity"/>.
    /// This is the ticket's own dedicated test file (testing policy MV-465, Rule 1): every method below
    /// is new, proven to fail on 2d58995 (main HEAD before this ticket), since neither the over-cap grant,
    /// the un-clamped <see cref="PickupWallet.SetPowerCells"/>, the RIG top bar's dominant cell chip, nor
    /// the Supercell pickup-event cleanup existed there — <see cref="PickupWallet.AddSupercell"/> banked
    /// a separate <c>SupercellsBanked</c> tally instead of granting cells, <c>SetPowerCells</c> clamped to
    /// capacity, and the RIG top bar built a SUPERCELLS tray beside a plain 24pt CELLS chip.
    /// </summary>
    public sealed class MV519SupercellInstantCellsTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            PickupWallet.Reset();
            RigState.Reset();
            RigFusionState.Reset();
            foreach (string id in RigBoard.AllCategoryIds) RigState.UnlockCategory(id);
        }

        // ---------------------------------------------------------------- AC1: instant over-cap grant

        [Test]
        public void AddSupercellGrantsTenCellsInstantly_EvenPastCapacity()
        {
            PickupWallet.SetPowerCells(12);
            PickupWallet.AddSupercell();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(22), "12 + 10 = 22, over the default 20 cap");
            Assert.That(PickupWallet.Capacity, Is.EqualTo(20), "fixture assumption: default capacity is 20");

            PickupWallet.Reset();
            PickupWallet.AddSupercell();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(10), "at 0 cells a Supercell leaves exactly 10");

            PickupWallet.Reset();
            PickupWallet.SetPowerCells(19);
            PickupWallet.AddSupercell();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(29), "at 19 cells a Supercell leaves exactly 29");
        }

        // ---------------------------------------------------------------- AC2: ordinary pickups keep MV-439's refusal, including over-cap

        [Test]
        public void OrdinaryCellPickupsStillRefuseAtOrAboveCapacity_IncludingOverCap()
        {
            PickupWallet.SetPowerCells(20);   // exactly at the 20 cap
            Assert.That(PickupWallet.AddPowerCell(), Is.False, "at capacity an ordinary pickup must refuse");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(20), "a refused pickup must not change the count");

            PickupWallet.SetPowerCells(22);   // over cap, e.g. from a Supercell grant
            Assert.That(PickupWallet.AddPowerCell(), Is.False, "over cap an ordinary pickup must also refuse");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(22), "a refused pickup must not change an over-cap count either");

            PickupWallet.TrySpendPowerCell(); PickupWallet.TrySpendPowerCell(); PickupWallet.TrySpendPowerCell();
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(19), "fixture assumption: spent down to 19");
            Assert.That(PickupWallet.AddPowerCell(), Is.True, "below capacity an ordinary pickup must bank");
            Assert.That(PickupWallet.PowerCells, Is.EqualTo(20));
        }

        // ---------------------------------------------------------------- AC6: THE RIG top bar has no tray; the cell chip is dominant

        [Test]
        public void TheRigTopBarHasNoSupercellTray_AndTheCellReadoutIsTheLargestTextInIt()
        {
            WeaponSystemState.Reset();
            var go = new GameObject("WeaponsScreen");
            var screen = go.AddComponent<WeaponsScreen>();
            try
            {
                PickupWallet.SetPowerCells(8);   // a short string ("8/20 CELLS") so best-fit maxes out
                screen.Open();

                Assert.That(FindRect(go, "Supercell Tray"), Is.Null,
                    "MV-519: the SUPERCELLS tray must be gone, not merely hidden");

                var cellsChip = FindRect(go, "Cells Chip");
                Assert.That(cellsChip, Is.Not.Null, "the CELLS chip must still exist");
                var cellsText = cellsChip.GetComponentInChildren<Text>();
                Assert.That(cellsText, Is.Not.Null, "the CELLS chip must carry a label");

                float cellsSize = ResolvedFontSize(cellsText);
                float loudestOther = 0f;
                foreach (var text in screen.TopBar.GetComponentsInChildren<Text>(true))
                {
                    if (text == cellsText) continue;
                    float size = text.resizeTextForBestFit ? ResolvedFontSize(text) : text.fontSize;
                    loudestOther = Mathf.Max(loudestOther, size);
                }

                Assert.That(cellsSize, Is.GreaterThan(loudestOther),
                    $"the cell readout ({cellsSize}) must be the largest text in the top bar (next loudest: {loudestOther}) — includes \"THE RIG\" at its fixed 38pt");

                // Change item 5: an over-cap balance must read as a deliberate bonus, not a bug — the
                // chip's own resolved text colour must visibly change once a Supercell pushes it past
                // Capacity, not stay identical to a normal in-range balance. Closed and reopened rather
                // than mutated live — Refresh() is what BuildChip's colour actually goes through, and
                // Open() calls it directly, so this doesn't depend on which of PowerCellsChanged's
                // subscribers happen to still be live in this test run.
                Color underCapColor = cellsText.color;
                screen.Close();
                PickupWallet.AddSupercell();   // 8 -> 18, still under the default 20 cap
                screen.Open();
                cellsText = FindRect(go, "Cells Chip").GetComponentInChildren<Text>();
                Assert.That(cellsText.color, Is.EqualTo(underCapColor), "still under capacity — colour must not change yet");
                screen.Close();
                PickupWallet.AddSupercell();   // 18 -> 28, over the cap
                screen.Open();
                cellsText = FindRect(go, "Cells Chip").GetComponentInChildren<Text>();
                Assert.That(cellsText.color, Is.Not.EqualTo(underCapColor),
                    "an over-cap balance (28/20) must render in a visually distinct colour from a normal balance");
            }
            finally
            {
                Time.timeScale = 1f;
                Object.DestroyImmediate(go);
                PickupWallet.Reset();
                WeaponSystemState.Reset();
            }
        }

        private static float ResolvedFontSize(Text text)
        {
            var settings = text.GetGenerationSettings(text.rectTransform.rect.size);
            text.cachedTextGenerator.Populate(text.text, settings);
            return text.cachedTextGenerator.fontSizeUsedForBestFit;
        }

        private static RectTransform FindRect(GameObject go, string name)
        {
            foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == name) return rt;
            return null;
        }

        // ---------------------------------------------------------------- AC7: the pickup event self-terminates

        [Test]
        public void TheSupercellPickupEventLeavesNothingBehindAfterItsOwnDuration()
        {
            var camGo = new GameObject("MainCamera", typeof(Camera)) { tag = "MainCamera" };
            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HudController>();
            InvokeLifecycle(hud, "Awake");
            InvokeLifecycle(hud, "OnEnable");
            try
            {
                HudSignals.EmitSupercellCollected(new Vector3(0f, 0f, 5f), 12, 22);

                Assert.That((bool)GetField(hud, "_supercellFxActive"), Is.True,
                    "fixture assumption: the event must actually start (camera/canvas projection succeeded)");
                var burst = (Image)GetField(hud, "_supercellFxBurst");
                var label = (Text)GetField(hud, "_supercellFxLabel");
                Assert.That(burst.gameObject.activeSelf, Is.True, "the burst must be active once the event starts");
                Assert.That(label.gameObject.activeSelf, Is.True, "the flyup label must be active once the event starts");

                // Drive the whole ~0.6s beat (SupercellPickupEffect.Duration) in one simulated frame —
                // the same "advance the private timer directly" idiom this codebase already uses for
                // one-shot beats it can't wait real time for.
                InvokeUpdateSupercellFx(hud, 1.5f);

                Assert.That((bool)GetField(hud, "_supercellFxActive"), Is.False, "the event must have ended by 1.5s");
                Assert.That(burst.gameObject.activeSelf, Is.False, "the burst must be gone after the event ends");
                Assert.That(label.gameObject.activeSelf, Is.False, "the flyup label must be gone after the event ends");
            }
            finally
            {
                InvokeLifecycle(hud, "OnDisable");
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(camGo);
                PickupWallet.Reset();
            }
        }

        private static object GetField(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private static void InvokeUpdateSupercellFx(object target, float unscaledDt) =>
            target.GetType().GetMethod("UpdateSupercellFx", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, new object[] { unscaledDt });

        private static void InvokeLifecycle(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
    }
}
