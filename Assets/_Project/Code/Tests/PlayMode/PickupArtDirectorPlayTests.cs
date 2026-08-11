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
    /// machine-internals model; MV-180 reverted that to a plain chrome box. MV-305 reverses MV-180 again
    /// — every part pickup wears one of <see cref="WeaponPartArt.MachineInternalsKeys"/>'s irregular
    /// designs, rerolled at random on each fresh drop, same swap-in idiom the power cell always used.
    ///
    /// The load-bearing assertions here are that every part pickup gets a machine-internals PartArt:
    /// child with its own greybox hidden underneath, that a pooled part rerolls (and doesn't double up
    /// on) its design when it's dropped again, that it wears glint dots that actually shimmer, that the
    /// cell keeps its dedicated prop, and that every pickup — cell or part — carries the shared pulsing
    /// collectible glow.
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

        private Pickup MakeDevice()
        {
            var p = Pickup.Create(PickupKind.Device);
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
        public IEnumerator PartPickups_GetAMachineInternalsProp_WithGreyboxHidden()
        {
            // MV-305 reverses MV-180: every part now wears one of the machine-internals designs instead
            // of staying a plain glowing box.
            var beam = MakePart(PartKind.BeamNozzle);
            var harness = MakePart(PartKind.AugmentationHarness);

            yield return InstallDirector();

            foreach (var p in new[] { beam, harness })
            {
                Transform art = ArtOf(p);
                Assert.IsNotNull(art, $"{p.Part} got no PartArt: prop — MV-305 needs every part dressed.");
                string key = art.name.Substring("PartArt:".Length);
                CollectionAssert.Contains(WeaponPartArt.MachineInternalsKeys, key,
                    $"{p.Part} wears '{key}', which isn't one of the machine-internals designs.");

                var visual = p.transform.Find("Visual");
                Assert.IsTrue(visual == null || !visual.GetComponent<MeshRenderer>().enabled,
                    $"{p.Part}'s greybox box is still drawn under its machine-internals prop.");
            }
        }

        [UnityTest]
        public IEnumerator PartPickup_RerollsItsDesign_OnEveryFreshDrop_WithNoStaleLeftover()
        {
            // A pooled part pickup has to be able to change design when PickupDirector drops it again,
            // and not leave its old prop attached alongside the new one.
            var part = MakePart(PartKind.BeamNozzle);
            yield return InstallDirector();

            Assert.IsNotNull(ArtOf(part), "the part never got a machine-internals prop in the first place.");

            part.gameObject.SetActive(false);
            yield return null;
            part.gameObject.SetActive(true);
            yield return null;   // the reroll + stale-art Destroy() call land on this frame
            yield return null;   // Destroy() only actually removes the child at end of frame

            Transform art = ArtOf(part);
            Assert.IsNotNull(art, "the part lost its machine-internals prop after being redropped.");
            string key = art.name.Substring("PartArt:".Length);
            CollectionAssert.Contains(WeaponPartArt.MachineInternalsKeys, key,
                $"the redropped part wears '{key}', which isn't one of the machine-internals designs.");

            int partArtChildren = 0;
            foreach (Transform c in part.transform)
                if (c.name.StartsWith("PartArt:")) partArtChildren++;
            Assert.AreEqual(1, partArtChildren, "a stale machine-internals prop is still attached after redropping.");
        }

        [UnityTest]
        public IEnumerator PartPickup_GlintsShimmerOnItsMachineInternalsProp()
        {
            // Mirrors PowerCell_GlintsFlickerOnTheCasing: a part's random design has to actually
            // shimmer, not just sit at its build-time glint colour.
            var part = MakePart(PartKind.BeamNozzle);

            yield return InstallDirector();

            Transform art = ArtOf(part);
            Assert.IsNotNull(art, "the part got no art model.");

            Transform glint = null;
            foreach (Transform c in art)
                if (c.name.StartsWith(WeaponPartArt.GlistenPrefix)) { glint = c; break; }
            Assert.IsNotNull(glint, "the part's prop has no glint dot to animate.");
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

            Assert.IsTrue(changed, "the part's glint never changes brightness — it isn't shimmering.");
        }

        [UnityTest]
        public IEnumerator EveryPickup_CarriesThePulsingCollectibleGlow()
        {
            var part = MakePart(PartKind.BeamNozzle);
            var cell = MakeCell();
            var device = MakeDevice();

            yield return InstallDirector();

            foreach (var p in new[] { part, cell, device })
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

        [UnityTest]
        public IEnumerator DevicePickup_GetsTheSpecialProp_WithGreyboxHidden()
        {
            // MV-308: a shed's ability grant used to fall through the director's Update loop with no
            // branch at all, staying a plain greybox cube forever.
            var device = MakeDevice();

            yield return InstallDirector();

            Transform art = ArtOf(device);
            Assert.IsNotNull(art, "the device pickup got no special prop — MV-308 needs it dressed.");
            Assert.IsTrue(art.name.EndsWith(WeaponPartArt.Keys.HydroDevice),
                $"the device wears '{art.name}', expected the shared ability-device prop.");
            Assert.Greater(art.GetComponentsInChildren<MeshRenderer>().Length, 1, "the prop is empty.");

            var visual = device.transform.Find("Visual");
            Assert.IsTrue(visual == null || !visual.GetComponent<MeshRenderer>().enabled,
                "the device's greybox box is still drawn under its special prop.");
        }

        [UnityTest]
        public IEnumerator DevicePickup_GlintsShimmerOnItsProp()
        {
            var device = MakeDevice();

            yield return InstallDirector();

            Transform art = ArtOf(device);
            Assert.IsNotNull(art, "the device pickup got no art model.");

            var glint = art.Find(WeaponPartArt.GlistenPrefix + "0");
            Assert.IsNotNull(glint, "the device's prop has no glint dot to animate.");
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

            Assert.IsTrue(changed, "the device's glint never changes brightness — it isn't shimmering.");
        }

        [UnityTest]
        public IEnumerator DevicePickup_CoreRadiatesGently_LikeThePowerCell()
        {
            // MV-308 AC: the device needs "the glowing radiance the power cells have" — the same MV-304
            // Core breathe, not just the shared orange CollectibleGlow every pickup already wears.
            var device = MakeDevice();

            yield return InstallDirector();

            Transform art = ArtOf(device);
            Assert.IsNotNull(art, "the device pickup got no art model.");

            var core = art.Find("Core");
            Assert.IsNotNull(core, "the device's prop has no Core band to radiate.");
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

            Assert.IsTrue(changed, "the device's Core never changes brightness — it isn't radiating.");
        }
    }
}
