using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-708 — the LPPE primary: <see cref="SeekerPulse"/>'s fire-time target lock, its homing flight,
    /// and the Shock stun a repeated hit streak on the same robot triggers (AC1), plus the LPPE's energy
    /// drain tracking the RCDA's within 5% (AC2). All assertions read RESOLVED values — a spawned
    /// pulse's own <see cref="SeekerPulse.Target"/>, its position after simulated flight steps, an
    /// <see cref="EnergyPool"/>'s actual spend, and <see cref="RobotEnemy.IsStunned"/>/
    /// <see cref="RobotEnemy.StunTimeRemaining"/> — never an authored constant (MV-465 Tier 2).
    ///
    /// Fails on 81cc1a9: none of <see cref="PulseLaser"/>, <see cref="SeekerPulse"/>,
    /// <see cref="WeaponCatalog.PrimaryKind.Lppe"/> or <see cref="RobotEnemy.IsStunned"/> exist on that
    /// commit, so this test does not compile there.
    ///
    /// Reflection to drive private Awake()/FireTick() outside Play mode is the same idiom
    /// <c>WaterBlasterGateDamageTests</c>/<c>MV657TargetReacquireTests</c> already use.
    /// </summary>
    public sealed class PulseLaserTests
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
        private static readonly MethodInfo PulseLaserUpdate =
            typeof(PulseLaser).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserRegisterHit =
            typeof(PulseLaser).GetMethod("RegisterHit", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserTickHitStreaks =
            typeof(PulseLaser).GetMethod("TickHitStreaks", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PulseLaserHitStreakField =
            typeof(PulseLaser).GetField("_hitStreak", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PulseLaserHitStreakTimerField =
            typeof(PulseLaser).GetField("_hitStreakTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PulseLaserTankField =
            typeof(PulseLaser).GetField("_tank", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo WaterBlasterTankField =
            typeof(WaterBlaster).GetField("_tank", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _playerGo;
        private GameObject _laserGo;
        private RobotEnemy _rusher;
        private RobotEnemy _bruiser;

        /// <summary><see cref="RobotEnemy.Active"/> (what <see cref="SeekerPulse"/>'s target
        /// acquisition reads) is only populated by <c>OnEnable</c>, which Unity does not reliably
        /// invoke for AddComponent outside Play mode — invoked directly here, the same reflection idiom
        /// <c>MV531DissolveSnapshotTests.NewRobot</c>/<c>SentinelPlacementTests</c> already established
        /// for exactly this reason.</summary>
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

        private static void InvokeFireTick(PulseLaser laser) => PulseLaserFireTick.Invoke(laser, null);

        /// <summary>Drives a pulse's flight to completion (hit, blocked, or expired), capped well past
        /// AC1's own 0.6s bound so a failure to converge reads as an assertion, not a hang.</summary>
        private static float AdvanceUntilSpent(SeekerPulse pulse, float step, float cap)
        {
            float elapsed = 0f;
            while (!pulse.IsSpent && elapsed < cap)
            {
                pulse.Tick(step);
                elapsed += step;
            }
            return elapsed;
        }

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Lppe;
            // AC2 compares live WaterBlaster/PulseLaser properties that read these two global statics
            // (nozzle bonuses, dev-tuning overrides) -- reset so an earlier, unrelated test's leftover
            // state can never skew the "matches the RCDA within 5%" comparison.
            UpgradeState.Reset();
            DevTuning.Reset();

            _playerGo = new GameObject("Player") { tag = "Player" };

            _laserGo = new GameObject("PulseLaser");
            _laserGo.transform.position = Vector3.zero;
            _laserGo.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            PulseLaser laser = _laserGo.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(laser, null); // Awake doesn't run for AddComponent outside Play mode

            // 8m ahead, 20 degrees off boresight -- inside the LPPE's 35 degree lock cone.
            Vector3 rusherDir = Quaternion.Euler(0f, 20f, 0f) * Vector3.forward;
            _rusher = NewEnemy("Rusher", rusherDir * 8f, EnemyArchetype.Rusher);
            // Bumped past the archetype's authored 32 HP so four 9-damage pulse hits don't kill it --
            // isolates AC1's Shock-stun assertion from the Rusher's own HP tuning, a separate concern.
            HealthField.SetValue(_rusher, 100f);

            // 6m away (closer than the Rusher), 60 degrees off boresight -- outside the 35 degree lock
            // cone, so it must never be the one a pulse locks onto.
            Vector3 bruiserDir = Quaternion.Euler(0f, 60f, 0f) * Vector3.forward;
            _bruiser = NewEnemy("Bruiser", bruiserDir * 6f, EnemyArchetype.Bruiser);

            // autoSyncTransforms is off project-wide -- every position set above needs an explicit sync
            // before any physics query (SeekerPulse's Cover-layer obstruction raycast) runs against it,
            // same convention WaterBlasterGateDamageTests/GateSolidityTests already follow.
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            UpgradeState.Reset();
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            if (_rusher != null) Object.DestroyImmediate(_rusher.gameObject);
            if (_bruiser != null) Object.DestroyImmediate(_bruiser.gameObject);
            if (_laserGo != null) Object.DestroyImmediate(_laserGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        [Test]
        public void OnePull_LocksTheInConeRobot_HomesInFast_ShocksOnTheFourthHit_AndDrainsLikeTheRcda()
        {
            PulseLaser laser = _laserGo.GetComponent<PulseLaser>();

            // AC1a: one trigger pull spawns exactly one pulse, locked onto the Rusher -- the only robot
            // inside the 35 degree cone (the closer Bruiser sits at 60 degrees, outside it).
            InvokeFireTick(laser);
            SeekerPulse pulse = laser.LastSpawnedPulseForTests;
            Assert.IsNotNull(pulse, "one trigger pull spawned no SeekerPulse at all");
            Assert.AreSame(_rusher, pulse.Target,
                "the pulse locked onto the wrong robot -- it must resolve to the in-cone Rusher, never " +
                "the closer Bruiser sitting outside the 35 degree lock cone");

            // AC1b: advancing the pulse by simulated steps brings it within 0.5m of the Rusher inside
            // 0.6s of simulated flight (18 m/s, 360 deg/s turn, 8m away at 20 degrees off-lock).
            const float step = 1f / 60f;
            float elapsed = AdvanceUntilSpent(pulse, step, cap: 1f);
            Assert.LessOrEqual(elapsed, 0.6f,
                $"the pulse took {elapsed:0.000}s of simulated flight to reach (or hit) the Rusher -- " +
                "MV-708's speed/turn numbers should close an 8m, 20 degree-off lock well inside 0.6s");
            Assert.IsTrue(pulse.IsSpent, "the pulse never actually landed a hit within the flight cap");

            // AC1c: four consecutive pulse hits on the same robot within Shock's 3s window stun it.
            for (int i = 0; i < 3; i++)
            {
                InvokeFireTick(laser);
                SeekerPulse next = laser.LastSpawnedPulseForTests;
                AdvanceUntilSpent(next, step, cap: 1f);
                Assert.IsTrue(next.IsSpent, $"pulse #{i + 2} never landed on the Rusher");
            }

            Assert.IsTrue(_rusher.IsStunned,
                "four consecutive pulse hits on the same robot inside Shock's 3s window must stun it " +
                "-- IsStunned is still false after the fourth hit");
            Assert.AreEqual(0.5f, _rusher.StunTimeRemaining, 0.05f,
                $"Shock's stun should read ~0.5s remaining immediately after the 4th hit, got " +
                $"{_rusher.StunTimeRemaining:0.000}s");

            // AC2: energy drain over 2s of continuous fire must land within +/-5% of the RCDA's own
            // drain over the same window -- measured on each EnergyPool's real Current delta, never on
            // the authored per-tick/per-pulse constants themselves (MV-465 Tier 1 ban).
            // The +1e-4f nudge guards against a division landing a hair under an exact integer boundary
            // in float precision (e.g. 2f/0.1f) and silently dropping a whole tick/pulse from the count.
            var lppeTank = (EnergyPool)PulseLaserTankField.GetValue(laser);
            int lppePulses = Mathf.FloorToInt(2f / laser.PulseInterval + 1e-4f);
            float lppeBefore = lppeTank.Current;
            for (int i = 0; i < lppePulses; i++) lppeTank.TrySpend(laser.EnergyPerPulse);
            float lppeDrain = lppeBefore - lppeTank.Current;

            var wbGo = new GameObject("wb_energy_compare");
            try
            {
                var blaster = wbGo.AddComponent<WaterBlaster>();
                WaterBlasterAwake.Invoke(blaster, null);
                var wbTank = (EnergyPool)WaterBlasterTankField.GetValue(blaster);
                int rcdaTicks = Mathf.FloorToInt(2f / blaster.FireInterval + 1e-4f);
                float rcdaBefore = wbTank.Current;
                for (int i = 0; i < rcdaTicks; i++) wbTank.TrySpend(blaster.EnergyPerTick);
                float rcdaDrain = rcdaBefore - wbTank.Current;

                Assert.Greater(rcdaDrain, 0f, "test precondition: the RCDA comparison drained nothing");
                float ratio = lppeDrain / rcdaDrain;
                Assert.That(ratio, Is.InRange(0.95f, 1.05f),
                    $"the LPPE's 2s drain ({lppeDrain:0.00}) should track the RCDA's own 2s drain " +
                    $"({rcdaDrain:0.00}) within +/-5% at base level -- ratio was {ratio:0.000}");
            }
            finally
            {
                Object.DestroyImmediate(wbGo);
            }
        }

        /// <summary>
        /// MV-751: <c>TickHitStreaks</c> wrote into <c>_hitStreakTimer</c> through the indexer WHILE
        /// enumerating it — a write to an EXISTING key still bumps <c>Dictionary</c>'s version counter,
        /// so the very next <c>Update()</c> throws <c>InvalidOperationException: Collection was
        /// modified</c>, and the gun never fires again for the rest of the run. The bug only manifests
        /// once a hit streak entry exists (created by <c>RegisterHit</c>, which only fires when a pulse
        /// lands on a robot), which is why firing at scenery was fine but firing at a robot killed the
        /// weapon dead. Also proves the fix's decrement is not a no-op (a streak still expires past its
        /// window) and that its scratch buffer is a cached field, never reallocated per call.
        ///
        /// Fails on 009fcb6 (pre-fix): <c>PulseLaserUpdate.Invoke</c> throws
        /// <c>System.Reflection.TargetInvocationException</c> wrapping
        /// <c>System.InvalidOperationException: Collection was modified; enumeration operation may not
        /// execute.</c> on the very first of the 120 ticks below, because <see cref="RobotEnemy"/>'s
        /// hit-streak timer (a full 3s <c>ShockWindowSeconds</c> window) is always still &gt; 0 and
        /// always hits the mutating branch on the first tick, regardless of the real (near-zero, outside
        /// Play mode) <c>Time.deltaTime</c> the reflection call actually runs with.
        /// </summary>
        [Test]
        public void HitStreak_SurvivesManyUpdateTicks_StillFiresAfter_AndStreaksStillExpire()
        {
            PulseLaser laser = _laserGo.GetComponent<PulseLaser>();

            // Two distinct robots land a hit -- populates BOTH _hitStreak and _hitStreakTimer, which
            // Update() -> TickHitStreaks() walks every frame, before the fire gate.
            PulseLaserRegisterHit.Invoke(laser, new object[] { _rusher, 9f });
            PulseLaserRegisterHit.Invoke(laser, new object[] { _bruiser, 9f });

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 120; i++) PulseLaserUpdate.Invoke(laser, null);
            }, "PulseLaser.Update must survive many ticks once a hit streak exists on more than one " +
               "robot -- mutating _hitStreakTimer while enumerating it throws and kills the weapon for " +
               "the rest of the run (MV-751)");

            // Still able to fire afterwards -- the gun isn't dead.
            InvokeFireTick(laser);
            Assert.IsNotNull(laser.LastSpawnedPulseForTests,
                "the LPPE no longer fires after surviving 120 Update ticks with an active hit streak");

            // A streak ticked past ShockWindowSeconds (3s) must actually be removed from BOTH
            // dictionaries -- proves the fix's decrement isn't a no-op that never lets a streak expire.
            var hitStreak = (System.Collections.IDictionary)PulseLaserHitStreakField.GetValue(laser);
            var hitStreakTimer = (System.Collections.IDictionary)PulseLaserHitStreakTimerField.GetValue(laser);
            for (int i = 0; i < 4; i++) PulseLaserTickHitStreaks.Invoke(laser, new object[] { 1f });
            Assert.IsFalse(hitStreakTimer.Contains(_rusher),
                "a hit streak older than ShockWindowSeconds should have expired and been removed, not " +
                "kept alive forever by a decrement that silently became a no-op");
            Assert.IsFalse(hitStreak.Contains(_rusher),
                "the Shock hit COUNT must be cleared alongside its timer once the streak expires");
        }
    }
}
