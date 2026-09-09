using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-749 -- <see cref="SeekerPulse"/> (the LPPE's discrete pulse, MV-708) acquired and damaged
    /// ONLY the <see cref="RobotEnemy"/> it locked onto at fire time; nothing else in the world -- a
    /// gate, a Replicator, a boss -- could ever take damage from it, so a player could not open a gate,
    /// break a Replicator or hurt a boss with their own primary weapon (Lee's 9 Sep live playtest,
    /// World 2 area a1: the gate's HP bar never moved). Confirmed cause quoted in the ticket:
    /// <see cref="SeekerPulse.ApplyHit"/> only ever calls <c>TakeDamage</c> on the single target it
    /// locked at fire time -- there was no world-geometry hit at all.
    ///
    /// Both parts of the fix live in one test (Testing Policy MV-465: at most one new test per ticket):
    /// (1) with no robot anywhere in the scene, a pulse fired dead ahead at a solitary <see cref="AreaGate"/>
    /// must drop its RESOLVED health; (2) with a robot inside the lock cone AND that same gate standing
    /// between Max and the robot, the pulse must still lock onto the robot (MV-708's lock-on is
    /// unchanged) but damage the GATE, not the robot, because the gate is what its flight path reaches
    /// first.
    ///
    /// Fails on 573a207 (base commit, before this fix) -- <see cref="SeekerPulse"/> has no world-hit
    /// path at all, so part (1) fails with:
    /// "a lone gate directly ahead, with no robot anywhere in the scene, took no damage at all from a
    ///  fired pulse -- SeekerPulse only ever damages the RobotEnemy it locked onto (MV-749's bug).
    ///  Expected: less than 73.5999985f  But was:  73.5999985f"
    /// </summary>
    public sealed class SeekerPulseWorldTargetTests
    {
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RobotEnemyOnEnable =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        // Same reflection idiom WaterBlasterGateDamageTests already uses -- AddComponent doesn't
        // reliably run Awake outside Play mode.
        private static void InvokeAwake(Object component)
        {
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);
        }

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

        // A gate built exactly as MapRuntime.BuildAreaGate assembles one -- its own leaf collider
        // (where IDamageable actually lives) OFF the Cover layer, only its sibling ThresholdObject ON
        // it (MV-386's split) -- the same shape WaterBlasterGateDamageTests's regression fixture uses,
        // since that split is exactly what makes a naive single-hit raycast ambiguous (MV-749 fix note).
        private static AreaGate NewGate(Vector3 position)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.position = position;
            body.transform.localScale = new Vector3(4f, 3f, 0.6f);
            var gate = body.AddComponent<AreaGate>();
            InvokeAwake(gate);
            CoverLayer.Assign(gate.ThresholdObject);
            return gate;
        }

        private static void DestroyGate(AreaGate gate)
        {
            if (gate == null) return;
            GameObject threshold = gate.ThresholdObject;
            Object.DestroyImmediate(gate.gameObject);
            if (threshold != null) Object.DestroyImmediate(threshold);
        }

        /// <summary>Drives a pulse's flight to completion (hit, blocked, or expired).</summary>
        private static void AdvanceUntilSpent(SeekerPulse pulse, float step, float cap)
        {
            float elapsed = 0f;
            while (!pulse.IsSpent && elapsed < cap) { pulse.Tick(step); elapsed += step; }
        }

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
        }

        [Test]
        public void APulse_DamagesAGateWithNoRobotLocked_AndPrefersThatGateOverARobotLockedBehindIt()
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");

            const float step = 1f / 60f;
            const float speed = 18f;
            const float turnRate = 360f;
            const float damage = 9f;
            const float lockRange = 14f;
            const float lockHalfAngle = 35f;

            // --- Part 1: no robot anywhere, a lone gate dead ahead. ---
            AreaGate lonelyGate = NewGate(new Vector3(0f, 0f, 2f));
            try
            {
                float maxHp = lonelyGate.MaxHp;
                Assert.Greater(maxHp, 0f, "test precondition: the gate has HP to lose");
                Physics.SyncTransforms();

                SeekerPulse pulse = SeekerPulse.Fire(Vector3.zero, Vector3.forward, speed, turnRate,
                    lifetime: 1f, damage: damage, lockRange: lockRange, lockHalfAngleDeg: lockHalfAngle);
                Assert.IsNull(pulse.Target, "test precondition: no robot exists, so nothing should lock");

                AdvanceUntilSpent(pulse, step, cap: 1f);

                Assert.Less(lonelyGate.HealthCurrent, maxHp,
                    "a lone gate directly ahead, with no robot anywhere in the scene, took no damage " +
                    "at all from a fired pulse -- SeekerPulse only ever damages the RobotEnemy it " +
                    "locked onto (MV-749's bug).");
            }
            finally
            {
                DestroyGate(lonelyGate);
            }

            // --- Part 2: a robot inside the lock cone, with the same shaped gate standing between Max
            // and it. The pulse must still LOCK onto the robot (MV-708 unchanged) but damage the GATE,
            // since the gate is what its flight path actually reaches first.
            RobotEnemy rusher = null;
            AreaGate blockingGate = null;
            try
            {
                // 8m ahead, 20 degrees off boresight -- inside the 35 degree lock cone (same geometry
                // PulseLaserTests already established converges well inside a 1s flight cap).
                Vector3 rusherDir = Quaternion.Euler(0f, 20f, 0f) * Vector3.forward;
                rusher = NewEnemy("Rusher", rusherDir * 8f, EnemyArchetype.Rusher);
                float rusherHpBefore = rusher.HealthCurrent;

                // Closer than the robot, spanning the boresight the pulse starts on.
                blockingGate = NewGate(new Vector3(0f, 0f, 2f));
                float gateMaxHp = blockingGate.MaxHp;
                Physics.SyncTransforms();

                SeekerPulse pulse = SeekerPulse.Fire(Vector3.zero, Vector3.forward, speed, turnRate,
                    lifetime: 1f, damage: damage, lockRange: lockRange, lockHalfAngleDeg: lockHalfAngle);
                Assert.AreSame(rusher, pulse.Target,
                    "the pulse failed to lock onto the in-cone robot at all -- MV-708's lock-on must " +
                    "be unaffected by a gate sitting in the flight path");

                AdvanceUntilSpent(pulse, step, cap: 1f);

                Assert.Less(blockingGate.HealthCurrent, gateMaxHp,
                    "a gate standing between Max and his locked robot took no damage -- the pulse's " +
                    "flight path must damage whatever non-robot IDamageable it reaches first, even " +
                    "with a robot locked beyond it");
                Assert.AreEqual(rusherHpBefore, rusher.HealthCurrent,
                    "the locked robot took damage even though a gate stood between it and Max -- the " +
                    "gate should have stopped the pulse before it ever reached the robot");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    DestroyGate(blockingGate);
                    if (rusher != null) Object.DestroyImmediate(rusher.gameObject);
                }
                finally { LogAssert.ignoreFailingMessages = false; }
            }
        }
    }
}
