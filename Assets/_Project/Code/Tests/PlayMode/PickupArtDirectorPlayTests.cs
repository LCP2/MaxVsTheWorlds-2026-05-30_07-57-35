using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Pickups;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The pickups wear their real art (YT-134/145), and YT-180 reversed the part-like-model direction
    /// for the five parts: they stay their own greybox cube, just glowing and one consistent (non-brown)
    /// colour, while the power cell still wears its swapped-in <see cref="WeaponPartArt"/> prop.
    ///
    /// The load-bearing assertions here are that a part pickup is NEVER given a PartArt: child (no
    /// regression back to the reverted direction), that its own box stays visible and lit, that the cell
    /// still gets its dedicated prop with the greybox hidden underneath, and that every pickup — box or
    /// prop — carries the shared pulsing collectible glow.
    /// </summary>
    public sealed class PickupArtDirectorPlayTests
    {
        private GameObject _director;
        private readonly List<GameObject> _pickups = new List<GameObject>();

        [SetUp] public void SetUp() { Time.timeScale = 1f; }

        [TearDown]
        public void TearDown()
        {
            if (_director != null) Object.Destroy(_director);
            foreach (var p in _pickups) if (p != null) Object.Destroy(p);
            _pickups.Clear();
            Time.timeScale = 1f;
        }

        private Pickup MakePart(PartKind kind)
        {
            var p = Pickup.Create(PickupKind.Part);
            p.Part = kind;
            p.gameObject.SetActive(true);
            _pickups.Add(p.gameObject);
            return p;
        }

        private Pickup MakeCell()
        {
            var p = Pickup.Create(PickupKind.PowerCell);
            p.gameObject.SetActive(true);
            _pickups.Add(p.gameObject);
            return p;
        }

        /// <summary>Stand the director up by hand — its self-install is gated on a PickupDirector so it
        /// stays out of other tests, which is exactly the leak this project has been bitten by.</summary>
        private IEnumerator InstallDirector()
        {
            _director = new GameObject("PickupArt");
            _director.AddComponent<PickupArtDirector>();
            yield return null;   // one Update swaps the cell's art in
            yield return null;   // collider-strip Destroy lands
        }

        private static Transform ArtOf(Pickup p)
        {
            foreach (Transform c in p.transform)
                if (c.name.StartsWith("PartArt:")) return c;
            return null;
        }

        private static Transform GlowOf(Pickup p) => p.transform.Find("CollectibleGlow");

        [UnityTest]
        public IEnumerator PartPickups_StayBoxes_WithNoPropSwap()
        {
            var beam = MakePart(PartKind.BeamNozzle);
            var harness = MakePart(PartKind.AugmentationHarness);
            var hydro = MakePart(PartKind.Hydro);

            yield return InstallDirector();

            foreach (var p in new[] { beam, harness, hydro })
            {
                Assert.IsNull(ArtOf(p), $"{p.Part} was given a PartArt: prop — YT-180 reverted parts to boxes.");

                var visual = p.transform.Find("Visual");
                Assert.IsNotNull(visual, $"{p.Part} lost its greybox box.");
                Assert.IsTrue(visual.GetComponent<MeshRenderer>().enabled,
                    $"{p.Part}'s box is hidden — it has to stay visible now that nothing replaces it.");
            }
        }

        [UnityTest]
        public IEnumerator EveryPickup_CarriesThePulsingCollectibleGlow()
        {
            var part = MakePart(PartKind.BeamNozzle);
            var cell = MakeCell();

            yield return InstallDirector();

            foreach (var p in new[] { part, cell })
            {
                Transform glow = GlowOf(p);
                Assert.IsNotNull(glow, $"{p.Kind} got no collectible glow.");
                float scaleAtT0 = glow.localScale.x;

                yield return null;
                yield return null;

                Assert.AreNotEqual(scaleAtT0, glow.localScale.x,
                    $"{p.Kind}'s glow isn't pulsing — same scale two frames later.");
            }
        }

        [UnityTest]
        public IEnumerator PowerCell_GlintsFlickerOnTheCasing()
        {
            // YT-167: the director has to actually drive the glints WeaponPartArt built, not just leave
            // them sitting at their build-time colour — a static "highlight" isn't a sparkle.
            var cell = MakeCell();

            yield return InstallDirector();

            Transform art = ArtOf(cell);
            Assert.IsNotNull(art, "the power cell got no art model.");

            var glint = art.Find(WeaponPartArt.GlistenPrefix + "0");
            Assert.IsNotNull(glint, "the cell's art has no glint dot to animate.");
            var r = glint.GetComponent<MeshRenderer>();
            int baseColorId = Shader.PropertyToID("_BaseColor");

            Color ColorAt()
            {
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                return mpb.GetColor(baseColorId);
            }

            Color c0 = ColorAt();
            bool changed = false;
            for (int i = 0; i < 8; i++)
            {
                yield return null;
                if (ColorAt() != c0) { changed = true; break; }
            }

            Assert.IsTrue(changed, "the power cell's glint never changes brightness — it isn't sparkling.");
        }

        [UnityTest]
        public IEnumerator PowerCell_StillWearsItsSwappedProp_WithTheGreyboxHidden()
        {
            var cell = MakeCell();

            yield return InstallDirector();

            Transform art = ArtOf(cell);
            Assert.IsNotNull(art, "the power cell got no art model.");
            Assert.IsTrue(art.name.EndsWith(WeaponPartArt.Keys.PowerCell),
                $"the cell wears '{art.name}', expected the power-cell prop.");
            Assert.Greater(art.GetComponentsInChildren<MeshRenderer>().Length, 1, "the prop is empty.");

            var visual = cell.transform.Find("Visual");
            Assert.IsTrue(visual == null || !visual.GetComponent<MeshRenderer>().enabled,
                "the greybox stand-in is still drawn under the real cell prop — you'd see both.");
        }
    }
}
