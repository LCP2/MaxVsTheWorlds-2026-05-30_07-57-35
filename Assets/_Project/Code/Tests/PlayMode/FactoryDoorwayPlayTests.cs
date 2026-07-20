using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-108. The door gates the spawner — it may only emit while the shutter is open — which puts a
    /// deadlock one mistake away: if the door never opens, the factory never produces, and the whole
    /// level quietly has no enemies in it. That is not a subtle visual regression, it is the game not
    /// happening, and no EditMode test of the geometry can see it because the failure lives in the
    /// handshake between two components over time.
    ///
    /// So these run the real spawner against the real doorway and watch the clock.
    /// </summary>
    public sealed class FactoryDoorwayPlayTests
    {
        private GameObject _hutchGo, _doorGo, _floor;

        private IEnumerator BuildFactoryWithDoor()
        {
            // Something for the door's clearance probe to find, so the face choice runs its real path.
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "Floor";
            _floor.transform.position = new Vector3(0f, -0.5f, 0f);
            _floor.transform.localScale = new Vector3(60f, 1f, 60f);

            _hutchGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutchGo.name = "Mower Hutch";
            _hutchGo.transform.position = new Vector3(0f, 1f, 0f);
            _hutchGo.transform.localScale = new Vector3(3f, 2f, 3f);
            _hutchGo.AddComponent<MowerHutch>();          // brings EnemySpawner via RequireComponent

            yield return null;

            _doorGo = new GameObject("FactoryDoorway");
            _doorGo.SetActive(false);
            _doorGo.AddComponent<FactoryDoorway>().Bind(_hutchGo.GetComponent<MowerHutch>());
            _doorGo.SetActive(true);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[] { _hutchGo, _doorGo, _floor })
                if (go != null) Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheFactoryStillProduces_WithADoorInFrontOfIt()
        {
            yield return BuildFactoryWithDoor();

            var spawner = _hutchGo.GetComponent<EnemySpawner>();
            Assert.That(spawner, Is.Not.Null);

            // Well past the opening cadence (start interval 1.8 s + 0.38 s of shutter travel).
            float waited = 0f;
            while (waited < 6f && spawner.Emitted == 0)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            Assert.That(spawner.Emitted, Is.GreaterThan(0),
                "the door never let a robot out — the factory is gated shut and the level has no enemies");
        }

        [UnityTest]
        public IEnumerator TheShutterActuallyOpens()
        {
            yield return BuildFactoryWithDoor();

            var door = _doorGo.GetComponent<FactoryDoorway>();

            float peak = 0f, waited = 0f;
            while (waited < 6f)
            {
                peak = Mathf.Max(peak, door.Openness);
                waited += Time.deltaTime;
                yield return null;
            }

            Assert.That(peak, Is.GreaterThan(0.75f),
                "the shutter never reached the openness that lets a robot through");
        }

        [UnityTest]
        public IEnumerator TheShutterClosesAgain_SoTheCadenceIsVisible()
        {
            yield return BuildFactoryWithDoor();

            var door = _doorGo.GetComponent<FactoryDoorway>();

            // Wait for it to open...
            float waited = 0f;
            while (waited < 6f && door.Openness < 0.75f) { waited += Time.deltaTime; yield return null; }
            Assert.That(door.Openness, Is.GreaterThan(0.75f), "never opened");

            // ...then for it to come back down. A door stuck open is just a hole in the wall.
            float shut = 1f;
            waited = 0f;
            while (waited < 6f) { shut = Mathf.Min(shut, door.Openness); waited += Time.deltaTime; yield return null; }

            Assert.That(shut, Is.LessThan(0.25f), "the shutter went up and stayed up");
        }

        [UnityTest]
        public IEnumerator ADeadFactoryShutsItsDoorAndKeepsItShut()
        {
            yield return BuildFactoryWithDoor();

            var hutch = _hutchGo.GetComponent<MowerHutch>();
            var door = _doorGo.GetComponent<FactoryDoorway>();

            hutch.TakeDamage(new DamageInfo(100000f, hutch.transform.position, Vector3.forward, Team.Player));
            yield return null;
            yield return null;

            Assert.That(hutch.IsAlive, Is.False, "the factory should be destroyed");

            float peak = 0f, waited = 0f;
            while (waited < 3f) { peak = Mathf.Max(peak, door.Openness); waited += Time.deltaTime; yield return null; }

            Assert.That(peak, Is.LessThan(0.25f),
                "a destroyed factory kept cycling its door — advertising production that has stopped");
        }
    }
}
