using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Max's health ceiling is tunable at runtime through the Settings panel (YT-105, YT-120). Moving
    /// a ceiling under a value that is already sitting on it is the fiddly part, so it gets its own
    /// tests. Since YT-120 the override applies with no dev flag — the panel is a real feature now.
    ///
    /// MV-464: moved from PlayMode. <see cref="PlayerHealth.Initialize"/> is called directly instead
    /// of relying on Awake, which never runs from AddComponent outside Play mode.
    /// </summary>
    public sealed class PlayerMaxHealthTuningTests
    {
        private GameObject _go;
        private PlayerHealth _health;

        [SetUp]
        public void SetUp()
        {
            DevMode.Reset();
            DevTuning.Reset();   // overrides are no longer wiped by DevMode.Reset — clear explicitly
            _go = new GameObject("Max", typeof(CharacterController), typeof(PlayerController),
                                 typeof(PlayerHealth));
            _health = _go.GetComponent<PlayerHealth>();
            _health.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            DevMode.Reset();
            DevTuning.Reset();   // don't leak an override into the next fixture
        }

        [Test]
        public void WithNoOverride_MaxIsTheAuthoredNumber()
        {
            Assert.That(_health.Max, Is.EqualTo(_health.AuthoredMax).Within(0.001f));
            Assert.That(_health.Normalized, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void RaisingTheCeiling_GivesHeadroomNotAFreeHeal()
        {
            float before = _health.Current;

            DevTuning.PlayerMaxHealth = _health.AuthoredMax * 2f;
            _health.RefreshMax();

            Assert.That(_health.Current, Is.EqualTo(before).Within(0.001f),
                "Raising the ceiling must not top Max up — that would be a heal nobody asked for.");
            Assert.That(_health.Normalized, Is.EqualTo(0.5f).Within(0.001f),
                "The HUD bar should now read half full against the taller ceiling.");
        }

        [Test]
        public void LoweringTheCeilingBelowCurrentHp_ClampsAndNotifies()
        {
            float notified = -1f;
            _health.Changed += hp => notified = hp;

            DevTuning.PlayerMaxHealth = 40f;
            _health.RefreshMax();

            Assert.That(_health.Current, Is.EqualTo(40f).Within(0.001f),
                "Current HP above the new ceiling would draw a bar past 100%.");
            Assert.That(notified, Is.EqualTo(40f).Within(0.001f),
                "The HUD binds Changed; without a fire it keeps drawing the stale fraction.");
        }

        [Test]
        public void TheOverrideAppliesWithNoDevMode()
        {
            // The inverse of what this used to assert. The Settings panel is a real, always-present
            // feature now (YT-120), so a ceiling the player dialled in has to take effect with dev
            // mode off — that is the whole point of shipping the sliders.
            Assert.That(DevMode.Enabled, Is.False, "precondition: not in dev mode");
            DevTuning.PlayerMaxHealth = 250f;
            _health.RefreshMax();

            Assert.That(_health.Max, Is.EqualTo(250f).Within(0.001f),
                "a moved slider must change Max's ceiling in a normal session");
        }
    }
}
