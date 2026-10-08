using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1149 — <c>RigBoardLayout.BuildColumnLayout</c> scaled a node's own ability-radius term along
    /// with everything else when sizing a family's column half-width, even though a node's hex never
    /// scales (only its POSITION does — see <c>PhoneNodeSpacing</c>'s own doc comment). World 1's
    /// sparser tree kept phone mode's scale factor comfortably above 1, where that mismatch only
    /// widened a column's margin; World 3's denser tree pushed scale under 1, where the same mismatch
    /// instead shrank several families' columns by exactly abilityRadius*(1-scale) — 3-5 ref px — and
    /// their outermost node clipped. Sole guard on the fix; do not cull.
    /// </summary>
    public sealed class MV1149RigColumnWidthTests
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
            RigBoard.ResetForTests();
            RigBoardLayout.ResetForTests();
        }

        /// <summary>Builds the real board (via <see cref="WeaponsScreen"/>, not the raw layout data) for
        /// every shipped world and reads back real <c>RectTransform</c> world corners — the same
        /// node-vs-panel <c>GetWorldCorners</c> idiom <c>MV594RigBoardFixTests</c>' own AC1 already
        /// established for World 1, just driven across World 1/2/3 (that World-1-only test is this
        /// defect's blind spot: the panel's left/right edge for a boundary family is derived straight
        /// from <see cref="RigCategoryLayout.ColumnHalfWidth"/>, so a node escaping its column shows up
        /// as a node escaping its own panel). Both sides of each comparison go through the SAME
        /// RectTransform hierarchy (board scale-to-fit included), so the comparison stays valid
        /// regardless of the live scale factor at a given aspect.</summary>
        [Test]
        public void EveryAbilityHexSitsInsideItsOwnFamilyPanel_AcrossWorldsAndAspects()
        {
            _screen.Open();

            // MV-516 idiom: pin scaleFactor to 1 so GetWorldCorners reads back directly in ref px.
            var scaler = _screen.RootCanvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            _screen.RootCanvas.scaleFactor = 1f;

            Vector2 WorldXRange(RectTransform rt)
            {
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                return new Vector2(Mathf.Min(c[0].x, c[2].x), Mathf.Max(c[0].x, c[2].x));
            }

            float[] aspects = { 2.13f, 1.78f, 1.33f };
            var failures = new List<string>();

            for (int world = 0; world < 3; world++)
            {
                RigBoard.UseWorld(world);

                foreach (float aspect in aspects)
                {
                    _screen.ApplyBoardScale(aspect);
                    bool phoneMode = WeaponsScreen.IsPhoneLayout(aspect);
                    var categories = phoneMode ? RigBoardLayout.PhoneCategories : RigBoardLayout.Categories;
                    var abilities = phoneMode ? RigBoardLayout.PhoneAbilities : RigBoardLayout.Abilities;

                    foreach (var cat in categories)
                    {
                        var panel = _screen.CategoryPanel(cat.Id);
                        Assert.That(panel, Is.Not.Null,
                            $"world{world + 1} aspect {aspect}: no panel for '{cat.Id}'");
                        var panelRange = WorldXRange(panel.rectTransform);

                        var catNode = _screen.BoardNode(cat.Id);
                        Assert.That(catNode, Is.Not.Null,
                            $"world{world + 1} aspect {aspect}: no built node for category '{cat.Id}'");
                        var catRange = WorldXRange(catNode);
                        if (catRange.x < panelRange.x - 1f || catRange.y > panelRange.y + 1f)
                            failures.Add($"world{world + 1} aspect {aspect}: '{cat.Id}' category hex " +
                                $"[{catRange.x:0.0},{catRange.y:0.0}] escapes panel [{panelRange.x:0.0},{panelRange.y:0.0}]");

                        foreach (var ab in abilities)
                        {
                            if (ab.Category != cat.Id) continue;
                            var abNode = _screen.BoardNode(ab.Id);
                            Assert.That(abNode, Is.Not.Null,
                                $"world{world + 1} aspect {aspect}: no built node for ability '{ab.Id}'");
                            var abRange = WorldXRange(abNode);
                            if (abRange.x < panelRange.x - 1f || abRange.y > panelRange.y + 1f)
                                failures.Add($"world{world + 1} aspect {aspect}: '{ab.Id}' hex " +
                                    $"[{abRange.x:0.0},{abRange.y:0.0}] escapes '{cat.Id}' panel [{panelRange.x:0.0},{panelRange.y:0.0}]");
                        }
                    }
                }
            }

            Assert.That(failures, Is.Empty,
                "node hex escapes its own family's panel horizontally:\n" + string.Join("\n", failures));
        }
    }
}
