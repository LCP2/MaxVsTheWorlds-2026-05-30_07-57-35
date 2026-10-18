using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1236 — World 5's Orbitals secondary. The one new test the testing policy allows this ticket:
    /// with ORBS 3 (4 orbs) and REACH 1 (1.8m), a robot placed exactly 1.8m from Max sits ON the orbit
    /// circle, at orb 0's own starting angle. Every orb sweeps through every angle once per revolution
    /// (SPIN's own default level-1 rate, 180 deg/s, is exactly one revolution per the 2s window), so
    /// over the window the robot is actually crossed by all four orbs, each at its own time (orb 0 at
    /// ~0s and again at ~2s — a full revolution back to its start — orbs 3/2/1 at 0.5/1.0/1.5s) — the
    /// robot's health is boosted (reflection) so those four incidental cross-orb hits don't kill it
    /// before orb 0's own second pass lands. The 0.8s cooldown is per (orb, target) pair, not global
    /// (the ticket's own "the SAME orb cannot hit the same robot again for 0.8s"), so those
    /// different-orb hits landing well under 0.8s apart are CORRECT, not a violation — the test
    /// attributes each hit to whichever orb's own <see cref="Orbitals.OrbPosition"/> is nearest the
    /// robot at that instant (unambiguous: adjacent orbs are 90° apart, the real contact window — orb
    /// radius plus the robot's own CharacterController radius — is nowhere near that wide), and checks
    /// the 0.8s rule PER attributed orb index, while separately asserting orb 0 itself lands at least
    /// two hits (its own two passes).
    ///
    /// A sentinel placed at the diametrically opposite point is swept by all four orbs too (symmetric
    /// to the robot, just offset by half a revolution), but must take nothing from any of them — it's
    /// Team.Player. Max himself carries a real <see cref="PlayerHealth"/> too and is asserted
    /// untouched directly, not just argued geometrically: he sits at the orbit's own centre, more than
    /// Reach - ContactRadius away from every orb at any owned level, so no orb's capsule ever reaches
    /// his collider.
    ///
    /// Proven to fail on main pre-MV-1236: none of <see cref="Orbitals"/>, <see cref="OrbitalsTrackKind"/>,
    /// or <see cref="SecondaryKind.Orbitals"/> existed, and <c>rig_board.ship.json</c> had no
    /// <c>s_orb</c>/<c>s_rad</c> nodes — this test fails to compile against that commit (CS0246 "The
    /// type or namespace name 'Orbitals' could not be found"), the same class of pre-fix evidence
    /// <c>MV862SentinelFocusTests</c> documents.
    /// </summary>
    public sealed class MV1236OrbitalsTests
    {
        private const float Dt = 1f / 60f;

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo CcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly FieldInfo HealthField = typeof(RobotEnemy).GetField("_health", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            // s_orb/s_rad live only on rig_board.ship.json — RestoreSnapshot drops any id RigBoard.Exists
            // says isn't on the ACTIVE board, same reason MV694ShoulderRackTests selects World 2's board.
            RigBoard.UseWorld(WorldCatalog.IndexOf(WorldIds.Mothership));
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            RigBoard.ResetForTests();
        }

        [Test]
        public void OrbHitsTheRobotTwiceNeverWithinCooldownAndNeverTouchesTheSentinel_MV1236()
        {
            WeaponSystemState.SecondaryKind = SecondaryKind.Orbitals;
            // ORBS L3 (4 orbs), REACH L1 (1.8m) — RestoreSnapshot bypasses the draft/reach gate for test
            // setup, the same shortcut MV694ShoulderRackTests/RigStateTests use. POWER/SPIN are left
            // untouched: Orbitals' own tracks all start owned at level 1 (OrbitalsTrackKind's class
            // doc), and AbilityTuning's Orbitals curves floor an unset (level 0) track at level 1 too.
            RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_orb", 3 }, { "s_rad", 1 } },
                new[] { "SECONDARY" });

            var maxGo = new GameObject("Max");
            var orbitals = maxGo.AddComponent<Orbitals>();
            // RequireComponent chains in a PlayerController (Orbitals' own CharacterController
            // requirement is already satisfied) — its Awake never runs outside Play mode (same note
            // NewRobot below relies on for RobotEnemy), so this does not also self-attach a second
            // Orbitals the way the real PlayerController.Awake does.
            var maxHealth = maxGo.AddComponent<PlayerHealth>();
            maxHealth.Initialize();

            RobotEnemy robot = NewRobot(new Vector3(1.8f, 0f, 0f));
            // The robot also takes one incidental hit from each OTHER orb during the window (class doc
            // above) — boosted so those four don't kill it before orb 0's own second pass lands.
            HealthField.SetValue(robot, 1000f);
            GameObject sentinelGo = new GameObject("MV1236-Sentinel");
            Sentinel sentinel = sentinelGo.AddComponent<Sentinel>();
            sentinel.Init(new Vector3(-1.8f, 0f, 0f), maxHp: 100f, range: 10f, fireInterval: 1f,
                moveSpeed: 0f, standoffDistance: 0f, followTarget: maxGo.transform);

            try
            {
                Physics.SyncTransforms();

                float maxHealthBefore = maxHealth.Current;
                float sentinelHealthBefore = sentinel.HealthCurrent;
                float robotHealthBefore = robot.HealthCurrent;
                float robotHealthLast = robotHealthBefore;
                var lastHitElapsedBySlot = new Dictionary<int, float>();
                var hitCountBySlot = new Dictionary<int, int>();
                float elapsed = 0f;

                int steps = Mathf.CeilToInt(2f / Dt) + 1;
                for (int i = 0; i < steps; i++)
                {
                    orbitals.Tick(Dt);
                    elapsed += Dt;

                    if (robot.HealthCurrent < robotHealthLast)
                    {
                        int slot = NearestOrbSlot(orbitals, robot.transform.position);

                        if (lastHitElapsedBySlot.TryGetValue(slot, out float lastElapsed))
                            Assert.That(elapsed - lastElapsed, Is.GreaterThanOrEqualTo(Orbitals.PerTargetHitCooldownSeconds),
                                $"orb {slot} must not land a second hit on the robot within {Orbitals.PerTargetHitCooldownSeconds}s of its own last one");
                        lastHitElapsedBySlot[slot] = elapsed;
                        hitCountBySlot[slot] = hitCountBySlot.GetValueOrDefault(slot) + 1;

                        robotHealthLast = robot.HealthCurrent;
                    }
                }

                Assert.That(hitCountBySlot.GetValueOrDefault(0), Is.GreaterThanOrEqualTo(2),
                    "orb 0 itself must land at least two separate hits on the robot (one per revolution over the 2s window)");
                Assert.That(robot.HealthCurrent, Is.LessThan(robotHealthBefore), "the robot's health must have actually dropped");
                Assert.That(sentinel.HealthCurrent, Is.EqualTo(sentinelHealthBefore),
                    "a sentinel sitting in the ring (swept by every orb in turn, same as the robot) must take zero damage — it's Team.Player");
                Assert.That(maxHealth.Current, Is.EqualTo(maxHealthBefore),
                    "Max himself, standing at the orbit's own centre, must take zero damage from his own orbit");
            }
            finally
            {
                Object.DestroyImmediate(robot.gameObject);
                Object.DestroyImmediate(sentinelGo);
                Object.DestroyImmediate(maxGo);
            }
        }

        /// <summary>Which orb slot (0-based, XZ) sits nearest <paramref name="targetPosition"/> right
        /// now — adjacent orbs are 90° apart on this circle (ORBS L3 = 4 orbs) while the real contact
        /// window (orb radius plus the robot's own CharacterController radius) is well under 45°, so
        /// whichever orb is nearest is unambiguously the one a hit this tick came from.</summary>
        private static int NearestOrbSlot(Orbitals orbitals, Vector3 targetPosition)
        {
            int best = 0;
            float bestSq = float.MaxValue;
            for (int slot = 0; slot < orbitals.OrbCount; slot++)
            {
                Vector3 orbPos = orbitals.OrbPosition(slot);
                float dx = orbPos.x - targetPosition.x;
                float dz = orbPos.z - targetPosition.z;
                float distSq = dx * dx + dz * dz;
                if (distSq < bestSq) { bestSq = distSq; best = slot; }
            }
            return best;
        }

        private static RobotEnemy NewRobot(Vector3 position)
        {
            var go = new GameObject("MV1236-Robot");
            var cc = go.AddComponent<CharacterController>();
            var robot = go.AddComponent<RobotEnemy>();
            CcField.SetValue(robot, cc);
            robot.Apply(EnemyArchetype.Rusher);
            RobotOnEnableMethod.Invoke(robot, null); // seeds Active/ResetState — Awake/OnEnable don't run outside Play mode
            go.transform.position = position;
            return robot;
        }
    }
}
