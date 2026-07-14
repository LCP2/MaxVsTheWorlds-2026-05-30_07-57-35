using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Factories;
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
        private GameObject _hutch2;
        private GameObject _life2;

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
            foreach (GameObject go in new[] { _life, _hutch, _life2, _hutch2 })
                if (go != null) Object.Destroy(go);
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

            // Destroy the MACHINE, not "a machine". This used to fire the global FactoryDestroyed
            // signal, which is a position with no identity attached — and once the yard had two
            // factories (YT-92), listening to it meant the first kill stopped BOTH machines, leaving a
            // live, spawning hutch standing there with a dead impeller.
            _hutch.GetComponent<MowerHutch>().TakeDamage(
                new DamageInfo(100000f, _hutch.transform.position, Vector3.forward, Team.Player));
            for (int i = 0; i < 2; i++) yield return null;

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

        /// <summary>
        /// Two factories, two machines — and breaking one leaves the other running (YT-92).
        ///
        /// This is the bug a second factory would have shipped with: the art layer stopped itself on a
        /// GLOBAL "a factory was destroyed" signal, which carries a position and no identity. The first
        /// kill would have frozen the impeller, killed the vents and cut the exhaust on a factory that
        /// was still very much alive and still pouring robots at the player — a machine that looks dead
        /// and behaves alive, which is the single most misleading thing an objective can do.
        /// </summary>
        [UnityTest]
        public IEnumerator BreakingOneFactory_LeavesTheOtherRunning()
        {
            yield return InstallLife();
            var first = _life.GetComponent<FactoryLife>();

            _hutch2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutch2.name = "Greenhouse Hutch";
            _hutch2.transform.position = new Vector3(20f, 1f, 15f);
            _hutch2.transform.localScale = new Vector3(3f, 2f, 3f);
            var hutch2 = _hutch2.AddComponent<MowerHutch>();

            _life2 = new GameObject("FactoryLife (2)");
            _life2.SetActive(false);                       // bound before Awake builds the machine
            var second = _life2.AddComponent<FactoryLife>();
            second.Bind(hutch2);
            _life2.SetActive(true);
            yield return null;

            _hutch.GetComponent<MowerHutch>().TakeDamage(
                new DamageInfo(100000f, _hutch.transform.position, Vector3.forward, Team.Player));
            for (int i = 0; i < 2; i++) yield return null;

            Assert.IsFalse(first.Running, "the factory that was destroyed is still running.");
            Assert.IsTrue(second.Running,
                "the OTHER factory stopped too. It is alive, it is spawning robots, and its machine " +
                "has gone dead — the player has no way to tell it is still the thing hunting them.");
            Assert.IsTrue(hutch2.IsAlive, "the second factory died with the first");
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
