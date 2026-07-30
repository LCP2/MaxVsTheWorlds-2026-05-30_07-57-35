using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Bosses;
using MaxWorlds.CameraRig;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The combat-feel defaults baked from Lee's on-device tuning (YT-106, re-baked YT-200). These
    /// pin the shipped numbers — and, for the ones that are serialized (on the player, the factory,
    /// the spawner), guard against the scene silently shadowing them back to the old values (the
    /// exact trap that bit BlasterTuning and moved it to a const in the first place).
    ///
    /// Robot speed was retuned again at YT-169: YT-106 paired the rusher's number with Max's own
    /// on-device slowdown and quietly drifted the ratio to ~72% of Max; YT-169 pulls it back to ~60%
    /// so the swarm reads as a walk, not a rush.
    /// </summary>
    public sealed class TuningDefaultsTests
    {
        private static string RepoRoot => Directory.GetParent(Application.dataPath).FullName;

        [Test]
        public void TheCodeAuthoredDefaultsAreLeesNumbers()
        {
            Assert.That(BlasterTuning.EnergyPerSecond, Is.EqualTo(10.57f).Within(0.001f), "water deplete");
            Assert.That(BlasterTuning.RegenPerSec, Is.EqualTo(55f).Within(0.001f), "water replenish — unchanged");
            Assert.That(EnemyArchetype.Rusher.MoveSpeed, Is.EqualTo(1.85f).Within(0.001f), "robot speed");
            Assert.That(BossTuning.MoveSpeed, Is.EqualTo(3.6f).Within(0.001f), "boss speed — unchanged");
        }

        [Test]
        public void TheYT200AuthoredDefaultsAreLeesNumbers()
        {
            Assert.That(FixedAngleCameraRig.PhoneDistance, Is.EqualTo(16.1f).Within(0.001f), "camera zoom");
            // YT-210: the escalation rate is now DERIVED from Max/RunLengthSeconds rather than
            // hand-tuned, and the shed bump is a clock skip in seconds rather than a level bump.
            Assert.That(DifficultyDirector.AuthoredRatePerSecond,
                Is.EqualTo(DifficultyDirector.AuthoredMax / DifficultyDirector.AuthoredRunLengthSeconds).Within(0.0001f),
                "escalation rate");
            Assert.That(DifficultyDirector.AuthoredRunLengthSeconds, Is.EqualTo(360f).Within(0.001f), "run length");
            Assert.That(DifficultyDirector.AuthoredPerShedBump, Is.EqualTo(180f).Within(0.001f), "shed clock skip");
            Assert.That(EnemySpawner.DefaultRobotHealthMultiplier, Is.EqualTo(1.42f).Within(0.001f), "robot health");
            Assert.That(WaterBlaster.DefaultHydroDrainRate, Is.EqualTo(0.53f).Within(0.001f), "hydro drain");
            Assert.That(PickupDirector.DefaultPartInterval, Is.EqualTo(7.98f).Within(0.001f), "part pacing");
            Assert.That(EnemySpawner.DefaultSpawnIntervalPin, Is.EqualTo(3.996f).Within(0.001f), "spawn interval");
        }

        [Test]
        public void TheBruiserStaysHalfTheRushersSpeed()
        {
            // YT-66's fridge-on-legs: the tank is deliberately half-speed. Baking the rusher's new
            // number must not quietly make the bruiser as fast as it.
            Assert.That(EnemyArchetype.Bruiser.MoveSpeed,
                        Is.EqualTo(EnemyArchetype.Rusher.MoveSpeed * 0.5f).Within(0.02f),
                        "the bruiser should stay ~half the rusher's speed");
        }

        [Test]
        public void TheSerializedPlayerDefaultsAreLeesNumbers()
        {
            var go = new GameObject("Max", typeof(CharacterController), typeof(PlayerController),
                                    typeof(PlayerHealth));
            try
            {
                Assert.That(go.GetComponent<PlayerController>().AuthoredMoveSpeed,
                            Is.EqualTo(3.01f).Within(0.001f), "Max move speed default");
                Assert.That(go.GetComponent<PlayerHealth>().AuthoredMax,
                            Is.EqualTo(140.34f).Within(0.001f), "Max max-life default");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// The shipped scene must carry the same numbers, or the scene's serialized value wins over
        /// the code default and the build would still ship the old numbers.
        /// </summary>
        [Test]
        public void TheSceneDoesNotShadowTheSerializedDefaultsBackToTheOldValues()
        {
            string scene = Path.Combine(RepoRoot, "Assets", "_Project", "Scenes", "Backyard_Slice.unity");
            string text = File.ReadAllText(scene);

            AssertField(text, "MaxWorlds.Player.PlayerController", "moveSpeed", "3.01");
            AssertField(text, "MaxWorlds.Player.PlayerHealth", "maxHealth", "140.34");
            AssertField(text, "MaxWorlds.Factories.MowerHutch", "factoryHealth", "1501.5");
            AssertField(text, "MaxWorlds.Enemies.EnemySpawner", "spawnIntervalMin", "12");
        }

        /// <summary>Assert the serialized field directly under a component's class identifier reads
        /// the expected value — so we're checking the RIGHT component, not any field of that name.</summary>
        private static void AssertField(string scene, string classId, string field, string expected)
        {
            int at = scene.IndexOf(classId, System.StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThanOrEqualTo(0), $"{classId} not found in the scene");
            int key = scene.IndexOf(field + ":", at, System.StringComparison.Ordinal);
            Assert.That(key, Is.GreaterThanOrEqualTo(0), $"{field} not found under {classId}");
            string line = scene.Substring(key, scene.IndexOf('\n', key) - key);
            Assert.That(line.Trim(), Is.EqualTo($"{field}: {expected}"),
                        $"{classId}.{field} is '{line.Trim()}', not '{field}: {expected}' — " +
                        "the scene would shadow the code default back to the old number");
        }
    }
}
