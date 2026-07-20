using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-109. The husk exists because the factory's body is switched off on the frame it dies, so
    /// what the player watches during the destruction beat is entirely this object.
    ///
    /// Three ways it can be wrong that no EditMode test of the timing curve can see: it never appears
    /// at all (and the building still blinks out), it appears but never leaves (a wreck parked in the
    /// yard for the rest of the run, still costing a draw call), or it carries a collider and becomes
    /// a permanent invisible wall in the middle of the shed.
    /// </summary>
    public sealed class FactoryHuskPlayTests
    {
        private GameObject _hutchGo, _huskGo;

        private IEnumerator BuildFactoryWithHusk()
        {
            _hutchGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutchGo.name = "Mower Hutch";
            _hutchGo.transform.position = new Vector3(0f, 1f, 0f);
            _hutchGo.transform.localScale = new Vector3(3f, 2f, 3f);
            _hutchGo.AddComponent<MowerHutch>();

            yield return null;

            _huskGo = new GameObject("FactoryHusk");
            _huskGo.SetActive(false);
            _huskGo.AddComponent<FactoryHusk>().Bind(_hutchGo.GetComponent<MowerHutch>());
            _huskGo.SetActive(true);

            yield return null;
        }

        private void Kill() => _hutchGo.GetComponent<MowerHutch>().TakeDamage(
            new DamageInfo(100000f, _hutchGo.transform.position, Vector3.forward, Team.Player));

        private Transform Husk() => _huskGo.transform.Find("Husk");

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[] { _hutchGo, _huskGo })
                if (go != null) Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NoHuskWhileTheFactoryIsAlive()
        {
            yield return BuildFactoryWithHusk();

            for (int i = 0; i < 10; i++) yield return null;

            Assert.That(Husk(), Is.Null, "a wreck appeared beside a factory that is still standing");
        }

        [UnityTest]
        public IEnumerator TheWreckStandsWhereTheBuildingWas()
        {
            yield return BuildFactoryWithHusk();

            Vector3 was = _hutchGo.GetComponent<Renderer>().bounds.center;
            Kill();
            yield return null;
            yield return null;

            Transform husk = Husk();
            Assert.That(husk, Is.Not.Null, "the building vanished with nothing left in its place");
            // Level with the body it replaces, not sunk or floating, at the instant it appears.
            Assert.That(husk.position.y, Is.EqualTo(was.y).Within(0.2f));
            Assert.That(new Vector2(husk.position.x, husk.position.z),
                Is.EqualTo(new Vector2(was.x, was.z)).Using<Vector2>(
                    (a, b) => Vector2.Distance(a, b) < 0.2f ? 0 : 1),
                "the wreck is not standing where the factory was");
        }

        [UnityTest]
        public IEnumerator TheWreckIsSceneryAndNeverBlocksTheFight()
        {
            yield return BuildFactoryWithHusk();

            Kill();
            yield return null;
            yield return null;

            Transform husk = Husk();
            Assert.That(husk, Is.Not.Null);
            Assert.That(husk.GetComponentsInChildren<Collider>(true), Is.Empty,
                "the wreck carries a collider — it would pin robots against a building that is gone");
        }

        [UnityTest]
        public IEnumerator TheWreckGoesDown_AndTakesItselfAway()
        {
            yield return BuildFactoryWithHusk();

            Kill();
            yield return null;
            yield return null;

            Transform husk = Husk();
            Assert.That(husk, Is.Not.Null);
            float startY = husk.position.y;

            // Past the shudder and the whole collapse, with margin for frame timing.
            float budget = FactoryDeathTiming.ShudderSeconds + FactoryDeathTiming.CollapseSeconds + 1.5f;
            float waited = 0f;
            float lowest = startY;
            while (waited < budget && Husk() != null)
            {
                lowest = Mathf.Min(lowest, Husk().position.y);
                waited += Time.deltaTime;
                yield return null;
            }

            Assert.That(lowest, Is.LessThan(startY), "the wreck never sank — it just disappeared again");
            Assert.That(Husk(), Is.Null,
                "the wreck is still standing after the beat finished — a renderer the frame keeps paying for");
        }
    }
}
