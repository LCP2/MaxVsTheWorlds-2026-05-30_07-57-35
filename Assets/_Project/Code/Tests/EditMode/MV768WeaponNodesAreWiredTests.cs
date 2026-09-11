using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-768 — three World 2 weapon nodes cost parts and do nothing: <c>p_rof</c> (RATE) and
    /// <c>p_frk</c> (FORK) have zero runtime consumers at all, and <c>s_clu</c> (CLUSTER)'s own bomblet
    /// code exists but was gated on a maxed Salvo track instead of its own dedicated RIG node. This is
    /// the one new EditMode test the testing policy allows this ticket — every ticket bullet lands in
    /// one method, all assertions on RESOLVED values (MV-465 Tier 2): <see cref="PulseLaser.PulseInterval"/>
    /// actually read live, a real <see cref="SeekerPulse"/> fork spawned by a real kill, and actual
    /// health lost to <see cref="PlayerRocket"/>'s own bomblet splash off a real detonation — plus AC5's
    /// own guard against the whole class of bug: <c>MV734CooldownRemovalTests</c> already runs this scan
    /// against World 1's board (<c>rig_board.json</c>); it never checked World 2's board
    /// (<c>rig_board.world2.json</c>) at all, which is exactly how <c>p_rof</c>/<c>p_frk</c> evaded it.
    ///
    /// Fails on 0c0bff2 (main HEAD before this fix):
    /// - RATE: <c>PulseLaser.PulseInterval</c> is a bare field read, never routed through
    ///   <c>p_rof</c>'s level at all, so it never changes as the track is raised.
    /// - FORK: <c>PulseLaser</c> has no <c>LastForkedPulseForTests</c> member and no fork mechanism at
    ///   all — this test does not compile against that commit.
    /// - CLUSTER: <c>ShoulderRack.FireSalvo</c> gates the bomblet flag on
    ///   <c>salvoLevel &gt;= WeaponCatalog.MaxLevel(ShoulderRackTrackKind.Salvo)</c>, never on
    ///   <c>s_clu</c>, so raising <c>s_clu</c> to level 1 (Salvo left at its default, unmaxed) changes
    ///   nothing.
    /// - AC5: <c>p_rof</c>/<c>p_frk</c> appear nowhere under Runtime outside Dev/UI tooling on
    ///   <c>rig_board.world2.json</c>.
    /// </summary>
    public sealed class MV768WeaponNodesAreWiredTests
    {
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo HealthField =
            typeof(RobotEnemy).GetField("_health", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserFireTick =
            typeof(PulseLaser).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PlayerRocketDetonate =
            typeof(PlayerRocket).GetMethod("Detonate", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly string RuntimeConsumerRoot =
            Path.Combine(Application.dataPath, "_Project", "Code", "Runtime");

        // Same comment-stripping idiom MV734CooldownRemovalTests uses -- a comment merely MENTIONING an
        // id in quotes must not count as a "consumer", or this guard would be blind to exactly the class
        // of bug it exists to catch.
        private static readonly Regex CommentRegex = new Regex(
            @"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        private static string StripComments(string text) =>
            CommentRegex.Replace(text, m =>
            {
                var blanked = new char[m.Length];
                for (int i = 0; i < m.Length; i++)
                    blanked[i] = text[m.Index + i] == '\n' ? '\n' : ' ';
                return new string(blanked);
            });

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RigBoard.ResetForTests();
        }

        [Test]
        public void RateForkAndClusterAreWiredToTheirOwnRigNodes_AndEveryWorld2NodeHasARuntimeConsumer()
        {
            AssertRate();
            AssertFork();
            AssertCluster();
            AssertEveryWorld2NodeHasARuntimeConsumer();
        }

        // ------------------------------------------------------------ p_rof RATE

        private static void AssertRate()
        {
            WeaponSystemState.Reset();
            WeaponSystemState.ApplyWeaponCoreMorph(1); // World 2 board, ActivePrimary -> Lppe

            var go = new GameObject("PulseLaser_RateTest");
            PulseLaser laser = go.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(laser, null); // Awake doesn't run for AddComponent outside Play mode

            try
            {
                Assert.AreEqual(0, RigState.Level("p_rof"), "test precondition: p_rof starts unbought");
                Assert.AreEqual(PulseLaser.DefaultPulseInterval, laser.PulseInterval, 1e-4f,
                    "p_rof at level 0 must resolve the LPPE's base 0.22s pulse interval");

                WeaponSystemState.AcquireById("p_rof");
                WeaponSystemState.RaiseLevelById("p_rof");
                WeaponSystemState.RaiseLevelById("p_rof");
                WeaponSystemState.RaiseLevelById("p_rof");
                Assert.AreEqual(4, RigState.Level("p_rof"), "test precondition: p_rof must reach level 4");

                Assert.AreEqual(PulseLaser.DefaultRateFloorInterval, laser.PulseInterval, 1e-4f,
                    "p_rof maxed at level 4 must resolve the board's own 0.16s floor fire interval -- " +
                    "PulseLaser.PulseInterval is a dead node, still reading the authored 0.22s base " +
                    "whatever p_rof's level is (MV-768's own RATE dead node)");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------ p_frk FORK

        private static void AssertFork()
        {
            WeaponSystemState.Reset();
            WeaponSystemState.ApplyWeaponCoreMorph(1);

            var go = new GameObject("PulseLaser_ForkTest");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            PulseLaser laser = go.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(laser, null);

            const float step = 1f / 60f;

            try
            {
                // --- p_frk at level 0: a killing pulse must release nothing extra. ---
                RobotEnemy a0 = NewRegisteredEnemy(EnemyArchetype.Rusher, new Vector3(0f, 0f, 8f));
                HealthField.SetValue(a0, 1f);
                RobotEnemy b0 = NewRegisteredEnemy(EnemyArchetype.Rusher, new Vector3(1.5f, 0f, 8f));
                HealthField.SetValue(b0, 1f);
                Physics.SyncTransforms();

                try
                {
                    Assert.AreEqual(0, RigState.Level("p_frk"), "test precondition: p_frk starts unbought");

                    PulseLaserFireTick.Invoke(laser, null);
                    SeekerPulse pulse0 = laser.LastSpawnedPulseForTests;
                    Assert.AreSame(a0, pulse0.Target, "test precondition: the pulse must lock onto a0");
                    AdvanceUntilSpent(pulse0, step, cap: 1f);
                    Assert.IsFalse(a0.IsAlive, "test precondition: a0 must die from the killing pulse");

                    Assert.IsNull(laser.LastForkedPulseForTests,
                        "p_frk at level 0 must not release any additional pulse from a killing hit " +
                        "(MV-768's own FORK dead node)");
                }
                finally
                {
                    Object.DestroyImmediate(a0.gameObject);
                    Object.DestroyImmediate(b0.gameObject);
                }

                RobotEnemy.ResetRegistry();

                // --- p_frk at level 1: exactly one fork, and it never chains even though it also kills. ---
                WeaponSystemState.AcquireById("p_rng"); // p_frk's own parent
                WeaponSystemState.AcquireById("p_frk");
                Assert.AreEqual(1, RigState.Level("p_frk"), "test precondition: p_frk must reach level 1");

                RobotEnemy a1 = NewRegisteredEnemy(EnemyArchetype.Rusher, new Vector3(0f, 0f, 8f));
                HealthField.SetValue(a1, 1f);
                RobotEnemy b1 = NewRegisteredEnemy(EnemyArchetype.Rusher, new Vector3(1.5f, 0f, 8f));
                HealthField.SetValue(b1, 1f);
                RobotEnemy c1 = NewRegisteredEnemy(EnemyArchetype.Rusher, new Vector3(3f, 0f, 8f));
                HealthField.SetValue(c1, 1000f);
                Physics.SyncTransforms();

                try
                {
                    PulseLaserFireTick.Invoke(laser, null);
                    SeekerPulse pulse1 = laser.LastSpawnedPulseForTests;
                    Assert.AreSame(a1, pulse1.Target, "test precondition: the pulse must lock onto a1");
                    AdvanceUntilSpent(pulse1, step, cap: 1f);
                    Assert.IsFalse(a1.IsAlive, "test precondition: a1 must die from the killing pulse");

                    SeekerPulse forked = laser.LastForkedPulseForTests;
                    Assert.IsNotNull(forked,
                        "p_frk at level 1 must release exactly one additional pulse when the original " +
                        "pulse's hit kills its target");
                    Assert.AreSame(b1, forked.Target,
                        "the forked pulse must lock onto the nearest OTHER valid target within lock " +
                        "range (b1), not some other robot");

                    AdvanceUntilSpent(forked, step, cap: 1f);
                    Assert.IsFalse(b1.IsAlive, "test precondition: the forked pulse must also kill b1, " +
                        "to prove the no-chain rule under the harder case");

                    Assert.AreSame(forked, laser.LastForkedPulseForTests,
                        "a forked pulse must never itself fork, however many kills it lands (MV-768's " +
                        "own no-chain rule) -- LastForkedPulseForTests changed, meaning a second fork fired");
                    Assert.AreEqual(1000f, c1.HealthCurrent, 0.01f,
                        "c1 must take no damage at all -- a chained fork would have targeted it next");
                }
                finally
                {
                    Object.DestroyImmediate(a1.gameObject);
                    Object.DestroyImmediate(b1.gameObject);
                    Object.DestroyImmediate(c1.gameObject);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
                RobotEnemy.ResetRegistry();
            }
        }

        // ------------------------------------------------------------ s_clu CLUSTER

        private static void AssertCluster()
        {
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
            RigBoard.UseWorld(1);
            WeaponSystemState.SecondaryKind = SecondaryKind.ShoulderRack;

            var maxGo = new GameObject("Max_ClusterTest");
            ShoulderRack rack = maxGo.AddComponent<ShoulderRack>();
            RobotEnemy fireTarget = NewPhysicsEnemy(EnemyArchetype.Rusher, new Vector3(6f, 0f, 0f));

            try
            {
                float[] dropOff = FireAndMeasureRingDamage(rack, clusterOn: false);
                float[] dropOn = FireAndMeasureRingDamage(rack, clusterOn: true);

                for (int i = 0; i < 3; i++)
                {
                    Assert.Greater(dropOn[i], dropOff[i],
                        $"ring position {i}: s_clu at level 1 must deal MORE damage there than s_clu at " +
                        $"level 0 -- a bomblet must land there (level 0: {dropOff[i]:0.0}, level 1: " +
                        $"{dropOn[i]:0.0}). CLUSTER is still gated on a maxed Salvo track, not s_clu " +
                        "(MV-768's own dead node).");
                }
            }
            finally
            {
                Object.DestroyImmediate(fireTarget.gameObject);
                Object.DestroyImmediate(maxGo);
                PlayerRocket.DestroyAllActive();
            }
        }

        /// <summary>Fires exactly one salvo (one rocket) with <c>s_clu</c> either owned or not, detonates
        /// it immediately in place (skipping flight -- PlayerRocket's flight/detonation timing runs on
        /// <c>Time.deltaTime</c> inside <c>Update</c> and is explicitly not EditMode-testable, per this
        /// project's standing PlayMode-is-CI's-problem rule; <c>MV694ShoulderRackTests</c> only ever
        /// exercises the firing decision for the same reason), and returns the health each of the three
        /// ring-position dummies lost -- the resolved effect of whatever bomblets actually spawned.</summary>
        private static float[] FireAndMeasureRingDamage(ShoulderRack rack, bool clusterOn)
        {
            var levels = new Dictionary<string, int> { { "s_rkt", 1 } };
            if (clusterOn) levels["s_clu"] = 1;
            RigState.RestoreSnapshot(levels, new[] { "SECONDARY" });
            PickupWallet.SetPowerCellSecondary(1);
            PlayerRocket.DestroyAllActive();

            const float dt = 1f / 60f;
            int steps = Mathf.CeilToInt(1.8f / dt) + 1; // one ReloadSeconds window (base 1.8s)
            for (int i = 0; i < steps; i++) rack.Tick(dt);

            Assert.That(PlayerRocket.Active.Count, Is.EqualTo(1),
                $"test precondition: exactly one rocket must fire (clusterOn={clusterOn})");
            PlayerRocket rocket = PlayerRocket.Active[0];

            // The three bomblet ring positions PlayerRocket.SpawnClusterBomblets computes around the
            // impact point (origin -- the rocket never actually flew, so it detonates exactly where it
            // spawned): 3 bomblets, 1.5m ring radius, evenly spaced.
            var ring = new RobotEnemy[3];
            for (int i = 0; i < 3; i++)
            {
                float angle = i * (360f / 3f) * Mathf.Deg2Rad;
                Vector3 point = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.5f;
                ring[i] = NewPhysicsEnemy(EnemyArchetype.Rusher, point);
            }
            Physics.SyncTransforms();

            float[] before = ring.Select(r => r.HealthCurrent).ToArray();

            // PlayerRocket.Detonate() calls plain Destroy(), never DestroyImmediate() -- fine in Play
            // mode (its only real caller), but outside it Unity logs an Error that the test framework
            // would otherwise fail the test on. Same ignoreFailingMessages idiom
            // SeekerPulseWorldTargetTests already uses for exactly this class of expected edit-mode
            // noise.
            LogAssert.ignoreFailingMessages = true;
            try { PlayerRocketDetonate.Invoke(rocket, null); }
            finally { LogAssert.ignoreFailingMessages = false; }

            float[] drop = new float[3];
            for (int i = 0; i < 3; i++) drop[i] = before[i] - ring[i].HealthCurrent;

            foreach (RobotEnemy r in ring) Object.DestroyImmediate(r.gameObject);
            PlayerRocket.DestroyAllActive();
            return drop;
        }

        // ------------------------------------------------------------ AC5 guard

        /// <summary>MV-734's own board-consumer scan (<c>MV734CooldownRemovalTests</c>), but for World
        /// 2's board -- which that ticket's guard never checked, exactly how <c>p_rof</c>/<c>p_frk</c>
        /// evaded it. Excludes <c>Runtime/Dev</c> (capture/conformance tooling) and <c>Runtime/UI</c>
        /// (a RIG board screen renders whatever ids the board data supplies -- it never needs to
        /// hardcode one) from counting as a "consumer".</summary>
        private static void AssertEveryWorld2NodeHasARuntimeConsumer()
        {
            RigBoard.UseWorld(1); // rig_board.world2.json

            Assert.IsTrue(Directory.Exists(RuntimeConsumerRoot), $"Runtime source root not found: {RuntimeConsumerRoot}");
            string combinedSource = string.Join("\n", Directory
                .GetFiles(RuntimeConsumerRoot, "*.cs", SearchOption.AllDirectories)
                .Where(p =>
                {
                    string norm = p.Replace('\\', '/');
                    return !norm.Contains("/Runtime/Dev/") && !norm.Contains("/Runtime/UI/");
                })
                .Select(p => StripComments(File.ReadAllText(p))));

            var withoutConsumer = RigBoard.AllIds.Where(nodeId => !combinedSource.Contains($"\"{nodeId}\"")).ToList();
            Assert.That(withoutConsumer, Is.Empty,
                "World 2 RIG node(s) with no runtime consumer outside Dev/UI tooling (MV-768's own class " +
                "of bug -- a node the player pays parts for that does nothing): " + string.Join(", ", withoutConsumer));
        }

        // ------------------------------------------------------------ helpers

        /// <summary>Registers the robot in <see cref="RobotEnemy.Active"/> (invoking <c>OnEnable</c>
        /// directly, since AddComponent doesn't reliably run it outside Play mode) -- needed for
        /// anything <see cref="SeekerPulse"/>'s target acquisition or FORK's own nearest-other search
        /// reads. Same idiom <c>PulseLaserTests</c>/<c>SeekerPulseWorldTargetTests</c> already use.</summary>
        private static RobotEnemy NewRegisteredEnemy(string name, Vector3 position, in EnemyArchetype archetype)
        {
            RobotEnemy e = NewPhysicsEnemy(archetype, position, name);
            RobotEnemyOnEnable.Invoke(e, null);
            return e;
        }

        private static RobotEnemy NewRegisteredEnemy(in EnemyArchetype archetype, Vector3 position) =>
            NewRegisteredEnemy(archetype.Kind.ToString(), position, archetype);

        /// <summary>A collider-backed robot with no <see cref="RobotEnemy.Active"/> registration -- all
        /// <see cref="ShoulderRack"/>/<see cref="PlayerRocket"/> need, since both query live colliders
        /// (<c>Physics.OverlapSphereNonAlloc</c>), never the registry. Same shape
        /// <c>MV694ShoulderRackTests.NewEnemy</c> already uses.</summary>
        private static RobotEnemy NewPhysicsEnemy(in EnemyArchetype archetype, Vector3 position, string name = null)
        {
            var go = new GameObject(name ?? $"Enemy {archetype.Kind}");
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            go.transform.position = position;
            e.Apply(archetype);
            return e;
        }

        private static void AdvanceUntilSpent(SeekerPulse pulse, float step, float cap)
        {
            float elapsed = 0f;
            while (!pulse.IsSpent && elapsed < cap) { pulse.Tick(step); elapsed += step; }
        }
    }
}
