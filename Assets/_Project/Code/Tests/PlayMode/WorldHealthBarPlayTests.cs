using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The floating HP readout over each unit (YT-111).
    ///
    /// Measured in world metres rather than authored numbers, for the reason FactoryBarPlayTests
    /// records: a bar authored at 180 px on a body that carries a scale renders metres wide, and
    /// only a measurement in the units the player sees would catch it. Robots make that sharper —
    /// a rusher is 0.8x0.7x0.8 and a bruiser 1.15 all round, and the scale is stamped on AFTER the
    /// component exists.
    /// </summary>
    public sealed class WorldHealthBarPlayTests
    {
        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            yield return null;
        }

        /// <summary>A stand-in unit, so these test the bar rather than the robot AI.</summary>
        private sealed class FakeUnit : MonoBehaviour, IHealthReadout
        {
            public float Hp = 100f;
            public float MaxHp = 100f;
            public bool Alive = true;
            public float HealthNormalized => MaxHp > 0f ? Mathf.Clamp01(Hp / MaxHp) : 0f;
            public float HealthCurrent => Hp;
            public string ReadoutName => "TEST UNIT";
            public bool IsAlive => Alive;
        }

        private FakeUnit NewUnit(Vector3 bodyScale, bool alwaysShow = false)
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _go.transform.localScale = bodyScale;
            var unit = _go.AddComponent<FakeUnit>();
            WorldHealthBar.Attach(_go, unit, heightAboveCentre: 1.15f, worldWidth: 1.1f, alwaysShow);
            return unit;
        }

        private WorldHealthBar Bar => _go.GetComponent<WorldHealthBar>();
        private RectTransform Canvas => (RectTransform)_go.GetComponentInChildren<Canvas>(true).transform;

        /// <summary>A Max-like unit: a life bar with a water gauge stacked above (YT-121).</summary>
        private FakeUnit NewUnitWithWater(System.Func<float> water)
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var unit = _go.AddComponent<FakeUnit>();
            WorldHealthBar.Attach(_go, unit, heightAboveCentre: 1.55f, worldWidth: 1.5f,
                                  alwaysShow: true, secondary: water,
                                  secondaryColor: new Color(0.2f, 0.62f, 0.92f));
            return unit;
        }

        private static float WorldWidth(RectTransform rt) => rt.sizeDelta.x * rt.lossyScale.x;

        // ---------------------------------------------------------------- Max's water stack (YT-121)

        [UnityTest]
        public IEnumerator MaxsBarCarriesAWaterGaugeStackedAboveItsLifeBar()
        {
            float water = 0.5f;
            NewUnitWithWater(() => water);
            yield return null;

            Assert.That(Bar.HasSecondary, Is.True, "Max's stack has no water gauge above the life bar");

            var waterFill = FindImage("Water Fill");
            var lifeFill = FindImage("Fill");
            Assert.That(waterFill.fillAmount, Is.EqualTo(0.5f).Within(0.02f), "the gauge did not read the tank");

            // Stacked ABOVE: the water gauge sits higher on screen than the life bar.
            var wc = new Vector3[4]; var lc = new Vector3[4];
            waterFill.rectTransform.GetWorldCorners(wc);
            lifeFill.rectTransform.GetWorldCorners(lc);
            Assert.That(wc[1].y, Is.GreaterThan(lc[1].y),
                "the water gauge must sit above the life bar, not below or on it");
        }

        [UnityTest]
        public IEnumerator TheWaterGaugeTracksTheTankLive()
        {
            float water = 1f;
            NewUnitWithWater(() => water);
            yield return null;

            water = 0.2f;
            yield return null;
            Assert.That(FindImage("Water Fill").fillAmount, Is.EqualTo(0.2f).Within(0.02f),
                "draining the tank must drain the gauge without a rebuild");
        }

        [UnityTest]
        public IEnumerator ARobotHasNoWaterGauge()
        {
            NewUnit(Vector3.one).Hp = 50f;
            yield return null;
            Assert.That(Bar.HasSecondary, Is.False, "only Max carries a water gauge; robots must not");
        }

        // ---------------------------------------------------------------- size and place

        [UnityTest]
        public IEnumerator TheBarIsAboutAsWideAsAskedForRegardlessOfTheBodysScale()
        {
            foreach (Vector3 scale in new[]
                     { Vector3.one, new Vector3(0.8f, 0.7f, 0.8f), new Vector3(1.15f, 1.15f, 1.15f) })
            {
                NewUnit(scale).Hp = 50f;
                yield return null;

                float w = WorldWidth(Canvas);
                Assert.That(w, Is.EqualTo(1.1f).Within(0.05f),
                            $"a body scaled {scale} rendered a {w:0.00} m bar — the scale leaked in");

                Object.DestroyImmediate(_go);
                _go = null;
            }
        }

        [UnityTest]
        public IEnumerator TheBarSitsAboveTheUnitNotInsideItOrInOrbit()
        {
            NewUnit(new Vector3(0.8f, 0.7f, 0.8f)).Hp = 50f;
            yield return null;

            float above = Canvas.position.y - _go.transform.position.y;
            Assert.That(above, Is.GreaterThan(0.5f), "the bar is buried in the body");
            Assert.That(above, Is.LessThan(2.5f), "the bar is floating in orbit above the unit");
        }

        // ---------------------------------------------------------------- what it says

        [UnityTest]
        public IEnumerator TheBarTracksDamageAndPrintsTheNumber()
        {
            FakeUnit unit = NewUnit(Vector3.one);
            unit.Hp = 25f;
            yield return null;

            var fill = FindImage("Fill");
            Assert.That(fill.fillAmount, Is.EqualTo(0.25f).Within(0.01f));

            bool printed = false;
            foreach (UnityEngine.UI.Text t in _go.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                if (t.text == "25") printed = true;

            Assert.That(printed, Is.True, "the bar shows no numeric HP — the ticket asks for the figure");
        }

        [UnityTest]
        public IEnumerator TheBarNamesTheUnit()
        {
            NewUnit(Vector3.one).Hp = 50f;
            yield return null;

            bool named = false;
            foreach (UnityEngine.UI.Text t in _go.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                if (t.text == "TEST UNIT") named = true;

            Assert.That(named, Is.True, "the bar does not say what it is sitting on");
        }

        [UnityTest]
        public IEnumerator TheBarChangesColourAsThingsGetSerious()
        {
            FakeUnit unit = NewUnit(Vector3.one);
            unit.Hp = 90f;
            yield return null;
            Color healthy = FindImage("Fill").color;

            unit.Hp = 10f;
            yield return null;
            Color critical = FindImage("Fill").color;

            Assert.That(critical, Is.Not.EqualTo(healthy),
                        "a bar at 10% looks the same as one at 90%");
            Assert.That(critical.r, Is.GreaterThan(critical.g),
                        "the critical colour should read as danger, not as health");
        }

        // ---------------------------------------------------------------- clutter rules

        [UnityTest]
        public IEnumerator AnUntouchedRobotDoesNotCarryABar()
        {
            NewUnit(Vector3.one).Hp = 100f;
            yield return null;

            Assert.That(Bar.Showing, Is.False,
                        "a field of full-health robots each with a bar is the clutter the ticket warned about");
        }

        [UnityTest]
        public IEnumerator BeingHitBringsTheBarOut()
        {
            FakeUnit unit = NewUnit(Vector3.one);
            unit.Hp = 100f;
            yield return null;
            Assert.That(Bar.Showing, Is.False);

            unit.Hp = 99f;
            yield return null;
            Assert.That(Bar.Showing, Is.True, "took damage and still shows nothing");
        }

        [UnityTest]
        public IEnumerator MaxAlwaysCarriesHisOwnBarEvenAtFullHealth()
        {
            NewUnit(Vector3.one, alwaysShow: true).Hp = 100f;
            yield return null;

            Assert.That(Bar.Showing, Is.True,
                        "you should be able to find your own health without being hit first");
        }

        [UnityTest]
        public IEnumerator ADeadUnitTakesItsBarWithIt()
        {
            FakeUnit unit = NewUnit(Vector3.one, alwaysShow: true);
            unit.Hp = 0f;
            unit.Alive = false;
            yield return null;

            Assert.That(Bar.Showing, Is.False, "a corpse is still advertising its health");
        }

        // ---------------------------------------------------------------- the real robot

        /// <summary>
        /// Robots are pooled: a dead one is deactivated and handed back, not destroyed. The bar is a
        /// child so it returns with the body — this pins that, because the failure mode is a second
        /// wave that spawns with no bars and looks like the feature was never built.
        /// </summary>
        [UnityTest]
        public IEnumerator ARecycledRobotComesBackWithItsBar()
        {
            _go = new GameObject("Robot", typeof(CharacterController));
            var robot = _go.AddComponent<RobotEnemy>();
            yield return null;

            Assert.IsNotNull(_go.GetComponent<WorldHealthBar>(), "a robot spawned with no bar");

            _go.SetActive(false);
            yield return null;
            _go.SetActive(true);
            robot.ResetState();
            yield return null;

            var bar = _go.GetComponent<WorldHealthBar>();
            Assert.IsNotNull(bar, "the bar did not survive being pooled");
            Assert.IsNotNull(_go.GetComponentInChildren<Canvas>(true),
                             "the bar's canvas did not come back with the recycled body");
        }

        /// <summary>
        /// YT-122: a robot shows its bar from the moment it spawns, at full health — not only once
        /// it has been hit. That "hidden until hit" default (YT-111) is what read on device as the
        /// robots having no life bars at all.
        /// </summary>
        [UnityTest]
        public IEnumerator AFullHealthRobotAlreadyShowsItsColourCodedBar()
        {
            _go = new GameObject("Robot", typeof(CharacterController));
            _go.AddComponent<RobotEnemy>();
            yield return null;

            var bar = _go.GetComponent<WorldHealthBar>();
            Assert.That(bar.Showing, Is.True,
                "a full-health robot must already show its bar — that is the whole ticket");

            // Full health reads green (the calm end of the shared ramp), so a wall of them stays quiet.
            Color full = FindImage("Fill").color;
            Assert.That(full.g, Is.GreaterThan(full.r).And.GreaterThan(full.b),
                "a healthy robot's bar should be green, not already alarming");
        }

        private UnityEngine.UI.Image FindImage(string name)
        {
            foreach (UnityEngine.UI.Image i in _go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (i.name == name) return i;
            Assert.Fail($"no '{name}' image on the bar");
            return null;
        }
    }
}
