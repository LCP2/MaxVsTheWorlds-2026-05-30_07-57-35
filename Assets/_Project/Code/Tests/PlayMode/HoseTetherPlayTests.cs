using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Hose;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The hose weapon core wired up for real (YT-129): the leash actually constrains Max through his
    /// CharacterController, the tap + tether install themselves with no scene wiring, and the opening
    /// spray is the short-and-wide base the nozzle upgrades build on.
    /// </summary>
    public sealed class HoseTetherPlayTests
    {
        private GameObject _max;
        private GameObject _director;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DevTuning.Reset();
            if (_max != null) Object.Destroy(_max);
            if (_director != null) Object.Destroy(_director);
            yield return null;
            // Taps register into a static list — clear any this test spawned so the next test starts clean.
            foreach (var t in Object.FindObjectsByType<Tap>(FindObjectsSortMode.None))
                Object.Destroy(t.gameObject);
            foreach (var d in Object.FindObjectsByType<HoseDirector>(FindObjectsSortMode.None))
                Object.Destroy(d.gameObject);
            yield return null;
        }

        // Iterator helpers can't take out/ref params, so the tether lands in a field the tests read.
        private IEnumerator LeashedMax(Tap tap)
        {
            _max = new GameObject("Max");
            _max.AddComponent<CharacterController>();
            _max.transform.position = tap.transform.position;   // start plugged in, at the tap
            var tether = _max.AddComponent<HoseTether>();
            tether.SetTap(tap);
            yield return null;   // Awake builds the hose renderer
        }

        [UnityTest]
        public IEnumerator TheLeashPinsMaxToTheTether_ThroughHisCharacterController()
        {
            var tap = Tap.Create("Test Tap", Vector3.zero);
            yield return LeashedMax(tap);

            // Bolt 100 m away — a CharacterController caches its own position, so this is exactly the
            // teleport the clamp has to survive, not just fight a walk.
            _max.transform.position = new Vector3(0f, 1f, 100f);
            yield return null;   // LateUpdate clamps

            float dist = new Vector2(_max.transform.position.x, _max.transform.position.z).magnitude;
            Assert.That(dist, Is.EqualTo(HoseTether.AuthoredLength).Within(0.05f),
                $"Max ran 100 m from a {HoseTether.AuthoredLength} m tether and wasn't reeled in — " +
                "the leash isn't holding through the CharacterController");
        }

        [UnityTest]
        public IEnumerator InsideTheLeashHeIsNotTugged()
        {
            var tap = Tap.Create("Test Tap", Vector3.zero);
            yield return LeashedMax(tap);

            var spot = new Vector3(0f, 1f, 5f);   // 5 m out, well inside 20
            _max.transform.position = spot;
            yield return null;

            Assert.That(new Vector2(_max.transform.position.x, _max.transform.position.z).magnitude,
                Is.EqualTo(5f).Within(0.05f), "the leash tugged Max while he was well inside its range");
        }

        [UnityTest]
        public IEnumerator TheTetherSliderReLeashesMaxLive()
        {
            var tap = Tap.Create("Test Tap", Vector3.zero);
            yield return LeashedMax(tap);

            DevTuning.HoseTetherLength = 8f;             // as the Settings slider would
            _max.transform.position = new Vector3(0f, 1f, 100f);
            yield return null;

            float dist = new Vector2(_max.transform.position.x, _max.transform.position.z).magnitude;
            Assert.That(dist, Is.EqualTo(8f).Within(0.05f),
                "dropping the tether slider to 8 m must reel Max in to 8 m on the next frame, no push");
        }

        [UnityTest]
        public IEnumerator TheHoseWiresItself_TapAndTether_WithNoSceneWiring()
        {
            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.AddComponent<CharacterController>();
            // The director only wires the ARMED player (the one with a WaterBlaster) — a bare capsule
            // is a test greybox, not Max. Give this one a blaster so it qualifies.
            _max.AddComponent<MaxWorlds.Combat.WaterBlaster>();
            _max.transform.position = HoseDirector.StartTapPosition + new Vector3(0f, 1f, 1f);

            _director = new GameObject("HoseDirector");
            _director.AddComponent<HoseDirector>();
            yield return null;   // director's Update finds Max
            yield return null;

            var tether = _max.GetComponent<HoseTether>();
            Assert.IsNotNull(tether, "the director didn't give Max a hose tether — code-driven wiring failed");
            Assert.Greater(Tap.All.Count, 0, "no starting tap was placed");
            Assert.IsNotNull(tether.Tap, "Max's hose was never plugged into a tap");
        }

        [UnityTest]
        public IEnumerator TheOpeningSprayIsShortAndWide()
        {
            _max = new GameObject("Max");
            _max.transform.position = new Vector3(0f, 1f, 0f);
            var blaster = _max.AddComponent<WaterBlaster>();
            yield return null;

            // Short: the spray reaches far less than the leash, so the hose is about positioning, not
            // sniping. Wide: a broad forgiving fan, the base the nozzle upgrades (YT-133) narrow.
            Assert.Less(blaster.Range, HoseTether.AuthoredLength * 0.5f,
                "the opening spray should be much shorter than the leash — it's short-range by design");
            Assert.LessOrEqual(blaster.Range, 5f, "the opening spray should be short (<= 5 m)");
            Assert.GreaterOrEqual(blaster.ConeHalfAngle, 45f,
                "the opening spray should be a wide, forgiving arc (>= 45 deg half-angle)");
        }
    }
}
