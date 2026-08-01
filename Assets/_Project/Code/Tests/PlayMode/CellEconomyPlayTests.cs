using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The power-cell economy recut's weakened-damage rule (WV-227): a live PlayerHealth needs its
    /// Awake to have run (to seed <c>Current</c> from <c>Max</c>) before TakeDamage means anything, so
    /// this needs a frame to yield — a plain EditMode [Test] can't. The rest of WV-227 (efficiency
    /// formula, IsWeakened flips) needs no MonoBehaviour lifecycle and lives in EditMode's
    /// CellEconomyTests.
    /// </summary>
    public sealed class CellEconomyPlayTests
    {
        private GameObject _go;
        private PlayerHealth _health;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PickupWallet.Reset();
            DevMode.Reset();
            DevTuning.Reset();
            _go = new GameObject("Max", typeof(CharacterController), typeof(PlayerController),
                                 typeof(PlayerHealth));
            _health = _go.GetComponent<PlayerHealth>();
            yield return null;   // let Awake run
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            PickupWallet.Reset();
            DevMode.Reset();
            DevTuning.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator WeakenedMaxTakesExtraDamage()
        {
            float before = _health.Current;

            // Full reserve: the authored hit lands as-is.
            PickupWallet.AddPowerCell();
            _health.TakeDamage(new DamageInfo(10f, _go.transform.position, Vector3.forward, Team.Enemy));
            float afterFullReserve = before - _health.Current;
            Assert.That(afterFullReserve, Is.EqualTo(10f).Within(1e-3f), "unweakened damage must land unscaled");

            // Empty reserve: the same hit must land harder.
            PickupWallet.Reset();
            float beforeWeakened = _health.Current;
            _health.TakeDamage(new DamageInfo(10f, _go.transform.position, Vector3.forward, Team.Enemy));
            float afterWeakened = beforeWeakened - _health.Current;
            Assert.That(afterWeakened, Is.EqualTo(10f * PlayerHealth.DefaultWeakenedDamageMultiplier).Within(1e-3f),
                "at 0 cells the same hit must be scaled by the weakened-damage multiplier");
            yield break;
        }
    }
}
