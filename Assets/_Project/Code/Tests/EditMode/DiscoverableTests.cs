using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The record of what the player has found (YT-107). Small surface, but the two properties tested
    /// here are the entire contract the fog rests on: it latches, and an unmarked thing is visible.
    /// </summary>
    public sealed class DiscoverableTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private Discoverable NewLandmark()
        {
            _go = new GameObject("Landmark");
            return _go.AddComponent<Discoverable>();
        }

        [Test]
        public void ALandmarkStartsUnfound()
        {
            Assert.That(NewLandmark().Found, Is.False,
                        "A fresh run must not begin with the level already explored.");
        }

        [Test]
        public void BeingSeenOnceIsEnough_AndTheFlagNeverGoesBack()
        {
            Discoverable mark = NewLandmark();
            int announcements = 0;
            mark.Revealed += () => announcements++;

            mark.Reveal();
            mark.Reveal();
            mark.Reveal();

            Assert.That(mark.Found, Is.True);
            Assert.That(announcements, Is.EqualTo(1),
                        "Revealed is the moment of discovery — it must fire once, not once a frame.");
        }

        /// <summary>
        /// The failure this project keeps producing is the silent one, so the direction it fails in
        /// matters. An unmarked landmark reads as FOUND: a factory built outside the map path shows up
        /// early, which anyone can see. The other way round it would vanish, with nothing on screen to
        /// say why — and a map that quietly stops drawing the objective looks exactly like a map that
        /// is working.
        /// </summary>
        [Test]
        public void SomethingWithNoMarkerAtAll_ReadsAsAlreadyFound()
        {
            _go = new GameObject("Unmarked");
            var probe = _go.AddComponent<BoxCollider>();

            Assert.That(Discoverable.FoundOn(probe), Is.True);
        }

        [Test]
        public void AMarkedLandmarkReadsAsHiddenUntilItIsRevealed()
        {
            Discoverable mark = NewLandmark();

            Assert.That(Discoverable.FoundOn(mark), Is.False);
            mark.Reveal();
            Assert.That(Discoverable.FoundOn(mark), Is.True);
        }
    }
}
