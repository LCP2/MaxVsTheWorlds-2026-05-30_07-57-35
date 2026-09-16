using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-814 — FORK (<c>p_frk</c>) read as doing nothing: measured against a scripted 60s engagement
    /// (see <see cref="PulseLaser.RegisterKill"/>'s own doc comment for the counts), the old kill-only
    /// trigger released far fewer forks than a widened one that also fires on a near-death hit, and even
    /// when it did fire the forked bolt was visually identical to an ordinary one. This is the one new
    /// EditMode test the testing policy allows this ticket, asserting entirely from RESOLVED state
    /// (MV-465 Tier 2) — a spawned <see cref="SeekerPulse"/>'s own <see cref="SeekerPulse.Target"/> and
    /// its resolved bolt <see cref="MeshRenderer"/> bounds, never an authored constant.
    ///
    /// Fails on 9751529 (base commit before this fix): a pulse that only brings its target under 15% of
    /// max health (without killing it) releases no fork at all — <c>PulseLaser.RegisterKill</c> is wired
    /// to a plain "!killedTarget.IsAlive" kill-only check on that commit, so
    /// <c>laser.LastForkedPulseForTests</c> stays null and the very first assertion below
    /// ("IsNotNull(forked, ...)") fails.
    ///
    /// Reflection to drive private Awake()/FireTick() outside Play mode is the same idiom
    /// <c>PulseLaserTests</c>/<c>MV768WeaponNodesAreWiredTests</c> already use.
    /// </summary>
    public sealed class MV814ForkLandsTests
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

        private static RobotEnemy NewEnemy(string name, Vector3 position, in EnemyArchetype archetype)
        {
            var go = new GameObject(name);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            go.transform.position = position;
            e.Apply(archetype);
            RobotEnemyOnEnable.Invoke(e, null);
            return e;
        }

        private static void AdvanceUntilSpent(SeekerPulse pulse, float step, float cap)
        {
            float elapsed = 0f;
            while (!pulse.IsSpent && elapsed < cap) { pulse.Tick(step); elapsed += step; }
        }

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            RobotEnemy.ResetRegistry();
            RigBoard.ResetForTests();
        }

        [Test]
        public void NearDeathHitForksOnce_NeverChains_AndTheForkedBoltReadsWiderThanAnOrdinaryOne()
        {
            WeaponSystemState.ApplyWeaponCoreMorph(1); // World 2 board, ActivePrimary -> Lppe
            WeaponSystemState.AcquireById("p_rng");    // p_frk's own parent
            WeaponSystemState.AcquireById("p_frk");    // FORK at level 1
            Assert.AreEqual(1, RigState.Level("p_frk"), "test precondition: p_frk must reach level 1");

            var laserGo = new GameObject("PulseLaser_MV814");
            laserGo.transform.position = Vector3.zero;
            laserGo.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            PulseLaser laser = laserGo.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(laser, null); // Awake doesn't run for AddComponent outside Play mode

            const float step = 1f / 60f;
            float dmg = laser.EffectiveDamagePerPulse;

            // a: dead ahead, nearest -- the main pulse's own target. Its own health is set so the hit
            // leaves it well under 15% of its 32 HP max (4.8) without killing it (dmg dealt, 2 left).
            RobotEnemy a = NewEnemy("A", new Vector3(0f, 0f, 8f), EnemyArchetype.Rusher);
            HealthField.SetValue(a, dmg + 2f);

            // b: FORK's own second target -- farther than a, so never the main pulse's own lock, but
            // well within the LPPE's lock range for the fork's own release. Its health is set low enough
            // that the FORKED pulse's own hit kills it outright -- the harder case for the no-chain rule
            // (MV-768's own rule: a fork's kill must never itself release a further fork).
            RobotEnemy b = NewEnemy("B", new Vector3(1.5f, 0f, 8f), EnemyArchetype.Rusher);
            HealthField.SetValue(b, dmg - 1f);

            // c: a third robot within the FORKED pulse's own lock range, at a healthy 1000 HP -- proof
            // no chain reaches it, the same bystander idiom MV768WeaponNodesAreWiredTests' own c1 uses.
            RobotEnemy c = NewEnemy("C", new Vector3(-1.5f, 0f, 8f), EnemyArchetype.Rusher);
            HealthField.SetValue(c, 1000f);
            Physics.SyncTransforms();

            try
            {
                PulseLaserFireTick.Invoke(laser, null);
                SeekerPulse pulse = laser.LastSpawnedPulseForTests;
                Assert.AreSame(a, pulse.Target, "test precondition: the main pulse must lock onto a");
                AdvanceUntilSpent(pulse, step, cap: 1f);
                Assert.IsTrue(a.IsAlive, "test precondition: a must survive its own hit (near-death, not a kill)");
                Assert.Less(a.HealthNormalized, 0.15f,
                    $"test precondition: a's hit must leave it under 15% health, was {a.HealthNormalized:P1}");

                SeekerPulse forked = laser.LastForkedPulseForTests;
                Assert.IsNotNull(forked,
                    "a pulse that takes its target under the near-death threshold (without killing it) " +
                    "must still release exactly one further pulse -- FORK's old kill-only trigger misses " +
                    "this case entirely");
                Assert.AreSame(b, forked.Target,
                    "the forked pulse must aim at the second robot (b), not a or the bystander c");

                AdvanceUntilSpent(forked, step, cap: 1f);
                Assert.IsFalse(b.IsAlive, "test precondition: the forked pulse must kill b outright");
                Assert.AreSame(forked, laser.LastForkedPulseForTests,
                    "a forked pulse must never itself release a further fork, however it kills its own " +
                    "target -- LastForkedPulseForTests changed, meaning a second fork fired (chained)");
                Assert.AreEqual(1000f, c.HealthCurrent, 0.01f,
                    "c must take no damage at all -- a chained fork would have targeted it next");

                // The forked bolt's own tell: built facing the SAME direction as an ordinary pulse (so
                // their resolved MeshRenderer bounds are directly comparable per-axis), it must read at
                // least 1.2x wider across its cross-section (spec: 1.25x).
                //
                // MV-825: measured along Y on the SHEATH specifically (BoltRendererForTests now
                // resolves "Sheath", not whichever renderer happened to build first) -- FORK's own
                // wider cross-section (see SeekerPulse.GetSheathMesh) scales the sheath, not the core,
                // which only changes colour (item 8). Y isolates the cross-section's own radius on a
                // circular tube built straight along local Z, same as X would.
                SeekerPulse ordinary = SeekerPulse.Fire(Vector3.zero, Vector3.forward, PulseLaser.DefaultPulseSpeed,
                    PulseLaser.DefaultPulseTurnRateDegPerSec, PulseLaser.DefaultPulseLifetime, dmg, laser.LockRange,
                    PulseLaser.DefaultLockHalfAngle, canFork: false);
                SeekerPulse forkVisual = SeekerPulse.Fire(Vector3.zero, Vector3.forward, PulseLaser.DefaultPulseSpeed,
                    PulseLaser.DefaultPulseTurnRateDegPerSec, PulseLaser.DefaultPulseLifetime, dmg, laser.LockRange,
                    PulseLaser.DefaultLockHalfAngle, canFork: false, isFork: true);
                try
                {
                    float ordinaryWidth = ordinary.BoltRendererForTests.bounds.size.y;
                    float forkWidth = forkVisual.BoltRendererForTests.bounds.size.y;
                    Assert.GreaterOrEqual(forkWidth, ordinaryWidth * 1.2f,
                        $"a forked pulse's bolt must render at least 1.2x an ordinary pulse's cross-section " +
                        $"width -- ordinary was {ordinaryWidth:0.000}m, forked was {forkWidth:0.000}m");
                }
                finally
                {
                    Object.DestroyImmediate(ordinary.gameObject);
                    Object.DestroyImmediate(forkVisual.gameObject);
                }
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
                Object.DestroyImmediate(c.gameObject);
                Object.DestroyImmediate(laserGo);
            }
        }
    }
}
