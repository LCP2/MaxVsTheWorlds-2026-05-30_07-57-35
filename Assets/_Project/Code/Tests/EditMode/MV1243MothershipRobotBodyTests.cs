using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1243: as MV-1226 did for the Drone/Sentry, the Mothership gives the Gunner (Lensman) and
    /// Brute (Hauler) their own ship bodies -- the same per-world silhouette mechanism, dispatched from
    /// <see cref="RobotBodies.Build"/> via the existing "mothership" skin tag. The ONE new test this
    /// ticket adds (testing policy v2, MV-465), covering AC1's two resolved-value measurements plus the
    /// Reef-untouched guard in one method.
    ///
    /// Fails on 89912e1 (the commit before this ticket): mothership_config.json's enemyOverrides carries
    /// no gunner/brute entries, so EnemyArchetype.For(Gunner, mothershipConfig).Skin is empty and
    /// RobotBodies.Build falls through to the base Gunner tripod body -- nowhere near 1.7m tall -- and
    /// RobotBodies.TryBuildMothership has no dispatch for Gunner/Brute at all.
    /// </summary>
    public sealed class MV1243MothershipRobotBodyTests
    {
        [Test]
        public void MothershipGivesLensmanAndHaulerTheirOwnBodies_ReefUnaffected()
        {
            WorldConfig mothershipConfig = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(mothershipConfig, "mothership_config.json failed to load");

            // --- AC1: in the Mothership, the Lensman (Gunner) resolves 1.7m +/-0.1 tall. ---
            EnemyArchetype lensmanArchetype = EnemyArchetype.For(EnemyKind.Gunner, mothershipConfig);
            GameObject lensmanGo = null;
            try
            {
                RobotRig lensmanRig = BuildViaSpawner(lensmanArchetype, out lensmanGo);
                Assert.AreEqual("mothership", lensmanArchetype.Skin, "the Mothership's Gunner override must carry the mothership skin tag");

                float lensmanHeight = CombinedBounds(lensmanRig).size.y;
                Assert.That(lensmanHeight, Is.InRange(1.6f, 1.8f),
                    $"the Mothership Lensman's resolved body is {lensmanHeight:0.000}m tall, not 1.7m +/-0.1");
            }
            finally { if (lensmanGo != null) Object.DestroyImmediate(lensmanGo); }

            // --- AC1: in the Mothership, the Hauler (Brute) resolves 1.25m +/-0.05 wide. ---
            EnemyArchetype haulerArchetype = EnemyArchetype.For(EnemyKind.Brute, mothershipConfig);
            GameObject haulerGo = null;
            try
            {
                RobotRig haulerRig = BuildViaSpawner(haulerArchetype, out haulerGo);
                Assert.AreEqual("mothership", haulerArchetype.Skin, "the Mothership's Brute override must carry the mothership skin tag");

                float haulerWidth = CombinedBounds(haulerRig).size.x;
                Assert.That(haulerWidth, Is.InRange(1.2f, 1.3f),
                    $"the Mothership Hauler's resolved body is {haulerWidth:0.000}m wide, not 1.25m +/-0.05");
            }
            finally { if (haulerGo != null) Object.DestroyImmediate(haulerGo); }

            // --- AC1: in the Reef, the same two kinds still build the Reef's own bodies (untouched by
            // this ticket) rather than falling through to the Mothership dispatch. ---
            WorldConfig reefConfig = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(reefConfig, "world3_config.json (the Reef) failed to load");

            EnemyArchetype reefGunner = EnemyArchetype.For(EnemyKind.Gunner, reefConfig);
            Assert.AreEqual("reef", reefGunner.Skin, "the Reef's Gunner override must still carry the reef skin tag");
            EnemyArchetype reefBrute = EnemyArchetype.For(EnemyKind.Brute, reefConfig);
            Assert.AreNotEqual("mothership", reefBrute.Skin, "the Reef's Brute must not pick up the Mothership skin");
        }

        /// <summary>Combined world-space bounds of every built part, excluding the destroyed-at-build
        /// greybox stand-in -- same technique <c>MV1226MothershipRobotBodyTests.CombinedBounds</c> uses.</summary>
        private static Bounds CombinedBounds(RobotRig rig)
        {
            var greybox = rig.GetComponent<MeshRenderer>();
            Bounds? combined = null;
            foreach (var r in rig.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == greybox) continue;
                if (combined == null) { combined = r.bounds; continue; }
                var b = combined.Value;
                b.Encapsulate(r.bounds);
                combined = b;
            }
            Assert.IsNotNull(combined, $"{rig.name} built no visible parts at all");
            return combined.Value;
        }

        /// <summary>Drives the real, private <c>EnemySpawner.CreateInstance(in EnemyArchetype)</c> via
        /// reflection, then forces <see cref="RobotRig.EnsureBuilt"/> -- same technique
        /// <c>MV1226MothershipRobotBodyTests</c> uses, for the same reason: Awake/OnEnable aren't
        /// reliably invoked for AddComponent outside Play mode.</summary>
        private static RobotRig BuildViaSpawner(EnemyArchetype archetype, out GameObject enemyGo)
        {
            var spawnerGo = new GameObject("MV1243 test spawner");
            try
            {
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                MethodInfo createInstance = typeof(EnemySpawner).GetMethod(
                    "CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(createInstance, "EnemySpawner.CreateInstance went missing");

                LogAssert.ignoreFailingMessages = true;
                RobotEnemy e;
                try { e = (RobotEnemy)createInstance.Invoke(spawner, new object[] { archetype }); }
                finally { LogAssert.ignoreFailingMessages = false; }

                enemyGo = e.gameObject;
                var rig = e.GetComponent<RobotRig>();
                Assert.IsNotNull(rig, $"CreateInstance did not attach a RobotRig for {archetype.Kind}");

                LogAssert.ignoreFailingMessages = true;
                try
                {
                    typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(rig, null);
                }
                finally { LogAssert.ignoreFailingMessages = false; }

                Assert.IsTrue(rig.Built, $"RobotRig never finished building for {archetype.Kind}");

                // CreateInstance parents every spawned body under the spawner's own "Robots" metre-space
                // container -- detach before the spawner is torn down below, or DestroyImmediate(spawnerGo)
                // cascades and destroys the very GameObject this method hands back to its caller.
                enemyGo.transform.SetParent(null, worldPositionStays: false);
                return rig;
            }
            finally
            {
                Object.DestroyImmediate(spawnerGo);
            }
        }
    }
}
