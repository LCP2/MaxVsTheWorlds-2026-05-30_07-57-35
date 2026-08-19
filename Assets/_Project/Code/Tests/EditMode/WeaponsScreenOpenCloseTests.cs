using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-423 replaced <c>WeaponsScreenPlayTests.cs</c> (a PlayMode suite exercising the old
    /// primary-track/Water-Balloon/abilities grids this ticket removed) with this EditMode coverage of
    /// what's unchanged — open/close/pause. Not a PlayMode rewrite: <c>CC_AUTONOMY.md</c> bars
    /// authoring PlayMode tests (Unity batch-mode PlayMode has stalled this worker three times), and
    /// <see cref="WeaponsScreen.Open"/> already builds and refreshes its canvas synchronously, so no
    /// coroutine/frame-wait is actually needed here.
    /// </summary>
    public sealed class WeaponsScreenOpenCloseTests
    {
        private GameObject _go;
        private WeaponsScreen _screen;

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            _go = new GameObject("WeaponsScreen");
            _screen = _go.AddComponent<WeaponsScreen>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_go != null) Object.DestroyImmediate(_go);
            WeaponSystemState.Reset();
            PickupWallet.Reset();
        }

        [Test]
        public void OpeningPausesTheGame()
        {
            _screen.Open();

            Assert.That(_screen.IsOpen, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
        }

        [Test]
        public void ClosingRestoresWhateverTimescaleItPausedFrom()
        {
            Time.timeScale = 0.5f;   // e.g. a slow-mo beat
            _screen.Open();
            Assert.That(Time.timeScale, Is.EqualTo(0f), "open must freeze regardless of the prior speed");

            _screen.Close();
            Assert.That(_screen.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(0.5f), "close must restore the speed it paused from, not assume 1");
        }

        [Test]
        public void OpeningTwiceIsANoOp()
        {
            _screen.Open();
            Time.timeScale = 0.3f;   // if Open() re-ran, this would get clobbered as the "prior" speed
            _screen.Open();

            _screen.Close();
            Assert.That(Time.timeScale, Is.EqualTo(1f), "the second Open() must not have overwritten the saved pre-pause speed");
        }

        // ------------------------------------------------------------------ MV-440: the backdrop must
        // never outlive the screen it belongs to (see WeaponsScreen's own MV-440 comments on Build()).
        // Fails on f2aab92: that commit builds Background as a direct Canvas child, active from Build()
        // onward, with only "_root" (a grandchild) toggled by Open()/Close() — Build() never deactivates
        // Background, so BackgroundIsHiddenBeforeTheScreenIsEverOpened below fails with
        // "Expected: False But was: True" against f2aab92, before Open() has ever been called.

        [Test]
        public void BackgroundIsHiddenBeforeTheScreenIsEverOpened()
        {
            // Scene load calls Start() -> Build() on its own (RuntimeInitializeOnLoadMethod Install()),
            // but EditMode tests never run the player loop, so Start() never fires here — invoke the
            // private Build() directly instead of Open(), so this pins the state a player sees before
            // ever tapping WEAPONS, not merely "after a round trip through Open()/Close()".
            typeof(WeaponsScreen)
                .GetMethod("Build", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(_screen, null);

            Assert.That(_screen.Background, Is.Not.Null);
            Assert.That(_screen.Background.gameObject.activeInHierarchy, Is.False,
                "the backdrop must not be visible before the screen has ever been opened");
        }

        [Test]
        public void BackgroundTracksOpenAndClose()
        {
            _screen.Open();
            Assert.That(_screen.Background.gameObject.activeInHierarchy, Is.True,
                "the backdrop must come up together with the rest of the screen on Open()");

            _screen.Close();
            Assert.That(_screen.Background.gameObject.activeInHierarchy, Is.False,
                "the backdrop must go back down together with the rest of the screen on Close()");
        }
    }
}
