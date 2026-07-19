using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Max's health ceiling became tunable at runtime (YT-105). Moving a ceiling under a value that
    /// is already sitting on it is the fiddly part, so it gets its own tests.
    /// </summary>
    public sealed class PlayerMaxHealthTuningPlayTests
    {
        private GameObject _go;
        private PlayerHealth _health;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DevMode.Reset();
            _go = new GameObject("Max", typeof(CharacterController), typeof(PlayerController),
                                 typeof(PlayerHealth));
            _health = _go.GetComponent<PlayerHealth>();
            yield return null;   // let Awake run
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            DevMode.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator WithNoOverride_MaxIsTheAuthoredNumber()
        {
            Assert.That(_health.Max, Is.EqualTo(_health.AuthoredMax).Within(0.001f));
            Assert.That(_health.Normalized, Is.EqualTo(1f).Within(0.001f));
            yield break;
        }

        [UnityTest]
        public IEnumerator RaisingTheCeiling_GivesHeadroomNotAFreeHeal()
        {
            DevMode.Enabled = true;
            float before = _health.Current;

            DevTuning.PlayerMaxHealth = _health.AuthoredMax * 2f;
            _health.RefreshMax();
            yield return null;

            Assert.That(_health.Current, Is.EqualTo(before).Within(0.001f),
                "Raising the ceiling must not top Max up — that would be a heal nobody asked for.");
            Assert.That(_health.Normalized, Is.EqualTo(0.5f).Within(0.001f),
                "The HUD bar should now read half full against the taller ceiling.");
        }

        [UnityTest]
        public IEnumerator LoweringTheCeilingBelowCurrentHp_ClampsAndNotifies()
        {
            DevMode.Enabled = true;

            float notified = -1f;
            _health.Changed += hp => notified = hp;

            DevTuning.PlayerMaxHealth = 40f;
            _health.RefreshMax();
            yield return null;

            Assert.That(_health.Current, Is.EqualTo(40f).Within(0.001f),
                "Current HP above the new ceiling would draw a bar past 100%.");
            Assert.That(notified, Is.EqualTo(40f).Within(0.001f),
                "The HUD binds Changed; without a fire it keeps drawing the stale fraction.");
        }

        [UnityTest]
        public IEnumerator WithDevModeOff_TheOverrideIsIgnored()
        {
            DevTuning.PlayerMaxHealth = 999f;
            DevMode.Enabled = false;
            yield return null;

            Assert.That(_health.Max, Is.EqualTo(_health.AuthoredMax).Within(0.001f),
                "A tuning override must never reach a player's session.");
        }
    }
}
