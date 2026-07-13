using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Factories;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The factory, running and then not (YT-78).
    ///
    /// PlayMode, because all of this is Awake/Update/signal behaviour: FactoryLife builds itself off
    /// the Hutch's bounds in Awake, and the whole point of the ticket is what happens over time.
    /// </summary>
    public sealed class FactoryLifePlayTests
    {
        private GameObject _hutch;
        private GameObject _life;

        [SetUp]
        public void SetUp()
        {
            // A Hutch to run. MowerHutch [RequireComponent]s an EnemySpawner, so this is the whole
            // factory as far as the art layer can see it.
            _hutch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutch.name = "Mower Hutch";
            _hutch.transform.position = new Vector3(0f, 1f, 15f);
            _hutch.transform.localScale = new Vector3(3f, 2f, 3f);
            _hutch.AddComponent<MowerHutch>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_life != null) Object.Destroy(_life);
            if (_hutch != null) Object.Destroy(_hutch);
        }

        private IEnumerator InstallLife()
        {
            // Self-installs at AfterSceneLoad in the game; that moment is long gone inside a test, so
            // stand it up by hand — which is also the point of the code-driven rule: it can be.
            _life = new GameObject("FactoryLife");
            _life.AddComponent<FactoryLife>();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheFactoryRuns_WhileItIsAlive()
        {
            yield return InstallLife();

            var life = _life.GetComponent<FactoryLife>();
            Assert.IsTrue(life.Running, "the factory isn't running. It is alive and it makes robots — " +
                                        "a machine that shows no sign of working is scenery, and the " +
                                        "one thing the objective must never be is scenery (YT-38).");

            var renderers = _life.GetComponentsInChildren<MeshRenderer>();
            Assert.IsNotEmpty(renderers, "the factory has no moving parts at all.");
            Assert.IsTrue(renderers.All(r => r.enabled), "parts of the machine are already switched off.");

            Assert.IsNotNull(_life.GetComponentInChildren<ParticleSystem>(),
                "nothing is coming out of the stack — the exhaust is what says it is BURNING something.");
        }

        /// <summary>
        /// The AC, in one test: it goes still when destroyed.
        ///
        /// And "still" has to mean GONE, not merely stopped. MowerHutch hides its own body but keeps
        /// its GameObject alive — the robots it already spawned are parented to it and must keep
        /// fighting — so an impeller left turning would hang in mid-air over a factory that isn't
        /// there any more.
        /// </summary>
        [UnityTest]
        public IEnumerator TheFactoryGoesStill_WhenItIsDestroyed()
        {
            yield return InstallLife();
            var life = _life.GetComponent<FactoryLife>();

            HudSignals.EmitFactoryDestroyed(_hutch.transform.position);
            yield return null;

            Assert.IsFalse(life.Running, "the factory is still running after being destroyed.");

            foreach (var r in _life.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                Assert.IsFalse(r.enabled,
                    $"'{r.name}' is still being drawn. The Hutch hides its own body on death, so this " +
                    "is a part of a machine floating in the air where the machine used to be.");
            }

            var exhaust = _life.GetComponentInChildren<ParticleSystem>();
            Assert.IsFalse(exhaust.emission.enabled,
                "it is still smoking. The source is gone; nothing is burning any more.");
        }

        /// <summary>Ambience is never the point of the frame. The existing motes are held to the same
        /// line (AmbiencePlayTests) and the exhaust has to live inside it too.</summary>
        [UnityTest]
        public IEnumerator TheExhaust_StaysInsideTheFrameBudget()
        {
            yield return InstallLife();

            var exhaust = _life.GetComponentInChildren<ParticleSystem>();
            Assert.That(exhaust.main.maxParticles, Is.LessThanOrEqualTo(200),
                "the factory's exhaust is not the point of the frame; it must stay well inside budget.");
        }
    }
}
