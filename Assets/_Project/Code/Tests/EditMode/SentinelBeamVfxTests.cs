using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-616: the sentinel's beam was a bare two-point <c>LineRenderer</c> — a flat-coloured laser,
    /// no particles, no splash, no muzzle flash, no droop. This proves the fix in one test (three
    /// facets of the same regression — the beam's identity as a VFX object — not independent
    /// regressions, per the testing policy's "one new test per ticket" rule):
    /// (AC1) a firing sentinel has at least one active <see cref="ParticleSystem"/> under it, and no
    /// <see cref="LineRenderer"/> remains; (AC2) 1 second after the last shot, every particle system
    /// the beam touched is empty; (AC3) with the maximum deployable number of sentinels firing at
    /// once, total live particles stay under the budget stated in the PR.
    /// </summary>
    public sealed class SentinelBeamVfxTests
    {
        /// <summary>The maximum number of sentinels Max can have deployed at once — u_slt's authored
        /// cap level (<c>maxLevel: 4</c> in rig_board.json) fed through
        /// <see cref="MaxWorlds.Weapons.AbilityTuning.SentinelDeploymentSlots"/>. This is the "sentinel
        /// slot cap" the ticket's budget is stated against.</summary>
        private const int MaxDeployableSentinels = 4;

        /// <summary>Budget (PR-stated, per the ticket's requirement to scale particle counts down from
        /// the jet's rather than matching them): total live particles across every particle system
        /// touched by <see cref="MaxDeployableSentinels"/> sentinels all firing at the same instant.
        /// Each shot's own burst (stream + core + muzzle, emitting only for the sub-0.12s
        /// <c>BeamVisibleSeconds</c> window, plus a 4-14 droplet splash and a single flash particle)
        /// comes to roughly 90-120 live particles at peak per sentinel — this budget leaves headroom
        /// above 4x that without approaching the jet's own single-instance ceiling (maxParticles sums
        /// to 1380 for ONE WaterVfx).</summary>
        private const int MaxLiveParticlesBudget = 700;

        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

        private static void InvokeUpdate(Sentinel sentinel)
        {
            typeof(Sentinel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(sentinel, null);
        }

        private static RobotEnemy NewTarget(Vector3 position)
        {
            var go = new GameObject("Target Robot");
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.ResetState(); // EditMode has no Awake/OnEnable lifecycle — init explicitly
            return e;
        }

        private static ParticleSystem[] AllScopedParticleSystems() =>
            Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        private static void SimulateAll(ParticleSystem[] systems, float seconds)
        {
            foreach (var ps in systems)
                ps.Simulate(seconds, withChildren: false, restart: false, fixedTimeStep: true);
        }

        [Test]
        public void FiringBeamsAreParticlesWithABudgetAndTheyClearWithinASecond()
        {
            var sentinels = new Sentinel[MaxDeployableSentinels];
            var targets = new RobotEnemy[MaxDeployableSentinels];
            var roots = new GameObject[MaxDeployableSentinels];
            try
            {
                // Spread pairs far apart so no sentinel's OverlapSphere range query or separation
                // step reaches another pair's sentinel/target.
                for (int i = 0; i < MaxDeployableSentinels; i++)
                {
                    Vector3 origin = new Vector3(i * 40f, 0f, 0f);
                    var go = new GameObject($"Sentinel{i}");
                    roots[i] = go;
                    var sentinel = go.AddComponent<Sentinel>();
                    sentinel.Init(origin, 60f, range: 7f, fireInterval: 0.6f,
                        moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                    sentinels[i] = sentinel;
                    targets[i] = NewTarget(origin + new Vector3(2f, 0f, 0f));
                }

                Physics.SyncTransforms();
                foreach (var sentinel in sentinels) InvokeUpdate(sentinel);

                // --- AC1: each firing sentinel has an active ParticleSystem under it, no LineRenderer ---
                foreach (var sentinel in sentinels)
                {
                    var systems = sentinel.transform.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
                    Assert.That(systems.Length, Is.GreaterThanOrEqualTo(1),
                        $"{sentinel.name}: no ParticleSystem was built under a firing sentinel");
                    Assert.IsTrue(systems.Any(ps => ps.isPlaying),
                        $"{sentinel.name}: it built particle systems but none are actually playing");

                    var lines = sentinel.GetComponentsInChildren<LineRenderer>();
                    Assert.That(lines.Length, Is.EqualTo(0),
                        $"{sentinel.name}: a bare LineRenderer beam is still present");
                }

                // --- AC3: budget across every sentinel firing at once ---
                // 0.12f mirrors Sentinel.BeamVisibleSeconds's own cap (Mathf.Min(0.12f, fireInterval * 0.9f))
                // for the 0.6f fireInterval every sentinel above was Init'd with.
                ParticleSystem[] allSystems = AllScopedParticleSystems();
                SimulateAll(allSystems, 0.12f);

                int totalLive = allSystems.Sum(ps => ps.particleCount);
                Assert.That(totalLive, Is.LessThanOrEqualTo(MaxLiveParticlesBudget),
                    $"{MaxDeployableSentinels} sentinels firing at once cost {totalLive} live particles, " +
                    $"over the {MaxLiveParticlesBudget}-particle budget stated in the PR");

                // --- AC2: 1 second after the last shot, nothing is left alive ---
                foreach (var go in roots)
                {
                    var vfx = go.transform.Find("BeamOrigin").GetComponent<WaterVfx>();
                    vfx.SetStreaming(false);
                }
                SimulateAll(allSystems, 1f);

                foreach (var ps in allSystems)
                {
                    Assert.That(ps.particleCount, Is.EqualTo(0),
                        $"'{ps.name}' still has live particles a full second after the last shot");
                }
            }
            finally
            {
                foreach (var go in roots) if (go != null) Object.DestroyImmediate(go);
                foreach (var t in targets) if (t != null) Object.DestroyImmediate(t.gameObject);
            }
        }
    }
}
