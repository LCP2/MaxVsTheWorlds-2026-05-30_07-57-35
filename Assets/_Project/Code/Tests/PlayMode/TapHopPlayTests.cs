using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Hose;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Tap-hopping (YT-130): the tap network installs itself, and Max re-plugs the hose to a tap he
    /// walks up to — instantly, no pause — with the leash re-anchoring to the new tap.
    /// </summary>
    public sealed class TapHopPlayTests
    {
        private GameObject _max;
        private GameObject _director;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DevTuning.Reset();
            yield return CleanUp();   // start from a clean tap/director slate
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DevTuning.Reset();
            if (_max != null) Object.Destroy(_max);
            if (_director != null) Object.Destroy(_director);
            yield return CleanUp();
        }

        // Taps and the director self-install and persist across the PlayMode run; clear them so each
        // test owns exactly the taps it creates. Also clear any stray "Player"-tagged object leaked by
        // another test class — the director finds Max by tag (GameObject.FindGameObjectWithTag), and a
        // leftover Player would be wired instead of ours.
        private static IEnumerator CleanUp()
        {
            foreach (var d in Object.FindObjectsByType<HoseDirector>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);
            foreach (var t in Object.FindObjectsByType<Tap>(FindObjectsSortMode.None))
                Object.Destroy(t.gameObject);
            foreach (var p in GameObject.FindGameObjectsWithTag("Player"))
                Object.Destroy(p);
            yield return null;   // let OnDisable pull taps out of Tap.All and the strays out of the scene
        }

        private static float PlanarDist(Vector3 a, Vector3 b) =>
            new Vector2(a.x - b.x, a.z - b.z).magnitude;

        [UnityTest]
        public IEnumerator TheTapNetworkInstallsItself()
        {
            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.AddComponent<CharacterController>();
            _max.AddComponent<MaxWorlds.Combat.WaterBlaster>();   // the armed player the hose wires to
            _max.transform.position = HoseDirector.StartTapPosition + new Vector3(0f, 1f, 1f);

            _director = new GameObject("HoseDirector");
            _director.AddComponent<HoseDirector>();
            yield return null;
            yield return null;

            Assert.That(Tap.All.Count, Is.EqualTo(HoseDirector.TapPositions.Length),
                "the whole tap network should be placed, not just the starting tap");

            var tether = _max.GetComponent<HoseTether>();
            Assert.That(tether, Is.Not.Null, "Max never got a tether");
            Assert.That(tether.Tap, Is.Not.Null, "Max isn't plugged into any tap");
            Assert.That(tether.Tap.IsConnected, Is.True, "the tap Max is on should read as connected");
        }

        [UnityTest]
        public IEnumerator WalkingOntoAnotherTapReplugsInstantly()
        {
            var a = Tap.Create("A", Vector3.zero);
            var b = Tap.Create("B", new Vector3(0f, 0f, 16f));

            _max = new GameObject("Max");
            _max.AddComponent<CharacterController>();
            var tether = _max.AddComponent<HoseTether>();
            _max.transform.position = a.transform.position;
            tether.SetTap(a);
            yield return null;

            Assert.That(tether.Tap, Is.EqualTo(a), "precondition: plugged into A");
            Assert.That(a.IsConnected, Is.True);
            Assert.That(b.IsConnected, Is.False, "only the connected tap should glow");

            // Walk onto B. One frame later the hose has swapped — no pause, no button.
            _max.transform.position = b.transform.position;
            yield return null;

            Assert.That(tether.Tap, Is.EqualTo(b), "walking onto B must re-plug the hose to B");
            Assert.That(b.IsConnected, Is.True, "B should now glow");
            Assert.That(a.IsConnected, Is.False, "A should have dimmed — exactly one tap connected");
        }

        [UnityTest]
        public IEnumerator TheLeashReAnchorsToTheNewTap()
        {
            var a = Tap.Create("A", Vector3.zero);
            var b = Tap.Create("B", new Vector3(0f, 0f, 16f));

            _max = new GameObject("Max");
            _max.AddComponent<CharacterController>();
            var tether = _max.AddComponent<HoseTether>();
            _max.transform.position = a.transform.position;
            tether.SetTap(a);
            yield return null;

            // Hop to B, then bolt far away. The leash must now hold him at tether-length from B — the
            // new anchor — not from A.
            _max.transform.position = b.transform.position;
            yield return null;
            _max.transform.position = b.transform.position + new Vector3(0f, 1f, 100f);
            yield return null;

            Assert.That(tether.Tap, Is.EqualTo(b), "should still be plugged into B");
            Assert.That(PlanarDist(_max.transform.position, b.transform.position),
                Is.EqualTo(HoseTether.AuthoredLength).Within(0.05f),
                "the leash must re-anchor to B — Max should be held a tether-length from B, not A");
        }
    }
}
