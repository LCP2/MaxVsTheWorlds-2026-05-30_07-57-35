using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The boost has to reach the actual gun (YT-67). The maths being right is worth nothing if the
    /// level-up never lands on the blaster — which is exactly the state the slice was in: an XP bar
    /// and a LEVEL popup wired to nothing.
    /// </summary>
    public sealed class PowerRampPlayTests
    {
        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            yield return null;
        }

        private IEnumerator NewBlaster()
        {
            _go = new GameObject("Blaster");
            _go.AddComponent<WaterBlaster>();
            yield return null;   // Awake caches the base numbers and attaches PlayerPower
        }

        [UnityTest]
        public IEnumerator TheBlasterCarriesItsOwnPowerRamp_WithNoSceneWiring()
        {
            yield return NewBlaster();
            Assert.IsNotNull(_go.GetComponent<PlayerPower>(),
                "the ramp must attach itself — a code-driven scene can't wire it by hand");
            Assert.AreEqual(1, _go.GetComponent<PlayerPower>().Level);
        }

        [UnityTest]
        public IEnumerator LevellingUp_ActuallyMakesTheGunHitHarder()
        {
            yield return NewBlaster();
            var blaster = _go.GetComponent<WaterBlaster>();

            float dpsAtStart = blaster.DamagePerSecond;
            Assert.Greater(dpsAtStart, 0f);

            HudSignals.EmitLevelUp(5, Vector3.zero);
            yield return null;

            Assert.AreEqual(5, _go.GetComponent<PlayerPower>().Level);
            Assert.AreEqual(dpsAtStart * PowerRamp.DpsMultiplier(5), blaster.DamagePerSecond, 0.01f,
                "the level-up didn't reach the gun");
            Assert.Greater(blaster.DamagePerSecond, dpsAtStart * 1.6f);
        }

        [UnityTest]
        public IEnumerator AFireRateBoostIsNotSecretlySelfCancelling()
        {
            // The trap: more ticks per second at the same energy cost per tick just empties the tank
            // proportionally faster, so the player fires more often, runs dry sooner, and does the
            // same damage per tankful. The upgrade would feel like nothing. Holding the trigger must
            // therefore cost the SAME per second at every level, while output climbs.
            yield return NewBlaster();
            var blaster = _go.GetComponent<WaterBlaster>();

            float energyPerSecondAtStart = blaster.EnergyPerSecond;
            float dpsAtStart = blaster.DamagePerSecond;

            for (int level = 2; level <= 8; level++)
            {
                HudSignals.EmitLevelUp(level, Vector3.zero);
                yield return null;

                Assert.AreEqual(energyPerSecondAtStart, blaster.EnergyPerSecond, 0.01f,
                    $"level {level} made the stream thirstier — the boost pays for itself and vanishes");
                Assert.Greater(blaster.DamagePerSecond, dpsAtStart,
                    $"level {level} didn't increase output");
            }

            Assert.Less(blaster.FireInterval, 0.1f, "fire rate never actually got faster");
        }

        [UnityTest]
        public IEnumerator PowerIsDerivedFromTheLevel_SoASkippedLevelUpStillLandsRight()
        {
            // The XP track can grant two levels from one kill. Power is a function of the CURRENT
            // level, so jumping straight to 6 must equal walking up to 6.
            yield return NewBlaster();
            var blaster = _go.GetComponent<WaterBlaster>();

            HudSignals.EmitLevelUp(6, Vector3.zero);   // straight there — no 2,3,4,5
            yield return null;
            float jumped = blaster.DamagePerSecond;

            for (int level = 2; level <= 6; level++)
            {
                HudSignals.EmitLevelUp(level, Vector3.zero);
                yield return null;
            }

            Assert.AreEqual(jumped, blaster.DamagePerSecond, 0.01f,
                "re-applying the ramp compounded it — multipliers must never stack onto live values");
        }
    }
}
