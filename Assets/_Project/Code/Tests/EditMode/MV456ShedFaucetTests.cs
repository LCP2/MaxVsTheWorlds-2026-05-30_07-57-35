using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-456: a shed area is meant to be a renewable cell faucet — destroying the Mower Hutch must
    /// not permanently silence its <see cref="EnemySpawner"/>, only slow it to a steady trickle.
    /// Before this fix, <see cref="MowerHutch.OnDestroyed"/> called <see cref="EnemySpawner.Stop"/>,
    /// which latches <c>_running</c> off for good (YT-100) — the exact bug this ticket exists to fix.
    /// </summary>
    public sealed class MV456ShedFaucetTests
    {
        // Awake isn't reliably invoked for AddComponent outside Play mode (same note
        // WaterBlasterGateDamageTests/AreaGateTests carry for MV-386) — drive it directly so the
        // hutch's DestructibleHealth actually exists before TakeDamage is exercised.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        [Test]
        public void FactoryDestruction_KeepsTheSpawnerRunningAtAReducedCadence()
        {
            var go = new GameObject("Hutch");
            try
            {
                // MowerHutch.BuildCore destroys the primitive core's stock collider via Object.Destroy,
                // which is edit-mode-illegal and logs an [Error] regardless of who calls Awake — same
                // shape as WaterBlasterGateDamageTests' cleanup, just triggered on the way in here
                // instead of on the way out.
                LogAssert.ignoreFailingMessages = true;

                var hutch = go.AddComponent<MowerHutch>();   // RequireComponent brings EnemySpawner
                InvokeAwake(hutch);
                var spawner = go.GetComponent<EnemySpawner>();
                float liveSteadyState = spawner.AuthoredSpawnIntervalMin;

                hutch.TakeDamage(new DamageInfo(hutch.AuthoredMax + 1f, Vector3.zero, Vector3.forward, Team.Player));

                Assert.That(hutch.IsAlive, Is.False, "the hutch must actually be dead for this test to mean anything");
                Assert.That(spawner.IsRunning, Is.True,
                    "MV-456: a destroyed shed area must keep streaming robots at a reduced cadence, not stop for good");
                Assert.That(spawner.CurrentInterval, Is.GreaterThan(liveSteadyState),
                    "the post-destruction faucet must be slower than the live factory's steady-state cadence");
            }
            finally
            {
                Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
