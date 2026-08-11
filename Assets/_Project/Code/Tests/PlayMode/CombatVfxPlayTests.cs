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

        /// <summary>
        /// MV-330: the Blinker's blink used to be a silent, single-frame snap — nothing subscribed to
        /// it at all. This proves the surge fires immediately (the departure beat) and the flash fires
        /// a second time after the stagger (the arrival beat), so the sequence actually reaches two
        /// different points rather than one flash at the origin.
        /// </summary>
        [UnityTest]
        public IEnumerator Director_ReactsToABlinkerTeleport_WithASurgeAndTwoFlashes()
        {
            yield return null;

            var surge = FindSystem("TeleportSurge");
            var flash = FindSystem("TeleportFlash");
            Assert.IsNotNull(surge, "the teleport surge system was never built");
            Assert.IsNotNull(flash, "the teleport flash system was never built");

            int surgeBefore = surge.particleCount;
            int flashBefore = flash.particleCount;

            HudSignals.EmitBlinkerTeleported(new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f));
            yield return null;

            Assert.That(surge.particleCount, Is.GreaterThan(surgeBefore),
                "the energy-surge burst never fired at the departure point");
            int flashAfterDeparture = flash.particleCount;
            Assert.That(flashAfterDeparture, Is.GreaterThan(flashBefore),
                "the vanish flash never fired at the departure point");

            // Past the ~0.08s stagger but comfortably short of the flash particle's own ~0.16s
            // lifetime, so the departure flash is still alive when the arrival flash joins it —
            // a tight window is the whole point: it proves TWO flashes exist, not one that decayed
            // before a second, later read could re-count it.
            yield return new WaitForSeconds(0.1f);

            Assert.That(flash.particleCount, Is.GreaterThan(flashAfterDeparture),
                "the arrival flash never fired at the destination — only one flash played, not two");
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
