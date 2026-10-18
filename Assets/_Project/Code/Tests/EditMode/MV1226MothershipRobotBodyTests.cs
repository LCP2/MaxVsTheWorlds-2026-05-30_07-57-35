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
    /// MV-1226: the Mothership gives the Drone (Rusher behaviour) and Sentry (Turret behaviour) their
    /// own ship bodies, the same per-world silhouette mechanism MV-746 established for the Reef. The
    /// ONE new test this ticket adds (testing policy v2, MV-465), covering AC1's two resolved-value
    /// measurements plus the Reef-untouched guard in one method.
    ///
    /// Fails on 349d63e (the commit before this ticket): mothership_config.json carries no
    /// enemyOverrides block, so EnemyArchetype.For(Rusher, mothershipConfig).Skin is empty and
    /// RobotBodies.Build falls through to the base Rusher buggy body — nowhere near 0.8m wide — and
    /// there is no Mothership-only dispatch for Turret either (RobotBodies.Build takes no mothership
    /// skin tag at all). Confirmed via cc-test.bat on base-plus-test-only (the Rusher body resolves to
    /// ~0.47m wide, not 0.75-0.85m).
    /// </summary>
    public sealed class MV1226MothershipRobotBodyTests
    {
        [Test]
        public void MothershipGivesDroneAndSentryTheirOwnBodies_ReefUnaffected()
        {
            WorldConfig mothershipConfig = WorldLibrary.Load(WorldLibrary.Mothership);
            Assert.IsNotNull(mothershipConfig, "mothership_config.json failed to load");

            // --- AC1: in the Mothership, the Drone (Rusher) resolves 0.8m +/-0.05 wide. ---
            EnemyArchetype droneArchetype = EnemyArchetype.For(EnemyKind.Rusher, mothershipConfig);
            GameObject droneGo = null;
            try
            {
                RobotRig droneRig = BuildViaSpawner(droneArchetype, out droneGo);
                Assert.AreEqual("mothership", droneArchetype.Skin, "the Mothership's Rusher override must carry the mothership skin tag");

                float droneWidth = CombinedBounds(droneRig).size.x;
                Assert.That(droneWidth, Is.InRange(0.75f, 0.85f),
                    $"the Mothership Drone's resolved body is {droneWidth:0.000}m wide, not 0.8m +/-0.05");
            }
            finally { if (droneGo != null) Object.DestroyImmediate(droneGo); }

            // --- AC1: in the Mothership, the Sentry (Turret) resolves 1.4m +/-0.1 tall. ---
            EnemyArchetype sentryArchetype = EnemyArchetype.For(EnemyKind.Turret, mothershipConfig);
            GameObject sentryGo = null;
            try
            {
                RobotRig sentryRig = BuildViaSpawner(sentryArchetype, out sentryGo);
                Assert.AreEqual("mothership", sentryArchetype.Skin, "the Mothership's Turret override must carry the mothership skin tag");

                float sentryHeight = CombinedBounds(sentryRig).size.y;
                Assert.That(sentryHeight, Is.InRange(1.3f, 1.5f),
                    $"the Mothership Sentry's resolved body is {sentryHeight:0.000}m tall, not 1.4m +/-0.1");
            }
            finally { if (sentryGo != null) Object.DestroyImmediate(sentryGo); }

            // --- AC1: in the Reef, the same two kinds still build the Reef's own bodies (untouched by
            // this ticket) rather than falling through to the new Mothership dispatch. ---
            WorldConfig reefConfig = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(reefConfig, "world3_config.json (the Reef) failed to load");

            EnemyArchetype reefRusher = EnemyArchetype.For(EnemyKind.Rusher, reefConfig);
            Assert.AreEqual("reef", reefRusher.Skin, "the Reef's Rusher override must still carry the reef skin tag");
            EnemyArchetype reefTurret = EnemyArchetype.For(EnemyKind.Turret, reefConfig);
            Assert.AreNotEqual("mothership", reefTurret.Skin, "the Reef's Turret must not pick up the Mothership skin");
        }

        /// <summary>Combined world-space bounds of every built part, excluding the destroyed-at-build
        /// greybox stand-in — same technique <c>RobotBodySizeTests.CombinedBoundsHeight</c> and
        /// <c>MV746ReefBodyTests</c> use.</summary>
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
        /// reflection, then forces <see cref="RobotRig.EnsureBuilt"/> — the same technique
        /// <c>MV535RobotBodyOrderingTests</c>/<c>MV746ReefBodyTests</c> use, for the same reason:
        /// Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode.</summary>
        private static RobotRig BuildViaSpawner(EnemyArchetype archetype, out GameObject enemyGo)
        {
            var spawnerGo = new GameObject("MV1226 test spawner");
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
                // container — detach before the spawner is torn down below, or DestroyImmediate(spawnerGo)
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
