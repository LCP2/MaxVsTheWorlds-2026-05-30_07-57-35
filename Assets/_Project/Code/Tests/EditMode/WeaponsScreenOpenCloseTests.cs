using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
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

        /// <summary>MV-515 AC6: THE RIG's own half of "no active user-facing Text says Part/Parts" —
        /// scans every built Text component's rendered string once the board is actually up, not the
        /// source. Proven to fail on cdb39c7 (main HEAD before this ticket), whose PARTS tray literally
        /// set its label text to "PARTS" and its sub-caption to "tap a node to fit one"/"N banked".</summary>
        [Test]
        public void OpeningNeverShowsUserFacingPartOrPartsText_MV515()
        {
            _screen.Open();

            foreach (var text in _go.GetComponentsInChildren<Text>(true))
                Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text.text ?? string.Empty, @"\bParts?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase), Is.False,
                    $"THE RIG Text on '{text.gameObject.name}' still reads \"{text.text}\" — MV-515 renamed the currency to Supercell");
        }

    }
}
