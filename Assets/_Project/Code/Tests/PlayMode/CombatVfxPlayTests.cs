using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-48 — the combat VFX director's lifecycle. This has to be a PlayMode test: Awake and
    /// OnEnable never run in edit mode, so an EditMode version would "pass" without the
    /// director ever having subscribed to anything.
    /// </summary>
    public sealed class CombatVfxPlayTests
    {
        [UnityTest]
        public IEnumerator Director_InstallsItself_AndReactsToCombatSignals()
        {
            yield return null;   // let RuntimeInitializeOnLoadMethod install it

            var director = Object.FindFirstObjectByType<CombatVfx>();
            Assert.IsNotNull(director, "CombatVfx should install itself with no scene wiring");

            var deathSparks = FindSystem("DeathSparks");
            Assert.IsNotNull(deathSparks, "the death-pop system was never built");
            Assert.IsNotNull(deathSparks.GetComponent<ParticleSystemRenderer>().sharedMaterial,
                "no material — the burst would render as nothing in a build");

            int before = deathSparks.particleCount;
            HudSignals.EmitEnemyKilled(new Vector3(0f, 0f, 5f));
            yield return null;

            Assert.That(deathSparks.particleCount, Is.GreaterThan(before),
                "an enemy kill should throw a burst of sparks");
        }

        [UnityTest]
        public IEnumerator Director_UnsubscribesWhenDestroyed()
        {
            yield return null;

            var director = Object.FindFirstObjectByType<CombatVfx>();
            Assert.IsNotNull(director);

            Object.DestroyImmediate(director.gameObject);
            yield return null;

            // HudSignals is static and outlives the scene, so a handler left attached would
            // now be firing into destroyed ParticleSystems.
            Assert.DoesNotThrow(() =>
            {
                HudSignals.EmitDamage(Vector3.zero, 4f);
                HudSignals.EmitEnemyKilled(Vector3.zero);
                HudSignals.EmitFactoryDestroyed(Vector3.zero);
            }, "the destroyed director is still listening to HudSignals — that is a leak");
        }

        private static ParticleSystem FindSystem(string name)
        {
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps.name == name) return ps;
            }
            return null;
        }
    }
}
