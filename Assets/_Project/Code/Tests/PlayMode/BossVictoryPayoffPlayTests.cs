using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Bosses;
using MaxWorlds.Pickups;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// End-to-end coverage of the boss-death payoff (YT-152): the kill flings four-plus collectible
    /// parts into the arena, holds the results card back while Max is still out there, and only cues
    /// results when he walks through the exit gate — with a grace timeout so a win can never stall.
    /// Drives the real director; the pure geometry is in the EditMode companion.
    /// </summary>
    public sealed class BossVictoryPayoffPlayTests
    {
        private GameObject _player;
        private GameObject _dir;
        private bool _payoffFired;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            PickupWallet.Reset();
            MaxWorlds.Upgrades.UpgradeState.Reset();
            _payoffFired = false;
            HudSignals.BossPayoffFinished += OnPayoff;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            HudSignals.BossPayoffFinished -= OnPayoff;
            if (_dir != null) Object.Destroy(_dir);
            if (_player != null) Object.Destroy(_player);
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                Object.Destroy(p.gameObject);
            Time.timeScale = 1f;
            PickupWallet.Reset();
            MaxWorlds.Upgrades.UpgradeState.Reset();
            yield return null;
        }

        private void OnPayoff() => _payoffFired = true;

        private BossVictoryPayoff MakeDirector()
        {
            _dir = new GameObject("BossVictoryPayoff Test");
            return _dir.AddComponent<BossVictoryPayoff>();
        }

        private static int PartCount() =>
            Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None)
                  .Count(p => p.Kind == PickupKind.Part);

        [UnityTest]
        public IEnumerator BossDeath_FlingsFourPlusParts_AndHoldsResultsUntilMaxWalksThroughTheDoor()
        {
            _player = new GameObject("Player Test") { tag = "Player" };
            _player.transform.position = Vector3.zero;   // far from the arena and the door

            var d = MakeDirector();
            d.doorArmDelay = 0f;
            d.resultsTimeout = 30f;      // long, so the DOOR — not the timeout — is what ends this test
            yield return null;           // Awake / OnEnable

            HudSignals.EmitBossDefeated();
            yield return new WaitForSeconds(0.9f);   // let the parts fly out and land

            Assert.GreaterOrEqual(PartCount(), 4, "the boss death should fling at least four parts");
            Assert.IsFalse(_payoffFired, "results must be held while Max is still out in the arena");

            // Walk Max into the open gateway in the back wall.
            var layout = MaxWorlds.Arena.BackyardPathLayout.Default;
            _player.transform.position = new Vector3(0f, 0f, layout.ArenaEndZ - 0.5f);
            yield return null;
            yield return null;

            Assert.IsTrue(_payoffFired, "walking through the door should finish the payoff and cue results");
        }

        [UnityTest]
        public IEnumerator IfMaxNeverWalksOut_TheGraceTimeoutStillCuesResults()
        {
            _player = new GameObject("Player Test") { tag = "Player" };
            _player.transform.position = Vector3.zero;   // stays far from the door

            var d = MakeDirector();
            d.doorArmDelay = 0f;
            d.resultsTimeout = 0.25f;    // short grace for the test
            yield return null;

            HudSignals.EmitBossDefeated();
            Assert.IsFalse(_payoffFired, "results shouldn't fire on the same frame as the kill");

            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(_payoffFired, "the grace timeout should cue results even if Max never walks out");
        }
    }
}
