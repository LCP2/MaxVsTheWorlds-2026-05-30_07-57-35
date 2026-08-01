using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Hose;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The taps wear their real art (YT-134), as pure passive set-dressing with no connection point
    /// implied (WV-239) — dressing swaps the cosmetic post + spout for the standpipe and nothing else.
    /// </summary>
    public sealed class TapArtDirectorPlayTests
    {
        private GameObject _director;
        private readonly List<GameObject> _taps = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_director != null) Object.Destroy(_director);
            foreach (var t in _taps) if (t != null) Object.Destroy(t);
            _taps.Clear();
            yield return null;   // let Tap deregister from its static registry before the next test
        }

        private Tap MakeTap()
        {
            var tap = Tap.Create("TestTap", Vector3.zero);
            _taps.Add(tap.gameObject);
            return tap;
        }

        private IEnumerator InstallDirector()
        {
            _director = new GameObject("TapArt");
            _director.AddComponent<TapArtDirector>();
            yield return null;
            yield return null;   // one Update dresses it, a frame for the collider-strip Destroy
        }

        [UnityTest]
        public IEnumerator DressesTheTap_AsPassiveSetDressing()
        {
            var tap = MakeTap();
            yield return InstallDirector();

            Transform art = tap.transform.Find("TapArt");
            Assert.IsNotNull(art, "the tap was not dressed with the standpipe art.");
            Assert.Greater(art.GetComponentsInChildren<MeshRenderer>().Length, 3, "the tap art is empty.");

            // The cosmetic greybox is hidden...
            Assert.IsFalse(tap.transform.Find("TapPost").GetComponent<MeshRenderer>().enabled,
                "the greybox post is still drawn inside the art tap.");
            Assert.IsFalse(tap.transform.Find("TapSpout").GetComponent<MeshRenderer>().enabled,
                "the greybox spout is still drawn inside the art tap.");

            // No connection indicator of any kind (WV-239) — a tap implies nothing to plug into.
            Assert.IsNull(tap.transform.Find("TapIndicator"),
                "a connection indicator still exists on a tap that nothing can plug into.");
        }

        [UnityTest]
        public IEnumerator TheTapDrips_WithAWetPatchAtItsBase()
        {
            // YT-142 — the drip is the "here I am" beacon so players can find the taps.
            var tap = MakeTap();
            yield return InstallDirector();

            var art = tap.transform.Find("TapArt");
            Assert.IsNotNull(art, "the tap was not dressed.");
            Assert.IsNotNull(art.GetComponentInChildren<ParticleSystem>(),
                "the tap has no drip — nothing marks it as a water source at a glance.");
            Assert.IsNotNull(art.Find("WetPatch"), "the tap has no wet patch pooling at its base.");
            // Still scenery — the drip and patch must not have brought a collider back.
            Assert.IsEmpty(tap.transform.Find("TapArt").GetComponentsInChildren<Collider>(),
                "the drip dressing added a collider.");
        }

        [UnityTest]
        public IEnumerator DressesEachTapOnce_NoDuplicates()
        {
            var tap = MakeTap();
            yield return InstallDirector();
            // Several more frames — the director must not add a second art tap each frame.
            yield return null;
            yield return null;

            int artCount = 0;
            foreach (Transform c in tap.transform) if (c.name == "TapArt") artCount++;
            Assert.AreEqual(1, artCount, "the director dressed the tap more than once.");
        }
    }
}
