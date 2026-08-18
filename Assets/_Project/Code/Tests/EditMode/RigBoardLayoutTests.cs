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
    /// MV-423 — THE RIG board must be driven exactly by <c>rig_board.json</c>, never re-derived: every
    /// category/ability/fusion node's built <see cref="RectTransform"/> is asserted against the data
    /// file's own (x, y, radius), the amber "+" badge only ever appears on nodes a part would actually
    /// raise, no node's rendered text carries a non-ASCII glyph (icons are geometry, per
    /// <see cref="HudTextures.VectorIcon"/>, never font symbols), and no two nodes' hit rects overlap.
    /// <see cref="WeaponsScreen.Open"/> builds and refreshes the canvas synchronously, so this needs no
    /// Play mode / coroutine — same pattern <see cref="SplashScreenTests"/> already uses for UI layout.
    /// </summary>
    public sealed class RigBoardLayoutTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
            PickupWallet.Reset();
        }

        /// <summary>Opens the screen once whatever fixture state a test needs is already in place.
        /// <see cref="MonoBehaviour"/> event callbacks (<c>OnEnable</c>) aren't reliably driven by the
        /// Editor outside Play mode, so tests never lean on a live <c>RigState.Changed</c>/
        /// <c>PickupWallet.PartsChanged</c> refresh firing after the fact — set state up first, open
        /// second, same rule <c>AreaAccumulationWorldConfigTests</c> already documents for this project.</summary>
        private void OpenScreen() => _screen.Open();

        [Test]
        public void EveryCategoryNodeMatchesTheDataFileExactly()
        {
            OpenScreen();
            foreach (var cat in RigBoardLayout.Categories)
                AssertNodeMatches(cat.Id, cat.X, cat.Y, RigBoardLayout.RadiusCategory);
        }

        [Test]
        public void EveryAbilityNodeMatchesTheDataFileExactly()
        {
            OpenScreen();
            foreach (var ab in RigBoardLayout.Abilities)
                AssertNodeMatches(ab.Id, ab.X, ab.Y, RigBoardLayout.RadiusAbility);
        }

        [Test]
        public void EveryFusionNodeMatchesTheDataFileExactly()
        {
            OpenScreen();
            foreach (var f in RigBoardLayout.Fusions)
                AssertNodeMatches(f.Id, f.X, f.Y, RigBoardLayout.RadiusFusion);
        }

        /// <summary>The root rect is a full <c>2r x 2r</c> hit square (MV-423: "give every node a full
        /// hit rect; do not shrink any radius"), not the hex's own narrower bounding box — this is the
        /// rect the AC's "size equals 2 x radius" wording describes.</summary>
        private void AssertNodeMatches(string id, float x, float y, float r)
        {
            var node = _screen.BoardNode(id);
            Assert.That(node, Is.Not.Null, $"no board node found for '{id}'");
            Assert.That(node.anchoredPosition.x, Is.EqualTo(x).Within(0.5f), $"{id} anchored x");
            Assert.That(node.anchoredPosition.y, Is.EqualTo(-y).Within(0.5f), $"{id} anchored y (canvas is y-down, RectTransform is y-up)");
            Assert.That(node.sizeDelta.x, Is.EqualTo(r * 2f).Within(0.5f), $"{id} width");
            Assert.That(node.sizeDelta.y, Is.EqualTo(r * 2f).Within(0.5f), $"{id} height");
        }

        [Test]
        public void NoNodeTextCarriesANonAsciiGlyph()
        {
            OpenScreen();
            foreach (var t in _go.GetComponentsInChildren<Text>(true))
            foreach (char c in t.text)
                Assert.That(c, Is.LessThan((char)128),
                    $"Text \"{t.text}\" on '{t.name}' carries a non-ASCII glyph — icons must be geometry, never font symbols");
        }

        [Test]
        public void NoTwoNodeHitRectsOverlap()
        {
            OpenScreen();
            var ids = new List<string>();
            foreach (var c in RigBoardLayout.Categories) ids.Add(c.Id);
            foreach (var a in RigBoardLayout.Abilities) ids.Add(a.Id);
            foreach (var f in RigBoardLayout.Fusions) ids.Add(f.Id);

            var bounds = new List<(string Id, Rect Rect)>();
            foreach (var id in ids)
            {
                var node = _screen.BoardNode(id);
                Assert.That(node, Is.Not.Null, $"no board node found for '{id}'");
                Vector2 c = node.anchoredPosition, s = node.sizeDelta;
                bounds.Add((id, new Rect(c.x - s.x * 0.5f, c.y - s.y * 0.5f, s.x, s.y)));
            }

            for (int i = 0; i < bounds.Count; i++)
            for (int j = i + 1; j < bounds.Count; j++)
                Assert.That(bounds[i].Rect.Overlaps(bounds[j].Rect), Is.False,
                    $"'{bounds[i].Id}' and '{bounds[j].Id}' hit rects overlap");
        }

        // Run-start spendable set, per RigStateTests (schema 3, MV-436 — the cap/stat split is
        // retired). p_dmg is the one ability owned at run start (level 1 of 6, no parent), so it's
        // the only one a part can raise. Every other ability — including p_rng/p_flw, which are
        // merely REACHED (p_dmg's own direct children) — sits at level 0, and a part can never
        // perform any ability's 0->1 unlock (model.rules); only a Morphing Module draft can.
        private static readonly HashSet<string> RunStartSpendable = new HashSet<string> { "p_dmg" };

        [Test]
        public void AmberBadgeAppearsOnExactlyTheSpendableNodesWithOneBankedPart()
        {
            PickupWallet.AddPart();
            OpenScreen();

            foreach (var ab in RigBoardLayout.Abilities)
            {
                var node = _screen.BoardNode(ab.Id);
                var badge = node.Find("Part Badge");
                bool expected = RunStartSpendable.Contains(ab.Id);
                Assert.That(badge.gameObject.activeSelf, Is.EqualTo(expected),
                    $"'{ab.Id}' amber badge should be {(expected ? "shown" : "hidden")} with 1 part banked");
            }
        }

        [Test]
        public void NoBadgeShowsWithNothingBanked()
        {
            // SetUp leaves PickupWallet at 0 parts (Reset()) — no badge should be lit anywhere,
            // matching MV-423.png vs MV-423-noparts.png.
            OpenScreen();
            foreach (var ab in RigBoardLayout.Abilities)
            {
                var node = _screen.BoardNode(ab.Id);
                var badge = node.Find("Part Badge");
                Assert.That(badge.gameObject.activeSelf, Is.False, $"'{ab.Id}' badge should be hidden with nothing banked");
            }
        }
    }
}
