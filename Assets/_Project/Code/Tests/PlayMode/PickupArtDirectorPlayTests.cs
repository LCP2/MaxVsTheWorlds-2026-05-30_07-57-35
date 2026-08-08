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
    /// The pickups wear their real art (YT-134/145). WV-237 had swapped every dropped part for a random
    /// machine-internals model; MV-180 reverses that — a part pickup stays the plain chrome box
    /// <see cref="Pickup"/> already builds (one consistent, non-brown colour), now wearing a couple of
    /// specular glint dots so it reads as "shiny" rather than flat. The power cell is unaffected: it
    /// keeps its own always-the-same swapped prop.
    ///
    /// The load-bearing assertions here are that a part pickup keeps its box visible (no PartArt: swap,
    /// no hidden greybox), that it wears glint dots that actually shimmer, that a pooled part redropped
    /// doesn't grow duplicate glint dots, that the cell keeps its dedicated prop, and that every pickup —
    /// cell or part — carries the shared pulsing collectible glow.
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
        private static Transform VisualOf(Pickup p) => p.transform.Find("Visual");

        private static Transform[] GlintsOf(Pickup p)
        {
            var visual = VisualOf(p);
            if (visual == null) return System.Array.Empty<Transform>();
            var glints = new List<Transform>();
            foreach (Transform c in visual)
                if (c.name.StartsWith("PartGlisten")) glints.Add(c);
            return glints.ToArray();
        }

        [UnityTest]
        public IEnumerator PartPickups_StayAsBoxes_InOneConsistentColour()
        {
            // MV-180 reverses WV-237: a part pickup keeps the plain chrome box Pickup already builds —
            // no swapped-in PartArt: prop, no hidden greybox — and every part shares that one colour.
            var beam = MakePart(PartKind.BeamNozzle);
            var harness = MakePart(PartKind.AugmentationHarness);

            yield return InstallDirector();

            Material firstMat = null;
            foreach (var p in new[] { beam, harness })
            {
                Assert.IsNull(ArtOf(p), $"{p.Part} got a swapped-in PartArt: prop — MV-180 keeps it a plain box.");

                var visual = VisualOf(p);
                Assert.IsNotNull(visual, $"{p.Part} has no box.");
                var mr = visual.GetComponent<MeshRenderer>();
                Assert.IsTrue(mr.enabled, $"{p.Part}'s box is hidden — MV-180 needs it visible.");

                firstMat ??= mr.sharedMaterial;
                Assert.AreSame(firstMat, mr.sharedMaterial,
                    $"{p.Part}'s box wears a different material than the first part — colours aren't consistent.");
            }
        }

        [UnityTest]
        public IEnumerator PartPickup_KeepsExactlyOnePairOfGlints_AfterBeingRedropped()
        {
            // A pooled part pickup gets reused for a fresh drop (PickupDirector's deactivate/reactivate
            // cycle); its glint dots must not double up across that cycle.
            var part = MakePart(PartKind.BeamNozzle);
            yield return InstallDirector();

            Assert.AreEqual(2, GlintsOf(part).Length, "the part didn't get its two glint dots in the first place.");

            part.gameObject.SetActive(false);
            yield return null;
            part.gameObject.SetActive(true);
            yield return null;

            Assert.AreEqual(2, GlintsOf(part).Length, "a stale/duplicate glint dot appeared after redropping.");
        }

        [UnityTest]
        public IEnumerator PartPickup_GlintsShimmerOnItsBox()
        {
            // Mirrors PowerCell_GlintsFlickerOnTheCasing: the box's glint has to actually shimmer, not
            // just sit at its build-time colour.
            var part = MakePart(PartKind.BeamNozzle);

            yield return InstallDirector();

            Transform[] glints = GlintsOf(part);
            Assert.IsNotEmpty(glints, "the part's box has no glint dot to animate.");
            var r = glints[0].GetComponent<MeshRenderer>();
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

            Assert.IsTrue(changed, "the part's glint never changes brightness — it isn't shimmering.");
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
        public IEnumerator PowerCell_CoreRadiatesGently()
        {
            // MV-304: the cell's "Core" charge band has to breathe on its own, distinct from the shared
            // orange CollectibleGlow pulse below it — a fixed-brightness core reads as a lamp, not
            // energy.
            var cell = MakeCell();

            yield return InstallDirector();

            Transform art = ArtOf(cell);
            Assert.IsNotNull(art, "the power cell got no art model.");

            var core = art.Find("Core");
            Assert.IsNotNull(core, "the cell's art has no Core band to radiate.");
            var r = core.GetComponent<MeshRenderer>();
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

            Assert.IsTrue(changed, "the power cell's Core never changes brightness — it isn't radiating.");
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
