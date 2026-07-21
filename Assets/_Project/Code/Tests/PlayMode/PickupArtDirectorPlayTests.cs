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
    /// The pickups wear their real art (YT-134). YT-131/133 drop all five parts as the SAME gold cube;
    /// this director swaps in the distinct <see cref="WeaponPartArt"/> prop per <see cref="PartKind"/>.
    ///
    /// The one thing the five parts must never be is indistinguishable, so the load-bearing assertion is
    /// that a beam nozzle and a Hydro device end up wearing different props — and that a POOLED pickup
    /// rebuilds when it comes back as a different part rather than keeping its old body or stacking a
    /// second one (deferred Destroy lingers a frame, which is the easy way to get duplicates).
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
            yield return null;   // one Update swaps the art in
            yield return null;   // collider-strip Destroy lands
        }

        private static Transform ArtOf(Pickup p)
        {
            foreach (Transform c in p.transform)
                if (c.name.StartsWith("PartArt:")) return c;
            return null;
        }

        private void AssertWears(Pickup p, string key)
        {
            Transform art = ArtOf(p);
            Assert.IsNotNull(art, $"{p.Kind}/{p.Part} got no art model.");
            Assert.IsTrue(art.name.EndsWith(key), $"{p.Part} wears '{art.name}', expected key '{key}'.");
            Assert.Greater(art.GetComponentsInChildren<MeshRenderer>().Length, 1, "the prop is empty.");

            var visual = p.transform.Find("Visual");
            Assert.IsTrue(visual == null || !visual.GetComponent<MeshRenderer>().enabled,
                "the greybox stand-in is still drawn under the real prop — you'd see both.");
        }

        [UnityTest]
        public IEnumerator EachPart_WearsItsOwnDistinctProp()
        {
            var beam = MakePart(PartKind.BeamNozzle);
            var harness = MakePart(PartKind.AugmentationHarness);
            var hydro = MakePart(PartKind.Hydro);
            var cell = MakeCell();

            yield return InstallDirector();

            AssertWears(beam, WeaponPartArt.Keys.BeamNozzle);
            AssertWears(harness, WeaponPartArt.Keys.AugmentationHarness);
            AssertWears(hydro, WeaponPartArt.Keys.HydroDevice);
            AssertWears(cell, WeaponPartArt.Keys.PowerCell);

            Assert.AreNotEqual(ArtOf(beam).name, ArtOf(hydro).name,
                "the beam nozzle and the Hydro device wear the same prop — the drops are indistinguishable.");
        }

        [UnityTest]
        public IEnumerator APooledPickup_RebuildsWhenItComesBackAsADifferentPart()
        {
            var p = MakePart(PartKind.BeamNozzle);
            yield return InstallDirector();
            AssertWears(p, WeaponPartArt.Keys.BeamNozzle);

            // Reuse it as a different part, exactly as the pool would.
            p.Part = PartKind.AccelerationEngine;
            yield return null;   // director notices the key changed, clears the old, builds the new
            yield return null;   // the old one's deferred Destroy lands
            yield return null;

            AssertWears(p, WeaponPartArt.Keys.AccelerationEngine);

            int artChildren = 0;
            foreach (Transform c in p.transform) if (c.name.StartsWith("PartArt:")) artChildren++;
            Assert.AreEqual(1, artChildren,
                "a pooled pickup that changed part is wearing more than one prop — the old one wasn't cleared.");
        }
    }
}
