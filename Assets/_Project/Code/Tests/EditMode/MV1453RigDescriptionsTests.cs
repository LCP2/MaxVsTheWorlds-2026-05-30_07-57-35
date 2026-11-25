using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1453 - THE RIG detail panel's description line and KEY chip. Resolved values only (Tier 2): the text
    /// the built panel carries for the selected node, whether the KEY chip is active, and the world-space
    /// rects of the description and chip against the Cost row. Reads the panel's built fields by name so the
    /// test compiles (and fails) on the base commit, before those fields exist.
    /// </summary>
    public sealed class MV1453RigDescriptionsTests
    {
        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset(); RigState.Reset(); RigFusionState.Reset(); PickupWallet.Reset();
            Time.timeScale = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset(); RigState.Reset(); RigFusionState.Reset(); PickupWallet.Reset();
            Time.timeScale = 1f;
            RigBoard.UseWorld(0);
            RigBoardLayout.UseWorld(0);
            RigBoardLayout.ResetForTests();
            CrispUi.Resolution = 1;
        }

        private static T Field<T>(WeaponsScreen s, string name) where T : class
        {
            var f = typeof(WeaponsScreen).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(f, Is.Not.Null, $"the detail panel builds a field '{name}'");
            return f.GetValue(s) as T;
        }

        private static void SelectWorld(int w)
        {
            WorldDefinition row = WorldCatalog.Get(w);
            RigBoard.UseWorld(w);
            RigBoardLayout.UseWorld(w);
            WeaponSystemState.ActivePrimary = row.PrimaryWeapon;
            WeaponSystemState.SecondaryKind = row.SecondaryWeapon;
        }

        private static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        [Test]
        public void DetailPanel_ShowsDescriptionAndKeyChip_AndEveryNodeHasOne()
        {
            int city = WorldCatalog.IndexOf(WorldIds.City);
            SelectWorld(city);
            var go = new GameObject("WeaponsScreen");
            try
            {
                var screen = go.AddComponent<WeaponsScreen>();
                PickupWallet.SetPowerCells(500);
                screen.Open();

                screen.SelectNode("p_ric");
                var desc = Field<TextMeshProUGUI>(screen, "_detailDesc");
                var chip = Field<Image>(screen, "_detailKeyChip");
                Assert.That(desc.text, Is.EqualTo("Bounces per disc. Each bounce hits harder."), "p_ric description");
                Assert.That(desc.gameObject.activeInHierarchy, Is.True, "description is shown");
                Assert.That(chip.gameObject.activeInHierarchy, Is.True, "p_ric is KEY");

                // The description and chip end above the Cost row (world-space rects, no overlap).
                var costLabel = Field<TextMeshProUGUI[]>(screen, "_costLabel")[0];
                Assert.That(costLabel.gameObject.activeInHierarchy, Is.True, "fixture: the Cost row is showing");
                Rect cost = WorldRect(costLabel.rectTransform);
                Assert.That(WorldRect(desc.rectTransform).Overlaps(cost), Is.False, "description does not overlap the Cost row");
                Assert.That(WorldRect(chip.rectTransform).Overlaps(cost), Is.False, "KEY chip does not overlap the Cost row");
                Assert.That(WorldRect(desc.rectTransform).Overlaps(WorldRect(chip.rectTransform)), Is.False, "description does not overlap the chip");

                screen.SelectNode("p_dmg");
                Assert.That(desc.text, Is.EqualTo("How hard each disc hits."), "p_dmg description");
                Assert.That(chip.gameObject.activeInHierarchy, Is.False, "p_dmg is not KEY");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }

            // Every node on every world's boards has a non-empty description of at most 60 characters.
            Type table = typeof(RigEffectLines).Assembly.GetType("MaxWorlds.Weapons.RigDescriptions");
            Assert.That(table, Is.Not.Null, "the description table exists");
            var get = table.GetMethod("Description", BindingFlags.Public | BindingFlags.Static);
            Assert.That(get, Is.Not.Null, "the table resolves a description");
            for (int w = 0; w < WorldCatalog.Count; w++)
            {
                SelectWorld(w);
                foreach (var node in RigBoardLayout.PhoneAbilities)
                {
                    string text = (string)get.Invoke(null, new object[] { WeaponSystemState.ActivePrimary, WeaponSystemState.SecondaryKind, node.Id });
                    Assert.That(text, Is.Not.Null.And.Not.Empty, $"world {w} node {node.Id} has a description");
                    Assert.That(text.Length, Is.LessThanOrEqualTo(60), $"world {w} node {node.Id} description length");
                }
            }
        }
    }
}
