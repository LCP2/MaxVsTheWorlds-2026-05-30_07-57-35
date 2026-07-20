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

        private static float WorldWidth(RectTransform rt) => rt.sizeDelta.x * rt.lossyScale.x;

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

        private UnityEngine.UI.Image FindImage(string name)
        {
            foreach (UnityEngine.UI.Image i in _go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (i.name == name) return i;
            Assert.Fail($"no '{name}' image on the bar");
            return null;
        }
    }
}
