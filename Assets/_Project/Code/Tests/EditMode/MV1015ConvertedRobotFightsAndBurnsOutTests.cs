using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1015 — World 3's cavitation-implosion conversion (MV-716) flipped a robot's team but never
    /// ticked it: <c>TickConversion</c> was never called from <c>TickBody</c>/<c>Tick</c>/<c>Update</c>,
    /// so a converted robot never expired, never burned out, and the 3-robot conversion cap filled
    /// permanently. <c>IsConverted</c> was also never read by the AI, so a converted robot kept
    /// acquiring Max/a Sentinel as its target and kept hitting with a hardcoded <c>Team.Enemy</c>
    /// attacker. Fixed by wiring <c>TickConversion</c> into <c>TickBody</c>, giving a converted robot
    /// its own retarget pass (<c>RetargetToNearestEnemyRobot</c>) instead of the Max/Sentinel dance,
    /// and passing the robot's own (already-mutable) <c>_team</c> as attacker at every contact-damage
    /// call site instead of a hardcoded <c>Team.Enemy</c>.
    ///
    /// Drives the REAL per-frame path — <see cref="RobotEnemy.Tick"/>, now public for exactly this
    /// reason — not a hand-picked private Tick* method, so this proves the actual production decision
    /// chain (<c>TickBody</c> -&gt; <c>TickChase</c> -&gt; <c>RetargetIfNeeded</c> -&gt;
    /// <c>RetargetToNearestEnemyRobot</c>) rather than a simulated shortcut.
    ///
    /// Fails on 7c4ab56 (MV-1014, the commit before this ticket): <c>RobotEnemy.Tick</c> does not exist
    /// as a public member (it was private), so this file does not compile there — the same "doesn't
    /// compile on the base commit" proof <c>MV716HijackingTests</c> uses for the methods that ticket
    /// added.
    /// </summary>
    public sealed class MV1015ConvertedRobotFightsAndBurnsOutTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            RobotEnemy.ResetRegistry();
            DevTuning.Reset(); // a stray dev-tuned contact cooldown from another test must not leak in
            EnemyNavigation.Reset();
        }

        /// <summary>Same construction idiom as <c>MV832SentinelTargetingTests.NewRobot</c>: AddComponent
        /// doesn't reliably run Awake/OnEnable outside Play mode, so <c>_cc</c> is stamped by hand and
        /// <c>OnEnable</c> is invoked directly — reflection used only for spawn WIRING, never for driving
        /// the conversion tick itself (that's <see cref="RobotEnemy.Tick"/>, called directly below).</summary>
        private static RobotEnemy NewRobot(string name, Vector3 position)
        {
            var go = new GameObject(name);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Bruiser); // no lunge/telegraph — contact damage lands on a plain Chase tick
            RobotOnEnableMethod.Invoke(e, null); // seeds RobotEnemy.Active
            go.transform.position = position;
            return e;
        }

        [Test]
        public void ConvertedRobotTargetsAndDamagesAnotherRobot_ThenBurnsOutAndFreesItsSlot_MV1015()
        {
            GameObject maxGo = null;
            RobotEnemy attacker = null, victim = null;
            try
            {
                // Tagged "Player" (unlike a bare test stand-in) so AcquireTarget/ResetState actually
                // finds it and seeds _playerTarget — required for the AC1(a) assertion below to mean
                // anything: without a real Max to have been targeting, "not Max" would be vacuous.
                maxGo = new GameObject("MV-1015 test Max", typeof(CharacterController)) { tag = "Player" };
                var playerHealth = maxGo.AddComponent<PlayerHealth>();
                playerHealth.Initialize();
                maxGo.transform.position = new Vector3(0f, 0f, 50f); // far from both robots

                attacker = NewRobot("Attacker", new Vector3(0f, 0f, 0f));
                victim = NewRobot("Victim", new Vector3(1f, 0f, 0f)); // 1m — inside Bruiser's 1.4m contactRadius
                attacker.SetHealthFraction(0.2f); // port-exposed: below the 25% conversion threshold
                Physics.SyncTransforms();

                Assert.AreSame(maxGo.transform, attacker.CurrentTarget,
                    "test setup: before conversion, the attacker must be targeting the tagged Max");

                float victimHealthBefore = victim.HealthCurrent;
                float maxHealthBefore = playerHealth.Current;

                Assert.IsTrue(attacker.TryConvert(), "test setup: a port-exposed robot must convert");
                Assert.AreEqual(Team.Player, attacker.Team, "test setup: a converted robot's team must flip to Player");

                // Real ~60fps frames (not a couple of huge 1s jumps): a large first dt would make the
                // very first tick's retarget-triggered sight reset (RetargetTo -> Perception.Spawn)
                // read as "hasn't gotten closer in over minHuntTime" and detour the robot into
                // State.Search before it ever reaches contact damage. Small steps avoid that entirely,
                // the same cadence Update() actually drives in Play mode.
                const float dt = 1f / 60f;
                const int framesPastContactCooldown = 90; // 1.5s — safely past the 1.0s cooldown

                for (int i = 0; i < framesPastContactCooldown; i++) attacker.Tick(dt);

                Assert.AreSame(victim.transform, attacker.CurrentTarget,
                    "(a) a converted robot's target must be the other robot, not Max");
                Assert.Less(victim.HealthCurrent, victimHealthBefore,
                    "(b) a converted robot's hit must reduce the other robot's HP");
                Assert.AreEqual(maxHealthBefore, playerHealth.Current, 1e-3f,
                    "(b) a converted robot's hit must never reduce Max's HP");

                // (c) tick the remaining time out to comfortably past the 20s conversion timeout, then
                // confirm burnout.
                int framesToBurnout = Mathf.CeilToInt((RobotEnemy.ConvertedDurationSeconds + 1f) / dt) - framesPastContactCooldown;
                for (int i = 0; i < framesToBurnout; i++) attacker.Tick(dt);

                Assert.IsFalse(attacker.IsAlive, "(c) a converted robot must be dead 20s after conversion");
                Assert.AreEqual(0, RobotEnemy.Converted.Count,
                    "(c) the conversion cap slot must free once the converted robot burns out");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (attacker != null) Object.DestroyImmediate(attacker.gameObject);
                if (victim != null) Object.DestroyImmediate(victim.gameObject);
            }
        }
    }
}
