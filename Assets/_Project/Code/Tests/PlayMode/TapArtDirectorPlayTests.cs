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
    /// The taps wear their real art (YT-134). The load-bearing assertion is that the swap KEEPS the
    /// functional connection bulb while replacing only the cosmetic post + spout — dress the tap and
    /// lose its "you're plugged in here" light and you've broken a gameplay read to gain a prettier one.
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
        public IEnumerator DressesTheTap_ButKeepsTheConnectionBulb()
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

            // ...but the functional connection light is untouched.
            var bulb = tap.transform.Find("TapIndicator");
            Assert.IsNotNull(bulb, "the connection bulb is gone.");
            Assert.IsTrue(bulb.GetComponent<MeshRenderer>().enabled,
                "the connection bulb was hidden — the player can no longer see which tap they're on.");
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
