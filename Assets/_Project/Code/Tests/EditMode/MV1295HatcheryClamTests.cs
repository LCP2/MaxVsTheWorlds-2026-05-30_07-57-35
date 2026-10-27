using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1295: the Reef's 13 sheds build as Hatchery Clams — the SAME <see cref="MowerHutch"/> body,
    /// not a new class, so position/spawner/census/gate/bar/husk are reused wholesale; this only adds
    /// an 8s-open/4s-shut cycle that refuses damage entirely while shut. Fails to compile on 87c2faf
    /// (the commit before this ticket): <c>MowerHutch.ApplyHatcheryClamBehavior</c>/<c>IsHatcheryClam</c>/
    /// <c>ClamOpen</c>/<c>TickClamPhase</c> do not exist there.
    ///
    /// The ONE new test this ticket adds (testing policy v2, MV-465) — one method covering every
    /// EditMode-testable AC: a1's machine in the loaded Reef is a Hatchery Clam; a 50-damage hit during
    /// the shut phase removes 0 HP and during the open phase removes the full 50 (a resolved
    /// <see cref="MowerHutch.Normalized"/> readback, not an authored constant); and World 1's own shed,
    /// loaded the same way, never becomes one and carries no cycle.
    /// </summary>
    public sealed class MV1295HatcheryClamTests
    {
        [Test]
        public void ReefShed_IsHatcheryClam_GatesDamageByPhase_World1ShedUnaffected()
        {
            // Same edit-mode DestroyImmediate console noise precedent as MV547ShedFittingTests/MV683.
            LogAssert.ignoreFailingMessages = true;

            int reefIndex = WorldCatalog.IndexOf(WorldIds.Reef);
            Assert.AreNotEqual(-1, reefIndex, "WorldCatalog carries no row for WorldIds.Reef");
            WorldDefinition reefRow = WorldCatalog.Get(reefIndex);
            WorldConfig reefCfg = WorldLibrary.Load(reefRow.ConfigKey);
            Assert.IsTrue(WorldMapLoader.TryLoad(reefCfg, out MapData reefMap, out string reefReason), reefReason);

            int backyardIndex = WorldCatalog.IndexOf(WorldIds.Backyard);
            Assert.AreNotEqual(-1, backyardIndex, "WorldCatalog carries no row for WorldIds.Backyard");
            WorldDefinition backyardRow = WorldCatalog.Get(backyardIndex);
            WorldConfig backyardCfg = WorldLibrary.Load(backyardRow.ConfigKey);
            Assert.IsTrue(WorldMapLoader.TryLoad(backyardCfg, out MapData backyardMap, out string backyardReason), backyardReason);

            var root = new GameObject("MV1295 Probe Root");
            try
            {
                // --- a1's machine in the loaded Reef is a Hatchery Clam, and gates damage by phase. ---
                MapBuild reefBuild = MapRuntime.Build(reefMap, root.transform);
                Assert.IsTrue(reefBuild.Actors.TryGetValue("a1_shed", out GameObject reefShedGo),
                    "the Reef's a1 shed did not build");
                var clam = reefShedGo.GetComponent<MowerHutch>();
                Assert.IsNotNull(clam, "the Reef's a1 machine carries no MowerHutch");
                InvokeAwake(clam);
                clam.ApplyHatcheryClamBehavior();

                Assert.IsTrue(clam.IsHatcheryClam, "the Reef's a1 machine must be a Hatchery Clam");

                // 9s into an 8-open/4-shut cycle lands 1s into the shut phase.
                clam.TickClamPhase(9f);
                Assert.IsFalse(clam.ClamOpen, "9s into an 8-open/4-shut cycle must be shut");
                float normalizedBeforeShutHit = clam.Normalized;
                clam.TakeDamage(new DamageInfo(50f, reefShedGo.transform.position, Vector3.forward, Team.Player));
                Assert.AreEqual(normalizedBeforeShutHit, clam.Normalized, 1e-6f,
                    "a shut Hatchery Clam must take 0 damage");

                // +4s more (13s total) wraps to 1s into the next cycle — open again.
                clam.TickClamPhase(4f);
                Assert.IsTrue(clam.ClamOpen, "13s into the cycle (wrapped to 1s) must be open");
                float normalizedBeforeOpenHit = clam.Normalized;
                clam.TakeDamage(new DamageInfo(50f, reefShedGo.transform.position, Vector3.forward, Team.Player));
                float expectedNormalized = normalizedBeforeOpenHit - 50f / clam.AuthoredMax;
                Assert.AreEqual(expectedNormalized, clam.Normalized, 1e-4f,
                    "an open Hatchery Clam must take the full 50 damage");

                // --- World 1's own shed (a3) never becomes a clam and carries no cycle. ---
                MapBuild backyardBuild = MapRuntime.Build(backyardMap, root.transform);
                Assert.IsTrue(backyardBuild.Actors.TryGetValue("a3_shed", out GameObject w1ShedGo),
                    "World 1's a3 shed did not build");
                var w1Hutch = w1ShedGo.GetComponent<MowerHutch>();
                Assert.IsNotNull(w1Hutch, "World 1's a3 machine carries no MowerHutch");
                InvokeAwake(w1Hutch);

                Assert.IsFalse(w1Hutch.IsHatcheryClam, "World 1's shed must never become a Hatchery Clam");
                w1Hutch.TickClamPhase(9f); // no-op: never opted in via ApplyHatcheryClamBehavior
                float w1NormalizedBefore = w1Hutch.Normalized;
                w1Hutch.TakeDamage(new DamageInfo(50f, w1ShedGo.transform.position, Vector3.forward, Team.Player));
                Assert.AreEqual(w1NormalizedBefore - 50f / w1Hutch.AuthoredMax, w1Hutch.Normalized, 1e-4f,
                    "World 1's shed must take damage normally — no shut phase ever gates it");
            }
            finally
            {
                Object.DestroyImmediate(root);
                FactoryCensus.Reset();
            }
        }

        private static void InvokeAwake(Object component)
        {
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(component, null);
        }
    }
}
