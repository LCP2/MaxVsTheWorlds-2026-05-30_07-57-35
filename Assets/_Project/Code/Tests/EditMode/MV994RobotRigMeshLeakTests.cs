using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-994: <see cref="RobotRig"/>'s <c>CombineWheelGroups</c>/<c>CombineStaticParts</c> (MV-969) each
    /// build a fresh runtime <see cref="Mesh"/> named "Wheel"/"RobotStatic" per robot, and Unity does not
    /// free a runtime Mesh just because its GameObject died — so every robot spawned leaked one or more
    /// meshes until the next scene load. Charger is used because its wheels are authored as co-located
    /// tyre+hub parts (RobotRig.cs's own <c>CombineWheelGroups</c> doc comment), so a built Charger
    /// exercises BOTH combine paths and creates both a "Wheel" and a "RobotStatic" mesh.
    ///
    /// Tier 2 (resolved value): <see cref="Resources.FindObjectsOfTypeAll{T}"/> after
    /// <see cref="Object.DestroyImmediate(Object)"/> is what the engine actually still holds in memory —
    /// not a count re-derived from source. Must fail on 8c86feb, where the count grows by one "Wheel" and
    /// one "RobotStatic" mesh per iteration instead of returning to its starting value.
    /// </summary>
    public sealed class MV994RobotRigMeshLeakTests
    {
        private const int RobotCount = 50;

        [Test]
        public void FiftyRobotsBuiltAndDestroyed_LeavesNoOwnedMeshesBehind()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();

            var playerGo = new GameObject("Player") { tag = "Player" };
            try
            {
                int before = CountOwnedMeshes();

                for (int i = 0; i < RobotCount; i++)
                {
                    var go = new GameObject($"Enemy Charger {i}");
                    var cc = go.AddComponent<CharacterController>();
                    var enemy = go.AddComponent<RobotEnemy>();
                    typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance)
                        .SetValue(enemy, cc);
                    enemy.Apply(EnemyArchetype.Charger);

                    var rig = go.AddComponent<RobotRig>();
                    InvokeEnsureBuilt(rig);
                    Assert.IsTrue(rig.Built, $"robot {i}'s RobotRig never finished building");

                    // Unity does not reliably dispatch OnDestroy synchronously for a component created
                    // and torn down within the same EditMode test tick without ever running Awake/Start
                    // (MV574MobileHeatAndModalIdleTests hits the same limit) — invoke it directly, exactly
                    // as Unity would call it on a real robot death, rather than relying on
                    // DestroyImmediate's callback timing.
                    InvokeOnDestroy(rig);
                    Object.DestroyImmediate(go);
                }

                int after = CountOwnedMeshes();
                Assert.AreEqual(before, after,
                    $"{RobotCount} robots built and destroyed left {after - before} 'RobotStatic'/'Wheel' " +
                    "meshes behind — RobotRig.OnDestroy is not freeing the meshes it created for itself");
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
                RobotEnemy.ResetRegistry();
                DevTuning.Reset();
            }
        }

        private static int CountOwnedMeshes()
        {
            int count = 0;
            foreach (Mesh mesh in Resources.FindObjectsOfTypeAll<Mesh>())
            {
                if (mesh != null && (mesh.name == "RobotStatic" || mesh.name == "Wheel")) count++;
            }
            return count;
        }

        /// <summary>Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode (same note
        /// as RobotRigTests) — drive the private build step directly, and swallow whatever it logs: a
        /// build in this test environment can warn about a missing character shader, which Unity's
        /// default test rules would otherwise count as a failure.</summary>
        private static void InvokeEnsureBuilt(RobotRig rig)
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        private static void InvokeOnDestroy(RobotRig rig) =>
            typeof(RobotRig).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(rig, null);
    }
}
