using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-691's Pipe Turret: a static wall-mounted lobber that fires a corrosive coolant glob every
    /// 2.2 s (0.4 s telegraph + 1.8 s recover) at Max's position at fire time, and whose impact puddle
    /// marks a receiver CORRODED — damage taken x1.25 for 4 s. EditMode only, reflection-driven
    /// (repo convention): <c>Update()</c> never runs outside Play mode, so the private Tick* methods
    /// are invoked directly and <c>_stateTimer</c> is force-advanced rather than looping real frames —
    /// only <c>Update()</c> itself ever increments that clock, the same idiom as
    /// <c>MV428MeleeReadabilityTests</c>.
    ///
    /// AC1's "resolve its impact at Max's position" is tested via <see cref="CorrosionPuddle"/>
    /// directly (spawned at Max's position with the same authored radius/duration
    /// <c>RobotEnemy.TickGlobFire</c> hands <c>CorrosiveGlob.Fire</c>) rather than driving the glob's
    /// own flight to completion — the same "test the extracted, testable piece, not the live
    /// MonoBehaviour's per-frame flight" idiom <c>BolterBoltTests</c>/<c>HomingMissile</c>'s own tests
    /// already use, since <see cref="CorrosiveGlob.Detonate"/> (private) does nothing on impact but
    /// call <see cref="CorrosionPuddle.Spawn"/> at the locked impact point.
    /// </summary>
    public sealed class MV691PipeTurretTests
    {
        private GameObject _playerGo;
        private PlayerHealth _playerHealth;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();

            _playerGo = new GameObject("Player", typeof(CharacterController)) { tag = "Player" };
            _playerGo.AddComponent<PlayerController>();
            _playerHealth = _playerGo.AddComponent<PlayerHealth>();
            _playerHealth.Initialize(); // MV-464: exposed publicly so an EditMode test can invoke it directly

            foreach (var stray in Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<CorrosionPuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            DevTuning.Reset();
            foreach (var g in Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                Object.DestroyImmediate(g.gameObject);
            foreach (var p in Object.FindObjectsByType<CorrosionPuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        // ------------------------------------------------------------------ helpers

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private static RobotEnemy NewTurret(Vector3 position)
        {
            var go = new GameObject("Enemy Turret");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // EditMode never runs Awake/OnEnable (same note as EnemyFriendlyFireTests.NewEnemy), so
            // _cc — normally seeded there — has to be stamped by hand before TickChase's movement can
            // call CharacterControllerMotion.SafeMove on it.
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Turret); // stamps stats and re-runs ResetState, which finds the tagged Player
            return e;
        }

        private static void InvokeTickChase(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickChase", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void InvokeTickTelegraph(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickTelegraph", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void InvokeTickLunge(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickLunge", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void InvokeTickRecover(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickRecover", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        /// <summary>Force-advances <c>_stateTimer</c> directly (MV428's own idiom) — the private
        /// Tick* methods only ever READ this clock, <c>Update()</c> is the only place that advances
        /// it (<c>_stateTimer += dt;</c>), and <c>Update()</c> never runs outside Play mode.</summary>
        private static void SetStateTimer(RobotEnemy e, float value) =>
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(e, value);

        // ------------------------------------------------------------------ AC1: fire cadence

        [Test]
        public void Turret_Awake_MaxEightMetresAway_FiresExactlyOneGlob_OverTheAuthoredTwoPointTwoSecondCadence()
        {
            var turret = NewTurret(new Vector3(8f, 0f, 0f));
            try
            {
                turret.Sight.Tick(true, _playerHealth.transform.position, 0.02f); // gives it sight of Max right now

                // Chase -> Telegraph: within the archetype's 11 m LungeRange, sight held, no attack
                // token needed for a Turret (only Rusher/Blinker are token-gated).
                InvokeTickChase(turret, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Telegraph, turret.Current,
                    "8 m is inside the Turret's 11 m fire range with sight — it must wind up immediately");

                // Just short of the archetype's own 0.4 s telegraph — no glob yet, the tell is what
                // makes it dodgeable.
                SetStateTimer(turret, 0.39f);
                InvokeTickTelegraph(turret, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Telegraph, turret.Current, "must not fire before the telegraph completes");
                Assert.AreEqual(0, Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None).Length,
                    "must not fire before the telegraph completes");

                // Cross the 0.4s threshold -> Lunge.
                SetStateTimer(turret, 0.4f);
                InvokeTickTelegraph(turret, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Lunge, turret.Current);

                // Lunge: instant release (LungeTime 0) — exactly one glob on the very first Lunge tick.
                InvokeTickLunge(turret, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Recover, turret.Current, "instant release: straight to Recover");

                CorrosiveGlob[] globsAfterFire = Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None);
                Assert.AreEqual(1, globsAfterFire.Length, "exactly one glob must exist after the Turret fires");

                // Cross the archetype's own 1.8s recover threshold — 0.4 (telegraph) + 1.8 (recover) =
                // the ticket's own authored 2.2s cadence — and still exactly the one glob already
                // fired; the next cycle hasn't reached Telegraph again yet.
                SetStateTimer(turret, 1.8f);
                InvokeTickRecover(turret, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Chase, turret.Current, "the cadence must have handed back to Chase");
                Assert.AreEqual(1, Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None).Length,
                    "after the ticket's own 2.2s cadence, exactly one glob must exist — not zero, not two");
            }
            finally
            {
                Object.DestroyImmediate(turret.gameObject);
            }
        }

        // ------------------------------------------------------------------ AC1: resolved impact -> CORRODED

        [Test]
        public void ResolvingTheGlobsImpact_MarksMaxCorroded_AndAmplifiesASubsequentHit()
        {
            // CorrosiveGlob.Detonate (private) does nothing on impact but spawn a CorrosionPuddle at
            // the locked impact point with the same authored radius/duration RobotEnemy.TickGlobFire
            // hands it — this is that resolved behaviour, driven directly at Max's own position.
            CorrosionPuddle puddle = CorrosionPuddle.Spawn(_playerHealth.transform.position, radius: 1.5f, duration: 3f);
            try
            {
                Assert.IsFalse(_playerHealth.IsCorroded, "must not be corroded before the puddle ticks");

                puddle.Tick(0.01f); // one evaluation: Max is standing exactly at the impact point

                Assert.IsTrue(_playerHealth.IsCorroded, "standing in the puddle must apply CORRODED");
                Assert.AreEqual(CorrodedStatus.Duration, _playerHealth.CorrodedRemaining, 0.02f,
                    "a fresh CORRODED application must read close to the full 4 s duration");
                Assert.AreEqual(1.25f, _playerHealth.DamageTakenMultiplier, 1e-4f,
                    "the resolved multiplier must be the ticket's own 1.25x, not left at 1x");

                float before = _playerHealth.Current;
                _playerHealth.TakeDamage(new DamageInfo(20f, _playerHealth.transform.position, Vector3.forward, Team.Enemy));
                float applied = before - _playerHealth.Current;

                Assert.AreEqual(25f, applied, 1e-3f,
                    "a 20-damage hit while CORRODED must resolve to 25 (20 x 1.25), never a hardcoded 25");
            }
            finally
            {
                Object.DestroyImmediate(puddle.gameObject);
            }
        }
    }
}
