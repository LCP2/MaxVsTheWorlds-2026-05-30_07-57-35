using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Bosses;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The combat-feel defaults baked from Lee's on-device tuning (YT-106). These pin the shipped
    /// numbers — and, for the two that are serialized on the player, guard against the scene silently
    /// shadowing them back to the old values (the exact trap that bit BlasterTuning and moved it to
    /// a const in the first place).
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
            Assert.That(BlasterTuning.EnergyPerSecond, Is.EqualTo(19.94f).Within(0.001f), "water deplete");
            Assert.That(BlasterTuning.RegenPerSec, Is.EqualTo(55f).Within(0.001f), "water replenish — unchanged");
            Assert.That(EnemyArchetype.Rusher.MoveSpeed, Is.EqualTo(1.85f).Within(0.001f), "robot speed");
            Assert.That(BossTuning.MoveSpeed, Is.EqualTo(3.6f).Within(0.001f), "boss speed — unchanged");
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
                            Is.EqualTo(69.82f).Within(0.001f), "Max max-life default");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// The shipped scene must carry the same numbers, or the scene's serialized value wins over
        /// the code default and the player would still run at 6 m/s with 100 hp on the build.
        /// </summary>
        [Test]
        public void TheSceneDoesNotShadowThePlayerDefaultsBackToTheOldValues()
        {
            string scene = Path.Combine(RepoRoot, "Assets", "_Project", "Scenes", "Backyard_Slice.unity");
            string text = File.ReadAllText(scene);

            AssertField(text, "MaxWorlds.Player.PlayerController", "moveSpeed", "3.01");
            AssertField(text, "MaxWorlds.Player.PlayerHealth", "maxHealth", "69.82");
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
